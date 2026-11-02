"""Makes the viewer's Windows icon (src/Tlp.Viewer/Assets/app.ico) from a square SVG logo.

    python3 tools/make-icon.py docs/logo/a-neon.svg [docs/logo/a-neon-small.svg]

The optional second SVG is a simpler drawing for 32 px and under, where fine detail turns to mud.

Needs cairosvg and Pillow (pip install cairosvg pillow). The installer and the window both use
app.ico. The host pages' tab icon is the same SVG, inlined in wwwroot/index.html and
Services/NightPage.cs; paste a new one there by hand.
"""
import sys
from pathlib import Path

import cairosvg
from PIL import Image

root = Path(__file__).resolve().parent.parent
svg = Path(sys.argv[1] if len(sys.argv) > 1 else root / "docs/logo/a-neon.svg")
small = Path(sys.argv[2]) if len(sys.argv) > 2 else svg.with_name(svg.stem + "-small.svg")
if not small.exists():
    small = svg
tmp = root / "src/Tlp.Viewer/Assets/icon.tmp.png"
frames = []
for size in (256, 128, 64, 48, 32, 24, 16):
    cairosvg.svg2png(url=str(small if size <= 32 else svg), write_to=str(tmp), output_width=size, output_height=size)
    frames.append(Image.open(tmp).convert("RGBA").copy())
tmp.unlink()
out = root / "src/Tlp.Viewer/Assets/app.ico"
frames[0].save(out, format="ICO", append_images=frames[1:], sizes=[f.size for f in frames])
print(f"wrote {out} from {svg} (small sizes from {small})")
