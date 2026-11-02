using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.Client;
using Tlp.Core;

namespace Tlp.Host.Services;

/// <summary>One "get it to my server" download, as saved in DataDir/fetches.json.</summary>
public sealed record FetchRecord
{
    public required string Id { get; init; }
    public required string Magnet { get; init; }
    public string? Name { get; init; }
    public DateTimeOffset Added { get; init; }
    /// <summary>queued, finding, downloading, moving, done, failed.</summary>
    public string State { get; init; } = "queued";
    public string? Error { get; init; }
    /// <summary>Where the files landed on the shelf, once done.</summary>
    public IReadOnlyList<string> ShelfPaths { get; init; } = [];
}

public sealed record FetchView(string Id, string? Name, string State, double Percent, long? Size, long Rate, int Peers, string? Error, IReadOnlyList<string> ShelfPaths);

/// <summary>
/// Downloads a public torrent onto the host's own shelf, so friends then get it from the host:
/// a direct download plus the host's private group torrent, instead of the public swarm.
/// Files download into DataDir/fetching and move onto the shelf only when complete, so a half
/// file never shows up on the shelf. Nights that list the same magnet switch to the shelf copy
/// by themselves. The public torrent is stopped once done (no seeding back to the swarm).
/// </summary>
public sealed class ShelfFetcher : BackgroundService
{
    private readonly TlpOptions _opts;
    private readonly Shelf _shelf;
    private readonly NightStore _nights;
    private readonly HashCache _hashes;
    private readonly GroupTorrents _torrents;
    private readonly ILogger<ShelfFetcher> _log;
    private readonly string _workDir;
    private readonly string _recordsPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly ConcurrentDictionary<string, FetchRecord> _records = new();
    private readonly ConcurrentDictionary<string, TorrentManager> _managers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancels = new();
    private readonly System.Threading.Channels.Channel<string> _queue = System.Threading.Channels.Channel.CreateUnbounded<string>();
    private ClientEngine? _engine;

    /// <summary>Give up when the swarm sends nothing for this long.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromMinutes(20);

    public ShelfFetcher(IOptions<TlpOptions> options, Shelf shelf, NightStore nights, HashCache hashes, GroupTorrents torrents, ILogger<ShelfFetcher> log)
    {
        _opts = options.Value;
        _shelf = shelf;
        _nights = nights;
        _hashes = hashes;
        _torrents = torrents;
        _log = log;
        _workDir = Path.Combine(_opts.DataDir, "fetching");
        _recordsPath = Path.Combine(_opts.DataDir, "fetches.json");
        if (File.Exists(_recordsPath))
        {
            try
            {
                foreach (var r in PackJson.Deserialize<List<FetchRecord>>(File.ReadAllText(_recordsPath)) ?? [])
                    _records[r.Id] = r;
            }
            catch (System.Text.Json.JsonException ex) { _log.LogWarning(ex, "Ignoring unreadable {Path}", _recordsPath); }
        }
    }

    /// <summary>Starts getting a magnet onto the shelf. Returns the existing entry if it's already there or on its way.</summary>
    public async Task<FetchRecord> AddAsync(string magnetUri)
    {
        if (!_opts.FetchEnabled) throw new InvalidOperationException("Downloading to the server is turned off (Tlp__FetchEnabled).");
        if (!MagnetInfo.TryParse(magnetUri, out var info) || info is null) throw new ArgumentException("That magnet link isn't valid.");
        var id = HashOf(info.Uri) ?? throw new ArgumentException("That magnet link isn't valid.");
        if (_records.TryGetValue(id, out var existing))
        {
            if (existing.State == "done" && existing.ShelfPaths.Count > 0 && existing.ShelfPaths.All(p => _shelf.Resolve(p) is not null))
            {
                // Already on the shelf: just point any new nights at it.
                await LinkNightsAsync(existing.Magnet, existing.ShelfPaths);
                return existing;
            }
            if (existing.State is not ("done" or "failed")) return existing;
        }
        var record = new FetchRecord { Id = id, Magnet = info.Uri, Name = info.DisplayName, Added = DateTimeOffset.UtcNow };
        _records[id] = record;
        await SaveAsync();
        _queue.Writer.TryWrite(id);
        return record;
    }

    /// <summary>Stops a download and deletes its partial data. Files already moved to the shelf stay.</summary>
    public async Task<bool> RemoveAsync(string id)
    {
        if (!_records.TryRemove(id, out _)) return false;
        if (_cancels.TryRemove(id, out var cts)) cts.Cancel();
        if (_managers.TryRemove(id, out var manager) && _engine is not null)
        {
            try
            {
                if (manager.State is not (TorrentState.Stopped or TorrentState.Stopping)) await manager.StopAsync();
                await _engine.RemoveAsync(manager, RemoveMode.CacheDataAndDownloadedData);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException) { _log.LogWarning(ex, "Couldn't fully remove fetch {Id}", id); }
        }
        TryDeleteDir(Path.Combine(_workDir, id));
        await SaveAsync();
        return true;
    }

    public IReadOnlyList<FetchView> List() => _records.Values.OrderByDescending(r => r.Added).Select(r =>
    {
        _managers.TryGetValue(r.Id, out var m);
        var size = m?.Torrent is { } t ? t.Size : (long?)null;
        var percent = r.State == "done" ? 100 : m?.Progress ?? 0;
        return new FetchView(r.Id, m?.Torrent?.Name ?? r.Name, r.State, Math.Round(percent, 1), size,
            m?.Monitor.DownloadRate ?? 0, m?.OpenConnections ?? 0, r.Error, r.ShelfPaths);
    }).ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.FetchEnabled) return;
        Directory.CreateDirectory(_workDir);
        var settings = new EngineSettingsBuilder
        {
            CacheDirectory = Path.Combine(_opts.DataDir, "fetch-cache"),
            AllowPortForwarding = _opts.FetchPublicNetwork,
            AllowLocalPeerDiscovery = _opts.FetchPublicNetwork,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            AutoSaveLoadDhtCache = true,
            UsePartialFiles = true,
        };
        if (!_opts.FetchPublicNetwork) settings.DhtEndPoint = null;
        if (_opts.FetchPort > 0)
            settings.ListenEndPoints = new Dictionary<string, System.Net.IPEndPoint> { ["ipv4"] = new(System.Net.IPAddress.Any, _opts.FetchPort) };
        _engine = new ClientEngine(settings.ToSettings());

        // Pick up where a restart left off.
        foreach (var r in _records.Values.Where(r => r.State is not ("done" or "failed")))
            _queue.Writer.TryWrite(r.Id);

        var running = new List<Task>();
        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            running.RemoveAll(t => t.IsCompleted);
            running.Add(RunOneAsync(id, stoppingToken));
        }
        await Task.WhenAll(running);
    }

    private async Task RunOneAsync(string id, CancellationToken stoppingToken)
    {
        if (!_records.TryGetValue(id, out var record) || _engine is null) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _cancels[id] = cts;
        var ct = cts.Token;
        try
        {
            var folder = Path.Combine(_workDir, id);
            Directory.CreateDirectory(folder);
            var manager = _engine.Torrents.FirstOrDefault(t => t.InfoHashes.V1OrV2.ToHex().Equals(id, StringComparison.OrdinalIgnoreCase))
                          ?? await _engine.AddAsync(MagnetLink.Parse(record.Magnet), folder, new TorrentSettingsBuilder().ToSettings());
            _managers[id] = manager;
            await SetAsync(id, r => r with { State = "finding", Error = null });
            await manager.StartAsync();
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                wait.CancelAfter(StallTimeout);
                try { await manager.WaitForMetadataAsync(wait.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException("Nobody is sharing this torrent right now.");
                }
            }

            var size = manager.Torrent!.Size;
            if (_shelf.Disk() is { } disk && disk.Free < size + (1L << 30))
                throw new IOException($"Not enough space: it needs {size / 1e9:0.0} GB and the shelf's disk has {disk.Free / 1e9:0.0} GB free.");
            await SetAsync(id, r => r with { State = "downloading", Name = manager.Torrent.Name });

            double last = -1;
            var lastChange = DateTime.UtcNow;
            while (!manager.Complete)
            {
                ct.ThrowIfCancellationRequested();
                if (manager.State == TorrentState.Error)
                    throw new IOException($"Torrent error: {manager.Error?.Exception?.Message ?? "unknown"}");
                if (manager.Progress != last) { last = manager.Progress; lastChange = DateTime.UtcNow; }
                else if (DateTime.UtcNow - lastChange > StallTimeout)
                    throw new TimeoutException($"Stalled at {manager.Progress:0}%: nobody sent anything for {StallTimeout.TotalMinutes:0} minutes.");
                await Task.Delay(1000, ct);
            }

            await SetAsync(id, r => r with { State = "moving" });
            await manager.StopAsync();
            // Partial files get their real names once complete; give the engine a moment.
            for (var i = 0; i < 40 && manager.Files.Any(f => !File.Exists(f.DownloadCompleteFullPath)); i++) await Task.Delay(250, ct);
            var moved = MoveToShelf(manager, folder);
            await _engine.RemoveAsync(manager, RemoveMode.CacheDataOnly);
            _managers.TryRemove(id, out _);
            TryDeleteDir(folder);
            await SetAsync(id, r => r with { State = "done", ShelfPaths = moved });
            _log.LogInformation("Fetched {Name} to the shelf: {Files}", manager.Torrent.Name, string.Join(", ", moved));

            await LinkNightsAsync(record.Magnet, moved);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Removed by the admin, or the host is stopping (it resumes next start).
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Fetch {Id} failed", id);
            if (_managers.TryGetValue(id, out var m) && m.State is not (TorrentState.Stopped or TorrentState.Stopping))
            {
                try { await m.StopAsync(); } catch (InvalidOperationException) { }
            }
            await SetAsync(id, r => r with { State = "failed", Error = ex.Message });
        }
        finally
        {
            _cancels.TryRemove(id, out _);
        }
    }

    /// <summary>Moves a finished torrent's files onto the shelf, keeping its folder. Returns shelf-relative paths.</summary>
    private List<string> MoveToShelf(TorrentManager manager, string folder)
    {
        var moved = new List<string>();
        foreach (var file in manager.Files)
        {
            if (!File.Exists(file.DownloadCompleteFullPath)) continue;
            var relative = Path.GetRelativePath(folder, file.DownloadCompleteFullPath).Replace('\\', '/');
            if (relative.StartsWith("..", StringComparison.Ordinal)) continue;
            var target = ShelfTarget(relative, file.Length);
            if (target is null) { moved.Add(relative); continue; } // the same file is already there
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_shelf.Root, target))!);
            File.Move(file.DownloadCompleteFullPath, Path.Combine(_shelf.Root, target));
            moved.Add(target);
        }
        return moved;
    }

    /// <summary>A free shelf path for this file, or null if an identical-size file already sits there.</summary>
    private string? ShelfTarget(string relative, long length)
    {
        var full = Path.Combine(_shelf.Root, relative);
        if (!File.Exists(full)) return relative;
        if (new FileInfo(full).Length == length) return null;
        var dir = Path.GetDirectoryName(relative) ?? "";
        var stem = Path.GetFileNameWithoutExtension(relative);
        var ext = Path.GetExtension(relative);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}").Replace('\\', '/');
            if (!File.Exists(Path.Combine(_shelf.Root, candidate))) return candidate;
        }
    }

    /// <summary>Nights that list this magnet now get the shelf copy too (direct download and group torrent).</summary>
    private async Task LinkNightsAsync(string magnet, IReadOnlyList<string> shelfPaths)
    {
        var hash = HashOf(magnet);
        if (hash is null) return;
        var linked = await _nights.UpdateItemsAsync(item =>
        {
            if (item.ShelfPath is not null || item.Magnet is null) return null;
            if (HashOf(item.Magnet) != hash) return null;
            var match = PickShelfFile(shelfPaths, item.FileName);
            return match is null ? null : item with { ShelfPath = match };
        });
        foreach (var path in linked)
        {
            _hashes.Get(path);
            _torrents.Get(path);
        }
    }

    /// <summary>A magnet's info hash as lowercase hex, or null if MonoTorrent can't read it.</summary>
    public static string? HashOf(string magnet)
    {
        try { return MagnetLink.Parse(magnet.Trim()).InfoHashes.V1OrV2.ToHex().ToLowerInvariant(); }
        catch (Exception ex) when (ex is FormatException or ArgumentException or UriFormatException or NotSupportedException) { return null; }
    }

    /// <summary>The fetched file that is this night item: same name, or the only video if names differ.</summary>
    public static string? PickShelfFile(IReadOnlyList<string> shelfPaths, string fileName)
    {
        var byName = shelfPaths.FirstOrDefault(p => Path.GetFileName(p).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;
        string[] video = [".mkv", ".mp4", ".avi", ".webm", ".m4v", ".mov"];
        var videos = shelfPaths.Where(p => video.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).ToList();
        return videos.Count == 1 ? videos[0] : null;
    }

    private async Task SetAsync(string id, Func<FetchRecord, FetchRecord> change)
    {
        if (!_records.TryGetValue(id, out var r)) return;
        _records[id] = change(r);
        await SaveAsync();
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var tmp = _recordsPath + ".tmp";
            await File.WriteAllTextAsync(tmp, PackJson.Serialize(_records.Values.OrderBy(r => r.Added).ToList()));
            File.Move(tmp, _recordsPath, overwrite: true);
        }
        finally { _saveLock.Release(); }
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public override void Dispose()
    {
        _engine?.Dispose();
        base.Dispose();
    }
}
