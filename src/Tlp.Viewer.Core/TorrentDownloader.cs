using MonoTorrent;
using MonoTorrent.Client;
using Tlp.Core;

namespace Tlp.Viewer.Core;

/// <summary>
/// Built-in torrent client (MonoTorrent, MIT licensed, pure C#). Replaces uTorrent for
/// movie night magnets: no ads, no bundled extras, and files land exactly where Syncplay
/// expects them.
/// </summary>
public sealed class TorrentDownloader : IAsyncDisposable
{
    private static readonly string[] VideoExtensions = [".mkv", ".mp4", ".avi", ".m4v", ".webm", ".mov", ".ts"];
    private readonly ClientEngine _engine;
    private readonly bool _seedWhileOpen;

    /// <summary>Give up on a torrent (so the next source can be tried) after this long with no new data.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <param name="listenPort">TCP port for incoming peers; 0 lets the engine pick its default.</param>
    /// <param name="publicNetwork">False turns off DHT, UPnP and local discovery (used by tests).</param>
    public TorrentDownloader(string cacheDirectory, bool seedWhileOpen = true, int listenPort = 0, bool publicNetwork = true)
    {
        Directory.CreateDirectory(cacheDirectory);
        var builder = new EngineSettingsBuilder
        {
            CacheDirectory = cacheDirectory,
            AllowPortForwarding = publicNetwork,
            AllowLocalPeerDiscovery = publicNetwork,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            AutoSaveLoadDhtCache = publicNetwork,
            // Unfinished files are named *.!mt, so a half-done torrent never looks like a finished episode.
            UsePartialFiles = true,
            // Group torrents list the host's shelf link as a web seed. Use it soon if friends
            // can't supply the file fast enough (INFERRED tuning, adjust after a real night).
            WebSeedDelay = TimeSpan.FromSeconds(10),
            WebSeedSpeedTrigger = 2 * 1024 * 1024,
        };
        if (!publicNetwork) builder.DhtEndPoint = null;
        if (listenPort > 0)
            builder.ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint>
            {
                ["ipv4"] = new(System.Net.IPAddress.Any, listenPort),
            };
        var settings = builder.ToSettings();
        _engine = new ClientEngine(settings);
        _seedWhileOpen = seedWhileOpen;
    }

    /// <summary>Downloads a public torrent from its magnet link.</summary>
    public async Task DownloadAsync(string magnetUri, string folder, PackItem item, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var magnet = MagnetLink.Parse(magnetUri);
        Directory.CreateDirectory(folder);
        var manager = _engine.Torrents.FirstOrDefault(t => t.InfoHashes.Equals(magnet.InfoHashes))
                      ?? await _engine.AddAsync(magnet, folder, new TorrentSettingsBuilder().ToSettings());
        await RunAsync(manager, folder, item, progress, ct);
    }

    /// <summary>Downloads the host's group torrent (.torrent file contents).</summary>
    public async Task DownloadTorrentAsync(byte[] torrentFile, string folder, PackItem item, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var torrent = Torrent.Load(torrentFile);
        Directory.CreateDirectory(folder);
        var manager = _engine.Torrents.FirstOrDefault(t => t.InfoHashes.Equals(torrent.InfoHashes))
                      ?? await _engine.AddAsync(torrent, folder, new TorrentSettingsBuilder().ToSettings());
        await RunAsync(manager, folder, item, progress, ct);
    }

    private async Task RunAsync(TorrentManager manager, string folder, PackItem item, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var target = FileCheck.TargetPath(folder, item);
        await manager.StartAsync();
        try
        {
            progress?.Report(new DownloadProgress(0, item.Size, 0, "Finding peers"));
            using (var metadataWait = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                metadataWait.CancelAfter(StallTimeout);
                try { await manager.WaitForMetadataAsync(metadataWait.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("Nobody is sharing this torrent right now.");
                }
            }

            var wanted = PickFile(manager.Files, item.FileName);
            foreach (var f in manager.Files)
                if (!ReferenceEquals(f, wanted))
                    await manager.SetFilePriorityAsync(f, Priority.DoNotDownload);

            if (item.Size is { } expected && wanted.Length != expected)
                throw new InvalidDataException($"The torrent's file is {wanted.Length} bytes but movie night expects {expected}. It's a different release.");

            long lastDone = -1;
            var lastChange = DateTime.UtcNow;
            while (!IsFileComplete(wanted))
            {
                ct.ThrowIfCancellationRequested();
                if (manager.State == TorrentState.Error)
                    throw new IOException($"Torrent error: {manager.Error?.Exception?.Message ?? "unknown"}");
                var done = (long)(wanted.Length * (wanted.BitField.PercentComplete / 100.0));
                if (done != lastDone) { lastDone = done; lastChange = DateTime.UtcNow; }
                else if (DateTime.UtcNow - lastChange > StallTimeout)
                    throw new TimeoutException($"The torrent stalled at {done * 100 / Math.Max(1, wanted.Length)}%.");
                progress?.Report(new DownloadProgress(done, wanted.Length, manager.Monitor.DownloadRate,
                    $"Downloading from {manager.OpenConnections} peer(s)"));
                await Task.Delay(500, ct);
            }

            // With partial files on, the engine renames name.!mt to the real name once the last
            // piece checks out; give it a moment.
            for (var i = 0; i < 40 && !File.Exists(wanted.FullPath); i++) await Task.Delay(250, ct);

            // If the file already sits under the right name, leave it so we keep sharing it.
            if (PathsEqual(wanted.FullPath, target))
            {
                if (!_seedWhileOpen) await manager.StopAsync();
            }
            else
            {
                await manager.StopAsync();
                await _engine.RemoveAsync(manager, RemoveMode.CacheDataOnly);
                File.Move(wanted.FullPath, target, overwrite: true);
                TryDeleteEmptyParents(Path.GetDirectoryName(wanted.FullPath)!, folder);
            }
            progress?.Report(new DownloadProgress(wanted.Length, wanted.Length, 0, "Downloaded"));
        }
        catch
        {
            // Keep the partial data for a later retry, but stop using the network for it.
            if (manager.State is not (TorrentState.Stopped or TorrentState.Stopping)) await manager.StopAsync();
            throw;
        }
    }

    /// <summary>The file in the torrent that is this movie night item.</summary>
    public static ITorrentManagerFile PickFile(IList<ITorrentManagerFile> files, string fileName)
    {
        if (files.Count == 1) return files[0];
        return files.FirstOrDefault(f => Path.GetFileName(f.Path).Equals(fileName, StringComparison.OrdinalIgnoreCase))
               ?? files.Where(f => VideoExtensions.Contains(Path.GetExtension(f.Path).ToLowerInvariant()))
                       .OrderByDescending(f => f.Length).FirstOrDefault()
               ?? files.OrderByDescending(f => f.Length).First();
    }

    private static bool IsFileComplete(ITorrentManagerFile f) => f.BitField.AllTrue;

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void TryDeleteEmptyParents(string dir, string stopAt)
    {
        try
        {
            var stop = Path.GetFullPath(stopAt);
            while (Path.GetFullPath(dir).Length > stop.Length && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir)!;
            }
        }
        catch (IOException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _engine.StopAllAsync();
        _engine.Dispose();
    }
}
