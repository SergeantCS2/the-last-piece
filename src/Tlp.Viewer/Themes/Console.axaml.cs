using Avalonia.Controls;

namespace Tlp.Viewer.Themes;

public partial class ConsoleTheme : ResourceDictionary
{
    public ConsoleTheme() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
