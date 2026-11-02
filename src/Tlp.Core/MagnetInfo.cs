using System.Text.RegularExpressions;

namespace Tlp.Core;

/// <summary>Minimal magnet URI parser: enough to validate and label a magnet.</summary>
public sealed partial record MagnetInfo
{
    public required string Uri { get; init; }
    /// <summary>v1 info hash as 40 lowercase hex chars, or v2 multihash after "urn:btmh:".</summary>
    public required string InfoHash { get; init; }
    public string? DisplayName { get; init; }
    public long? ExactLength { get; init; }
    public IReadOnlyList<string> Trackers { get; init; } = [];

    public static bool TryParse(string? text, out MagnetInfo? magnet)
    {
        magnet = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (!text.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return false;

        string? hash = null;
        string? name = null;
        long? length = null;
        var trackers = new List<string>();

        foreach (var part in text["magnet:?".Length..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var key = part[..eq].ToLowerInvariant();
            string value;
            try { value = System.Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' ')); }
            catch (UriFormatException) { continue; }

            switch (key)
            {
                case "xt":
                    hash ??= ParseExactTopic(value);
                    break;
                case "dn":
                    name ??= value;
                    break;
                case "xl" when long.TryParse(value, out var xl) && xl > 0:
                    length ??= xl;
                    break;
                case "tr":
                    trackers.Add(value);
                    break;
            }
        }

        if (hash is null) return false;
        magnet = new MagnetInfo
        {
            Uri = text,
            InfoHash = hash,
            DisplayName = string.IsNullOrWhiteSpace(name) ? null : name,
            ExactLength = length,
            Trackers = trackers,
        };
        return true;
    }

    private static string? ParseExactTopic(string xt)
    {
        const string v1 = "urn:btih:";
        const string v2 = "urn:btmh:";
        if (xt.StartsWith(v1, StringComparison.OrdinalIgnoreCase))
        {
            var h = xt[v1.Length..];
            if (Hex40().IsMatch(h)) return h.ToLowerInvariant();
            if (Base32_32().IsMatch(h)) return Base32ToHex(h.ToUpperInvariant());
            return null;
        }
        if (xt.StartsWith(v2, StringComparison.OrdinalIgnoreCase))
        {
            var h = xt[v2.Length..];
            return HexAny().IsMatch(h) && h.Length >= 68 ? h.ToLowerInvariant() : null;
        }
        return null;
    }

    private static string Base32ToHex(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new byte[20];
        int buffer = 0, bits = 0, index = 0;
        foreach (var c in base32)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes[index++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
            }
        }
        return Convert.ToHexStringLower(bytes);
    }

    [GeneratedRegex("^[0-9a-fA-F]{40}$")] private static partial Regex Hex40();
    [GeneratedRegex("^[A-Za-z2-7]{32}$")] private static partial Regex Base32_32();
    [GeneratedRegex("^[0-9a-fA-F]+$")] private static partial Regex HexAny();
}
