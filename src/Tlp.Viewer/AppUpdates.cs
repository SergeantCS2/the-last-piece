using Velopack;
using Velopack.Sources;

namespace Tlp.Viewer;

/// <summary>
/// Updates for installed copies (Setup.exe). Checks GitHub Releases in the background and downloads
/// a newer version; Velopack installs it the next time the app starts, or straight away if the
/// person presses "Restart to update". Does nothing for the unzipped (portable zip) copy.
/// </summary>
public static class AppUpdates
{
    public const string ReleasesRepo = "https://github.com/SergeantCS2/the-last-piece";

    private static UpdateManager? _manager;
    private static UpdateInfo? _ready;

    /// <summary>Returns the new version's number once it's downloaded, or null if there's nothing to do.</summary>
    public static async Task<string?> DownloadInBackgroundAsync(CancellationToken ct = default)
    {
        try
        {
            _manager ??= new UpdateManager(new GithubSource(ReleasesRepo, null, false));
            if (!_manager.IsInstalled) return null;
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return null;
            await _manager.DownloadUpdatesAsync(info, null, ct);
            _ready = info;
            return info.TargetFullRelease.Version.ToString();
        }
        catch (Exception)
        {
            // Offline, GitHub rate limit, no releases yet: try again next start.
            return null;
        }
    }

    public static void RestartNow()
    {
        if (_manager is not null && _ready is not null) _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }

    public static string CurrentVersion =>
        (_manager?.IsInstalled == true ? _manager.CurrentVersion?.ToString() : null)
        ?? typeof(AppUpdates).Assembly.GetName().Version?.ToString(3) ?? "?";
}
