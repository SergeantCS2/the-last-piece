using Avalonia.Controls;
using System.Net;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Tlp.Core;
using Tlp.Viewer.Core;

namespace Tlp.Viewer;

/// <summary>
/// Run by CI on a Windows machine (TheLastPiece.exe --self-test DIR) against the real, trimmed
/// build: opens the window, reads a sample session, writes Syncplay's settings, starts and
/// stops the torrent engine, saves a picture of the window, and exits 0 if all of that worked.
/// It touches nothing outside DIR and the system temp folder.
/// </summary>
internal static class SelfTest
{
    private const string SamplePack = """
        {
          "version": 1,
          "title": "Self-test session",
          "startsAt": "2026-11-06T20:00:00-05:00",
          "syncplay": { "host": "movies.example.com", "port": 8999, "room": "session", "password": "pw" },
          "items": [
            { "fileName": "[Grp] Show - 01 (1080p).mkv", "size": 1400000000,
              "sources": [ { "kind": "torrent", "uri": "https://movies.example.com/n/t/files/0/group.torrent" },
                           { "kind": "http", "uri": "https://movies.example.com/n/t/files/0/x.mkv" } ] },
            { "fileName": "[Grp] Show - 02 (1080p).mkv", "size": 1350000000,
              "sources": [ { "kind": "magnet", "uri": "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=x" } ] }
          ]
        }
        """;

    /// <summary>The window on the sample night in one look, left open (see Program.Showcase).</summary>
    public static void Showcase(MainWindow window, string look, bool settingsOpen, bool dark)
    {
        var pack = PackJson.DeserializePack(SamplePack)! with { Title = "Friday anime" };
        window.ShowPack(pack);
        window.Rows[0].Status = "Downloading (the group torrent) · 600 MB of 1.30 GB · 9.8 MB/s";
        window.Rows[0].Percent = 45;
        var name = ThemeManager.Apply(Avalonia.Application.Current!, look);
        window.ThemeBox.SelectedItem = name;
        if (dark) Avalonia.Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        if (Program.Showcase?.Welcome == true) window.ShowWelcome();
        if (settingsOpen && window.FindControl<Expander>("SettingsExpander") is { } settings)
        {
            settings.IsExpanded = true;
            window.Height = 1080;
        }
        window.Position = new PixelPoint(0, 0);
    }

    private static void SaveWindowPicture(MainWindow window, string path)
    {
        var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(window);
        bitmap.Save(path);
    }

    public static async Task<int> RunAsync(MainWindow window, string dir)
    {
        Directory.CreateDirectory(dir);
        var log = new List<string>();
        try
        {
            var pack = PackJson.DeserializePack(SamplePack) ?? throw new InvalidDataException("sample pack parsed to null");
            PackClient.Validate(pack);
            if (pack.Items[0].Sources[0].Kind != PackSourceKind.Torrent) throw new InvalidDataException("enum names didn't survive trimming");
            log.Add("pack json: ok");

            // Every theme must define every key the styles use, or that part of the window loses its
            // colour, and every font it names must load from the app (not fall back to a system font).
            foreach (var name in ThemeManager.Available)
            {
                ThemeManager.Apply(Avalonia.Application.Current!, name);
                foreach (var key in new[] { "TlpWindowBackground", "TlpSurface", "TlpBorder", "TlpText", "TlpMutedText", "TlpAccent", "TlpAccentText", "TlpProgress", "TlpTitleFont", "TlpBodyFont", "TlpTitleSize", "TlpCorner", "TlpInput", "TlpTitleForeground", "TlpTitleSpacing", "TlpBodySize", "TlpHeadingSize", "TlpSmallSize", "TlpVariant" })
                    if (!window.TryFindResource(key, window.ActualThemeVariant, out _)) throw new InvalidDataException($"theme {name} has no {key}");
                foreach (var key in new[] { "TlpTitleFont", "TlpBodyFont", "TlpHeadFont", "TlpMonoFont" })
                {
                    if (!window.TryFindResource(key, window.ActualThemeVariant, out var f) || f is not FontFamily family) continue;
                    var wanted = family.FamilyNames.Where(n => n != "$Default").ToList();
                    var got = FontManager.Current.TryGetGlyphTypeface(new Typeface(family), out var glyphs) ? glyphs.FamilyName : "(none)";
                    if (!wanted.Any(w => w.EndsWith(got, StringComparison.OrdinalIgnoreCase) || got.StartsWith(w.Split('#').Last(), StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException($"theme {name}: {key} wants {string.Join(" or ", wanted)} but got {got}");
                    log.Add($"font {name} {key}: {got}");
                }
            }
            ThemeManager.Apply(Avalonia.Application.Current!, ThemeManager.DefaultTheme);
            log.Add($"themes: ok ({string.Join(", ", ThemeManager.Available)})");

            // Turning motion off in Settings keeps every look still, whether or not the window is active.
            window.MotionBox.IsChecked = false;
            var backdrop = window.FindControl<ContentControl>("Backdrop") ?? throw new InvalidDataException("no Backdrop");
            if (!backdrop.Classes.Contains("still")) throw new InvalidDataException("motion off didn't stop the look's animation");
            window.MotionBox.IsChecked = true;
            log.Add("motion setting: ok");

            // The big title is the host's group name, and back to the default without one.
            window.ShowPack(pack with { Group = "Friday crew" });
            if (window.Brand.Content as string != "Friday crew") throw new InvalidDataException("the group name didn't become the title");
            window.ShowPack(pack);
            if (window.Brand.Content as string != MainWindow.DefaultTitle) throw new InvalidDataException("the title didn't go back to the default");
            log.Add("title: ok");

            // First run: the welcome shows, refuses a folder it can't use, and hides once done.
            var welcomeTemp = Directory.CreateTempSubdirectory("tlp-welcome-").FullName;
            window.ShowWelcome("https://movies.example.com/n/abc");
            if (!window.WelcomeShown) throw new InvalidDataException("the welcome didn't show");
            if (window.WelcomeLink.Text != "https://movies.example.com/n/abc") throw new InvalidDataException("the welcome lost the link it was opened with");
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();
            SaveWindowPicture(window, Path.Combine(dir, "welcome.png"));
            window.WelcomeFolder.Text = "not a full path";
            if (await window.FinishWelcomeAsync() || !window.WelcomeShown) throw new InvalidDataException("the welcome accepted a folder it can't use");
            window.WelcomeFolder.Text = Path.Combine(welcomeTemp, "The Last Piece");
            window.WelcomeName.Text = "Self-test";
            window.WelcomeLink.Text = "";
            if (!await window.FinishWelcomeAsync() || window.WelcomeShown) throw new InvalidDataException("the welcome didn't finish");
            if (!Directory.Exists(Path.Combine(welcomeTemp, "The Last Piece"))) throw new InvalidDataException("the welcome didn't make the folder");
            if (!ViewerSettings.Load().SetupDone) throw new InvalidDataException("the welcome didn't save that setup is done");
            if (!window.BugReportText().Contains(AppUpdates.CurrentVersion)) throw new InvalidDataException("the bug report details lack the version");
            log.Add("welcome: ok");

            window.Rows[0].Status = "Downloading (the group torrent) · 600 MB of 1.30 GB · 9.8 MB/s";
            window.Rows[0].Percent = 45;
            log.Add("window: ok");

            var temp = Directory.CreateTempSubdirectory("tlp-selftest-").FullName;
            var ini = SyncplayLauncher.PortableConfig("Chatroom") + SyncplayLauncher.RelativeConfig(pack, temp);
            File.WriteAllText(Path.Combine(dir, "syncplay-settings.txt"), ini);
            log.Add("syncplay settings: ok");

            log.Add(await TorrentFromWebSeedAsync(temp));

            // CI only: also start the bundled Syncplay + mpv against a test server, the way Join does.
            if (Environment.GetEnvironmentVariable("TLP_SELFTEST_SYNCPLAY") is { Length: > 0 } server)
                log.Add(LaunchSyncplay(server, Environment.GetEnvironmentVariable("TLP_SELFTEST_MEDIA"), temp));

            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            SaveWindowPicture(window, Path.Combine(dir, "viewer.png"));
            log.Add("screenshot: ok");

            // Two pictures per theme, for choosing between the drafts: the window as it opens, and
            // taller with Settings open.
            var settings = window.FindControl<Expander>("SettingsExpander");
            var oldHeight = window.Height;
            foreach (var name in ThemeManager.Available)
            {
                ThemeManager.Apply(Avalonia.Application.Current!, name);
                window.ThemeBox.SelectedItem = name;
                var file = name.Replace(' ', '-').ToLowerInvariant();
                foreach (var (open, height, suffix) in new[] { (false, oldHeight, ""), (true, 1080d, "-settings") })
                {
                    if (settings is not null) settings.IsExpanded = open;
                    window.Height = height;
                    await Task.Delay(150);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    using var themed = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
                    themed.Render(window);
                    themed.Save(Path.Combine(dir, $"theme-{file}{suffix}.png"));
                }
            }
            if (settings is not null) settings.IsExpanded = false;
            window.ThemeBox.SelectedItem = ThemeManager.DefaultTheme;
            window.Height = oldHeight;
            ThemeManager.Apply(Avalonia.Application.Current!, ThemeManager.DefaultTheme);
            log.Add("theme pictures: ok");

            log.Add("PASS");
            return 0;
        }
        catch (Exception ex)
        {
            log.Add("FAIL " + ex);
            return 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(dir, "self-test.txt"), log);
        }
    }

    /// <summary>
    /// Starts the bundled Syncplay exactly as the Join button does, against "host:port:password".
    /// CI then checks from outside that "selftest" turned up in the room, which can only happen
    /// if Syncplay's connect window didn't stop it.
    /// </summary>
    private static string LaunchSyncplay(string server, string? media, string temp)
    {
        var parts = server.Split(':', 3);
        if (media is null || !File.Exists(media)) throw new FileNotFoundException("TLP_SELFTEST_MEDIA must point at a media file", media);
        var folder = Directory.CreateDirectory(Path.Combine(temp, "night")).FullName;
        var name = "selftest" + Path.GetExtension(media);
        File.Copy(media, Path.Combine(folder, name), overwrite: true);
        var pack = new Pack
        {
            Title = "CI",
            Syncplay = new SyncplayInfo { Host = parts[0], Port = int.Parse(parts[1]), Room = "ci", Password = parts.Length > 2 ? parts[2] : null },
            Items = [new PackItem { FileName = name, Size = new FileInfo(Path.Combine(folder, name)).Length, Sources = [] }],
        };
        var syncplay = Path.Combine(AppContext.BaseDirectory, "syncplay", "Syncplay.exe");
        var mpv = Path.Combine(AppContext.BaseDirectory, "mpv", "mpv.exe");
        var process = SyncplayLauncher.Launch(pack, folder, "selftest", syncplay, mpv, "Chatroom", bundled: true);
        return $"syncplay started (pid {process.Id})";
    }

    /// <summary>
    /// A real torrent download in this (trimmed) build: a 1 MB file shared only by a web seed on
    /// this machine, the same way the host's shelf link backs a group torrent.
    /// </summary>
    private static async Task<string> TorrentFromWebSeedAsync(string temp)
    {
        var seedDir = Directory.CreateDirectory(Path.Combine(temp, "seed")).FullName;
        var data = new byte[1024 * 1024 + 777];
        Random.Shared.NextBytes(data);
        const string name = "selftest.mkv";
        await File.WriteAllBytesAsync(Path.Combine(seedDir, name), data);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var server = Task.Run(() => MiniRangeServer.RunAsync(listener, data, stop.Token));
        try
        {
            var creator = new MonoTorrent.TorrentCreator(MonoTorrent.TorrentType.V1Only);
            var dict = await creator.CreateAsync(new MonoTorrent.TorrentFileSource(Path.Combine(seedDir, name)));
            var info = (MonoTorrent.BEncoding.BEncodedDictionary)dict["info"];
            info["private"] = new MonoTorrent.BEncoding.BEncodedNumber(1);
            var torrent = new MonoTorrent.BEncoding.BEncodedDictionary
            {
                ["info"] = info,
                ["url-list"] = new MonoTorrent.BEncoding.BEncodedList { new MonoTorrent.BEncoding.BEncodedString($"http://127.0.0.1:{port}/{name}") },
            };
            var item = new PackItem { FileName = name, Size = data.Length, Sources = [] };
            await using var engine = new TorrentDownloader(Path.Combine(temp, "cache"), seedWhileOpen: false, publicNetwork: false);
            var dl = Path.Combine(temp, "dl");
            await engine.DownloadTorrentAsync(torrent.Encode(), dl, item, null, stop.Token);
            var got = await File.ReadAllBytesAsync(Path.Combine(dl, name));
            if (!data.AsSpan().SequenceEqual(got))
                throw new InvalidDataException("torrent download came back different");
            return "torrent download from web seed: ok";
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            try { await server; } catch { }
        }
    }
}

/// <summary>Just enough HTTP for a web seed: GET with an optional single byte range.</summary>
internal static class MiniRangeServer
{
    public static async Task RunAsync(TcpListener listener, byte[] data, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(ct);
            _ = Task.Run(async () =>
            {
                using var c = client;
                var stream = c.GetStream();
                var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, 1024, leaveOpen: true);
                while (!ct.IsCancellationRequested)
                {
                    var requestLine = await reader.ReadLineAsync(ct);
                    if (string.IsNullOrEmpty(requestLine)) return;
                    long start = 0, end = data.Length - 1;
                    var ranged = false;
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
                    {
                        if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line["Range: bytes=".Length..].Split('-');
                            start = long.Parse(parts[0]);
                            if (parts[1].Length > 0) end = Math.Min(long.Parse(parts[1]), data.Length - 1);
                            ranged = true;
                        }
                    }
                    var length = end - start + 1;
                    var header = (ranged ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n")
                                 + $"Content-Length: {length}\r\nContent-Type: application/octet-stream\r\n"
                                 + (ranged ? $"Content-Range: bytes {start}-{end}/{data.Length}\r\n" : "")
                                 + "Connection: keep-alive\r\n\r\n";
                    await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(header), ct);
                    await stream.WriteAsync(data.AsMemory((int)start, (int)length), ct);
                }
            }, ct);
        }
    }
}
