# Logo concepts

First ideas for the host to pick from or redirect. Each is a single SVG that works on light and
dark backgrounds (`logo-concepts.png` shows both).

- **A, the piece:** a puzzle piece with a play button, in a pink-to-violet gradient.
- **B, the last piece:** three grey tiles and the missing fourth, a red piece with a play button.
- **C, film piece:** a puzzle-piece outline with film sprocket holes and a play button.

Once one is picked it gets a proper wordmark, app icon sizes (Windows `.ico`, installer,
favicon for the host pages) and colours to match the chosen theme.

Until then concept B stands in as a placeholder: the app's window and taskbar icon, the
installer, the host pages' browser tab and the README. To swap it, run
`python3 tools/make-icon.py docs/logo/<picked>.svg`, point the README's logo at the same file,
and paste the SVG into the host pages' `<link rel="icon">` (see the script's notes).
