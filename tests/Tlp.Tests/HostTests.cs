using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tlp.Core;
using Tlp.Host.Services;

namespace Tlp.Tests;

public sealed class HostFixture : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("tlp-test-").FullName;
    public string Shelf => Path.Combine(Root, "shelf");
    public string Data => Path.Combine(Root, "data");
    public const string Token = "test-admin-token";
    public WebApplicationFactory<Program> Factory { get; }

    public HostFixture()
    {
        Directory.CreateDirectory(Shelf);
        Directory.CreateDirectory(Path.Combine(Shelf, "Season 1"));
        File.WriteAllBytes(Path.Combine(Shelf, "Season 1", "[Grp] Show - 01.mkv"), Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray());
        File.WriteAllText(Path.Combine(Root, "outside.txt"), "secret");
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Tlp:ShelfDir", Shelf);
            b.UseSetting("Tlp:DataDir", Data);
            b.UseSetting("Tlp:AdminToken", Token);
            b.UseSetting("Tlp:PublicBaseUrl", "http://localhost");
            b.UseSetting("Tlp:Syncplay:PublicHost", "sync.example.com");
            b.UseSetting("Tlp:Syncplay:Password", "pw");
            b.UseSetting("Tlp:CleanupInterval", "01:00:00");
        });
    }

    public HttpClient Admin()
    {
        var c = Factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return c;
    }

    public void Dispose()
    {
        Factory.Dispose();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}

public class HostTests(HostFixture fx) : IClassFixture<HostFixture>
{
    private const string Hash = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task Admin_api_needs_the_token()
    {
        var res = await fx.Factory.CreateClient().GetAsync("/api/admin/shelf");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Shelf_lists_files_with_relative_paths()
    {
        var json = await fx.Admin().GetStringAsync("/api/admin/shelf");
        Assert.Contains("Season 1/[Grp] Show - 01.mkv", json);
        Assert.DoesNotContain("outside.txt", json);
    }

    [Fact]
    public async Task Night_with_shelf_file_and_magnet_produces_a_working_pack()
    {
        var admin = fx.Admin();
        var create = await admin.PostAsJsonAsync("/api/admin/nights", new
        {
            title = "Friday",
            startsAt = DateTimeOffset.Parse("2026-11-06T20:00:00-05:00"),
            items = new object[]
            {
                new { shelfPath = "Season 1/[Grp] Show - 01.mkv" },
                new { magnet = $"magnet:?xt=urn:btih:{Hash}&dn=Show%20-%2002.mkv&xl=5" },
            },
        });
        create.EnsureSuccessStatusCode();
        var night = await create.Content.ReadFromJsonAsync<NightView>(PackJson.Options);
        Assert.NotNull(night);

        var anon = fx.Factory.CreateClient();
        var pack = await anon.GetFromJsonAsync<Pack>(new Uri(night!.ShareUrl + "/pack.json").PathAndQuery, PackJson.Options);
        Assert.NotNull(pack);
        Assert.Equal("sync.example.com", pack!.Syncplay.Host);
        Assert.Equal("movienight", pack.Syncplay.Room);
        Assert.Equal("pw", pack.Syncplay.Password);
        Assert.Equal(["[Grp] Show - 01.mkv", "Show - 02.mkv"], pack.Items.Select(i => i.FileName));
        Assert.Equal(300_000, pack.Items[0].Size);
        Assert.Equal(5, pack.Items[1].Size);
        Assert.Equal(PackSourceKind.Magnet, Assert.Single(pack.Items[1].Sources).Kind);

        // Range requests work, so downloads can resume and mpv can seek in a streamed file.
        var fileUrl = new Uri(Assert.Single(pack.Items[0].Sources).Uri).PathAndQuery;
        var req = new HttpRequestMessage(HttpMethod.Get, fileUrl);
        req.Headers.Range = new RangeHeaderValue(1000, 1999);
        var part = await anon.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        var bytes = await part.Content.ReadAsByteArrayAsync();
        Assert.Equal(1000, bytes.Length);
        Assert.Equal((byte)(1000 % 256), bytes[0]);

        var page = await anon.GetStringAsync(new Uri(night.ShareUrl).PathAndQuery);
        Assert.Contains("[Grp] Show - 01.mkv", page);
        Assert.Contains("tlp://open?pack=", page);

        // The shelf file is now used by a night, so it has an auto-delete date.
        var shelf = await admin.GetStringAsync("/api/admin/shelf");
        Assert.Contains("Friday", shelf);
    }

    [Fact]
    public async Task Wrong_night_token_and_path_escape_are_refused()
    {
        var anon = fx.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/n/not-a-token/pack.json")).StatusCode);

        var bad = await fx.Admin().PostAsJsonAsync("/api/admin/nights", new { title = "x", items = new[] { new { shelfPath = "../outside.txt" } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task From_page_accepts_a_pasted_magnet_without_fetching()
    {
        var res = await fx.Admin().PostAsJsonAsync("/api/admin/magnets/from-page", new { url = $"magnet:?xt=urn:btih:{Hash}&dn=A.mkv" });
        res.EnsureSuccessStatusCode();
        Assert.Contains("A.mkv", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task From_page_refuses_private_addresses()
    {
        var res = await fx.Admin().PostAsJsonAsync("/api/admin/magnets/from-page", new { url = "http://127.0.0.1:1/" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("not a public address", await res.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("1.1.1.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Public_address_check(string ip, bool isPublic) =>
        Assert.Equal(isPublic, PageFetcher.IsPublic(IPAddress.Parse(ip)));
}

public class CleanupPlannerTests
{
    private static Night NightOn(string id, DateTimeOffset when, params string[] paths) => new()
    {
        Id = id, Token = id, Title = id, Room = "r", StartsAt = when, CreatedAt = when,
        Items = paths.Select(p => new NightItem { FileName = p, ShelfPath = p }).ToList(),
    };

    [Fact]
    public void Deletes_only_files_past_retention_of_their_latest_night_and_not_pinned()
    {
        var now = DateTimeOffset.Parse("2026-11-20T00:00:00Z");
        var files = new[] { "old.mkv", "reused.mkv", "pinned.mkv", "loose.mkv" }
            .Select(p => new ShelfFile(p, 1, now.AddDays(-60))).ToList();
        var nights = new[]
        {
            NightOn("a", now.AddDays(-30), "old.mkv", "reused.mkv", "pinned.mkv"),
            NightOn("b", now.AddDays(-2), "reused.mkv"),
        };
        var entries = CleanupPlanner.Describe(files, nights, new HashSet<string> { "pinned.mkv" }, retentionDays: 7);
        var due = CleanupPlanner.Due(entries, now).Select(e => e.Path).ToList();
        Assert.Equal(["old.mkv"], due);
        Assert.Null(entries.Single(e => e.Path == "loose.mkv").DeleteAfter); // not in any night: manual only
    }
}
