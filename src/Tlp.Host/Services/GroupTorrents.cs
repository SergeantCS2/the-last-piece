using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using MonoTorrent;
using MonoTorrent.BEncoding;
using MonoTorrent.Connections.TrackerServer;
using MonoTorrent.TrackerServer;

namespace Tlp.Host.Services;

/// <summary>
/// Group torrents: a private .torrent for each shelf file used in a session, so friends
/// share pieces with each other instead of all pulling the whole file from the host.
///
/// - The host doesn't run a torrent client. Its shelf download link is the torrent's web
///   seed (BEP 19), so the host's copy is always one "peer" that never leaves.
/// - The host runs the tracker itself, behind the night's secret link, so only people with
///   the link find each other. The torrent is marked private, so clients don't use DHT or
///   peer exchange to leak it to strangers.
/// - The info dictionary (and so the infohash) depends only on the file. The tracker and
///   web seed addresses are added per night when the .torrent is served.
/// </summary>
public sealed class GroupTorrents : BackgroundService
{
    private readonly Shelf _shelf;
    private readonly ILogger<GroupTorrents> _log;
    private readonly string _dir;
    private readonly bool _enabled;
    private readonly ConcurrentDictionary<string, byte[]> _torrents = new();
    private readonly System.Threading.Channels.Channel<string> _queue =
        System.Threading.Channels.Channel.CreateUnbounded<string>();
    private readonly TrackerServer _tracker = new() { AllowUnregisteredTorrents = false, AllowScrape = false };
    private readonly InProcessTrackerListener _listener = new();

    public GroupTorrents(Shelf shelf, IOptions<TlpOptions> options, ILogger<GroupTorrents> log)
    {
        _shelf = shelf;
        _log = log;
        _enabled = options.Value.GroupTorrents;
        _dir = Path.Combine(options.Value.DataDir, "torrents");
        Directory.CreateDirectory(_dir);
        _tracker.AnnounceInterval = TimeSpan.FromMinutes(2);
        _tracker.MinAnnounceInterval = TimeSpan.FromSeconds(30);
        _tracker.RegisterListener(_listener);
        _listener.Start();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.torrent"))
        {
            try
            {
                var bytes = File.ReadAllBytes(file);
                var key = Path.GetFileNameWithoutExtension(file);
                _torrents[key] = bytes;
                Register(bytes);
            }
            catch (Exception ex) when (ex is IOException or TorrentException or BEncodingException)
            {
                _log.LogWarning(ex, "Ignoring unreadable cached torrent {File}", file);
            }
        }
    }

    public bool Enabled => _enabled;

    /// <summary>How many shelf files have a group torrent ready.</summary>
    public int Count => _torrents.Count;

    /// <summary>The bare .torrent (no tracker, no web seed) if made; otherwise queues it and returns null.</summary>
    public byte[]? Get(string shelfPath)
    {
        if (!_enabled) return null;
        var key = Key(shelfPath);
        if (key is null) return null;
        if (_torrents.TryGetValue(key, out var bytes)) return bytes;
        _queue.Writer.TryWrite(shelfPath);
        return null;
    }

    /// <summary>The .torrent for one night: our tracker behind the night's link, and the shelf link as web seed.</summary>
    public static byte[] ForNight(byte[] bare, string announceUrl, string webSeedUrl)
    {
        var dict = BEncodedDictionary.Decode<BEncodedDictionary>(bare);
        dict["announce"] = new BEncodedString(announceUrl);
        dict["url-list"] = new BEncodedList { new BEncodedString(webSeedUrl) };
        dict["created by"] = new BEncodedString(Tlp.Core.AppInfo.Name);
        return dict.Encode();
    }

    /// <summary>Answers a tracker announce. Returns a bencoded response.</summary>
    public byte[] Announce(string rawQuery, IPAddress client) => _listener.Announce(rawQuery, client).Encode();

    public static byte[] Failure(string reason) =>
        new BEncodedDictionary { ["failure reason"] = new BEncodedString(reason) }.Encode();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var shelfPath in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var key = Key(shelfPath);
            var full = _shelf.Resolve(shelfPath);
            if (key is null || full is null || _torrents.ContainsKey(key)) continue;
            try
            {
                var bytes = await CreateAsync(full, stoppingToken);
                await File.WriteAllBytesAsync(Path.Combine(_dir, key + ".torrent"), bytes, stoppingToken);
                _torrents[key] = bytes;
                Register(bytes);
                _log.LogInformation("Made group torrent for {Path}", shelfPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogWarning(ex, "Could not make a torrent for {Path}", shelfPath);
            }
        }
    }

    /// <summary>A private, v1 single-file torrent with no tracker; the tracker is added per night.</summary>
    public static async Task<byte[]> CreateAsync(string fullPath, CancellationToken ct = default)
    {
        var creator = new TorrentCreator(TorrentType.V1Only);
        var dict = await creator.CreateAsync(new TorrentFileSource(fullPath), ct);
        var info = (BEncodedDictionary)dict["info"];
        info["private"] = new BEncodedNumber(1);
        var bare = new BEncodedDictionary { ["info"] = info };
        return bare.Encode();
    }

    private void Register(byte[] bytes)
    {
        var torrent = Torrent.Load(bytes);
        var trackable = new InfoHashTrackable(torrent.Name, torrent.InfoHashes.V1OrV2);
        if (!_tracker.Contains(trackable)) _tracker.Add(trackable);
    }

    // Key from the file's identity, so a replaced file gets a new torrent. Safe as a file name.
    private string? Key(string shelfPath)
    {
        var full = _shelf.Resolve(shelfPath);
        if (full is null) return null;
        var info = new FileInfo(full);
        var id = $"{shelfPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..32];
    }

    public override void Dispose()
    {
        _tracker.Dispose();
        base.Dispose();
    }

    /// <summary>Feeds announces that arrive on our own web endpoint into MonoTorrent's tracker.</summary>
    private sealed class InProcessTrackerListener : TrackerListener
    {
        public BEncodedDictionary Announce(string rawQuery, IPAddress client) => Handle(rawQuery, client, false);
        protected override void StartCore(CancellationToken token) { }
    }
}
