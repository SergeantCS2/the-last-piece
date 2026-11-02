# Viewer themes

Each theme is one `ResourceDictionary` file in this folder that sets the `Tlp*` keys from
`Default.axaml`: colours and brushes (solid, gradient or a tiled pattern), fonts, text sizes,
title spacing and an optional title glow, corner radius, the accent Windows controls use, and
`TlpVariant` (Light, Dark, or Default to follow Windows). `Styles.axaml` decides where each key is
used, so themes never touch layout. The app's self-test fails if a theme misses a key, and saves
a picture of every theme (`theme-*.png`).

To add one: copy a theme's `.axaml` and `.axaml.cs`, rename the class, change the values, and add
it to the list in `ThemeManager.cs`. It then shows under Settings, "Look". Picking a look previews
it straight away; Save settings keeps it.

Drafts for the host to pick from (pictures in `docs/themes`):

| Name | Idea | Fonts |
|---|---|---|
| Sakura | Anime: pastel pink and lilac, rounded type, soft corners | Nunito |
| Marquee | Movie and fantasy: velvet red, gold engraved titles | Cinzel, Lora |
| Console | Tech: slate dashboard, cyan accent, monospaced titles | JetBrains Mono, Inter |
| Neon City | Cyberpunk: black, hazard yellow and cyan, hard edges, faint grid | Orbitron, Rajdhani |
| Outrun | Synthwave: purple dusk, pink and cyan, sunset title with glow | Audiowide, Inter |
| Green Screen | Retro terminal inspired by 1950s-future computer terminals: green phosphor, scanlines | VT323 |

Our own colours and freely licensed fonts (SIL Open Font License, texts in
`Assets/Fonts/licenses`). No artwork, logos or names from any game or film.
