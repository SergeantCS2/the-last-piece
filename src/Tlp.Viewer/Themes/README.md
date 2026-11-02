# Viewer themes

Each theme is one `ResourceDictionary` file in this folder that sets the `Tlp*` keys from
`Default.axaml`: colours (with Light and Dark versions, or one fixed set), fonts, title size,
corner radius and `TlpVariant`. `Styles.axaml` decides where each key is used, so themes never
touch layout.

To add one: copy `Default.axaml` and `Default.axaml.cs` to `Name.axaml(.cs)`, rename the class,
change the values, and add it to the list in `ThemeManager.cs`. It then shows in the app's
Settings, which hides the choice while there's only one theme.

Planned drafts for the host to pick from: anime, movie/fantasy, tech, cyberpunk, synthwave, and a
retro green terminal look inspired by 1950s-future computer terminals. Themes use our own colours
and freely licensed fonts only: no artwork, logos or names from any game or film.
