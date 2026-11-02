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
            key.SetValue("", $"URL:{Tlp.Core.AppInfo.Name} session link");
            key.SetValue("URL Protocol", "");
            using var cmd = key.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch (UnauthorizedAccessException)
        {
            // Not fatal: friends can still paste the link.
        }
    }

    /// <summary>
    /// Gives the app's entry in Windows' Apps list its full name. The installer names it with the
    /// short name (which the Start menu shortcut also uses, and a shortcut can't contain ":").
    /// Runs after install, after each update and at start, and only touches our own entry.
    /// </summary>
    public static void EnsureFullName()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\TheLastPiece", writable: true);
            if (key is null) return;
            if (key.GetValue("DisplayName") as string != Tlp.Core.AppInfo.FullName) key.SetValue("DisplayName", Tlp.Core.AppInfo.FullName);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Not fatal: the entry keeps the short name.
        }
    }

    /// <summary>Removes tlp:// on uninstall, but only if it still points at this copy of the app.</summary>
    public static void Unregister()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var cmd = Registry.CurrentUser.OpenSubKey(@"Software\Classes\tlp\shell\open\command");
            var current = cmd?.GetValue("") as string;
            var exe = Environment.ProcessPath;
            if (current is null || exe is null || !current.Contains(exe, StringComparison.OrdinalIgnoreCase)) return;
            cmd!.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\tlp", throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
        }
    }
}
