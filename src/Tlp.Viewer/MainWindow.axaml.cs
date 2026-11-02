using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Tlp.Core;
using Tlp.Viewer.Core;

namespace Tlp.Viewer;

public partial class MainWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ObservableCollection<ItemRow> _rows = [];
    private ViewerSettings _settings = ViewerSettings.Load();
    private TorrentDownloader? _torrents;
    private Pack? _pack;
    private CancellationTokenSource? _downloading;
    private readonly DispatcherTimer _reportTimer;
    private bool _reportDirty;
    private DateTime _lastReport = DateTime.MinValue;
    private bool _reporting;

    public MainWindow() : this(null) { }

    public MainWindow(string? startupLink)
    {
        InitializeComponent();
        Title = Tlp.Core.AppInfo.Name;
        ShowGroup(_settings.GroupName);
        // The looks' motion (Themes/) pauses while the window is in the background, so a look
        // never costs anything while the movie plays.
        Activated += (_, _) => SetStill(false);
        Deactivated += (_, _) => SetStill(true);
        MotionBox.IsCheckedChanged += (_, _) => SetStill(!IsActive);
        ItemsList.ItemsSource = _rows;
        ShowSettings();

        LoadButton.Click += async (_, _) => await LoadAsync(LinkBox.Text);
        DownloadButton.Click += async (_, _) => await DownloadAllAsync();
        JoinButton.Click += async (_, _) => await JoinAsync();
        SaveButton.Click += (_, _) => SaveSettings();
        // Picking a look previews it straight away; Save settings keeps it.
        ThemeBox.SelectionChanged += (_, _) =>
        {
            if (ThemeBox.SelectedItem is string name && Avalonia.Application.Current is { } app) ThemeManager.Apply(app, name);
        };
        BrowseButton.Click += async (_, _) => await BrowseAsync();
        Closing += async (_, _) => { _downloading?.Cancel(); if (_torrents is not null) await _torrents.DisposeAsync(); };
        _reportTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, async (_, _) => await ReportIfDueAsync());
        _reportTimer.Start();
        UpdateButton.Click += (_, _) =>
        {
            if (_downloading is not null) { UpdateText.Text = "Wait for downloads to finish, then restart to update."; return; }
            AppUpdates.RestartNow();
        };
        Opened += async (_, _) =>
        {
            if (Program.SelfTestDir is not null || Program.Showcase is not null) return;
            if (await AppUpdates.DownloadInBackgroundAsync() is { } version)
            {
                UpdateText.Text = $"Version {version} is ready. It installs next time you open the app, or now if you restart.";
                UpdateBar.IsVisible = true;
            }
        };

        if (!string.IsNullOrWhiteSpace(startupLink))
        {
            LinkBox.Text = startupLink;
            Opened += async (_, _) => await LoadAsync(startupLink);
        }
        else
        {
            StatusText.Text = "Paste the link the host sent you, then press Open.";
        }
    }

    private static string BundledPath(params string[] parts) => Path.Combine([AppContext.BaseDirectory, .. parts]);
    private string MpvPath => _settings.MpvPath ?? BundledPath("mpv", "mpv.exe");
    private string SyncplayPath => _settings.SyncplayPath ?? BundledPath("syncplay", "Syncplay.exe");

    private void SetStill(bool still)
    {
        still |= MotionBox.IsChecked == false;
        foreach (var part in new Control[] { Backdrop, Overlay, Brand, HeaderSide })
            part.Classes.Set("still", still);
    }

    private void ShowSettings()
    {
        NameBox.Text = _settings.Name;
        FolderBox.Text = _settings.MovieNightFolder;
        SourceBox.SelectedIndex = (int)_settings.SourcePreference;
        ChatBox.SelectedIndex = _settings.ChatStyle == "Scrolling" ? 1 : 0;
        SeedBox.IsChecked = _settings.SeedWhileOpen;
        ShareBox.IsChecked = _settings.ShareProgress;
        MotionBox.IsChecked = _settings.Motion;
        TracksBox.SelectedIndex = (int)_settings.Tracks;
        VolumeBox.ItemsSource = PlayerOptions.VolumeChoices.Select(v => $"{v}%").ToList();
        VolumeBox.SelectedIndex = Math.Max(0, PlayerOptions.VolumeChoices.ToList().IndexOf(
            PlayerOptions.VolumeChoices.LastOrDefault(v => v <= _settings.MaxVolume, PlayerOptions.VolumeChoices[0])));
        PlayerKeys.Text = PlayerHelp(_settings.MaxVolume);
        ThemeBox.ItemsSource = ThemeManager.Available;
        ThemeBox.SelectedItem = ThemeManager.Normalize(_settings.Theme);
        // Only one look so far; the choice appears once there are more.
        ThemeLabel.IsVisible = ThemeBox.IsVisible = ThemeManager.Available.Count > 1;
    }

    /// <summary>The keys from the bundled mpv's input.conf, in a line people can read before joining.</summary>
    private static string PlayerHelp(int maxVolume) =>
        $"In the player: right-click for the menu, S picks subtitles, A picks audio, V and B cycle them like VLC. " +
        $"Scroll or Ctrl+Up/Down for volume, up to {Math.Clamp(maxVolume, 100, 300)}%. Enter to chat.";

    private void SaveSettings()
    {
        _settings = _settings with
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? Environment.UserName : NameBox.Text.Trim(),
            MovieNightFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? _settings.MovieNightFolder : FolderBox.Text.Trim(),
            SourcePreference = (SourcePreference)Math.Max(0, SourceBox.SelectedIndex),
            ChatStyle = ChatBox.SelectedIndex == 1 ? "Scrolling" : "Chatroom",
            SeedWhileOpen = SeedBox.IsChecked == true,
            ShareProgress = ShareBox.IsChecked == true,
            Motion = MotionBox.IsChecked != false,
            Tracks = (TrackChoice)Math.Clamp(TracksBox.SelectedIndex, 0, 2),
            MaxVolume = PlayerOptions.VolumeChoices[Math.Clamp(VolumeBox.SelectedIndex, 0, PlayerOptions.VolumeChoices.Count - 1)],
            Theme = ThemeBox.SelectedItem as string ?? ThemeManager.DefaultTheme,
        };
        _settings.Save();
        PlayerKeys.Text = PlayerHelp(_settings.MaxVolume);
        if (Avalonia.Application.Current is { } app) ThemeManager.Apply(app, _settings.Theme);
        _reportDirty = true;
        StatusText.Text = "Settings saved.";
        RefreshStatus();
    }

    private async Task BrowseAsync()
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Movie night folder", AllowMultiple = false });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path) FolderBox.Text = path;
    }

    private async Task LoadAsync(string? input)
    {
        var uri = PackLink.ToPackUri(input);
        if (uri is null)
        {
            StatusText.Text = "That doesn't look like a movie night link. It should look like https://…/n/…";
            return;
        }
        StatusText.Text = "Opening movie night…";
        try
        {
            _pack = await new PackClient(Http).FetchAsync(uri);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or TaskCanceledException)
        {
            StatusText.Text = "Couldn't open that movie night. " + NetCheck.ExplainLinkFailure(ex, uri);
            return;
        }

        ShowPack(_pack);
    }

    public const string DefaultTitle = "Movie night";

    /// <summary>The big title: the host's group name, or "Movie night".</summary>
    private void ShowGroup(string? group) =>
        Brand.Content = string.IsNullOrWhiteSpace(group) ? DefaultTitle : group.Trim();

    public void ShowPack(Pack pack)
    {
        _pack = pack;
        ShowGroup(pack.Group);
        if (pack.Group != _settings.GroupName)
        {
            _settings = _settings with { GroupName = pack.Group };
            try { _settings.Save(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        NightTitle.Text = pack.Title;
        NightInfo.Text = (pack.StartsAt is { } at ? $"Starts {at.ToLocalTime():dddd d MMM, h:mm tt}. " : "")
                         + $"{pack.Items.Count} file(s). Room \"{pack.Syncplay.Room}\".";
        _rows.Clear();
        foreach (var item in pack.Items)
        {
            var row = new ItemRow(item);
            row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ItemRow.Shared)) _reportDirty = true; };
            _rows.Add(row);
        }
        RefreshStatus();
        _reportDirty = true;
    }

    internal IList<ItemRow> Rows => _rows;

    /// <summary>A link clicked while the app was already open.</summary>
    public void OpenFromOutside(string link)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (string.IsNullOrWhiteSpace(link)) return;
        LinkBox.Text = link;
        if (_downloading is not null)
        {
            StatusText.Text = "Downloads are running for this night. Press Open on the new link once they finish.";
            return;
        }
        _ = LoadAsync(link);
    }

    private void RefreshStatus()
    {
        if (_pack is null) return;
        var session = NewSession();
        foreach (var (row, status) in _rows.Zip(session.QuickStatus())) row.Apply(status);
        var missing = _rows.Count(r => !r.Ready);
        DownloadButton.IsEnabled = missing > 0 && _downloading is null;
        JoinButton.IsEnabled = _rows.Any(r => r.Ready);
        StatusText.Text = missing == 0 ? "Everything's downloaded. Join when it's time." : $"{missing} file(s) to download into {_settings.MovieNightFolder}.";
    }

    private NightSession NewSession()
    {
        _torrents ??= new TorrentDownloader(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheLastPiece", "torrent-cache"), _settings.SeedWhileOpen);
        return new NightSession(_pack!, _settings, new HttpDownloader(Http), _torrents);
    }

    private async Task DownloadAllAsync()
    {
        if (_pack is null) return;
        _downloading = new CancellationTokenSource();
        DownloadButton.IsEnabled = false;
        var session = NewSession();
        var failures = 0;
        foreach (var row in _rows.Where(r => !r.Ready).ToList())
        {
            var progress = new Progress<ItemStatus>(s => Dispatcher.UIThread.Post(() => row.Apply(s)));
            try
            {
                StatusText.Text = $"Getting {row.Name}…";
                var result = await Task.Run(() => session.FetchAsync(row.Item, progress, _downloading.Token));
                row.Apply(result);
                if (result.State is not (FileState.SizeMatches or FileState.Verified)) failures++;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                row.Fail("Failed: " + ex.Message);
            }
        }
        _downloading = null;
        RefreshStatus();
        if (failures > 0) StatusText.Text = $"{failures} file(s) didn't download. Press Download all to retry; finished parts are kept.";
    }

    private DateTime _joinWarnedAt = DateTime.MinValue;

    private async Task JoinAsync()
    {
        if (_pack is null) return;
        if (!File.Exists(SyncplayPath) || !File.Exists(MpvPath))
        {
            StatusText.Text = "Syncplay or mpv is missing from the app folder. Re-download the viewer zip.";
            return;
        }
        // Check the room server answers first, so a closed port gets a clear message here rather
        // than Syncplay retrying on its own. A second press within a minute starts it anyway.
        var sp = _pack.Syncplay;
        if (DateTime.UtcNow - _joinWarnedAt > TimeSpan.FromMinutes(1))
        {
            StatusText.Text = "Checking the room server…";
            JoinButton.IsEnabled = false;
            var ok = await NetCheck.PortAnswersAsync(sp.Host, sp.Port, TimeSpan.FromSeconds(4));
            JoinButton.IsEnabled = true;
            if (!ok)
            {
                _joinWarnedAt = DateTime.UtcNow;
                StatusText.Text = $"Can't reach the room server at {sp.Host}:{sp.Port}. The host may not have started it yet, or the port isn't open on their router. Press Join again to try anyway.";
                return;
            }
        }
        _joinWarnedAt = DateTime.MinValue;
        try
        {
            SyncplayLauncher.Launch(_pack, _settings.MovieNightFolder, _settings.Name, SyncplayPath, MpvPath, _settings.ChatStyle,
                bundled: _settings.SyncplayPath is null, player: new PlayerOptions(_settings.MaxVolume, _settings.Tracks));
            StatusText.Text = "Syncplay is starting with the player. Press Enter in the player to chat; voice stays on Discord.";
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "Couldn't start Syncplay: " + ex.Message;
        }
    }

    /// <summary>
    /// Tells the host how downloads are going: soon after something changes (at most every 5 s
    /// while downloading) and once a minute otherwise, so the host's list shows the app is open.
    /// </summary>
    private async Task ReportIfDueAsync()
    {
        if (_pack is null || !_settings.ShareProgress || _reporting) return;
        var due = _reportDirty || DateTime.UtcNow - _lastReport > TimeSpan.FromMinutes(1);
        if (!due) return;
        _reporting = true;
        _reportDirty = false;
        _lastReport = DateTime.UtcNow;
        try
        {
            var report = new ViewerReport
            {
                Name = _settings.Name,
                App = AppUpdates.CurrentVersion,
                Files = _rows.Select(r => r.Shared).ToList(),
            };
            await StatusReporter.SendAsync(Http, _pack, report);
        }
        finally
        {
            _reporting = false;
        }
    }
}
