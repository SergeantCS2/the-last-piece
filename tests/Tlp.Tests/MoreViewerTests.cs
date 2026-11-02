using System.Net.Http.Json;
using Tlp.Core;
using Tlp.Host;
using Tlp.Host.Services;
using Tlp.Viewer.Core;

namespace Tlp.Tests;

public class MoreViewerTests(HostFixture fx) : IClassFixture<HostFixture>
{
    private static PackItem Item(params PackSourceKind[] kinds) => new()
    {
        FileName = "a.mkv",
        Sources = kinds.Select(k => new PackSource { Kind = k, Uri = k == PackSourceKind.Magnet ? "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567" : "https://h/x" }).ToList(),
    };

    [Fact]
    public void Source_order_puts_the_group_torrent_first_on_auto()
    {
        var item = Item(PackSourceKind.Http, PackSourceKind.Magnet, PackSourceKind.Torrent);
        Assert.Equal([PackSourceKind.Torrent, PackSourceKind.Http, PackSourceKind.Magnet], NightSession.OrderSources(item, SourcePreference.Auto).Select(s => s.Kind));
        Assert.Equal([PackSourceKind.Torrent, PackSourceKind.Magnet, PackSourceKind.Http], NightSession.OrderSources(item, SourcePreference.PreferTorrent).Select(s => s.Kind));
        Assert.Equal([PackSourceKind.Http, PackSourceKind.Torrent, PackSourceKind.Magnet], NightSession.OrderSources(item, SourcePreference.PreferShelf).Select(s => s.Kind));
    }

    [Fact]
    public void Pack_json_round_trips_without_reflection_and_keeps_lowercase_kinds()
    {
        var pack = new Pack
        {
            Title = "t",
            Syncplay = new SyncplayInfo { Host = "h", Room = "r" },
            Items = [Item(PackSourceKind.Torrent)],
        };
        var json = PackJson.SerializePack(pack);
        Assert.Contains("\"kind\": \"torrent\"", json);
        Assert.Equal(PackSourceKind.Torrent, PackJson.DeserializePack(json)!.Items[0].Sources[0].Kind);
        // The host's reflection-based options give the same names.
        Assert.Contains("\"kind\": \"torrent\"", PackJson.Serialize(pack));
    }

    [Fact]
    public void Settings_save_and_load()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("tlp-settings-").FullName, "s.json");
        new ViewerSettings { Name = "Alex", SourcePreference = SourcePreference.PreferShelf }.Save(path);
        Assert.Contains("preferShelf", File.ReadAllText(path));
        var loaded = ViewerSettings.Load(path);
        Assert.Equal("Alex", loaded.Name);
        Assert.Equal(SourcePreference.PreferShelf, loaded.SourcePreference);
    }

    [Fact]
    public void Bundled_syncplay_skips_the_connect_window_and_update_popups()
    {
        var ini = SyncplayLauncher.PortableConfig("Scrolling");
        Assert.Contains("[client_settings]\nforceGuiPrompt = False", ini.ReplaceLineEndings("\n"));
        Assert.Contains("checkForUpdatesAutomatically = False", ini);
        Assert.Contains("chatOutputMode = Scrolling", ini);
    }

    [Fact(Timeout = 30_000)]
    public async Task Falls_back_to_the_host_when_the_torrent_route_fails()
    {
        var admin = fx.Admin();
        var created = await admin.PostAsJsonAsync("/api/admin/nights",
            new { title = "fallback", items = new[] { new { shelfPath = "Season 1/[Grp] Show - 01.mkv" } } });
        var night = (await created.Content.ReadFromJsonAsync<NightView>(PackJson.Options))!;
        var client = fx.Factory.CreateClient();
        var pack = PackJson.DeserializePack(await client.GetStringAsync(new Uri(night.ShareUrl + "/pack.json").PathAndQuery))!;
        var http = pack.Items[0].Sources.First(s => s.Kind == PackSourceKind.Http);
        // A group torrent that can't be fetched, listed first.
        var item = pack.Items[0] with
        {
            Sources = [new PackSource { Kind = PackSourceKind.Torrent, Uri = "http://localhost/n/nope/files/0/group.torrent" },
                       new PackSource { Kind = PackSourceKind.Http, Uri = new Uri(new Uri("http://localhost"), new Uri(http.Uri).PathAndQuery).ToString() }],
        };
        var folder = Directory.CreateTempSubdirectory("tlp-fallback-").FullName;
        var root = Directory.CreateTempSubdirectory("tlp-fallback-cache-").FullName;
        await using var torrents = new TorrentDownloader(root, publicNetwork: false);
        var session = new NightSession(pack with { Items = [item] }, new ViewerSettings { MovieNightFolder = folder }, new HttpDownloader(client), torrents);

        var result = await session.FetchAsync(item);

        Assert.True(result.State is FileState.Verified or FileState.SizeMatches, result.Message);
        Assert.Equal(300_000, new FileInfo(Path.Combine(folder, item.FileName)).Length);
    }

    [Fact]
    public async Task A_second_copy_hands_its_link_to_the_first()
    {
        var name = "tlp-test-" + Guid.NewGuid().ToString("N")[..8];
        var got = new TaskCompletionSource<string>();
        using var first = SingleInstance.TryStart(name, link => got.TrySetResult(link));
        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryStart(name, _ => { }));
        Assert.True(SingleInstance.SendToRunning(name, "tlp://open?pack=https%3A%2F%2Fh%2Fn%2Fx%2Fpack.json", TimeSpan.FromSeconds(5)));
        Assert.Equal("tlp://open?pack=https%3A%2F%2Fh%2Fn%2Fx%2Fpack.json", await got.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/abc", true)]
    [InlineData("https://discordapp.com/api/webhooks/1/abc", true)]
    [InlineData("http://discord.com/api/webhooks/1/abc", false)]
    [InlineData("https://evil.example/api/webhooks/1/abc", false)]
    [InlineData("https://discord.com/channels/1", false)]
    [InlineData(null, false)]
    public void Only_real_discord_webhooks_are_used(string? url, bool ok) => Assert.Equal(ok, DiscordAnnouncer.IsWebhookUrl(url));

    [Fact]
    public void Discord_post_has_the_link_and_a_local_time_stamp()
    {
        var night = new Night
        {
            Id = "i", Token = "tok", Title = "Friday anime", Room = "movienight", CreatedAt = DateTimeOffset.UtcNow,
            StartsAt = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), Items = [new NightItem { FileName = "a.mkv" }],
        };
        var msg = DiscordAnnouncer.Message(night, new TlpOptions { PublicBaseUrl = "https://movies.example.com" });
        Assert.Contains("**Friday anime**", msg);
        Assert.Contains("<t:1800000000:F>", msg);
        Assert.Contains("https://movies.example.com/n/tok", msg);
    }
}
