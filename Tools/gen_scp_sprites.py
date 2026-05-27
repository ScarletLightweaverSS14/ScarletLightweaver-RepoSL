#!/usr/bin/env python3
"""
Generates the SCP-Redacted entity sprite RSI (no dependencies beyond stdlib).

Run from the repository root:
    python Tools/gen_scp_sprites.py

Output:
    Resources/Textures/Mobs/_Starlight/SCP/scp_redacted.rsi/
        alive.png   (128x32 — south, north, east, west frames)
        meta.json
"""

import json
import os
import struct
import zlib

# ── Output paths ──────────────────────────────────────────────────────────────
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT   = os.path.dirname(SCRIPT_DIR)
RSI_DIR     = os.path.join(
    REPO_ROOT,
    "Resources", "Textures", "Mobs", "_Starlight", "SCP", "scp_redacted.rsi"
)

# ── Minimal stdlib PNG writer (no PIL needed) ─────────────────────────────────
def _chunk(tag: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(tag + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)

def write_png(path: str, width: int, height: int, rows):
    """Write an RGBA PNG from a list-of-lists of (r,g,b,a) tuples."""
    raw = b""
    for row in rows:
        raw += b"\x00"  # filter-type None per scanline
        for r, g, b, a in row:
            raw += bytes([r, g, b, a])

    # IHDR: width(4) height(4) bitdepth(1) colortype(1) compress(1) filter(1) interlace(1) = 13 bytes
    ihdr_data = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)

    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(_chunk(b"IHDR", ihdr_data))
        f.write(_chunk(b"IDAT", zlib.compress(raw, 9)))
        f.write(_chunk(b"IEND", b""))

# ── Canvas helpers ────────────────────────────────────────────────────────────
def make_canvas(w: int, h: int):
    return [[(0, 0, 0, 0)] * w for _ in range(h)]

def fill_rect(canvas, x1: int, y1: int, x2: int, y2: int, color):
    H = len(canvas)
    W = len(canvas[0])
    for y in range(max(0, y1), min(H, y2)):
        for x in range(max(0, x1), min(W, x2)):
            canvas[y][x] = color

def fill_ellipse(canvas, cx: float, cy: float, rx: float, ry: float, color):
    H = len(canvas)
    W = len(canvas[0])
    for y in range(max(0, int(cy - ry) - 1), min(H, int(cy + ry) + 2)):
        for x in range(max(0, int(cx - rx) - 1), min(W, int(cx + rx) + 2)):
            if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0:
                canvas[y][x] = color

# ── Silhouette colour ─────────────────────────────────────────────────────────
C = (10, 10, 10, 240)   # near-black, slightly transparent

# ── Drawing ───────────────────────────────────────────────────────────────────
def draw_front(canvas, ox: int):
    """Front-facing (south) or back-facing (north) silhouette."""
    fill_ellipse(canvas, ox + 16,  6,  4.5,  4.5, C)   # head
    fill_rect   (canvas, ox + 14, 10, ox + 18, 13, C)   # neck
    fill_rect   (canvas, ox +  9, 12, ox + 23, 15, C)   # shoulders
    fill_rect   (canvas, ox +  7, 14, ox + 11, 23, C)   # left arm
    fill_rect   (canvas, ox + 21, 14, ox + 25, 23, C)   # right arm
    fill_rect   (canvas, ox + 11, 14, ox + 21, 23, C)   # torso
    fill_rect   (canvas, ox + 11, 22, ox + 16, 31, C)   # left leg
    fill_rect   (canvas, ox + 16, 22, ox + 21, 31, C)   # right leg

def draw_side(canvas, ox: int, flip: bool = False):
    """Side-profile silhouette.  flip=True gives the mirrored (west) view."""

    def p(local_x: int) -> int:
        """Convert local 0-31 x to absolute canvas x, with optional mirror."""
        return ox + (31 - local_x if flip else local_x)

    def r(x1: int, y1: int, x2: int, y2: int):
        ax1, ax2 = p(x1), p(x2)
        fill_rect(canvas, min(ax1, ax2), y1, max(ax1, ax2), y2, C)

    # head — slightly forward of centre so the nose side is visible
    ecx = p(15)
    fill_ellipse(canvas, ecx, 6, 4.5, 4.5, C)
    r(14, 10, 18, 13)   # neck
    r(12, 12, 20, 23)   # torso (narrow profile, ~8 px)
    r( 8, 14, 12, 22)   # front arm (hanging slightly forward)
    r(20, 14, 23, 20)   # back arm  (slightly shorter)
    r(12, 22, 16, 31)   # front leg
    r(16, 22, 20, 28)   # back leg  (slightly shorter)

# ── Compose 4-direction sprite sheet (128 × 32) ───────────────────────────────
canvas = make_canvas(128, 32)

draw_front(canvas,  0)            # column 0:  South
draw_front(canvas, 32)            # column 1:  North  (same silhouette)
draw_side (canvas, 64, flip=False) # column 2:  East
draw_side (canvas, 96, flip=True)  # column 3:  West

# ── Write files ───────────────────────────────────────────────────────────────
os.makedirs(RSI_DIR, exist_ok=True)

write_png(os.path.join(RSI_DIR, "alive.png"), 128, 32, canvas)

meta = {
    "version": 1,
    "license": "CC0-1.0",
    "copyright": "Generated for Starlight Chimes — SCP Redacted entity",
    "size": {"x": 32, "y": 32},
    "states": [
        {"name": "alive", "directions": 4}
    ]
}
with open(os.path.join(RSI_DIR, "meta.json"), "w", encoding="utf-8") as f:
    json.dump(meta, f, indent=2)

print(f"Done! Sprite written to:\n  {RSI_DIR}")
