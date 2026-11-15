"""Rasterizes the menu icons from Material Symbols SVGs to white PNGs.

The SVGs are Google's Material Symbols (Rounded, filled, weight 400), Apache
License 2.0, from the @material-symbols/svg-400 npm package. They're kept in
icons/ so the PNGs can be remade without the network. Rendering uses headless
Edge (nothing else here draws SVG), screenshotting a transparent page with all
icons in a row, then cutting it into one PNG per icon in Assets/icons.

Usage: python scripts/icons.py [--download]
"""
import os
import subprocess
import sys
import tempfile
import urllib.request

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "icons")
OUT = os.path.join(ROOT, "Assets", "icons")
EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
VERSION = "0.27.0"
SIZE = 128

# Material Symbols names used by GIcons. The pinch icons aren't Material
# Symbols: they're the recorded hand, from scripts/tutorial.py.
NAMES = [
    "edit", "brush", "colorize", "save", "add", "delete", "info", "close",
    "settings", "home", "recenter", "back_hand", "format_color_fill", "open_with", "undo", "redo", "ink_selection", "folder",
]


def download():
    os.makedirs(SRC, exist_ok=True)
    for name in NAMES:
        url = f"https://cdn.jsdelivr.net/npm/@material-symbols/svg-400@{VERSION}/rounded/{name}-fill.svg"
        with urllib.request.urlopen(url) as r:
            data = r.read()
        with open(os.path.join(SRC, name + ".svg"), "wb") as f:
            f.write(data)


def render():
    os.makedirs(OUT, exist_ok=True)
    cells = []
    for name in NAMES:
        svg = open(os.path.join(SRC, name + ".svg"), encoding="utf8").read()
        # White, so the UI can tint it, at a fixed size.
        svg = svg.replace("<svg ", f'<svg fill="white" width="{SIZE}" height="{SIZE}" ', 1)
        cells.append(f'<div style="width:{SIZE}px;height:{SIZE}px">{svg}</div>')
    page = ("<!doctype html><html><body style=\"margin:0;background:transparent;display:flex\">"
            + "".join(cells) + "</body></html>")
    with tempfile.TemporaryDirectory() as tmp:
        html = os.path.join(tmp, "icons.html")
        shot = os.path.join(tmp, "icons.png")
        open(html, "w", encoding="utf8").write(page)
        subprocess.run([EDGE, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                        "--default-background-color=00000000", f"--user-data-dir={tmp}\\profile",
                        f"--window-size={SIZE * len(NAMES)},{SIZE}", "--force-device-scale-factor=1",
                        f"--screenshot={shot}", "file:///" + html.replace("\\", "/")],
                       check=True, capture_output=True)
        sheet = Image.open(shot).convert("RGBA")
        for i, name in enumerate(NAMES):
            icon = sheet.crop((i * SIZE, 0, (i + 1) * SIZE, SIZE))
            if icon.getchannel("A").getextrema()[1] == 0:
                raise SystemExit(f"{name} rendered empty")
            icon.save(os.path.join(OUT, name + ".png"))


if __name__ == "__main__":
    if "--download" in sys.argv:
        download()
    render()
    print(f"{len(NAMES)} icons in {OUT}")
