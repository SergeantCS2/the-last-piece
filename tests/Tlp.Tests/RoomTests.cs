using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tlp.Core;
using Tlp.Host;
using Tlp.Host.Services;

namespace Tlp.Tests;

/// <summary>
/// Just enough of a Syncplay 1.7.6 server for the room helper: Hello (with the room's playlist sent
/// before the Hello reply, as the real server does), room switches, List, playlist changes, and a
/// State ping every 200 ms that drops clients who stop answering. Checked against the real server
/// by hand and by the server CI workflow.
/// </summary>
internal sealed class FakeSyncplay : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    public readonly Dictionary<string, List<string>> Playlists = new();
    public readonly Dictionary<string, (string Room, string? File)> Users = new();
    public readonly List<string> Log = [];
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public FakeSyncplay()
    {
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    /// <summary>Puts a pretend viewer in a room (no connection needed).</summary>
    public void AddPerson(string name, string room, string? file)
    {
        lock (_gate) Users[name] = (room, file);
    }

    private async Task AcceptAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _listener.AcceptTcpClientAsync(_stop.Token); } catch { return; }
            _ = Task.Run(() => ServeAsync(c));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var reader = new StreamReader(stream);
        var writeLock = new SemaphoreSlim(1, 1);
        string? me = null;
        var lastAnswer = DateTime.UtcNow;
        async Task Send(JsonObject m)
        {
            var b = Encoding.UTF8.GetBytes(m.ToJsonString() + "\r\n");
            await writeLock.WaitAsync();
            try { await stream.WriteAsync(b); } finally { writeLock.Release(); }
        }
        async Task SendPlaylist(string room)
        {
            List<string> files;
            lock (_gate) files = Playlists.GetValueOrDefault(room) ?? [];
            await Send(new JsonObject { ["Set"] = new JsonObject { ["playlistChange"] = new JsonObject { ["user"] = null, ["files"] = new JsonArray(files.Select(f => (JsonNode?)f).ToArray()) } } });
            await Send(new JsonObject { ["Set"] = new JsonObject { ["playlistIndex"] = new JsonObject { ["user"] = null, ["index"] = 0 } } });
        }
        var pinger = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested && client.Connected)
            {
                await Task.Delay(200);
                if (me is null) continue;
                if (DateTime.UtcNow - lastAnswer > TimeSpan.FromSeconds(2)) { client.Close(); return; }
                try { await Send(new JsonObject { ["State"] = new JsonObject { ["ping"] = new JsonObject { ["latencyCalculation"] = 1.5 }, ["ignoringOnTheFly"] = new JsonObject { ["server"] = 1 } } }); }
                catch { return; }
            }
        });
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                var msg = JsonNode.Parse(line)!.AsObject();
                if (msg["Hello"] is JsonObject hello)
                {
                    me = hello["username"]!.GetValue<string>();
                    var room = hello["room"]!["name"]!.GetValue<string>();
                    lock (_gate) { Users[me] = (room, null); Log.Add($"{me} joined {room}"); }
                    lastAnswer = DateTime.UtcNow;
                    await SendPlaylist(room);
                    await Send(new JsonObject { ["Hello"] = new JsonObject { ["username"] = me, ["room"] = new JsonObject { ["name"] = room } } });
                }
                else if (msg["State"] is JsonObject state)
                {
                    if (state["playstate"] is not null) lock (_gate) Log.Add($"{me} sent a play state");
                    if (state["ignoringOnTheFly"]?["server"] is not null) lastAnswer = DateTime.UtcNow;
                }
                else if (msg.ContainsKey("List"))
                {
                    var list = new JsonObject();
                    lock (_gate)
                        foreach (var (name, (room, file)) in Users)
                        {
                            if (list[room] is not JsonObject r) list[room] = r = new JsonObject();
                            r[name] = new JsonObject { ["file"] = file is null ? new JsonObject() : new JsonObject { ["name"] = file } };
                        }
                    await Send(new JsonObject { ["List"] = list });
                }
                else if (msg["Set"] is JsonObject set)
                {
                    if (set["room"]?["name"]?.GetValue<string>() is { } room)
                    {
                        lock (_gate) { Users[me!] = (room, null); Log.Add($"{me} moved to {room}"); }
                        await SendPlaylist(room);
                    }
                    if (set["playlistChange"]?["files"] is JsonArray files)
                        lock (_gate) { Playlists[Users[me!].Room] = files.Select(f => f!.GetValue<string>()).ToList(); Log.Add($"{me} set playlist of {Users[me!].Room}"); }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        finally
        {
            if (me is not null) lock (_gate) Users.Remove(me);
        }
        await pinger;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await Task.Delay(50);
    }
}

public class RoomTests
{
    private static Night MakeNight(string room = "movienight", DateTimeOffset? startsAt = null, DateTimeOffset? created = null, params string[] files) => new()
    {
        Id = Guid.NewGuid().ToString("n")[..12],
        Token = "tok",
        Title = "T",
        Room = room,
        StartsAt = startsAt,
        CreatedAt = created ?? DateTimeOffset.UtcNow,
        Items = (files.Length == 0 ? ["a - 01.mkv", "a - 02.mkv"] : files).Select(f => new NightItem { FileName = f, ShelfPath = f }).ToList(),
    };

    [Fact]
    public void Active_nights_are_six_hours_either_side_of_the_start_and_the_newest_wins_per_room()
    {
        var now = DateTimeOffset.Parse("2026-11-07T20:00:00Z");
        var tonight = MakeNight(startsAt: now.AddHours(2), created: now.AddDays(-2));
        var lastWeek = MakeNight(startsAt: now.AddDays(-7), created: now.AddDays(-9));
        var unscheduledOld = MakeNight(room: "other", created: now.AddDays(-3));
        var unscheduledNew = MakeNight(room: "other2", created: now.AddHours(-1));
        var newerSameRoom = MakeNight(startsAt: now.AddHours(1), created: now.AddDays(-1));

        var active = RoomBot.ActiveNights([tonight, lastWeek, unscheduledOld, unscheduledNew], now);
        Assert.Equal([tonight.Id, unscheduledNew.Id], active.Select(n => n.Id).OrderBy(i => i == tonight.Id ? 0 : 1));
        Assert.Equal(newerSameRoom.Id, Assert.Single(RoomBot.ActiveNights([tonight, newerSameRoom], now)).Id);
        Assert.Empty(RoomBot.ActiveNights([tonight], now.AddHours(-5)));
    }

    [Fact]
    public void Room_snapshot_skips_the_helper_and_the_placeholders_for_empty_rooms()
    {
        var list = JsonNode.Parse("""
            { "movienight": { "alice": { "file": { "name": "a - 01.mkv" }, "isReady": true }, "TheLastPiece": { "file": {} }, " ": { "file": {} } },
              "lobby": { "  ": { "file": {} } } }
            """)!.AsObject();
        var snap = RoomSnapshot.From(list, "TheLastPiece", DateTimeOffset.UtcNow);
        var alice = Assert.Single(snap.UsersIn("movienight"));
        Assert.Equal(("alice", "a - 01.mkv", (bool?)true), (alice.Name, alice.File, alice.Ready));
        Assert.Empty(snap.UsersIn("lobby"));
    }

    [Fact]
    public void Ready_board_keeps_only_the_nights_files_and_a_clean_name()
    {
        var board = new ReadyBoard();
        var night = MakeNight();
        Assert.NotNull(board.Record(night, new ViewerReport { Name = "  \t ", Files = [] }));
        Assert.Null(board.Record(night, new ViewerReport
        {
            Name = "  Bob\u0007 the Builder of Very Long Names Indeed  ",
            App = "0.3.0",
            Files =
            [
                new ViewerFileReport { FileName = "a - 01.mkv", State = ViewerFileState.Ready, Percent = 250 },
                new ViewerFileReport { FileName = "../../evil.exe", State = ViewerFileState.Ready },
            ],
        }));
        var e = Assert.Single(board.For(night.Id));
        Assert.True(e.Report.Name.Length <= ViewerReport.MaxNameLength);
        Assert.StartsWith("Bob the Builder", e.Report.Name);
        var f = Assert.Single(e.Report.Files);
        Assert.Equal(100, f.Percent);

        // Same person reporting again replaces their line, whatever the case of the name.
        board.Record(night, new ViewerReport { Name = e.Report.Name.ToUpperInvariant(), Files = [] });
        Assert.Single(board.For(night.Id));
    }

    [Fact]
    public void Night_status_joins_reports_and_room_by_name()
    {
        var night = MakeNight();
        var now = DateTimeOffset.UtcNow;
        var board = new ReadyBoard();
        board.Record(night, new ViewerReport { Name = "Alice", Files = [new ViewerFileReport { FileName = "a - 01.mkv", State = ViewerFileState.Ready, Percent = 100 }] });
        var room = new RoomSnapshot(true, null, now, new Dictionary<string, IReadOnlyList<RoomUser>>
        {
            ["movienight"] = [new RoomUser("alice", "a - 01.mkv", null), new RoomUser("carol", null, null)],
        });
        var status = NightStatus.Build(night, board.For(night.Id), room, now);
        Assert.Equal(["Alice", "carol"], status.People.Select(p => p.Name));
        var alice = status.People[0];
        Assert.True(alice.InRoom);
        Assert.Equal(1, alice.ReadyCount);
        Assert.Equal(2, alice.Total);
        Assert.Equal(ViewerFileState.Missing, alice.Files[1].State);
        Assert.Equal("a - 01.mkv", alice.Playing);
        Assert.Null(status.People[1].ReadyCount); // own Syncplay, no report
    }

    private static (RoomBot Bot, NightStore Store, string Data) MakeBot(int port)
    {
        var data = Directory.CreateTempSubdirectory("tlp-bot-").FullName;
        var options = Options.Create(new TlpOptions
        {
            DataDir = data,
            Syncplay = new SyncplayOptions { BotHost = "127.0.0.1", Port = port, BotPollInterval = TimeSpan.FromMilliseconds(300) },
        });
        var store = new NightStore(options);
        return (new RoomBot(options, store, NullLogger<RoomBot>.Instance), store, data);
    }

    private static async Task Eventually(Func<bool> check, string what)
    {
        for (var i = 0; i < 100 && !check(); i++) await Task.Delay(100);
        Assert.True(check(), what);
    }

    [Fact]
    public async Task Room_helper_fills_an_empty_rooms_playlist_and_stays_connected_quietly()
    {
        await using var server = new FakeSyncplay();
        var (bot, store, _) = MakeBot(server.Port);
        await store.AddAsync("Tonight", null, "movienight", [new NightItem { FileName = "ep1.mkv", ShelfPath = "ep1.mkv" }, new NightItem { FileName = "ep2.mkv", ShelfPath = "ep2.mkv" }]);
        await bot.StartAsync(CancellationToken.None);
        try
        {
            await Eventually(() => { lock (server.Log) return server.Playlists.GetValueOrDefault("movienight") is { Count: 2 }; }, "playlist set");
            Assert.Equal(["ep1.mkv", "ep2.mkv"], server.Playlists["movienight"]);
            // Back in its own room afterwards, and still connected after several ping timeouts' worth of time.
            await Task.Delay(3000);
            lock (server.Log)
            {
                Assert.Equal("_thelastpiece", server.Users["TheLastPiece"].Room);
                Assert.DoesNotContain(server.Log, l => l.Contains("play state"));
            }
            Assert.True(bot.Snapshot.Connected);
        }
        finally
        {
            await bot.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Room_helper_waits_while_people_are_in_the_room_unless_forced()
    {
        await using var server = new FakeSyncplay();
        server.AddPerson("alice", "movienight", "old.mkv");
        var (bot, store, _) = MakeBot(server.Port);
        var night = await store.AddAsync("Tonight", null, "movienight", [new NightItem { FileName = "ep1.mkv", ShelfPath = "ep1.mkv" }]);
        await bot.StartAsync(CancellationToken.None);
        try
        {
            await Eventually(() => bot.Snapshot.UsersIn("movienight").Count == 1, "sees alice");
            await Task.Delay(1000);
            lock (server.Log) Assert.False(server.Playlists.ContainsKey("movienight"));

            var polite = await bot.PushNowAsync(night, force: false);
            Assert.Contains("in room movienight", polite);
            var forced = await bot.PushNowAsync(night, force: true);
            Assert.Contains("Put 1 episodes", forced);
            lock (server.Log) Assert.Equal(["ep1.mkv"], server.Playlists["movienight"]);
        }
        finally
        {
            await bot.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Viewer_status_endpoint_feeds_the_admin_status()
    {
        using var fx = new HostFixture();
        var admin = fx.Admin();
        var created = await admin.PostAsJsonAsync("/api/admin/nights", new { title = "N", items = new[] { new { shelfPath = "Season 1/[Grp] Show - 01.mkv" } } });
        created.EnsureSuccessStatusCode();
        var night = await created.Content.ReadFromJsonAsync<JsonObject>();
        var id = night!["id"]!.GetValue<string>();
        var pack = await fx.Factory.CreateClient().GetStringAsync(new Uri(night["shareUrl"]!.GetValue<string>()).AbsolutePath + "/pack.json");
        var statusUri = new Uri(PackJson.DeserializePack(pack)!.StatusUri!);

        var anon = fx.Factory.CreateClient();
        var report = PackJson.SerializeReport(new ViewerReport { Name = "dave", Files = [new ViewerFileReport { FileName = "[Grp] Show - 01.mkv", State = ViewerFileState.Downloading, Percent = 42 }] });
        var posted = await anon.PostAsync(statusUri.AbsolutePath, new StringContent(report, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NoContent, posted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.PostAsync("/n/wrong-token/status", new StringContent(report))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsync(statusUri.AbsolutePath, new StringContent("{nope"))).StatusCode);

        var status = await admin.GetFromJsonAsync<JsonObject>($"/api/admin/nights/{id}/status");
        var dave = status!["people"]!.AsArray().Single()!;
        Assert.Equal("dave", dave["name"]!.GetValue<string>());
        Assert.Equal(42, dave["files"]![0]!["percent"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/admin/nights/{id}/status")).StatusCode);
    }
}
