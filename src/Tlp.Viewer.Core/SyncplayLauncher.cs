using System.Diagnostics;
using System.Text;
using Tlp.Core;

namespace Tlp.Viewer.Core;

/// <summary>
/// Starts Syncplay already pointed at the movie night room, with mpv as the player.
/// Nothing is written to the user's own Syncplay settings:
/// - The bundled Syncplay gets a portable syncplay.ini next to Syncplay.exe. Syncplay 1.7.6
///   reads that instead of %APPDATA%\syncplay.ini (ConfigurationGetter._checkForPortableFile),
///   and it turns off the connect window and update pop-ups.
/// - Room-specific settings go in a ".syncplay" file in the movie night folder, which Syncplay
///   reads for files opened from that folder.
/// - Server, room, name and password are command-line arguments, with --no-store.
/// </summary>
public static class SyncplayLauncher
{
    public const string RelativeConfigName = ".syncplay";
    public const string PortableConfigName = "syncplay.ini";

    /// <summary>
    /// Settings for the bundled Syncplay. forceGuiPrompt = False skips the "connect" window when a
    /// file is given (Syncplay still shows it if a setting is missing). Update checks are off
    /// because the app ships a known Syncplay version.
    /// </summary>
    public static string PortableConfig(string chatStyle = "Chatroom")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"; Written by {AppInfo.Name}. Settings for the Syncplay bundled with it only.");
        sb.AppendLine("[client_settings]");
        sb.AppendLine("forceGuiPrompt = False");
        sb.AppendLine("pauseOnLeave = True");
        sb.AppendLine("[gui]");
        sb.AppendLine("chatInputEnabled = True");
        sb.AppendLine("chatOutputEnabled = True");
        sb.AppendLine($"chatOutputMode = {(chatStyle == "Scrolling" ? "Scrolling" : "Chatroom")}");
        sb.AppendLine("[general]");
        sb.AppendLine("checkForUpdatesAutomatically = False");
        return sb.ToString();
    }

    public static string RelativeConfig(Pack pack, string folder, string chatStyle = "Chatroom")
    {
        var trusted = new List<string>();
        foreach (var src in pack.Items.SelectMany(i => i.Sources).Where(s => s.Kind == PackSourceKind.Http))
            if (Uri.TryCreate(src.Uri, UriKind.Absolute, out var u) && !trusted.Contains(u.Host)) trusted.Add(u.Host);

        var sb = new StringBuilder();
        sb.AppendLine($"; Written by {AppInfo.Name} for this movie night folder.");
        sb.AppendLine("[client_settings]");
        sb.AppendLine($"mediaSearchDirectories = {PyList([folder])}");
        sb.AppendLine("sharedPlaylistEnabled = True");
        sb.AppendLine("onlySwitchToTrustedDomains = True");
        sb.AppendLine($"trustedDomains = {PyList(trusted)}");
        sb.AppendLine("pauseOnLeave = True");
        sb.AppendLine("[gui]");
        sb.AppendLine("chatInputEnabled = True");
        sb.AppendLine("chatOutputEnabled = True");
        sb.AppendLine($"chatOutputMode = {(chatStyle == "Scrolling" ? "Scrolling" : "Chatroom")}");
        return sb.ToString();
    }

    public static IReadOnlyList<string> Arguments(Pack pack, string name, string mpvPath, string firstFile, PlayerOptions? player = null)
    {
        var args = new List<string>
        {
            "--host", $"{pack.Syncplay.Host}:{pack.Syncplay.Port}",
            "--name", name,
            "--room", pack.Syncplay.Room,
            "--player-path", mpvPath,
            "--no-store",
        };
        if (!string.IsNullOrEmpty(pack.Syncplay.Password))
        {
            args.Add("--password");
            args.Add(pack.Syncplay.Password);
        }
        args.Add(firstFile);
        // Everything after "--" goes to mpv (Syncplay's "_args"; checked in 1.7.6 ConfigurationGetter
        // and players/mpv.py getStartupArgs, which turns "--name=value" into mpv options).
        var mpvArgs = (player ?? new PlayerOptions()).MpvArguments();
        if (mpvArgs.Count > 0)
        {
            args.Add("--");
            args.AddRange(mpvArgs);
        }
        return args;
    }

    /// <param name="bundled">True for the Syncplay inside the app folder, which gets the portable settings file.
    /// False for someone's own Syncplay install, which is left untouched (its connect window will show).</param>
    public static Process Launch(Pack pack, string folder, string name, string syncplayPath, string mpvPath, string chatStyle = "Chatroom", bool bundled = true, PlayerOptions? player = null)
    {
        var first = pack.Items.Select(i => FileCheck.TargetPath(folder, i)).FirstOrDefault(File.Exists)
                    ?? throw new FileNotFoundException("None of tonight's files are downloaded yet.");
        File.WriteAllText(Path.Combine(folder, RelativeConfigName), RelativeConfig(pack, folder, chatStyle));
        if (bundled)
        {
            try
            {
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(syncplayPath)!, PortableConfigName), PortableConfig(chatStyle));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Read-only app folder (e.g. Program Files): Syncplay shows its connect window, filled in.
            }
        }

        var psi = new ProcessStartInfo(syncplayPath) { UseShellExecute = false, WorkingDirectory = folder };
        foreach (var a in Arguments(pack, name, mpvPath, first, player)) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("Syncplay didn't start.");
    }

    /// <summary>A Python list literal, which is how Syncplay stores list settings.</summary>
    public static string PyList(IEnumerable<string> items) =>
        "[" + string.Join(", ", items.Select(s => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'")) + "]";
}

/// <summary>Per-viewer player choices, passed to mpv when Syncplay starts it. Nothing here is synced.</summary>
public sealed record PlayerOptions(int MaxVolume = PlayerOptions.DefaultMaxVolume, TrackChoice Tracks = TrackChoice.Original)
{
    public const int DefaultMaxVolume = 300;
    public static readonly IReadOnlyList<int> VolumeChoices = [130, 200, 300];

    private const string English = "en,eng,enUS,en-US";

    /// <summary>
    /// mpv options as "--name=value". Every value is non-empty: Syncplay 1.7.6 reads value[0]
    /// to strip quotes and fails on an empty one.
    /// </summary>
    public IReadOnlyList<string> MpvArguments()
    {
        var args = new List<string> { $"--volume-max={Math.Clamp(MaxVolume, 100, 300)}" };
        switch (Tracks)
        {
            case TrackChoice.EnglishDub:
                args.Add($"--alang={English},jpn,ja");
                args.Add($"--slang={English}");
                // Subtitles in the audio's language: only a forced (signs and songs) track.
                args.Add("--subs-with-matching-audio=forced");
                break;
            case TrackChoice.NoSubtitles:
                args.Add("--sid=no");
                break;
            // Original: mpv.conf already prefers Japanese audio and English subtitles.
        }
        return args;
    }
}
