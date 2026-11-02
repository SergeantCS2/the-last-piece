using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Tlp.Viewer;

/// <summary>
/// Swaps the app's look at runtime. A look is a set of keys (colours, fonts, sizes; a resource
/// dictionary made by tools/theme-tokens.py) plus, optionally, its own styles: templates for the
/// window's parts, decor and motion (Themes/*.axaml). See Themes/README.md.
/// </summary>
public static class ThemeManager
{
    public const string DefaultTheme = "Default";

    private sealed record Look(string Name, Func<ResourceDictionary> Tokens, Func<Styles>? Styles = null);

    /// <summary>Looks built into the app, in the order the Settings list shows them.</summary>
    private static readonly Look[] Looks =
    [
        new(DefaultTheme, () => new Themes.DefaultTheme(), () => new Themes.DefaultLook()),
        // The terminal look in two inks, the cyberpunk HUD and synthwave.
        new("Phosphor Amber", () => new Themes.PhosphorAmberTokens(), () => new Themes.PhosphorLook()),
        new("Phosphor Green", () => new Themes.PhosphorGreenTokens(), () => new Themes.PhosphorLook()),
        new("Breach", () => new Themes.BreachTokens(), () => new Themes.BreachLook()),
        new("Outrun", () => new Themes.OutrunTokens(), () => new Themes.OutrunLook()),
    ];

    public static readonly IReadOnlyList<string> Available = Looks.Select(t => t.Name).ToList();

    private static ResourceDictionary? _tokens;
    private static Styles? _styles;

    /// <summary>Old names of looks, from settings files saved by earlier versions.</summary>
    private static readonly Dictionary<string, string> Renamed = new()
    {
    };

    public static string Apply(Application app, string? name)
    {
        if (name is not null && Renamed.TryGetValue(name, out var renamed)) name = renamed;
        var look = Looks.FirstOrDefault(t => t.Name == name) ?? Looks[0];
        var tokens = look.Tokens();
        var styles = look.Styles?.Invoke();
        if (_styles is not null) app.Styles.Remove(_styles);
        if (_tokens is not null) app.Resources.MergedDictionaries.Remove(_tokens);
        app.Resources.MergedDictionaries.Add(tokens);
        if (styles is not null) app.Styles.Add(styles);
        _tokens = tokens;
        _styles = styles;
        app.RequestedThemeVariant = tokens.TryGetResource("TlpVariant", null, out var v) && v is string s
            ? s switch { "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default }
            : ThemeVariant.Default;
        return look.Name;
    }

    /// <summary>The name a saved setting means today (looks can be renamed between versions).</summary>
    public static string Normalize(string? name) =>
        name is not null && Renamed.TryGetValue(name, out var renamed) ? renamed
        : name is not null && Available.Contains(name) ? name : DefaultTheme;
}
