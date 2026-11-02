using System.Net;
using System.Net.Sockets;
using MonoTorrent;
using MonoTorrent.Client;
using MonoTorrent.Connections.TrackerServer;
using MonoTorrent.TrackerServer;
using Tlp.Core;
using Tlp.Viewer.Core;

namespace Tlp.Tests;

/// <summary>
/// Real BitTorrent transfer on this machine: a local tracker, a seeder engine (standing
/// in for the host or a friend) and the viewer's TorrentDownloader fetching by magnet.
/// This is the same shape as the planned group torrent (host tracker + seed, friends share).
/// </summary>
public class TorrentTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact(Timeout = 90_000)]
    public async Task Magnet_downloads_from_a_peer_and_lands_under_the_pack_file_name()
    {
        var root = Directory.CreateTempSubdirectory("tlp-torrent-").FullName;
        var seedDir = Path.Combine(root, "seed");
        var viewerDir = Path.Combine(root, "viewer");
        Directory.CreateDirectory(seedDir);
        const string fileName = "[Grp] Show - 03.mkv";
        var data = new byte[3 * 1024 * 1024 + 12345];
        new Random(7).NextBytes(data);
        await File.WriteAllBytesAsync(Path.Combine(seedDir, fileName), data);

        var trackerPort = FreePort();
        var announce = $"http://127.0.0.1:{trackerPort}/announce/";
        using var tracker = new TrackerServer { AllowUnregisteredTorrents = true };
        var listener = TrackerListenerFactory.CreateHttp($"http://127.0.0.1:{trackerPort}/announce/");
        tracker.RegisterListener(listener);
        listener.Start();

        try
        {
            var creator = new TorrentCreator(TorrentType.V1Only) { Announces = { new List<string> { announce } } };
            var torrent = Torrent.Load(await creator.CreateAsync(new TorrentFileSource(Path.Combine(seedDir, fileName))));

            var seederSettings = new EngineSettingsBuilder
            {
                CacheDirectory = Path.Combine(root, "seed-cache"),
                AllowPortForwarding = false,
                AllowLocalPeerDiscovery = false,
                DhtEndPoint = null,
                ListenEndPoints = new() { ["ipv4"] = new IPEndPoint(IPAddress.Any, FreePort()) },
            }.ToSettings();
            using var seeder = new ClientEngine(seederSettings);
            var seedManager = await seeder.AddAsync(torrent, seedDir);
            await seedManager.HashCheckAsync(autoStart: true);

            var magnet = new MagnetLink(torrent.InfoHashes, torrent.Name, [announce], null, torrent.Size).ToV1String();
            Assert.True(MagnetInfo.TryParse(magnet, out var parsed));

            var item = new PackItem
            {
                FileName = fileName,
                Size = data.Length,
                Sources = [new PackSource { Kind = PackSourceKind.Magnet, Uri = magnet }],
            };
            await using var viewer = new TorrentDownloader(Path.Combine(root, "viewer-cache"), seedWhileOpen: true, listenPort: FreePort(), publicNetwork: false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(75));
            await viewer.DownloadAsync(magnet, viewerDir, item, null, cts.Token);

            Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(viewerDir, fileName)));
            Assert.Equal(FileState.SizeMatches, FileCheck.Quick(viewerDir, item));
            await seeder.StopAllAsync();
        }
        finally
        {
            listener.Stop();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
