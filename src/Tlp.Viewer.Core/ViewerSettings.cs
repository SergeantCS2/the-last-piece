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

/// <summary>Which audio and subtitle tracks the player picks when a file opens. Anyone can still switch in the player.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrackChoice>))]
public enum TrackChoice
{
    /// <summary>Original audio (Japanese for anime) with full English subtitles.</summary>
    [JsonStringEnumMemberName("original")] Original,
    /// <summary>English audio, with only the signs-and-songs subtitle track when the file has one.</summary>
    [JsonStringEnumMemberName("englishDub")] EnglishDub,
    /// <summary>Original audio, subtitles off.</summary>
    [JsonStringEnumMemberName("noSubtitles")] NoSubtitles,
}

// Properties use set, not init: the JSON source generator fills init-only properties through an
// object initializer, so a setting missing from an older settings.json became false/0/null
// instead of keeping its default here.
public sealed record ViewerSettings
{
    public string Name { get; set; } = Environment.UserName;
    public string MovieNightFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "The Last Piece");
    /// <summary>Syncplay.exe; defaults to the copy bundled next to the viewer.</summary>
    public string? SyncplayPath { get; set; }
    /// <summary>mpv.exe; defaults to the copy bundled next to the viewer.</summary>
    public string? MpvPath { get; set; }
    public SourcePreference SourcePreference { get; set; } = SourcePreference.Auto;
    /// <summary>Keep sharing finished torrents with the group while the app is open.</summary>
    public bool SeedWhileOpen { get; set; } = true;
    public string ChatStyle { get; set; } = "Chatroom";
    /// <summary>Tell the night's host your name and how far your downloads have got (shown on their "who's ready" list).</summary>
    public bool ShareProgress { get; set; } = true;
    /// <summary>Highest volume the player allows, in percent (100 to 300). Above 100 is amplified.</summary>
    public int MaxVolume { get; set; } = PlayerOptions.DefaultMaxVolume;
    /// <summary>Audio and subtitle tracks picked when a file opens.</summary>
    public TrackChoice Tracks { get; set; } = TrackChoice.Original;
    /// <summary>Name of the app's look (a file in the viewer's Themes folder).</summary>
    public string Theme { get; set; } = "Default";
    /// <summary>Let the look animate (glow, glitch, flicker). Off keeps it still.</summary>
    public bool Motion { get; set; } = true;
    /// <summary>The group name from the last night opened, so the title stays put between nights.</summary>
    public string? GroupName { get; set; }
    /// <summary>The first-run welcome was finished (name, folder, look). Until then the app shows it on start.</summary>
    public bool SetupDone { get; set; }

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
