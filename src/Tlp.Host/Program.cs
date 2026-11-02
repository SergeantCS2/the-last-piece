using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tlp.Core;
using Tlp.Host;
using Tlp.Host.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<TlpOptions>(builder.Configuration.GetSection(TlpOptions.Section));
builder.Services.AddSingleton<Shelf>();
builder.Services.AddSingleton<NightStore>();
builder.Services.AddSingleton<PageFetcher>();
builder.Services.AddSingleton<HashCache>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HashCache>());
builder.Services.AddSingleton<CleanupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CleanupService>());
builder.Services.AddSingleton<GroupTorrents>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GroupTorrents>());
builder.Services.AddHostedService<SyncplayProcess>();
builder.Services.AddSingleton<ReadyBoard>();
builder.Services.AddSingleton<RoomBot>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<RoomBot>());
builder.Services.AddHttpClient<DiscordAnnouncer>();
builder.Services.AddSingleton<Diagnostics>();
builder.Services.AddSingleton<ShelfFetcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ShelfFetcher>());
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

var app = builder.Build();
var opts = app.Services.GetRequiredService<IOptions<TlpOptions>>().Value;
var adminToken = AdminToken.Resolve(opts, app.Logger);

if (opts.BehindProxy)
{
    // Trust X-Forwarded-For from the reverse proxy so the tracker hands out friends' real addresses.
    var fwd = new ForwardedHeadersOptions { ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto };
    fwd.KnownIPNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));
app.MapGet("/api/app", () => Results.Ok(new { name = Tlp.Core.AppInfo.Name, publisher = Tlp.Core.AppInfo.Publisher }));

// ---------- Viewer-facing: everything is keyed by the night's secret token ----------

app.MapGet("/n/{token}/pack.json", (string token, NightStore store, Shelf shelf, HashCache hashes, GroupTorrents torrents) =>
{
    var night = store.FindByToken(token);
    return night is null ? Results.NotFound() : Results.Json(PackBuilder.Build(night, opts, shelf, hashes, torrents), PackJson.Options);
});

// Group torrent for one file of a night: tracker and web seed both sit behind the night's link.
app.MapGet("/n/{token}/files/{index:int}/group.torrent", (string token, int index, NightStore store, Shelf shelf, GroupTorrents torrents) =>
{
    var night = store.FindByToken(token);
    if (night is null || index < 0 || index >= night.Items.Count || night.Items[index].ShelfPath is not { } path) return Results.NotFound();
    var bare = torrents.Get(path);
    if (bare is null) return Results.NotFound();
    var baseUrl = opts.PublicBaseUrl.TrimEnd('/');
    var bytes = GroupTorrents.ForNight(bare, $"{baseUrl}/n/{token}/announce",
        $"{baseUrl}/n/{token}/files/{index}/{Uri.EscapeDataString(night.Items[index].FileName)}");
    return Results.File(bytes, "application/x-bittorrent", Path.GetFileNameWithoutExtension(night.Items[index].FileName) + ".torrent");
});

app.MapGet("/n/{token}/announce", (HttpContext ctx, string token, NightStore store, GroupTorrents torrents, Diagnostics diag) =>
{
    var known = store.FindByToken(token) is not null;
    if (known) diag.RecordPeer(ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback);
    var body = !known
        ? GroupTorrents.Failure("Unknown movie night link.")
        : torrents.Announce(ctx.Request.QueryString.Value?.TrimStart('?') ?? "", ctx.Connection.RemoteIpAddress ?? IPAddress.Loopback);
    return Results.Bytes(body, "text/plain");
});

app.MapGet("/n/{token}/files/{index:int}/{name}", (string token, int index, string name, NightStore store, Shelf shelf) =>
{
    var night = store.FindByToken(token);
    if (night is null || index < 0 || index >= night.Items.Count) return Results.NotFound();
    var full = shelf.Resolve(night.Items[index].ShelfPath);
    if (full is null) return Results.NotFound();
    return Results.File(full, "application/octet-stream", night.Items[index].FileName, enableRangeProcessing: true);
});

// Viewer apps report their name and download progress here (see ViewerReport).
app.MapPost("/n/{token}/status", async (HttpContext ctx, string token, NightStore store, ReadyBoard board) =>
{
    var night = store.FindByToken(token);
    if (night is null) return Results.NotFound();
    if (ctx.Request.ContentLength is > 64 * 1024) return Results.StatusCode(413);
    ViewerReport? report;
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync(ctx.RequestAborted);
        if (body.Length > 64 * 1024) return Results.StatusCode(413);
        report = PackJson.DeserializeReport(body);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { error = "Not a viewer report." });
    }
    if (report is null) return Results.BadRequest(new { error = "Not a viewer report." });
    var error = board.Record(night, report);
    return error is null ? Results.NoContent() : Results.BadRequest(new { error });
});

app.MapGet("/n/{token}", (string token, NightStore store, Shelf shelf, HashCache hashes, GroupTorrents torrents) =>
{
    var night = store.FindByToken(token);
    if (night is null) return Results.NotFound();
    var pack = PackBuilder.Build(night, opts, shelf, hashes, torrents);
    var packUrl = $"{opts.PublicBaseUrl.TrimEnd('/')}/n/{token}/pack.json";
    return Results.Content(NightPage.Render(pack, packUrl), "text/html; charset=utf-8");
});

// ---------- Host-facing admin API ----------

var admin = app.MapGroup("/api/admin").AddEndpointFilter(async (ctx, next) =>
{
    var header = ctx.HttpContext.Request.Headers.Authorization.ToString();
    var given = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..] : "";
    var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(adminToken));
    return ok ? await next(ctx) : Results.Unauthorized();
});

admin.MapGet("/shelf", (Shelf shelf, NightStore store) =>
{
    var entries = CleanupPlanner.Describe(shelf.List(), store.Nights, store.Pins, opts.RetentionDays);
    var disk = shelf.Disk();
    return Results.Ok(new
    {
        files = entries,
        usedBytes = entries.Sum(e => e.Size),
        diskTotalBytes = disk?.Total,
        diskFreeBytes = disk?.Free,
        retentionDays = opts.RetentionDays,
        cleanupEnabled = opts.CleanupEnabled,
    });
});

admin.MapPost("/shelf/pin", async (PinRequest req, Shelf shelf, NightStore store) =>
{
    if (shelf.Resolve(req.Path) is null) return Results.NotFound();
    await store.SetPinnedAsync(req.Path, req.Pinned);
    return Results.NoContent();
});

admin.MapDelete("/shelf", (string path, Shelf shelf) => shelf.Delete(path) ? Results.NoContent() : Results.NotFound());

admin.MapGet("/cleanup", (CleanupService cleanup) => Results.Ok(cleanup.Plan()));
admin.MapGet("/diagnostics", async (Diagnostics diag, CancellationToken ct) => Results.Ok(await diag.RunAsync(ct)));
admin.MapPost("/cleanup", (CleanupService cleanup) => Results.Ok(cleanup.Run()));

admin.MapGet("/nights", (NightStore store) => Results.Ok(store.Nights.Select(n => NightView.From(n, opts))));

admin.MapPost("/nights", async (CreateNightRequest req, NightStore store, Shelf shelf, HashCache hashes, GroupTorrents torrents, ShelfFetcher fetcher) =>
{
    if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new { error = "Title is required." });
    if (req.Items is null || req.Items.Count == 0) return Results.BadRequest(new { error = "Add at least one file or magnet." });

    var items = new List<NightItem>();
    foreach (var (raw, i) in req.Items.Select((x, i) => (x, i)))
    {
        string? shelfPath = null, magnet = null, fileName = raw.FileName?.Trim();
        if (!string.IsNullOrWhiteSpace(raw.ShelfPath))
        {
            if (shelf.Resolve(raw.ShelfPath) is null) return Results.BadRequest(new { error = $"Item {i + 1}: '{raw.ShelfPath}' is not on the shelf." });
            shelfPath = raw.ShelfPath.Replace('\\', '/');
            fileName ??= Path.GetFileName(shelfPath);
            hashes.Get(shelfPath); // start hashing now so the pack has it soon
            torrents.Get(shelfPath); // and the group torrent
        }
        if (!string.IsNullOrWhiteSpace(raw.Magnet))
        {
            if (!MagnetInfo.TryParse(raw.Magnet, out var m) || m is null) return Results.BadRequest(new { error = $"Item {i + 1}: that magnet link isn't valid." });
            magnet = m.Uri;
            fileName ??= m.DisplayName;
        }
        if (shelfPath is null && magnet is null) return Results.BadRequest(new { error = $"Item {i + 1}: needs a shelf file or a magnet." });
        if (string.IsNullOrWhiteSpace(fileName)) return Results.BadRequest(new { error = $"Item {i + 1}: needs a file name (the magnet has none)." });
        if (!SafeFileName.IsSafe(fileName)) fileName = SafeFileName.Sanitize(fileName);
        items.Add(new NightItem { FileName = fileName, ShelfPath = shelfPath, Magnet = magnet });
    }

    var room = string.IsNullOrWhiteSpace(req.Room) ? opts.Syncplay.DefaultRoom : req.Room.Trim();
    var night = await store.AddAsync(req.Title.Trim(), req.StartsAt, room, items);
    if (req.FetchToServer == true && opts.FetchEnabled)
    {
        // The host downloads these itself; when done, the night switches to the host's copy.
        foreach (var item in items.Where(x => x.ShelfPath is null && x.Magnet is not null))
            await fetcher.AddAsync(item.Magnet!);
        night = store.Nights.First(n => n.Id == night.Id);
    }
    return Results.Ok(NightView.From(night, opts));
});

admin.MapPost("/nights/{id}/announce", async (string id, NightStore store, DiscordAnnouncer discord, CancellationToken ct) =>
{
    var night = store.Nights.FirstOrDefault(n => n.Id == id);
    if (night is null) return Results.NotFound();
    if (!discord.Configured) return Results.BadRequest(new { error = "Set Tlp:DiscordWebhookUrl (TLP_DISCORD_WEBHOOK in .env) first." });
    try
    {
        await discord.AnnounceAsync(night, ct);
        return Results.NoContent();
    }
    catch (HttpRequestException ex)
    {
        return Results.BadRequest(new { error = $"Discord didn't take it: {ex.Message}" });
    }
});

// Who's ready: download reports from viewer apps, and who's in the Syncplay room with which file.
admin.MapGet("/nights/{id}/status", (string id, NightStore store, ReadyBoard board, RoomBot bot) =>
{
    var night = store.Nights.FirstOrDefault(n => n.Id == id);
    if (night is null) return Results.NotFound();
    return Results.Ok(NightStatus.Build(night, board.For(id), bot.Snapshot, DateTimeOffset.UtcNow));
});

admin.MapPost("/nights/{id}/playlist", async (string id, bool? force, NightStore store, RoomBot bot) =>
{
    var night = store.Nights.FirstOrDefault(n => n.Id == id);
    if (night is null) return Results.NotFound();
    var push = bot.PushNowAsync(night, force ?? false);
    var finished = await Task.WhenAny(push, Task.Delay(TimeSpan.FromSeconds(30)));
    return finished == push
        ? Results.Ok(new { message = push.Result })
        : Results.Ok(new { message = "Syncplay isn't answering the room helper right now; it will keep trying." });
});

admin.MapGet("/settings", (DiscordAnnouncer discord) => Results.Ok(new { discord = discord.Configured, groupTorrents = opts.GroupTorrents, roomBot = opts.Syncplay.RoomBot }));

admin.MapDelete("/nights/{id}", async (string id, NightStore store, ReadyBoard board) =>
{
    board.Forget(id);
    return await store.RemoveAsync(id) ? Results.NoContent() : Results.NotFound();
});

admin.MapGet("/fetch", (ShelfFetcher fetcher) => Results.Ok(new { enabled = opts.FetchEnabled, items = fetcher.List() }));
admin.MapPost("/fetch", async (FetchRequest req, ShelfFetcher fetcher) =>
{
    try { return Results.Ok(await fetcher.AddAsync(req.Magnet ?? "")); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
admin.MapDelete("/fetch/{id}", async (string id, ShelfFetcher fetcher) => await fetcher.RemoveAsync(id) ? Results.NoContent() : Results.NotFound());

admin.MapPost("/magnets/from-page", async (FromPageRequest req, PageFetcher fetcher, CancellationToken ct) =>
{
    if (MagnetInfo.TryParse(req.Url, out var direct) && direct is not null)
        return Results.Ok(new[] { new FoundLink(direct.Uri, direct.DisplayName, FoundLink.MagnetKind) });
    if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        return Results.BadRequest(new { error = "Paste a magnet link or an http(s) page address." });
    try
    {
        var (html, finalUri) = await fetcher.FetchAsync(uri, ct);
        return Results.Ok(MagnetExtractor.Extract(html, finalUri));
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
    {
        return Results.BadRequest(new { error = $"Couldn't read that page: {ex.Message}" });
    }
});

app.Run();

public sealed record PinRequest(string Path, bool Pinned);
public sealed record FromPageRequest(string Url);
public sealed record CreateNightRequest(string Title, DateTimeOffset? StartsAt, string? Room, IReadOnlyList<CreateNightItem> Items, bool? FetchToServer = null);
public sealed record FetchRequest(string? Magnet);
public sealed record CreateNightItem(string? ShelfPath, string? Magnet, string? FileName);

public partial class Program;
