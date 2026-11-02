using Avalonia.Controls;

namespace Tlp.Viewer.Themes;

public partial class MarqueeTheme : ResourceDictionary
{
    public MarqueeTheme() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
