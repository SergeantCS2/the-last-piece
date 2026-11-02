using System.Net.Http.Json;
using Tlp.Core;
using Tlp.Host.Services;
using Tlp.Viewer.Core;

namespace Tlp.Tests;

public class PackLinkTests
{
    [Theory]
    [InlineData("https://h.example/n/AbC", "https://h.example/n/AbC/pack.json")]
    [InlineData("https://h.example/n/AbC/", "https://h.example/n/AbC/pack.json")]
    [InlineData("https://h.example/n/AbC/pack.json", "https://h.example/n/AbC/pack.json")]
    [InlineData("tlp://open?pack=https%3A%2F%2Fh.example%2Fn%2FAbC%2Fpack.json", "https://h.example/n/AbC/pack.json")]
    [InlineData("  \"https://h.example/n/AbC\" ", "https://h.example/n/AbC/pack.json")]
    public void Understands_links(string input, string expected) =>
        Assert.Equal(expected, PackLink.ToPackUri(input)?.AbsoluteUri);

    [Theory]
    [InlineData("https://h.example/other")]
    [InlineData("file:///C:/x/pack.json")]
    [InlineData("tlp://open")]
    [InlineData("nonsense")]
    public void Rejects_other_links(string input) => Assert.Null(PackLink.ToPackUri(input));
}

public class PackValidationTests
{
    private static Pack WithItem(string name, PackSource src) => new()
    {
        Title = "t",
        Syncplay = new SyncplayInfo { Host = "h", Room = "r" },
        Items = [new PackItem { FileName = name, Sources = [src] }],
    };

    [Fact]
    public void Rejects_path_escape_in_file_name() =>
        Assert.Throws<InvalidDataException>(() => PackClient.Validate(WithItem("..\\evil.exe",
            new PackSource { Kind = PackSourceKind.Http, Uri = "https://h/x" })));

    [Fact]
    public void Rejects_non_http_download() =>
        Assert.Throws<InvalidDataException>(() => PackClient.Validate(WithItem("a.mkv",
            new PackSource { Kind = PackSourceKind.Http, Uri = "file:///etc/passwd" })));

    [Fact]
    public void Chooses_source_by_preference()
    {
        var item = new PackItem
        {
            FileName = "a.mkv",
            Sources = [new() { Kind = PackSourceKind.Http, Uri = "https://h/a" }, new() { Kind = PackSourceKind.Magnet, Uri = "magnet:?xt=urn:btih:" + new string('a', 40) }],
        };
        Assert.Equal(PackSourceKind.Http, NightSession.ChooseSource(item, SourcePreference.Auto)!.Kind);
        Assert.Equal(PackSourceKind.Magnet, NightSession.ChooseSource(item, SourcePreference.PreferTorrent)!.Kind);
        Assert.Equal(PackSourceKind.Http, NightSession.ChooseSource(item, SourcePreference.PreferShelf)!.Kind);
    }
}

public class SyncplayLauncherTests
{
    private static readonly Pack Pack = new()
    {
        Title = "t",
        Syncplay = new SyncplayInfo { Host = "sync.example.com", Port = 8999, Room = "movienight", Password = "pw" },
        Items = [new PackItem { FileName = "a.mkv", Sources = [new() { Kind = PackSourceKind.Http, Uri = "https://movies.example.com/n/x/files/0/a.mkv" }] }],
    };

    [Fact]
    public void Python_list_escapes_windows_paths_and_quotes() =>
        Assert.Equal(@"['C:\\Users\\Jo\\Movie Night', 'it\'s']", SyncplayLauncher.PyList([@"C:\Users\Jo\Movie Night", "it's"]));

    [Fact]
    public void Relative_config_trusts_the_shelf_and_points_at_the_folder()
    {
        var ini = SyncplayLauncher.RelativeConfig(Pack, @"C:\MN");
        Assert.Contains(@"mediaSearchDirectories = ['C:\\MN']", ini);
        Assert.Contains("trustedDomains = ['movies.example.com']", ini);
        Assert.Contains("sharedPlaylistEnabled = True", ini);
        Assert.Contains("chatOutputEnabled = True", ini);
    }

    [Fact]
    public void Arguments_join_the_room_without_touching_saved_settings()
    {
        var args = SyncplayLauncher.Arguments(Pack, "Jo", @"C:\tlp\mpv\mpv.exe", @"C:\MN\a.mkv");
        Assert.Equal(["--host", "sync.example.com:8999", "--name", "Jo", "--room", "movienight",
            "--player-path", @"C:\tlp\mpv\mpv.exe", "--no-store", "--password", "pw", @"C:\MN\a.mkv"], args);
    }
}

/// <summary>End to end against the real host app: create a night, then download and verify like the viewer does.</summary>
public class DownloadTests(HostFixture fx) : IClassFixture<HostFixture>
{
    [Fact]
    public async Task Downloads_resumes_and_verifies_against_host_hash()
    {
        var admin = fx.Admin();
        var created = await admin.PostAsJsonAsync("/api/admin/nights",
            new { title = "dl", items = new[] { new { shelfPath = "Season 1/[Grp] Show - 01.mkv" } } });
        var night = (await created.Content.ReadFromJsonAsync<NightView>(PackJson.Options))!;

        var client = fx.Factory.CreateClient();
        var packPath = new Uri(night.ShareUrl + "/pack.json").PathAndQuery;
        Pack pack;
        var waited = 0;
        do // the host hashes in the background
        {
            pack = (await client.GetFromJsonAsync<Pack>(packPath, PackJson.Options))!;
            if (pack.Items[0].Sha256 is not null) break;
            await Task.Delay(100);
        } while ((waited += 100) < 10_000);
        var item = pack.Items[0];
        Assert.NotNull(item.Sha256);

        var folder = Directory.CreateTempSubdirectory("tlp-viewer-").FullName;
        try
        {
            // Simulate an interrupted download: first 100 KB already in the .part file.
            var original = await File.ReadAllBytesAsync(Path.Combine(fx.Shelf, "Season 1", "[Grp] Show - 01.mkv"));
            await File.WriteAllBytesAsync(Path.Combine(folder, item.FileName + FileCheck.PartSuffix), original[..100_000]);
            Assert.Equal(FileState.Partial, FileCheck.Quick(folder, item));

            var source = new Uri(new Uri("http://localhost"), new Uri(item.Sources.Single(s => s.Kind == PackSourceKind.Http).Uri).PathAndQuery);
            await new HttpDownloader(client).DownloadAsync(source, folder, item);

            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(folder, item.FileName)));
            Assert.Equal(FileState.Verified, await FileCheck.FullAsync(folder, item));

            // Same size, different bytes: caught by the hash.
            var path = Path.Combine(folder, item.FileName);
            var tampered = (byte[])original.Clone();
            tampered[12345] ^= 0xFF;
            await File.WriteAllBytesAsync(path, tampered);
            Assert.Equal(FileState.HashMismatch, await FileCheck.FullAsync(folder, item));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
