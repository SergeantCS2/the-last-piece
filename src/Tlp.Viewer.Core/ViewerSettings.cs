using Tlp.Core;

namespace Tlp.Viewer.Core;

public enum SourcePreference
{
    /// <summary>Shelf download when the host offers one, otherwise the magnet.</summary>
    Auto,
    /// <summary>Use the magnet (download it yourself from the swarm) when there is one.</summary>
    PreferTorrent,
    /// <summary>Use the host's shelf when there is one.</summary>
    PreferShelf,
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
            return File.Exists(path) ? PackJson.Deserialize<ViewerSettings>(File.ReadAllText(path)) ?? new() : new();
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
        File.WriteAllText(path, PackJson.Serialize(this));
    }
}
