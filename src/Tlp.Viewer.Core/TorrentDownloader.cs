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

    public async Task DownloadAsync(string magnetUri, string folder, PackItem item, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var magnet = MagnetLink.Parse(magnetUri);
        Directory.CreateDirectory(folder);
        var target = FileCheck.TargetPath(folder, item);

        var manager = _engine.Torrents.FirstOrDefault(t => t.InfoHashes.Equals(magnet.InfoHashes))
                      ?? await _engine.AddAsync(magnet, folder, new TorrentSettingsBuilder().ToSettings());
        await manager.StartAsync();

        progress?.Report(new DownloadProgress(0, item.Size, 0, "Finding peers"));
        await manager.WaitForMetadataAsync(ct);

        var wanted = PickFile(manager.Files, item.FileName);
        foreach (var f in manager.Files)
            if (!ReferenceEquals(f, wanted))
                await manager.SetFilePriorityAsync(f, Priority.DoNotDownload);

        if (item.Size is { } expected && wanted.Length != expected)
            throw new InvalidDataException($"The magnet's file is {wanted.Length} bytes but movie night expects {expected}. It's a different release.");

        while (!IsFileComplete(wanted))
        {
            ct.ThrowIfCancellationRequested();
            if (manager.State == TorrentState.Error)
                throw new IOException($"Torrent error: {manager.Error?.Exception?.Message ?? "unknown"}");
            var done = (long)(wanted.Length * (wanted.BitField.PercentComplete / 100.0));
            progress?.Report(new DownloadProgress(done, wanted.Length, manager.Monitor.DownloadRate,
                $"Downloading from {manager.OpenConnections} peer(s)"));
            await Task.Delay(500, ct);
        }

        // The file sits where the torrent put it. If that's already the right place and
        // name, leave it there so we can keep sharing it with the group.
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
