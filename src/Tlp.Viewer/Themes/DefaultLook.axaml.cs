using Avalonia.Markup.Xaml;

namespace Tlp.Viewer.Themes;

public partial class DefaultLook : Avalonia.Styling.Styles
{
    public DefaultLook() => AvaloniaXamlLoader.Load(this);
}
