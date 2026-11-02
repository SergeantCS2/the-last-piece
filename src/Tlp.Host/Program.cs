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
builder.Services.AddHostedService<SyncplayProcess>();
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
});

var app = builder.Build();
var opts = app.Services.GetRequiredService<IOptions<TlpOptions>>().Value;
var adminToken = AdminToken.Resolve(opts, app.Logger);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

// ---------- Viewer-facing: everything is keyed by the night's secret token ----------

app.MapGet("/n/{token}/pack.json", (string token, NightStore store, Shelf shelf, HashCache hashes) =>
{
    var night = store.FindByToken(token);
    return night is null ? Results.NotFound() : Results.Json(PackBuilder.Build(night, opts, shelf, hashes), PackJson.Options);
});

app.MapGet("/n/{token}/files/{index:int}/{name}", (string token, int index, string name, NightStore store, Shelf shelf) =>
{
    var night = store.FindByToken(token);
    if (night is null || index < 0 || index >= night.Items.Count) return Results.NotFound();
    var full = shelf.Resolve(night.Items[index].ShelfPath);
    if (full is null) return Results.NotFound();
    return Results.File(full, "application/octet-stream", night.Items[index].FileName, enableRangeProcessing: true);
});

app.MapGet("/n/{token}", (string token, NightStore store, Shelf shelf, HashCache hashes) =>
{
    var night = store.FindByToken(token);
    if (night is null) return Results.NotFound();
    var pack = PackBuilder.Build(night, opts, shelf, hashes);
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
admin.MapPost("/cleanup", (CleanupService cleanup) => Results.Ok(cleanup.Run()));

admin.MapGet("/nights", (NightStore store) => Results.Ok(store.Nights.Select(n => NightView.From(n, opts))));

admin.MapPost("/nights", async (CreateNightRequest req, NightStore store, Shelf shelf, HashCache hashes) =>
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
    return Results.Ok(NightView.From(night, opts));
});

admin.MapDelete("/nights/{id}", async (string id, NightStore store) =>
    await store.RemoveAsync(id) ? Results.NoContent() : Results.NotFound());

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
public sealed record CreateNightRequest(string Title, DateTimeOffset? StartsAt, string? Room, IReadOnlyList<CreateNightItem> Items);
public sealed record CreateNightItem(string? ShelfPath, string? Magnet, string? FileName);

public partial class Program;
