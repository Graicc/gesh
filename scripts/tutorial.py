"""Renders the tutorial animations and the pinch icons from tutorial/page.html.

tutorial/page.html replays hand recordings (tutorial/data.json, cut from a
recording on the headset) on Meta's hand mesh with three.js. This renders
it in headless Edge, frame by frame, and writes:

- Assets/tutorial/<pinch|middle|palm>.png: each animation's frames in a grid
  of COLUMNS, FRAME pixels square, FPS frames a second, see GFlipbook. The
  frame counts are printed; GHints needs them.
- Assets/icons/pinch.png, pinch_middle.png: the hand at 1.0 s into the pinch
  and middle pinch, white with its outline cut out, like the other icons.

To view or tweak the animations, open tutorial/page.html with data.json's
contents in place of __DATA__ (this script does that too, in capture.html).

Usage: python scripts/tutorial.py
"""
import base64
import json
import math
import os
import pathlib
import re
import subprocess
import sys
import tempfile

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parent.parent
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
FPS, FRAME, COLUMNS = 24, 256, 9
ICON = 128
ICON_TIME = 1.0


def capture():
    """Every frame and the icons, as PNG bytes, from the page in headless Edge."""
    page = (ROOT / "tutorial" / "page.html").read_text(encoding="utf-8")
    page = page.replace("__DATA__", (ROOT / "tutorial" / "data.json").read_text(encoding="utf-8"))
    # Instead of playing, step through each view and dump what it draws.
    loop = page.index("(function loop(now)")
    end = page.index("</script>", loop)
    page = page[:loop] + f"""
const out = {{ anims: {{}}, icons: {{}} }};
for (const v of views) {{
  v.canvas.style.width = '{FRAME}px'; v.canvas.style.height = '{FRAME}px';
  v.renderer.setPixelRatio(1);
  const frames = [], n = Math.round(v.length() * {FPS});
  for (let i = 0; i < n; i++) {{ v.frameAt(i / {FPS}); frames.push(v.canvas.toDataURL('image/png')); }}
  out.anims[v.kind] = frames;
  if (v.kind !== 'palm') out.icons[v.kind] = v.icon({ICON_TIME});
}}
const pre = document.createElement('pre'); pre.id = 'capture'; pre.textContent = JSON.stringify(out); document.body.appendChild(pre);
""" + page[end:]
    with tempfile.TemporaryDirectory() as tmp:
        html = pathlib.Path(tmp) / "capture.html"
        html.write_text(page, encoding="utf-8")
        dom = subprocess.run([EDGE, "--headless=new", "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
                              f"--user-data-dir={tmp}\\profile", "--window-size=1200,1200",
                              "--virtual-time-budget=600000", "--dump-dom", html.as_uri()],
                             capture_output=True, text=True, encoding="utf-8", timeout=900).stdout
    m = re.search(r'<pre id="capture">(.*?)</pre>', dom, re.S)
    if not m:
        sys.exit("The page didn't render its frames:\n" + dom[-2000:])
    text = m.group(1).replace("&quot;", '"').replace("&lt;", "<").replace("&gt;", ">").replace("&amp;", "&")
    data = json.loads(text)
    png = lambda url: base64.b64decode(url.split(",", 1)[1])
    return ({k: [png(f) for f in v] for k, v in data["anims"].items()},
            {k: png(v) for k, v in data["icons"].items()})


def load(data):
    with tempfile.TemporaryFile() as f:
        f.write(data)
        f.seek(0)
        return Image.open(f).convert("RGBA").copy()


def sheet(frames):
    rows = math.ceil(len(frames) / COLUMNS)
    out = Image.new("RGBA", (COLUMNS * FRAME, rows * FRAME))
    for i, data in enumerate(frames):
        out.paste(load(data), ((i % COLUMNS) * FRAME, (i // COLUMNS) * FRAME))
    return out


def icon(data):
    """White, its coverage the rendered hand's brightness times its alpha:
    the white fill stays, the black outline becomes a cut."""
    im = load(data)
    px = im.load()
    out = Image.new("RGBA", im.size)
    po = out.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255
            po[x, y] = (255, 255, 255, round(a * lum))
    return out.resize((ICON, ICON), Image.LANCZOS)


def main():
    anims, icons = capture()
    for kind, frames in anims.items():
        sheet(frames).save(ROOT / "Assets" / "tutorial" / f"{kind}.png", optimize=True)
        print(f"{kind}: {len(frames)} frames")
    icon(icons["pinch"]).save(ROOT / "Assets" / "icons" / "pinch.png")
    icon(icons["middle"]).save(ROOT / "Assets" / "icons" / "pinch_middle.png")


if __name__ == "__main__":
    main()
