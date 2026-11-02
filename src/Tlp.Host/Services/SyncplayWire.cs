using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Tlp.Host.Services;

/// <summary>
/// A minimal Syncplay client connection: newline-delimited JSON over TCP, as Syncplay 1.7.x
/// speaks it (checked against syncplay/protocols.py in 1.7.6).
///
/// It never sends a play state, only answers the server's pings. That matters because:
/// - the server drops a client that hasn't answered for 12.5 s (PROTOCOL_TIMEOUT);
/// - a client with no file open is left out of the "slowest viewer" position check and of the
///   readiness count (Watcher.__lt__ and isReadyWithFile in 1.7.6), so it can't hold anyone up.
/// </summary>
public sealed class SyncplayWire : IAsyncDisposable
{
    public const string ProtocolVersion = "1.7.6";

    private readonly TcpClient _tcp;
    private readonly StreamReader _reader;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Channel<JsonObject> _inbox = Channel.CreateBounded<JsonObject>(new BoundedChannelOptions(512) { FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource _stop = new();
    private Task? _readLoop;

    /// <summary>The name the server gave us (it adds underscores if the name was taken).</summary>
    public string Name { get; private set; } = "";
    public bool Connected => _readLoop is { IsCompleted: false };

    private SyncplayWire(TcpClient tcp)
    {
        _tcp = tcp;
        _stream = tcp.GetStream();
        _reader = new StreamReader(_stream, new UTF8Encoding(false));
    }

    public static async Task<SyncplayWire> ConnectAsync(string host, int port, string name, string room, string? password, CancellationToken ct)
    {
        var tcp = new TcpClient();
        try
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await tcp.ConnectAsync(host, port, timeout.Token);
            }
            var wire = new SyncplayWire(tcp);
            var hello = new JsonObject
            {
                ["username"] = name,
                ["room"] = new JsonObject { ["name"] = room },
                ["version"] = "1.2.255",
                ["realversion"] = ProtocolVersion,
                ["features"] = new JsonObject
                {
                    ["sharedPlaylists"] = true,
                    ["chat"] = true,
                    ["readiness"] = true,
                    ["managedRooms"] = true,
                    // Console mode: the server then leaves out the placeholder users (named with
                    // spaces) it adds to empty persistent rooms for the GUI's room list.
                    ["uiMode"] = "CLI",
                },
            };
            // Syncplay clients send the MD5 hex of the password, not the password itself.
            if (!string.IsNullOrEmpty(password))
                hello["password"] = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(password)));
            wire._readLoop = Task.Run(() => wire.ReadLoopAsync(wire._stop.Token));
            await wire.SendAsync(new JsonObject { ["Hello"] = hello }, ct);
            var reply = await wire.WaitForAsync("Hello", null, TimeSpan.FromSeconds(10), ct);
            wire.Name = reply["username"]?.GetValue<string>() ?? name;
            return wire;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public async Task SendAsync(JsonObject message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\r\n");
        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(bytes, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Waits for the next message with this key (and matching the filter), skipping others.</summary>
    public async Task<JsonNode> WaitForAsync(string key, Func<JsonNode, bool>? match, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var message = await _inbox.Reader.ReadAsync(cts.Token);
                if (message["Error"] is { } error)
                    throw new SyncplayException(error["message"]?.GetValue<string>() ?? "Syncplay server error");
                if (message[key] is { } body && (match is null || match(body))) return body;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Syncplay server didn't send {key} in time.");
        }
        catch (ChannelClosedException)
        {
            throw new SyncplayException("Syncplay server closed the connection.");
        }
    }

    /// <summary>Everyone on the server, by room: room name to (user name to details).</summary>
    public async Task<JsonObject> ListAsync(CancellationToken ct)
    {
        Drain();
        await SendAsync(new JsonObject { ["List"] = null }, ct);
        return (await WaitForAsync("List", n => n is JsonObject, TimeSpan.FromSeconds(10), ct)).AsObject();
    }

    /// <summary>Moves to a room. Returns that room's shared playlist, which the server sends on arrival.</summary>
    public async Task<IReadOnlyList<string>> JoinRoomAsync(string room, CancellationToken ct)
    {
        Drain(); // an older room's playlist may still be queued
        await SendAsync(new JsonObject { ["Set"] = new JsonObject { ["room"] = new JsonObject { ["name"] = room } } }, ct);
        var change = await WaitForAsync("Set", n => n["playlistChange"] is not null, TimeSpan.FromSeconds(10), ct);
        return change["playlistChange"]!["files"]?.AsArray().Select(f => f?.GetValue<string>() ?? "").ToList() ?? [];
    }

    public Task SetPlaylistAsync(IReadOnlyList<string> files, CancellationToken ct) => SendAsync(new JsonObject
    {
        ["Set"] = new JsonObject
        {
            ["playlistChange"] = new JsonObject { ["user"] = null, ["files"] = new JsonArray(files.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()) },
        },
    }, ct);

    public Task SetPlaylistIndexAsync(int index, CancellationToken ct) => SendAsync(new JsonObject
    {
        ["Set"] = new JsonObject { ["playlistIndex"] = new JsonObject { ["user"] = null, ["index"] = index } },
    }, ct);

    public Task ChatAsync(string text, CancellationToken ct) => SendAsync(new JsonObject { ["Chat"] = text }, ct);

    /// <summary>Drops queued messages, so the next wait only sees answers to what we send next.</summary>
    private void Drain()
    {
        while (_inbox.Reader.TryRead(out _)) { }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line is null) break;
                if (line.Length == 0) continue;
                JsonObject? message;
                try { message = JsonNode.Parse(line) as JsonObject; }
                catch (System.Text.Json.JsonException) { continue; }
                if (message is null) continue;
                if (message["State"] is JsonObject state)
                {
                    await AnswerStateAsync(state, ct);
                    continue;
                }
                await _inbox.Writer.WriteAsync(message, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
        finally
        {
            _inbox.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Answers a server State with the ping fields only, never a play state. Echoing
    /// ignoringOnTheFly.server is what lets the server count our answer after someone seeks.
    /// </summary>
    private Task AnswerStateAsync(JsonObject state, CancellationToken ct)
    {
        var reply = new JsonObject
        {
            ["ping"] = new JsonObject
            {
                ["latencyCalculation"] = state["ping"]?["latencyCalculation"]?.DeepClone() ?? 0,
                ["clientRtt"] = 0,
            },
        };
        if (state["ignoringOnTheFly"]?["server"] is { } server)
            reply["ignoringOnTheFly"] = new JsonObject { ["server"] = server.DeepClone() };
        return SendAsync(new JsonObject { ["State"] = reply }, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _tcp.Dispose();
        if (_readLoop is not null)
        {
            try { await _readLoop; } catch { }
        }
        _stop.Dispose();
    }
}

public sealed class SyncplayException(string message) : Exception(message);
