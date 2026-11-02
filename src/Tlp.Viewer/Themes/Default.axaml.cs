using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Tlp.Viewer.Themes;

public partial class DefaultTheme : ResourceDictionary
{
    public DefaultTheme() => AvaloniaXamlLoader.Load(this);
}
