namespace Tlp.Viewer.Core;

/// <summary>Turns whatever a friend pastes or clicks into the pack.json address.</summary>
public static class PackLink
{
    /// <summary>
    /// Accepts the night page (https://host/n/TOKEN), the pack itself (.../pack.json),
    /// or the app link (tlp://open?pack=...). Returns null for anything else.
    /// </summary>
    public static Uri? ToPackUri(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim().Trim('"');

        if (input.StartsWith("tlp:", StringComparison.OrdinalIgnoreCase))
        {
            var q = input.IndexOf('?');
            if (q < 0) return null;
            foreach (var pair in input[(q + 1)..].Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && pair[..eq].Equals("pack", StringComparison.OrdinalIgnoreCase))
                    return ToPackUri(Uri.UnescapeDataString(pair[(eq + 1)..]));
            }
            return null;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/pack.json", StringComparison.Ordinal)) return uri;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && segments[0] == "n")
            return new UriBuilder(uri) { Path = path + "/pack.json", Query = "", Fragment = "" }.Uri;
        return null;
    }
}
