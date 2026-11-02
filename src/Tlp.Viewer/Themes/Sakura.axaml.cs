using Avalonia.Controls;

namespace Tlp.Viewer.Themes;

public partial class SakuraTheme : ResourceDictionary
{
    public SakuraTheme() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
