# Logo

We picked **A, the piece** (a puzzle piece with a play button) and drew it in more detail in the
spirit of the cyberpunk and retro-terminal looks. **Neon** is the logo, with Terminal
kept as an alternate that suits the Phosphor look. Both are our own drawings:

- **Neon** (`a-neon.svg`): cut corners, a pink-to-violet edge with a cyan colour split, circuit
  traces, scanlines, hazard stripes and corner marks. The logo: the app icon, the host pages' tab icon and the
  README logo.
- **Terminal** (`a-terminal.svg`, alternate): a riveted steel plate with a pressed inset, an amber
  CRT screen with scanlines, a glowing play button, a cursor, a status lamp and grille slots on the
  knobs. The screen keeps clear of the left notch and the plate's edges.

Each has a `-small.svg` with the shape and play button only, used at 32 px and under where the
detail turns to mud. `make-icon.py` picks it up by itself.

To switch to Terminal: `python3 tools/make-icon.py docs/logo/a-terminal.svg`, point the README's
logo at it, and paste `a-terminal-small.svg` into the host pages' `<link rel="icon">`
(`wwwroot/index.html` and `Services/NightPage.cs`).

The first rough ideas (A, B and C) are kept as `concept-*.svg` and `logo-concepts.png`.
