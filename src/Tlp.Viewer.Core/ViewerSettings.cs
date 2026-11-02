using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tlp.Viewer.Core;

[JsonConverter(typeof(JsonStringEnumConverter<SourcePreference>))]
public enum SourcePreference
{
    /// <summary>Group torrent, then the host's copy, then the public torrent.</summary>
    [JsonStringEnumMemberName("auto")] Auto,
    /// <summary>Torrents first (download it yourself), the host's copy last.</summary>
    [JsonStringEnumMemberName("preferTorrent")] PreferTorrent,
    /// <summary>The host's copy first.</summary>
    [JsonStringEnumMemberName("preferShelf")] PreferShelf,
}

public sealed record ViewerSettings
{
    public string Name { get; init; } = Environment.UserName;
    public string MovieNightFolder { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Movie Night");
    /// <summary>Syncplay.exe; defaults to the copy bundled next to the viewer.</summary>
    public string? SyncplayPath { get; init; }
    /// <summary>mpv.exe; defaults to the copy bundled next to the viewer.</summary>
    public string? MpvPath { get; init; }
    public SourcePreference SourcePreference { get; init; } = SourcePreference.Auto;
    /// <summary>Keep sharing finished torrents with the group while the app is open.</summary>
    public bool SeedWhileOpen { get; init; } = true;
    public string ChatStyle { get; init; } = "Chatroom";

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TheLastPiece", "settings.json");

    public static ViewerSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), ViewerJsonContext.Default.ViewerSettings) ?? new() : new();
        }
        catch (System.Text.Json.JsonException)
        {
            return new();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, ViewerJsonContext.Default.ViewerSettings));
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ViewerSettings))]
internal sealed partial class ViewerJsonContext : JsonSerializerContext;
