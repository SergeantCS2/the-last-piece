#!/usr/bin/env python3
"""
Writes each look's keys (src/Tlp.Viewer/Themes/*Tokens.axaml): colours, fonts and sizes, plus the
keys Avalonia's Fluent controls read (text boxes, drop-downs, check boxes), so every control in a
look follows its palette. Edit a palette here and run:  python3 tools/theme-tokens.py

A look's templates, decor and motion are hand-written in Themes/<Name>.axaml and only refer to
these keys, so the two Phosphor looks share one set of templates with two palettes.
"""
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "src/Tlp.Viewer/Themes"
F = "avares://TheLastPiece/Assets/Fonts#"


def font(*names):
    return ", ".join(F + n for n in names) + ", $Default"


def rgba(hex_color, alpha):
    """#RRGGBB plus an opacity 0..1, as Avalonia's #AARRGGBB."""
    return "#%02X%s" % (round(alpha * 255), hex_color.lstrip("#").upper())


# Palette keys every look sets:
#   bg surface input border borderHi text textHi muted accent accentText alt alt2 alert hover glow
LOOKS = {
    "PhosphorAmber": dict(
        variant="Dark", corner=0,
        bg="#0B0803", surface="#100B04", input="#0E0A04", border="#6B4716", borderHi="#C08A35",
        text="#FFB641", textHi="#FFDCA0", muted="#C08A35", accent="#FFB641", accentText="#0B0803",
        alt="#9C6F2A", alt2="#2C1D08", alert="#FF6B3D", hover="#2C1D08", glow=("#FFB641", .55),
        fonts=dict(title=font("VT323"), body=font("IBM Plex Mono"), head=font("VT323"), mono=font("IBM Plex Mono SemiBold", "IBM Plex Mono")),
        sizes=dict(title=60, titleSpacing=2, heading=30, body=14, small=12, head=22),
    ),
    "PhosphorGreen": dict(
        variant="Dark", corner=0,
        bg="#030A05", surface="#051008", input="#040C06", border="#1A5C33", borderHi="#2FBF62",
        text="#45FF86", textHi="#C2FFD6", muted="#2FBF62", accent="#45FF86", accentText="#030A05",
        alt="#26944D", alt2="#0B2415", alert="#FFB641", hover="#0B2415", glow=("#45FF86", .5),
        fonts=dict(title=font("VT323"), body=font("IBM Plex Mono"), head=font("VT323"), mono=font("IBM Plex Mono SemiBold", "IBM Plex Mono")),
        sizes=dict(title=60, titleSpacing=2, heading=30, body=14, small=12, head=22),
    ),
    # Night-city HUD: red interface lines, pale cyan readouts, hazard yellow for the one thing to press.
    "Breach": dict(
        variant="Dark", corner=0,
        bg="#06080C", surface="#0C1017", input="#080B10", border="#7A2530", borderHi="#FF4655",
        text="#D8F6F8", textHi="#FFFFFF", muted="#6E9AA3", accent="#F3E600", accentText="#07080A",
        alt="#22E5F0", alt2="#FF4655", alert="#FF4655", hover="#2A0F15", glow=("#FF4655", .6),
        fonts=dict(title=font("Chakra Petch"), body=font("Rajdhani Medium", "Rajdhani"), head=font("Chakra Petch"), mono=font("Share Tech Mono")),
        sizes=dict(title=50, titleSpacing=6, heading=24, body=16, small=13, head=13),
    ),
    # Anime title card: candy pink and violet on a pale sky, chunky display type.
    # Picture palace: red velvet, gold leaf, a lit letter-board sign.
    # Engineering drawing: white line-work on blueprint blue, one safety-orange highlight.
    # Synthwave: dusk purple, neon pink and cyan, a striped sun over a grid.
    "Outrun": dict(
        variant="Dark", corner=6,
        bg="#120428", surface="#1B0A33", input="#160829", border="#6B2D8F", borderHi="#FF3FD2",
        text="#F7EBFF", textHi="#FFFFFF", muted="#BCA0DD", accent="#FF3FD2", accentText="#FFFFFF",
        alt="#2DE2E6", alt2="#FFD319", alert="#FF5E7E", hover="#2B0F4A", glow=("#FF3FD2", .85),
        fonts=dict(title=font("Monoton"), body=font("Exo 2 Medium", "Exo 2"), head=font("Exo 2"), mono=font("Exo 2 Medium", "Exo 2")),
        sizes=dict(title=48, titleSpacing=2, heading=23, body=14.5, small=12, head=13),
    ),
}


def glow(color, blur, opacity=1.0, key="TlpTitleEffect", dx=0, dy=0):
    return f'  <DropShadowEffect x:Key="{key}" Color="{color}" BlurRadius="{blur}" OffsetX="{dx}" OffsetY="{dy}" Opacity="{opacity}"/>'


def phosphor(p):
    g = p["glow"][0]
    return f'''  <!-- The tube: a warm centre fading to the edges, a glow band that rolls down the screen. -->
  <RadialGradientBrush x:Key="TlpWindowBackground" Center="50%,38%" GradientOrigin="50%,38%" RadiusX="75%" RadiusY="75%">
    <GradientStop Color="{p["alt2"]}" Offset="0"/>
    <GradientStop Color="{p["bg"]}" Offset="1"/>
  </RadialGradientBrush>
  <LinearGradientBrush x:Key="TlpBand" StartPoint="0%,0%" EndPoint="0%,100%">
    <GradientStop Color="{rgba(g, 0)}" Offset="0"/>
    <GradientStop Color="{rgba(g, .07)}" Offset="0.5"/>
    <GradientStop Color="{rgba(g, 0)}" Offset="1"/>
  </LinearGradientBrush>
{glow(g, 18)}
{glow(g, 8, .8, "TlpSoftGlow")}'''


EXTRAS = {"PhosphorAmber": phosphor, "PhosphorGreen": phosphor}


def tokens(name, p):
    b = lambda key, color: f'  <SolidColorBrush x:Key="{key}" Color="{color}"/>'
    lines = []
    for key, k in [("TlpWindowBackground", "bg"), ("TlpSurface", "surface"), ("TlpInput", "input"),
                   ("TlpBorder", "border"), ("TlpBorderHi", "borderHi"), ("TlpText", "text"),
                   ("TlpTextHi", "textHi"), ("TlpTitleForeground", "textHi"), ("TlpMutedText", "muted"),
                   ("TlpAccent", "accent"), ("TlpAccentText", "accentText"), ("TlpProgress", "accent"),
                   ("TlpAlt", "alt"), ("TlpAlt2", "alt2"), ("TlpAlert", "alert"), ("TlpHover", "hover")]:
        lines.append(b(key, p[k]))
    g, a = p["glow"]
    lines.append(f'  <SolidColorBrush x:Key="TlpGlow" Color="{rgba(g, a)}"/>')
    for key, k in [("TlpBgColor", "bg"), ("TlpAccentColor", "accent"), ("TlpAltColor", "alt"), ("TlpAlt2Color", "alt2"), ("TlpTextColor", "text"), ("TlpBorderColor", "border")]:
        lines.append(f'  <Color x:Key="{key}">{p[k]}</Color>')
    lines.append(f'  <Color x:Key="TlpGlowColor">{rgba(g, a)}</Color>')
    for key, k in [("TlpTitleFont", "title"), ("TlpBodyFont", "body"), ("TlpHeadFont", "head"), ("TlpMonoFont", "mono")]:
        lines.append(f'  <FontFamily x:Key="{key}">{p["fonts"][k]}</FontFamily>')
    for key, k in [("TlpTitleSize", "title"), ("TlpTitleSpacing", "titleSpacing"), ("TlpHeadingSize", "heading"),
                   ("TlpBodySize", "body"), ("TlpSmallSize", "small"), ("TlpHeadSize", "head")]:
        lines.append(f'  <x:Double x:Key="{key}">{p["sizes"][k]}</x:Double>')
    lines.append(f'  <CornerRadius x:Key="TlpCorner">{p["corner"]}</CornerRadius>')
    lines.append(f'  <CornerRadius x:Key="ControlCornerRadius">{p["corner"]}</CornerRadius>')
    lines.append(f'  <CornerRadius x:Key="OverlayCornerRadius">{p["corner"]}</CornerRadius>')
    lines.append(f'  <x:String x:Key="TlpVariant">{p["variant"]}</x:String>')
    for suffix in ["", "Dark1", "Dark2", "Dark3", "Light1", "Light2", "Light3"]:
        lines.append(f'  <Color x:Key="SystemAccentColor{suffix}">{p["accent"]}</Color>')

    # What Fluent's text boxes, drop-downs and check boxes read.
    fluent = {
        "TextControlForeground": "text", "TextControlForegroundPointerOver": "text", "TextControlForegroundFocused": "textHi",
        "TextControlBackground": "input", "TextControlBackgroundPointerOver": "input", "TextControlBackgroundFocused": "input",
        "TextControlBorderBrush": "border", "TextControlBorderBrushPointerOver": "borderHi", "TextControlBorderBrushFocused": "accent",
        "TextControlPlaceholderForeground": "muted", "TextControlPlaceholderForegroundPointerOver": "muted",
        "TextControlPlaceholderForegroundFocused": "muted", "TextControlSelectionHighlightColor": "accent",
        "ComboBoxBackground": "input", "ComboBoxBackgroundPointerOver": "hover", "ComboBoxBackgroundPressed": "hover",
        "ComboBoxBackgroundUnfocused": "input", "ComboBoxBackgroundBorderBrushFocused": "accent", "ComboBoxBackgroundBorderBrushUnfocused": "border",
        "ComboBoxBorderBrush": "border", "ComboBoxBorderBrushPointerOver": "borderHi", "ComboBoxBorderBrushPressed": "accent",
        "ComboBoxForeground": "text", "ComboBoxForegroundFocused": "textHi", "ComboBoxForegroundFocusedPressed": "textHi",
        "ComboBoxPlaceHolderForeground": "muted", "ComboBoxDropDownBackground": "surface", "ComboBoxDropDownBorderBrush": "borderHi",
        "ComboBoxDropDownGlyphForeground": "accent", "ComboBoxDropDownGlyphForegroundFocused": "accent", "ComboBoxDropDownGlyphForegroundFocusedPressed": "accent",
        "ComboBoxItemForeground": "text", "ComboBoxItemForegroundPointerOver": "textHi", "ComboBoxItemForegroundPressed": "textHi",
        "ComboBoxItemBackgroundPointerOver": "hover", "ComboBoxItemBackgroundPressed": "hover",
        "ComboBoxItemBackgroundSelected": "accent", "ComboBoxItemBackgroundSelectedPointerOver": "accent", "ComboBoxItemBackgroundSelectedPressed": "accent",
        "ComboBoxItemForegroundSelected": "accentText", "ComboBoxItemForegroundSelectedPointerOver": "accentText", "ComboBoxItemForegroundSelectedPressed": "accentText",
        "CheckBoxForegroundUnchecked": "text", "CheckBoxForegroundUncheckedPointerOver": "textHi", "CheckBoxForegroundUncheckedPressed": "textHi",
        "CheckBoxForegroundChecked": "text", "CheckBoxForegroundCheckedPointerOver": "textHi", "CheckBoxForegroundCheckedPressed": "textHi",
        "CheckBoxCheckBackgroundStrokeUnchecked": "borderHi", "CheckBoxCheckBackgroundStrokeUncheckedPointerOver": "accent", "CheckBoxCheckBackgroundStrokeUncheckedPressed": "accent",
        "CheckBoxCheckBackgroundFillUnchecked": "input", "CheckBoxCheckBackgroundFillUncheckedPointerOver": "hover", "CheckBoxCheckBackgroundFillUncheckedPressed": "hover",
        "CheckBoxCheckBackgroundStrokeChecked": "accent", "CheckBoxCheckBackgroundStrokeCheckedPointerOver": "accent", "CheckBoxCheckBackgroundStrokeCheckedPressed": "accent",
        "CheckBoxCheckBackgroundFillChecked": "accent", "CheckBoxCheckBackgroundFillCheckedPointerOver": "accent", "CheckBoxCheckBackgroundFillCheckedPressed": "accent",
        "CheckBoxCheckGlyphForegroundChecked": "accentText", "CheckBoxCheckGlyphForegroundCheckedPointerOver": "accentText", "CheckBoxCheckGlyphForegroundCheckedPressed": "accentText",
        "ScrollBarThumbFill": "border", "ScrollBarThumbFillPointerOver": "borderHi", "ScrollBarThumbFillPressed": "accent",
        "ScrollBarPanningThumbBackground": "border",
    }
    for key, k in fluent.items():
        if key.endswith("Color"):
            lines.append(f'  <Color x:Key="{key}">{p[k]}</Color>')
        else:
            lines.append(b(key, p[k]))
    if name in EXTRAS:
        extra = EXTRAS[name](p)
        # A look's own brush replaces the plain one with the same key.
        lines = [l for l in lines if not any(f'x:Key="{k}"' in extra for k in [l.split('x:Key="')[1].split('"')[0]])]
        lines.append(extra)
    return f'''<!-- Made by tools/theme-tokens.py from the "{name}" palette. Edit the palette there, not this file. -->
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    x:Class="Tlp.Viewer.Themes.{name}Tokens">
''' + "\n".join(lines) + "\n</ResourceDictionary>\n"


CODE = '''// Made by tools/theme-tokens.py.
using Avalonia.Markup.Xaml;

namespace Tlp.Viewer.Themes;

public partial class {name}Tokens : Avalonia.Controls.ResourceDictionary
{{
    public {name}Tokens() => AvaloniaXamlLoader.Load(this);
}}
'''

if __name__ == "__main__":
    for name, p in LOOKS.items():
        (OUT / f"{name}Tokens.axaml").write_text(tokens(name, p))
        (OUT / f"{name}Tokens.axaml.cs").write_text(CODE.format(name=name))
        print("wrote", name)
