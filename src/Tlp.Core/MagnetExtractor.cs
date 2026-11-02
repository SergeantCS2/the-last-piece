using System.Net;
using System.Text.RegularExpressions;

namespace Tlp.Core;

/// <summary>A link found on a torrent site's page.</summary>
public sealed record FoundLink(string Uri, string? Name, string Kind)
{
    public const string MagnetKind = "magnet";
    public const string TorrentFileKind = "torrent";
}

/// <summary>
/// Pulls magnet links (and .torrent file links) out of an HTML page, so the host can
/// paste a torrent site's page URL instead of hunting for the magnet button.
/// Content-neutral: it only reads the page the host gives it.
/// </summary>
public static partial class MagnetExtractor
{
    public static IReadOnlyList<FoundLink> Extract(string html, Uri? pageUri = null)
    {
        var results = new List<FoundLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in MagnetPattern().Matches(html))
        {
            var raw = WebUtility.HtmlDecode(m.Value);
            if (!MagnetInfo.TryParse(raw, out var magnet) || magnet is null) continue;
            if (!seen.Add("btih:" + magnet.InfoHash)) continue;
            results.Add(new FoundLink(magnet.Uri, magnet.DisplayName, FoundLink.MagnetKind));
        }

        foreach (Match m in HrefPattern().Matches(html))
        {
            var href = WebUtility.HtmlDecode(m.Groups["href"].Value);
            var path = href.Split('?', '#')[0];
            if (!path.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)) continue;
            if (!System.Uri.TryCreate(pageUri, href, out var abs) &&
                !System.Uri.TryCreate(href, UriKind.Absolute, out abs)) continue;
            if (abs.Scheme is not ("http" or "https")) continue;
            if (!seen.Add(abs.AbsoluteUri)) continue;
            var name = System.Uri.UnescapeDataString(abs.Segments[^1]);
            results.Add(new FoundLink(abs.AbsoluteUri, name, FoundLink.TorrentFileKind));
        }

        return results;
    }

    [GeneratedRegex(@"magnet:\?[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex MagnetPattern();

    [GeneratedRegex(@"href\s*=\s*[""'](?<href>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();
}
