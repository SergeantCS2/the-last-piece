namespace Tlp.Core;

/// <summary>
/// Pack file names come from the network, so viewers must never let one escape the
/// movie night folder or produce a name Windows can't create.
/// </summary>
public static class SafeFileName
{
    private static readonly char[] Invalid =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*', .. Enumerable.Range(0, 32).Select(i => (char)i)];

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>True if <paramref name="name"/> is a plain file name that is safe on Windows and Linux.</summary>
    public static bool IsSafe(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240) return false;
        if (name is "." or "..") return false;
        if (name.IndexOfAny(Invalid) >= 0) return false;
        if (name.EndsWith(' ') || name.EndsWith('.')) return false;
        var stem = name.Split('.')[0];
        return !Reserved.Contains(stem);
    }

    /// <summary>Turns any string into a safe file name by replacing bad characters with '_'.</summary>
    public static string Sanitize(string name)
    {
        var chars = name.Select(c => Invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim().TrimEnd('.');
        if (cleaned.Length > 240) cleaned = cleaned[..240];
        if (cleaned.Length == 0 || cleaned is "." or "..") cleaned = "file";
        if (Reserved.Contains(cleaned.Split('.')[0])) cleaned = "_" + cleaned;
        return cleaned;
    }
}
