"""Generate placeholder art PNGs for the card pipeline (stdlib only, no PIL).

Outputs (all consumed by Cards > Pipeline > Import All in Unity):
  Assets/GameData/art/cards/{cardId}__base.png   stick-figure-in-a-field placeholder, one per card JSON
  Assets/GameData/art/borders/{borderId}.png     colored frame with a transparent center, one per borders.json entry

Border colors follow the archetype pill colors (tank=orange, mage=purple,
healer=green, assassin=dark, bruiser=blue). Swap any of these by dropping a
real PNG over the generated file and re-running Import All.

Usage:  py tools/gen_placeholder_art.py
"""
import json
import math
import struct
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CARDS_DIR = ROOT / "Assets" / "GameData" / "cards"
ART_DIR = ROOT / "Assets" / "GameData" / "art" / "cards"
BORDER_DIR = ROOT / "Assets" / "GameData" / "art" / "borders"
BORDERS_JSON = ROOT / "Assets" / "GameData" / "borders.json"

BORDER_COLORS = {
    "tank":      (232, 137, 12),    # orange
    "bruiser":   (58, 108, 214),    # blue
    "assassin":  (45, 45, 52),      # near-black
    "mage":      (123, 47, 190),    # purple
    "healer":    (52, 168, 83),     # green
    "common":    (140, 140, 140),
    "rare":      (64, 140, 230),
    "epic":      (153, 76, 217),
    "legendary": (242, 153, 38),
}


# ---------------------------------------------------------------- PNG writing

def write_png(path: Path, width: int, height: int, pixels: bytearray) -> None:
    """pixels = RGBA bytes, row-major, top row first."""
    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)  # filter type: None
        raw.extend(pixels[y * stride:(y + 1) * stride])

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
           + chunk(b"IEND", b""))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


class Canvas:
    def __init__(self, width: int, height: int, fill=(0, 0, 0, 0)):
        self.w, self.h = width, height
        self.px = bytearray(width * height * 4)
        r, g, b, a = fill
        for i in range(0, len(self.px), 4):
            self.px[i:i + 4] = bytes((r, g, b, a))

    def set(self, x: int, y: int, color) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            i = (y * self.w + x) * 4
            r, g, b = color[:3]
            a = color[3] if len(color) > 3 else 255
            self.px[i:i + 4] = bytes((r, g, b, a))

    def rect(self, x0: int, y0: int, x1: int, y1: int, color) -> None:
        for y in range(max(0, y0), min(self.h, y1)):
            for x in range(max(0, x0), min(self.w, x1)):
                self.set(x, y, color)

    def disc(self, cx: float, cy: float, radius: float, color) -> None:
        r2 = radius * radius
        for y in range(int(cy - radius) - 1, int(cy + radius) + 2):
            for x in range(int(cx - radius) - 1, int(cx + radius) + 2):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r2:
                    self.set(x, y, color)

    def line(self, x0: float, y0: float, x1: float, y1: float, thickness: float, color) -> None:
        steps = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
        for i in range(steps + 1):
            t = i / steps
            self.disc(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, thickness / 2, color)

    def circle_outline(self, cx: float, cy: float, radius: float, thickness: float, color) -> None:
        for deg in range(0, 3600):
            ang = math.radians(deg / 10)
            self.disc(cx + radius * math.cos(ang), cy + radius * math.sin(ang), thickness / 2, color)


# ---------------------------------------------------------------- artwork

def stick_figure_art() -> Canvas:
    """The placeholder: stick figure standing in a field."""
    c = Canvas(512, 512, fill=(148, 205, 235, 255))          # sky
    c.rect(0, 400, 512, 512, (96, 176, 88))                  # grass
    ink = (20, 20, 20)
    c.circle_outline(256, 150, 44, 9, ink)                   # head
    c.line(256, 194, 256, 330, 9, ink)                       # body
    c.line(256, 235, 188, 292, 9, ink)                       # arms
    c.line(256, 235, 324, 292, 9, ink)
    c.line(256, 330, 198, 436, 9, ink)                       # legs
    c.line(256, 330, 314, 436, 9, ink)
    return c


def border_frame(color, width=480, height=672, edge=26) -> Canvas:
    """Colored frame with a transparent center and a thin inner accent line."""
    c = Canvas(width, height, fill=(0, 0, 0, 0))
    r, g, b = color
    dark = (max(0, r - 60), max(0, g - 60), max(0, b - 60))
    light = (min(255, r + 55), min(255, g + 55), min(255, b + 55))
    for y in range(height):
        for x in range(width):
            d = min(x, y, width - 1 - x, height - 1 - y)     # distance to nearest edge
            if d < 3:
                c.set(x, y, dark)
            elif d < edge - 4:
                c.set(x, y, color)
            elif d < edge:
                c.set(x, y, light)
    return c


def main() -> None:
    art = stick_figure_art()
    made = 0
    for card_json in sorted(CARDS_DIR.glob("*.json")):
        card_id = json.loads(card_json.read_text(encoding="utf-8"))["cardId"]
        out = ART_DIR / f"{card_id}__base.png"
        write_png(out, art.w, art.h, art.px)
        made += 1
    print(f"wrote {made} card art PNG(s) to {ART_DIR}")

    borders = json.loads(BORDERS_JSON.read_text(encoding="utf-8"))["borders"]
    for entry in borders:
        border_id = entry["borderId"]
        color = BORDER_COLORS.get(border_id, (128, 128, 128))
        frame = border_frame(color)
        write_png(BORDER_DIR / f"{border_id}.png", frame.w, frame.h, frame.px)
    print(f"wrote {len(borders)} border PNG(s) to {BORDER_DIR}")


if __name__ == "__main__":
    main()
