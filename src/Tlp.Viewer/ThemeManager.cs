using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Tlp.Viewer;

/// <summary>Swaps the app's look at runtime. A theme is a resource dictionary in Themes/.</summary>
public static class ThemeManager
{
    public const string DefaultTheme = "Default";

    /// <summary>Themes built into the app, in the order the Settings list shows them.</summary>
    private static readonly (string Name, Func<ResourceDictionary> Make)[] Themes =
    [
        (DefaultTheme, () => new Themes.DefaultTheme()),
        // Drafts for the host to pick from. The one he picks gets finished; the rest come out.
        ("Sakura", () => new Themes.SakuraTheme()),
        ("Marquee", () => new Themes.MarqueeTheme()),
        ("Console", () => new Themes.ConsoleTheme()),
        ("Neon City", () => new Themes.NeonCityTheme()),
        ("Outrun", () => new Themes.OutrunTheme()),
        ("Green Screen", () => new Themes.GreenScreenTheme()),
    ];

    public static readonly IReadOnlyList<string> Available = Themes.Select(t => t.Name).ToList();

    private static ResourceDictionary? _current;

    public static string Apply(Application app, string? name)
    {
        var entry = Themes.FirstOrDefault(t => t.Name == name);
        if (entry.Make is null) entry = Themes[0];
        name = entry.Name;
        var theme = entry.Make();
        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Add(theme);
        _current = theme;
        app.RequestedThemeVariant = theme.TryGetResource("TlpVariant", null, out var v) && v is string s
            ? s switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default }
            : ThemeVariant.Default;
        return name;
    }
}
