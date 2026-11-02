using Avalonia;

namespace Tlp.Viewer;

internal static class Program
{
    /// <summary>First argument may be a tlp://open?pack=... link from the browser.</summary>
    public static string? StartupLink { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        StartupLink = args.FirstOrDefault();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
