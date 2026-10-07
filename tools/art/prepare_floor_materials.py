"""Prepare runtime floor textures from docs/art/floor-materials-2026-10-06.

The generated sources are 1254 px squares whose repeat edges are close but not
exact. This script softens the wrap seam, resizes to power-of-two for mipmapped
repeat sampling, and cuts the carpet binding down to a whole number of its
leaf repeats so the strip tiles along its length.

    python3 tools/art/prepare_floor_materials.py
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "docs/art/floor-materials-2026-10-06"
OUT = ROOT / "Assets/Resources/Art/Floors"
BAND = 10  # source pixels blended toward the shared edge colour


def soften_wrap(im, horizontal=True, vertical=True):
    """Blend the outer pixels toward the mean of opposite edges so the wrap has no step."""
    im = im.copy()
    px = im.load()
    w, h = im.size
    if horizontal:
        for y in range(h):
            mid = tuple((a + b) // 2 for a, b in zip(px[0, y], px[w - 1, y]))
            for k in range(BAND):
                t = (1 - k / BAND) ** 2
                for x in (k, w - 1 - k):
                    px[x, y] = tuple(round(c + (m - c) * t) for c, m in zip(px[x, y], mid))
    if vertical:
        for x in range(w):
            mid = tuple((a + b) // 2 for a, b in zip(px[x, 0], px[x, h - 1]))
            for k in range(BAND):
                t = (1 - k / BAND) ** 2
                for y in (k, h - 1 - k):
                    px[x, y] = tuple(round(c + (m - c) * t) for c, m in zip(px[x, y], mid))
    return im


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name in ("floor-walnut-continuous-a", "floor-walnut-continuous-b", "carpet-burgundy-field", "carpet-charcoal-field"):
        im = Image.open(SRC / f"{name}.png").convert("RGB")
        soften_wrap(im).resize((1024, 1024), Image.LANCZOS).save(OUT / f"{name}.png", optimize=True)
    # Binding: the woven band spans rows 258-998; the leaf motif repeats every
    # 236 px. Four interior repeats avoid the source's mismatched outer edge.
    binding = Image.open(SRC / "carpet-antique-binding.png").convert("RGB").crop((228, 258, 228 + 4 * 236, 998))
    soften_wrap(binding, vertical=False).resize((1024, 256), Image.LANCZOS).save(OUT / "carpet-antique-binding.png", optimize=True)


if __name__ == "__main__":
    main()
