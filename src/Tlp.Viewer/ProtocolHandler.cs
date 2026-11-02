using Microsoft.Win32;

namespace Tlp.Viewer;

/// <summary>Registers tlp:// for the current Windows user so "Open in The Last Piece" links work.</summary>
public static class ProtocolHandler
{
    public static void EnsureRegistered()
    {
        if (!OperatingSystem.IsWindows()) return;
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\tlp");
            key.SetValue("", "URL:The Last Piece movie night");
            key.SetValue("URL Protocol", "");
            using var cmd = key.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch (UnauthorizedAccessException)
        {
            // Not fatal: friends can still paste the link.
        }
    }
}
