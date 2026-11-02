using Avalonia.Controls;

namespace Tlp.Viewer.Themes;

public partial class GreenScreenTheme : ResourceDictionary
{
    public GreenScreenTheme() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
