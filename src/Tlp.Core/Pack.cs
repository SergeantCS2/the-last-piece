using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tlp.Core;

/// <summary>
/// One movie night, as handed to viewers. Served by the host at /n/{token}/pack.json.
/// Everything a viewer needs: what to download, from where, and which Syncplay room to join.
/// </summary>
public sealed record Pack
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required string Title { get; init; }
    /// <summary>When the night starts, with offset. Null if not scheduled.</summary>
    public DateTimeOffset? StartsAt { get; init; }
    public required SyncplayInfo Syncplay { get; init; }
    public required IReadOnlyList<PackItem> Items { get; init; }
}

public sealed record SyncplayInfo
{
    public required string Host { get; init; }
    public int Port { get; init; } = 8999;
    public required string Room { get; init; }
    /// <summary>Server password. Shared with everyone who has the night link.</summary>
    public string? Password { get; init; }
}

public sealed record PackItem
{
    /// <summary>
    /// Exact file name to save as. Syncplay's shared playlist matches files by name,
    /// so every viewer must end up with this name.
    /// </summary>
    public required string FileName { get; init; }
    /// <summary>Size in bytes, if known (always known for shelf files).</summary>
    public long? Size { get; init; }
    /// <summary>
    /// SHA-256 of the file as lowercase hex, when the host has computed it. Viewers use it to
    /// prove they have the same file as everyone else, however they got it.
    /// </summary>
    public string? Sha256 { get; init; }
    public required IReadOnlyList<PackSource> Sources { get; init; }
}

public enum PackSourceKind
{
    /// <summary>Direct HTTPS download from the host's shelf (supports resume).</summary>
    Http,
    /// <summary>A magnet link, downloaded with the viewer's built-in torrent client.</summary>
    Magnet,
    /// <summary>
    /// Reserved for the group torrent (P2P between friends, host shelf as web seed).
    /// Uri points at a .torrent file served by the host. Not produced yet.
    /// </summary>
    Torrent,
}

public sealed record PackSource
{
    public required PackSourceKind Kind { get; init; }
    public required string Uri { get; init; }
}

public static class PackJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
