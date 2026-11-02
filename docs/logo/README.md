# Logo

The host picked **A, the piece** (a puzzle piece with a play button) and asked for more detail in the
spirit of the cyberpunk and retro-terminal looks. Two detailed takes, our own drawings:

- **Neon** (`a-neon.svg`): cut corners, a pink-to-violet edge with a cyan colour split, circuit
  traces, scanlines, hazard stripes and corner marks. Now the app icon, the host pages' tab icon
  and the README logo.
- **Terminal** (`a-terminal.svg`): a riveted steel plate with an amber CRT screen, scanlines, a
  glowing play button, a cursor and a status lamp on the knob.

Each has a `-small.svg` with the shape and play button only, used at 32 px and under where the
detail turns to mud. `make-icon.py` picks it up by itself.

To switch to Terminal: `python3 tools/make-icon.py docs/logo/a-terminal.svg`, point the README's
logo at it, and paste `a-terminal-small.svg` into the host pages' `<link rel="icon">`
(`wwwroot/index.html` and `Services/NightPage.cs`).

The first rough ideas (A, B and C) are kept as `concept-*.svg` and `logo-concepts.png`.
