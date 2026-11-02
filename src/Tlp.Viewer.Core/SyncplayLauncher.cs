using System.Diagnostics;
using System.Text;
using Tlp.Core;

namespace Tlp.Viewer.Core;

/// <summary>
/// Starts Syncplay already pointed at the movie night room, with mpv as the player.
/// Nothing is written to the user's own Syncplay settings: the room-specific settings go
/// in a ".syncplay" file inside the movie night folder (Syncplay reads it for files
/// opened from that folder) and the rest are command-line arguments with --no-store.
/// </summary>
public static class SyncplayLauncher
{
    public const string RelativeConfigName = ".syncplay";

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

    public static Process Launch(Pack pack, string folder, string name, string syncplayPath, string mpvPath, string chatStyle = "Chatroom")
    {
        var first = pack.Items.Select(i => FileCheck.TargetPath(folder, i)).FirstOrDefault(File.Exists)
                    ?? throw new FileNotFoundException("None of tonight's files are downloaded yet.");
        File.WriteAllText(Path.Combine(folder, RelativeConfigName), RelativeConfig(pack, folder, chatStyle));

        var psi = new ProcessStartInfo(syncplayPath) { UseShellExecute = false, WorkingDirectory = folder };
        foreach (var a in Arguments(pack, name, mpvPath, first)) psi.ArgumentList.Add(a);
        return Process.Start(psi) ?? throw new InvalidOperationException("Syncplay didn't start.");
    }

    /// <summary>A Python list literal, which is how Syncplay stores list settings.</summary>
    public static string PyList(IEnumerable<string> items) =>
        "[" + string.Join(", ", items.Select(s => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'")) + "]";
}
