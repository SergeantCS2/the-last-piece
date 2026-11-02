using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Tlp.Viewer.Themes;

public partial class BasicTheme : ResourceDictionary
{
    public BasicTheme() => AvaloniaXamlLoader.Load(this);
}
