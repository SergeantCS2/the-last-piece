using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Tlp.Core;

namespace Tlp.Host.Services;

/// <summary>
/// SHA-256 of shelf files, so viewers can prove they have exactly the host's file.
/// Hashes are computed in the background (a 1.5 GB episode takes a few seconds) and
/// cached on disk keyed by path, size and modified time.
/// </summary>
public sealed class HashCache : BackgroundService
{
    private readonly Shelf _shelf;
    private readonly ILogger<HashCache> _log;
    private readonly string _path;
    private readonly ConcurrentDictionary<string, string> _hashes;
    private readonly System.Threading.Channels.Channel<string> _queue =
        System.Threading.Channels.Channel.CreateUnbounded<string>();

    public HashCache(Shelf shelf, IOptions<TlpOptions> options, ILogger<HashCache> log)
    {
        _shelf = shelf;
        _log = log;
        Directory.CreateDirectory(options.Value.DataDir);
        _path = Path.Combine(options.Value.DataDir, "hashes.json");
        var saved = File.Exists(_path) ? PackJson.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) : null;
        _hashes = new ConcurrentDictionary<string, string>(saved ?? new());
    }

    /// <summary>The hash if known; otherwise queues the file and returns null.</summary>
    public string? Get(string shelfPath)
    {
        var key = Key(shelfPath);
        if (key is null) return null;
        if (_hashes.TryGetValue(key, out var hash)) return hash;
        _queue.Writer.TryWrite(shelfPath);
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var shelfPath in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var key = Key(shelfPath);
            var full = _shelf.Resolve(shelfPath);
            if (key is null || full is null || _hashes.ContainsKey(key)) continue;
            try
            {
                await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, stoppingToken));
                _hashes[key] = hash;
                await File.WriteAllTextAsync(_path, PackJson.Serialize(_hashes), stoppingToken);
                _log.LogInformation("Hashed {Path}", shelfPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "Could not hash {Path}", shelfPath);
            }
        }
    }

    private string? Key(string shelfPath)
    {
        var full = _shelf.Resolve(shelfPath);
        if (full is null) return null;
        var info = new FileInfo(full);
        return $"{shelfPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
    }
}
