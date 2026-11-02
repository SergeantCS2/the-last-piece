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

    /// <summary>Where updates come from: GitHub Releases, or a local folder for the CI update test.</summary>
    private static IUpdateSource Source { get; set; } = new GithubSource(ReleasesRepo, null, false);

    /// <summary>The last error from a check, for the update test's log. Normal use ignores it.</summary>
    public static Exception? LastError { get; private set; }

    /// <summary>Returns the new version's number once it's downloaded, or null if there's nothing to do.</summary>
    public static async Task<string?> DownloadInBackgroundAsync(CancellationToken ct = default)
    {
        try
        {
            _manager ??= new UpdateManager(Source);
            if (!_manager.IsInstalled) return null;
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return null;
            await _manager.DownloadUpdatesAsync(info, null, ct);
            _ready = info;
            return info.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex)
        {
            // Offline, GitHub rate limit, no releases yet: try again next start.
            LastError = ex;
            return null;
        }
    }

    /// <summary>
    /// CI only (--update-from FOLDER LOG): the installed app checks a local folder of packages
    /// instead of GitHub, downloads the newer one the same way it would normally, then hands over
    /// to Velopack to install it and exits. CI then checks the installed version changed. This
    /// proves the update path without publishing a release.
    /// </summary>
    public static async Task<int> TestUpdateFromAsync(string folder, string log)
    {
        var lines = new List<string> { $"running {CurrentVersionOf(new UpdateManager(new SimpleFileSource(new DirectoryInfo(folder))))}" };
        Source = new SimpleFileSource(new DirectoryInfo(folder));
        var version = await DownloadInBackgroundAsync();
        lines.Add(version is null ? $"no update found: {LastError?.Message ?? "nothing newer"}" : $"downloaded {version}");
        File.WriteAllLines(log, lines);
        if (version is null || _manager is null || _ready is null) return 1;
        _manager.ApplyUpdatesAndExit(_ready.TargetFullRelease);
        return 0;
    }

    private static string CurrentVersionOf(UpdateManager m) => m.IsInstalled ? m.CurrentVersion?.ToString() ?? "?" : "not installed";

    public static void RestartNow()
    {
        if (_manager is not null && _ready is not null) _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease);
    }

    public static string CurrentVersion =>
        (_manager?.IsInstalled == true ? _manager.CurrentVersion?.ToString() : null)
        ?? typeof(AppUpdates).Assembly.GetName().Version?.ToString(3) ?? "?";
}
