using Tlp.Core;

namespace Tlp.Tests;

public class MagnetInfoTests
{
    [Fact]
    public void Parses_hex_hash_name_size_and_trackers()
    {
        var uri = "magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&dn=%5BGroup%5D%20Show%20-%2001.mkv&xl=1234&tr=udp%3A%2F%2Ftracker.example%3A1337";
        Assert.True(MagnetInfo.TryParse(uri, out var m));
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", m!.InfoHash);
        Assert.Equal("[Group] Show - 01.mkv", m.DisplayName);
        Assert.Equal(1234, m.ExactLength);
        Assert.Equal(["udp://tracker.example:1337"], m.Trackers);
    }

    [Fact]
    public void Converts_base32_hash_to_hex()
    {
        // 20 zero bytes = 32 'A's in base32
        Assert.True(MagnetInfo.TryParse("magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", out var m));
        Assert.Equal(new string('0', 40), m!.InfoHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("magnet:?dn=no-hash")]
    [InlineData("magnet:?xt=urn:btih:xyz")]
    public void Rejects_invalid(string text) => Assert.False(MagnetInfo.TryParse(text, out _));
}

public class MagnetExtractorTests
{
    [Fact]
    public void Finds_magnets_and_torrent_files_once_each()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        var html = $"""
            <a href="magnet:?xt=urn:btih:{hash}&amp;dn=Show%20-%2001.mkv&amp;tr=udp%3A%2F%2Ft.example%3A80">Magnet</a>
            <a href='magnet:?xt=urn:btih:{hash.ToUpperInvariant()}'>dup</a>
            <a href="/download/123.torrent">Torrent</a>
            <a href="/view/123">not a torrent</a>
            """;
        var found = MagnetExtractor.Extract(html, new Uri("https://site.example/view/123"));
        var magnet = Assert.Single(found, f => f.Kind == FoundLink.MagnetKind);
        Assert.Equal("Show - 01.mkv", magnet.Name);
        Assert.Contains("&dn=", magnet.Uri);
        var torrent = Assert.Single(found, f => f.Kind == FoundLink.TorrentFileKind);
        Assert.Equal("https://site.example/download/123.torrent", torrent.Uri);
    }
}

public class SafeFileNameTests
{
    [Theory]
    [InlineData("[Group] Show - 01 (1080p).mkv", true)]
    [InlineData("../evil.mkv", false)]
    [InlineData("a/b.mkv", false)]
    [InlineData("C:evil.mkv", false)]
    [InlineData("CON.mkv", false)]
    [InlineData("trailing.", false)]
    [InlineData("..", false)]
    public void Checks_names(string name, bool safe) => Assert.Equal(safe, SafeFileName.IsSafe(name));

    [Fact]
    public void Sanitize_always_produces_a_safe_name()
    {
        foreach (var bad in new[] { "../x", "a:b?c", "CON", "...", "" })
            Assert.True(SafeFileName.IsSafe(SafeFileName.Sanitize(bad)), bad);
    }
}
