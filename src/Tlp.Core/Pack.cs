using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tlp.Core;

/// <summary>
/// One session, as handed to viewers. Served by the host at /n/{token}/pack.json.
/// Everything a viewer needs: what to download, from where, and which Syncplay room to join.
/// </summary>
public sealed record Pack
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public required string Title { get; init; }
    /// <summary>
    /// The group's name, shown as the viewer app's big title (e.g. "Friday crew"). Set by the
    /// host; null means the app's default, the app's name.
    /// </summary>
    public string? Group { get; init; }
    /// <summary>When the night starts, with offset. Null if not scheduled.</summary>
    public DateTimeOffset? StartsAt { get; init; }
    public required SyncplayInfo Syncplay { get; init; }
    public required IReadOnlyList<PackItem> Items { get; init; }
    /// <summary>
    /// Optional address where viewers post a <see cref="ViewerReport"/> (name and download progress),
    /// so the host can see who's ready. Viewers can turn this off.
    /// </summary>
    public string? StatusUri { get; init; }
}

/// <summary>What a viewer tells the host: who they are and how far each file has got.</summary>
public sealed record ViewerReport
{
    public const int MaxNameLength = 32;
    public required string Name { get; init; }
    /// <summary>The viewer app's version, for "who needs to update".</summary>
    public string? App { get; init; }
    public required IReadOnlyList<ViewerFileReport> Files { get; init; }
}

public sealed record ViewerFileReport
{
    public required string FileName { get; init; }
    public required ViewerFileState State { get; init; }
    /// <summary>0 to 100.</summary>
    public int Percent { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ViewerFileState>))]
public enum ViewerFileState
{
    [JsonStringEnumMemberName("missing")] Missing,
    [JsonStringEnumMemberName("downloading")] Downloading,
    [JsonStringEnumMemberName("checking")] Checking,
    [JsonStringEnumMemberName("ready")] Ready,
    [JsonStringEnumMemberName("failed")] Failed,
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

[JsonConverter(typeof(JsonStringEnumConverter<PackSourceKind>))]
public enum PackSourceKind
{
    /// <summary>Direct HTTPS download from the host's shelf (supports resume).</summary>
    [JsonStringEnumMemberName("http")] Http,
    /// <summary>A magnet link, downloaded with the viewer's built-in torrent client.</summary>
    [JsonStringEnumMemberName("magnet")] Magnet,
    /// <summary>
    /// The host's private group torrent: friends share pieces, the host's shelf link is the
    /// web seed and the host runs the tracker. Uri points at the .torrent file.
    /// </summary>
    [JsonStringEnumMemberName("torrent")] Torrent,
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

    [RequiresUnreferencedCode("Reflection-based; the trimmed viewer uses SerializePack.")]
    [RequiresDynamicCode("Reflection-based; the trimmed viewer uses SerializePack.")]
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    [RequiresUnreferencedCode("Reflection-based; the trimmed viewer uses DeserializePack.")]
    [RequiresDynamicCode("Reflection-based; the trimmed viewer uses DeserializePack.")]
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    // Reflection-free versions for the viewer app, which is trimmed to keep the download small.
    public static string SerializePack(Pack pack) => JsonSerializer.Serialize(pack, CoreJsonContext.Default.Pack);
    public static Pack? DeserializePack(string json) => JsonSerializer.Deserialize(json, CoreJsonContext.Default.Pack);
    public static string SerializeReport(ViewerReport report) => JsonSerializer.Serialize(report, CoreJsonContext.Default.ViewerReport);
    public static ViewerReport? DeserializeReport(string json) => JsonSerializer.Deserialize(json, CoreJsonContext.Default.ViewerReport);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Pack))]
[JsonSerializable(typeof(ViewerReport))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
