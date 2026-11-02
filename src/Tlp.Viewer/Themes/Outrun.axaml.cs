using Avalonia.Controls;

namespace Tlp.Viewer.Themes;

public partial class OutrunTheme : ResourceDictionary
{
    public OutrunTheme() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
