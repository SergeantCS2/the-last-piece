namespace Tlp.Viewer.Core;

/// <summary>
/// A small plain-text log of what the app did (links opened, downloads, joins, problems), so a
/// tester can send it with a bug report. Kept next to settings.json, under 1 MB plus one older file.
/// Never holds passwords: callers log names, hosts and outcomes only.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TheLastPiece", "logs");

    public static string FilePath => Path.Combine(Folder, "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(Folder, "app.old.log"), overwrite: true);
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  {message.ReplaceLineEndings(" ")}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never stop the app.
        }
    }

    /// <summary>The last lines of the log, oldest first.</summary>
    public static IReadOnlyList<string> Tail(int lines)
    {
        try
        {
            lock (Gate)
                return File.Exists(FilePath) ? File.ReadLines(FilePath).TakeLast(lines).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
