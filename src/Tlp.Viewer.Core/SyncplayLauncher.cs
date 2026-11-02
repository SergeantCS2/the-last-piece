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
        sb.AppendLine("; Written by The Last Piece. Settings for the Syncplay bundled with it only.");
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
        sb.AppendLine("; Written by The Last Piece for this movie night folder.");
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

    public static IReadOnlyList<string> Arguments(Pack pack, string name, string mpvPath, string firstFile)
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
        return args;
    }

    /// <param name="bundled">True for the Syncplay inside the app folder, which gets the portable settings file.
    /// False for someone's own Syncplay install, which is left untouched (its connect window will show).</param>
    public static Process Launch(Pack pack, string folder, string name, string syncplayPath, string mpvPath, string chatStyle = "Chatroom", bool bundled = true)
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
        foreach (var a in Arguments(pack, name, mpvPath, first)) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("Syncplay didn't start.");
    }

    /// <summary>A Python list literal, which is how Syncplay stores list settings.</summary>
    public static string PyList(IEnumerable<string> items) =>
        "[" + string.Join(", ", items.Select(s => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'")) + "]";
}
