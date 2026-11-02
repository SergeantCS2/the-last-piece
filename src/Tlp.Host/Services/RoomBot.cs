using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Tlp.Host.Services;

/// <summary>
/// Keeps a quiet Syncplay connection to the night's server, in a room of its own, so the host can:
/// - see who is in each room and which file they have open (the "who's ready" board);
/// - put the night's episodes in the room's shared playlist, so everyone moves to the next episode
///   together. Syncplay's server keeps that playlist for permanent and persistent rooms.
///
/// It only steps into a movie room while that room is empty: Syncplay pauses everyone in a room
/// when someone leaves it (pauseOnLeave), and the bot leaving counts.
/// </summary>
public sealed class RoomBot(IOptions<TlpOptions> options, NightStore nights, ILogger<RoomBot> log, TimeProvider? clock = null) : BackgroundService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Channel<PushRequest> _requests = Channel.CreateUnbounded<PushRequest>();
    private readonly Dictionary<string, string> _pushed = new(StringComparer.Ordinal);
    private volatile RoomSnapshot _snapshot = RoomSnapshot.Empty;
    private bool _pushedLoaded;

    public RoomSnapshot Snapshot => _snapshot;

    /// <summary>Bot's room. Starts with an underscore so it sorts away from real rooms in Syncplay's list.</summary>
    public string HomeRoom => options.Value.Syncplay.BotRoom;

    private sealed record PushRequest(Night Night, bool Force, TaskCompletionSource<string> Done);

    /// <summary>
    /// Puts the night's files in its room's shared playlist now. Without force it waits for the
    /// room to be empty; with force it goes in anyway (everyone in the room is paused once).
    /// </summary>
    public Task<string> PushNowAsync(Night night, bool force)
    {
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!options.Value.Syncplay.RoomBot) done.SetResult("The room helper is turned off (Tlp:Syncplay:RoomBot).");
        else _requests.Writer.TryWrite(new PushRequest(night, force, done));
        return done.Task;
    }

    /// <summary>
    /// Nights whose playlist the bot should look after now: from 6 hours before the start to 6 hours
    /// after, or the first day after creation for a night with no start time. Per room, the newest wins.
    /// </summary>
    public static IReadOnlyList<Night> ActiveNights(IEnumerable<Night> all, DateTimeOffset now) => all
        .Where(n => n.StartsAt is { } s
            ? now >= s - TimeSpan.FromHours(6) && now <= s + TimeSpan.FromHours(6)
            : now - n.CreatedAt <= TimeSpan.FromDays(1))
        .GroupBy(n => n.Room, StringComparer.Ordinal)
        .Select(g => g.OrderByDescending(n => n.CreatedAt).First())
        .ToList();

    public static IReadOnlyList<string> PlaylistFor(Night night) => night.Items.Select(i => i.FileName).ToList();

    /// <summary>A short fingerprint of a room's playlist, to remember what was already put there.</summary>
    public static string Fingerprint(string room, IReadOnlyList<string> files) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(room + "\n" + string.Join("\n", files))))[..16];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sp = options.Value.Syncplay;
        if (!sp.RoomBot)
        {
            _snapshot = RoomSnapshot.Empty with { Error = "turned off" };
            return;
        }
        LoadPushed();
        var backoff = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var wire = await SyncplayWire.ConnectAsync(sp.BotHost, sp.Port, sp.BotName, HomeRoom, sp.Password, stoppingToken);
                log.LogInformation("Room helper connected to Syncplay at {Host}:{Port} as {Name}", sp.BotHost, sp.Port, wire.Name);
                backoff = TimeSpan.FromSeconds(5);
                await RunConnectedAsync(wire, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _snapshot = _snapshot with { Connected = false, Error = ex.Message };
                log.LogWarning("Room helper can't reach Syncplay at {Host}:{Port}: {Error}", sp.BotHost, sp.Port, ex.Message);
            }
            try { await Task.Delay(backoff, stoppingToken); } catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 60));
        }
    }

    private async Task RunConnectedAsync(SyncplayWire wire, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && wire.Connected)
        {
            var list = await wire.ListAsync(ct);
            _snapshot = RoomSnapshot.From(list, wire.Name, _clock.GetUtcNow());

            // Answer any "do it now" requests, then look after active nights.
            while (_requests.Reader.TryRead(out var req))
                req.Done.TrySetResult(await PushAsync(wire, req.Night, req.Force, ct));
            foreach (var night in ActiveNights(nights.Nights, _clock.GetUtcNow()))
            {
                var key = Fingerprint(night.Room, PlaylistFor(night));
                if (_pushed.GetValueOrDefault(night.Room) == key) continue;
                if (_snapshot.UsersIn(night.Room).Count > 0) continue; // wait until the room is empty
                await PushAsync(wire, night, force: false, ct);
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(options.Value.Syncplay.BotPollInterval);
            try { await _requests.Reader.WaitToReadAsync(wait.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
    }

    private async Task<string> PushAsync(SyncplayWire wire, Night night, bool force, CancellationToken ct)
    {
        var files = PlaylistFor(night);
        var occupied = _snapshot.UsersIn(night.Room).Count;
        if (occupied > 0 && !force)
            return $"{occupied} people are in room {night.Room} now. Try again when it's empty, or force it (pauses them once).";
        try
        {
            var current = await wire.JoinRoomAsync(night.Room, ct);
            string result;
            if (current.SequenceEqual(files, StringComparer.Ordinal))
            {
                result = $"Room {night.Room} already has these {files.Count} episodes.";
            }
            else
            {
                await wire.SetPlaylistAsync(files, ct);
                await wire.SetPlaylistIndexAsync(0, ct);
                result = $"Put {files.Count} episodes in room {night.Room}'s playlist.";
            }
            _pushed[night.Room] = Fingerprint(night.Room, files);
            SavePushed();
            log.LogInformation("{Result}", result);
            return result;
        }
        finally
        {
            try { await wire.JoinRoomAsync(HomeRoom, ct); } catch (Exception ex) when (ex is not OperationCanceledException) { }
        }
    }

    private string PushedPath => Path.Combine(options.Value.DataDir, "room-playlists.json");

    private void LoadPushed()
    {
        if (_pushedLoaded) return;
        _pushedLoaded = true;
        try
        {
            if (File.Exists(PushedPath) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PushedPath)) is { } saved)
                foreach (var (k, v) in saved) _pushed[k] = v;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }

    private void SavePushed()
    {
        try { File.WriteAllText(PushedPath, JsonSerializer.Serialize(_pushed)); }
        catch (IOException) { }
    }
}

/// <summary>Who's where on the Syncplay server, as of the last check.</summary>
public sealed record RoomSnapshot(bool Connected, string? Error, DateTimeOffset? At, IReadOnlyDictionary<string, IReadOnlyList<RoomUser>> Rooms)
{
    public static readonly RoomSnapshot Empty = new(false, null, null, new Dictionary<string, IReadOnlyList<RoomUser>>());

    public IReadOnlyList<RoomUser> UsersIn(string room) => Rooms.TryGetValue(room, out var u) ? u : [];

    /// <summary>Reads Syncplay's List message: { room: { user: { file: { name, duration, size }, isReady, ... } } }.</summary>
    public static RoomSnapshot From(JsonObject list, string botName, DateTimeOffset at)
    {
        var rooms = new Dictionary<string, IReadOnlyList<RoomUser>>(StringComparer.Ordinal);
        foreach (var (room, usersNode) in list)
        {
            if (usersNode is not JsonObject users) continue;
            var people = new List<RoomUser>();
            foreach (var (user, details) in users)
            {
                if (user == botName || string.IsNullOrWhiteSpace(user)) continue; // placeholders for empty rooms
                var file = details?["file"] as JsonObject;
                var name = file?["name"] is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;
                bool? ready = details?["isReady"] is JsonValue r && r.TryGetValue<bool>(out var b) ? b : null;
                people.Add(new RoomUser(user, name, ready));
            }
            if (people.Count > 0) rooms[room] = people.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        return new RoomSnapshot(true, null, at, rooms);
    }
}

public sealed record RoomUser(string Name, string? File, bool? Ready);
