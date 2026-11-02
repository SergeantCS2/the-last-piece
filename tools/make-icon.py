"""Makes the viewer's Windows icon (src/Tlp.Viewer/Assets/app.ico) from a square SVG logo.

    python3 tools/make-icon.py docs/logo/concept-b.svg

Needs cairosvg and Pillow (pip install cairosvg pillow). The installer and the window both use
app.ico. The host pages' tab icon is the same SVG, inlined in wwwroot/index.html and
Services/NightPage.cs; paste a new one there by hand.
"""
import sys
from pathlib import Path

import cairosvg
from PIL import Image

root = Path(__file__).resolve().parent.parent
svg = Path(sys.argv[1] if len(sys.argv) > 1 else root / "docs/logo/concept-b.svg")
png = root / "src/Tlp.Viewer/Assets/app-256.tmp.png"
cairosvg.svg2png(url=str(svg), write_to=str(png), output_width=256, output_height=256)
image = Image.open(png).convert("RGBA")
out = root / "src/Tlp.Viewer/Assets/app.ico"
image.save(out, sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
png.unlink()
print(f"wrote {out} from {svg}")
