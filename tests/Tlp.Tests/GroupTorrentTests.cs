using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MonoTorrent;
using Tlp.Core;
using Tlp.Viewer.Core;

namespace Tlp.Tests;

/// <summary>
/// The group torrent end to end over real sockets: the real host app on Kestrel (tracker and
/// web seed), and two viewer torrent engines.
///  1. Friend A downloads with nobody else around, so every byte comes from the host's web seed.
///  2. The host's file is then deleted, so the web seed is gone. Friend B can only finish by
///     getting pieces from friend A, found through the host's tracker.
/// </summary>
public class GroupTorrentTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact(Timeout = 150_000)]
    public async Task Friends_get_the_file_from_the_host_web_seed_and_then_from_each_other()
    {
        var root = Directory.CreateTempSubdirectory("tlp-group-").FullName;
        var shelf = Path.Combine(root, "shelf");
        Directory.CreateDirectory(shelf);
        const string fileName = "[Grp] Show - 05.mkv";
        var data = new byte[2 * 1024 * 1024 + 4321];
        new Random(11).NextBytes(data);
        await File.WriteAllBytesAsync(Path.Combine(shelf, fileName), data);

        var port = FreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Tlp:ShelfDir", shelf);
            b.UseSetting("Tlp:DataDir", Path.Combine(root, "data"));
            b.UseSetting("Tlp:AdminToken", "t");
            b.UseSetting("Tlp:PublicBaseUrl", baseUrl);
            b.UseSetting("urls", baseUrl);
            b.UseSetting("Tlp:Syncplay:RoomBot", "false");
        });
        factory.UseKestrel();
        factory.StartServer();

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "t");
        var created = await (await http.PostAsJsonAsync("/api/admin/nights",
            new { title = "Group test", items = new[] { new { shelfPath = fileName } } })).Content.ReadFromJsonAsync<NightJson>();
        var packUrl = new Uri(created!.ShareUrl + "/pack.json");

        // The torrent is made in the background; wait for the pack to offer it.
        Pack pack;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            pack = PackJson.DeserializePack(await http.GetStringAsync(packUrl))!;
            if (pack.Items[0].Sources.Any(s => s.Kind == PackSourceKind.Torrent)) break;
            Assert.True(DateTime.UtcNow < deadline, "group torrent never appeared in the pack");
            await Task.Delay(200);
        }
        var item = pack.Items[0];
        Assert.Equal(PackSourceKind.Torrent, NightSession.ChooseSource(item, SourcePreference.Auto)!.Kind);

        var torrentFile = await http.GetByteArrayAsync(item.Sources.First(s => s.Kind == PackSourceKind.Torrent).Uri);
        var torrent = Torrent.Load(torrentFile);
        Assert.True(torrent.IsPrivate);
        Assert.Contains("/announce", torrent.AnnounceUrls[0][0]);
        Assert.Single(torrent.HttpSeeds);

        // 1. Friend A: nobody to share with, so the host's web seed supplies it.
        var dirA = Path.Combine(root, "friendA");
        await using var friendA = new TorrentDownloader(Path.Combine(root, "cacheA"), seedWhileOpen: true, listenPort: FreePort(), publicNetwork: false);
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            await friendA.DownloadTorrentAsync(torrentFile, dirA, item, null, cts.Token);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(dirA, fileName)));

        // 2. Host's copy gone: friend B must get it from friend A via the host's tracker.
        File.Delete(Path.Combine(shelf, fileName));
        var dirB = Path.Combine(root, "friendB");
        await using var friendB = new TorrentDownloader(Path.Combine(root, "cacheB"), seedWhileOpen: false, listenPort: FreePort(), publicNetwork: false);
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(75)))
            await friendB.DownloadTorrentAsync(torrentFile, dirB, item, null, cts.Token);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(dirB, fileName)));
    }

    [Fact]
    public async Task Tracker_refuses_unknown_night_links()
    {
        using var fx = new HostFixture();
        var body = await fx.Factory.CreateClient().GetStringAsync("/n/not-a-night/announce?info_hash=x");
        Assert.Contains("failure reason", body);
    }

    private sealed record NightJson(string Id, string ShareUrl);
}
