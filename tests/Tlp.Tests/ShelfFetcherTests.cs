using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections.TrackerServer;
using MonoTorrent.TrackerServer;
using Tlp.Core;
using Tlp.Host.Services;

namespace Tlp.Tests;

/// <summary>
/// "Get it to my server": the host downloads a public magnet onto its own shelf over a real
/// torrent transfer (local tracker and seeder standing in for the public swarm), and a night
/// that listed the magnet switches to the host's copy (direct download and group torrent).
/// </summary>
public class ShelfFetcherTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact(Timeout = 120_000)]
    public async Task Host_fetches_a_magnet_to_its_shelf_and_the_night_switches_to_it()
    {
        var root = Directory.CreateTempSubdirectory("tlp-fetch-").FullName;
        var seedDir = Path.Combine(root, "seed");
        var shelf = Path.Combine(root, "shelf");
        Directory.CreateDirectory(seedDir);
        Directory.CreateDirectory(shelf);
        const string fileName = "[Grp] Film (2024).mkv";
        var data = new byte[2 * 1024 * 1024 + 777];
        new Random(5).NextBytes(data);
        await File.WriteAllBytesAsync(Path.Combine(seedDir, fileName), data);

        var trackerPort = FreePort();
        var announce = $"http://127.0.0.1:{trackerPort}/announce/";
        using var tracker = new TrackerServer { AllowUnregisteredTorrents = true };
        var listener = TrackerListenerFactory.CreateHttp(announce);
        tracker.RegisterListener(listener);
        listener.Start();
        try
        {
            var creator = new TorrentCreator(TorrentType.V1Only) { Announces = { new List<string> { announce } } };
            var torrent = Torrent.Load(await creator.CreateAsync(new TorrentFileSource(Path.Combine(seedDir, fileName))));
            using var seeder = new ClientEngine(new EngineSettingsBuilder
            {
                CacheDirectory = Path.Combine(root, "seed-cache"),
                AllowPortForwarding = false,
                AllowLocalPeerDiscovery = false,
                DhtEndPoint = null,
                ListenEndPoints = new() { ["ipv4"] = new IPEndPoint(IPAddress.Any, FreePort()) },
            }.ToSettings());
            var seed = await seeder.AddAsync(torrent, seedDir);
            await seed.HashCheckAsync(autoStart: true);
            var magnet = new MagnetLink(torrent.InfoHashes, torrent.Name, [announce], null, torrent.Size).ToV1String();

            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            {
                b.UseSetting("Tlp:ShelfDir", shelf);
                b.UseSetting("Tlp:DataDir", Path.Combine(root, "data"));
                b.UseSetting("Tlp:AdminToken", "t");
                b.UseSetting("Tlp:PublicBaseUrl", "http://localhost");
                b.UseSetting("Tlp:Syncplay:RoomBot", "false");
                b.UseSetting("Tlp:FetchPublicNetwork", "false");
                b.UseSetting("Tlp:FetchPort", FreePort().ToString());
            });
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "t");

            // A night that only has the public magnet, told to fetch it to the server.
            var created = await http.PostAsJsonAsync("/api/admin/nights",
                new { title = "Fetch test", items = new[] { new { magnet } }, fetchToServer = true });
            created.EnsureSuccessStatusCode();
            var night = await created.Content.ReadFromJsonAsync<JsonElement>();
            var sharePath = new Uri(night.GetProperty("shareUrl").GetString()!).AbsolutePath;

            JsonElement item = default;
            for (var i = 0; i < 200; i++)
            {
                var list = await http.GetFromJsonAsync<JsonElement>("/api/admin/fetch");
                item = list.GetProperty("items").EnumerateArray().Single();
                if (item.GetProperty("state").GetString() is "done" or "failed") break;
                await Task.Delay(400);
            }
            Assert.Equal("done", item.GetProperty("state").GetString());
            Assert.Equal(fileName, item.GetProperty("shelfPaths")[0].GetString());
            Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(shelf, fileName)));
            Assert.False(Directory.EnumerateFileSystemEntries(Path.Combine(root, "data", "fetching")).Any());

            // The night now offers the host's own copy, not only the public magnet.
            var pack = PackJson.Deserialize<Pack>(await http.GetStringAsync(sharePath + "/pack.json"))!;
            var kinds = pack.Items.Single().Sources.Select(s => s.Kind).ToList();
            Assert.Contains(PackSourceKind.Http, kinds);
            Assert.Contains(PackSourceKind.Magnet, kinds);

            // Asking again for the same magnet doesn't download it twice.
            var again = await http.PostAsJsonAsync("/api/admin/fetch", new { magnet });
            again.EnsureSuccessStatusCode();
            Assert.Equal("done", (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());

            // Removing the entry keeps the file on the shelf.
            var id = item.GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/admin/fetch/{id}")).StatusCode);
            Assert.True(File.Exists(Path.Combine(shelf, fileName)));
            await seeder.StopAllAsync();
        }
        finally
        {
            listener.Stop();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Bad_magnets_are_refused()
    {
        using var fx = new HostFixture();
        var http = fx.Admin();
        var res = await http.PostAsJsonAsync("/api/admin/fetch", new { magnet = "not a magnet" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("isn't valid", await res.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await http.DeleteAsync("/api/admin/fetch/nope")).StatusCode);
    }

    [Theory]
    [InlineData("Film.mkv", "Show/Film.mkv")]
    [InlineData("Other name.mkv", "Show/Film.mkv")]
    public void Picks_the_matching_file_or_the_only_video(string itemName, string expected)
    {
        IReadOnlyList<string> fetched = ["Show/Film.mkv", "Show/Film.nfo", "Show/Subs/en.srt"];
        Assert.Equal(expected, ShelfFetcher.PickShelfFile(fetched, itemName));
    }

    [Fact]
    public void Two_videos_and_no_name_match_picks_nothing()
    {
        Assert.Null(ShelfFetcher.PickShelfFile(["a.mkv", "b.mkv"], "c.mkv"));
    }
}
