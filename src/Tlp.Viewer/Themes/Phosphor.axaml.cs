using Avalonia.Markup.Xaml;

namespace Tlp.Viewer.Themes;

public partial class PhosphorLook : Avalonia.Styling.Styles
{
    public PhosphorLook() => AvaloniaXamlLoader.Load(this);
}
