using Avalonia;
using Tlp.Viewer.Core;
using Velopack;

namespace Tlp.Viewer;

internal static class Program
{
    /// <summary>First argument may be a tlp://open?pack=... link from the browser.</summary>
    public static string? StartupLink { get; private set; }

    /// <summary>Folder for --self-test output (CI): the app opens, checks itself, saves a picture and exits.</summary>
    public static string? SelfTestDir { get; private set; }

    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: when the installer or updater starts the app with its own arguments,
        // this runs the matching hook and exits. Otherwise it does nothing.
        var velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
            velopack = velopack
                .OnAfterInstallFastCallback(_ => ProtocolHandler.EnsureRegistered())
                .OnAfterUpdateFastCallback(_ => ProtocolHandler.EnsureRegistered())
                .OnBeforeUninstallFastCallback(_ => ProtocolHandler.Unregister());
        velopack.Run();

        if (args.Length >= 2 && args[0] == "--self-test")
        {
            SelfTestDir = args[1];
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
        }

        StartupLink = args.FirstOrDefault();
        // A copy is already open: hand it the link and quit, so links reuse one window.
        Instance = SingleInstance.TryStart(SingleInstance.DefaultName, link => App.OnLinkFromAnotherCopy(link));
        if (Instance is null && SingleInstance.SendToRunning(SingleInstance.DefaultName, StartupLink ?? "", TimeSpan.FromSeconds(3)))
            return 0;

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance?.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
