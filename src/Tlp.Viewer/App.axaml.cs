using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Tlp.Viewer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        ThemeManager.Apply(this, Tlp.Viewer.Core.ViewerSettings.Load().Theme);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.SelfTestDir is { } dir)
            {
                var window = new MainWindow(null);
                desktop.MainWindow = window;
                window.Opened += async (_, _) => desktop.Shutdown(await SelfTest.RunAsync(window, dir));
            }
            else
            {
                ProtocolHandler.EnsureRegistered();
                desktop.MainWindow = new MainWindow(Program.StartupLink);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Another copy was started with a link (or none): show this window and open the link.</summary>
    public static void OnLinkFromAnotherCopy(string link) => Dispatcher.UIThread.Post(() =>
    {
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow w })
            w.OpenFromOutside(link);
    });
}
