#!/usr/bin/env python3
"""Turn AutoSprite exports into a TowerSpriteSet folder for Unity.

A clip is either an 8-direction pack folder (right.png/right.json ... from generate_isometric_pack)
or a single sheet plus its atlas (animate_asset). Every frame of every clip is cropped to one shared
box, so frames swap without moving the sprite on its square, and the feet sit on the bottom edge.

  python3 tools/art/tower_anim.py --out Assets/Resources/Art/TowerAnim/gun_turret_1 --height 1.35 \
      --idle pack:/path/idle --fire pack:/path/fire --release .3
  python3 tools/art/tower_anim.py --out Assets/Resources/Art/TowerAnim/slow_totem_1 --object-height 1.26 \
      --idle sheet:/path/sheet.png:/path/atlas.json
"""
import argparse, json, os
from PIL import Image, ImageChops

# TowerSpriteSet.DirNames order (CharacterSet.DirIndex): east, then counter-clockwise on screen.
PACK_DIRS = [("e", "right"), ("ne", "northeast"), ("n", "up"), ("nw", "northwest"),
             ("w", "left"), ("sw", "southwest"), ("s", "down"), ("se", "southeast")]


def atlas_frames(sheet, atlas):
    image = Image.open(sheet).convert("RGBA")
    frames = json.load(open(atlas))["frames"]
    items = frames.values() if isinstance(frames, dict) else frames
    out = []
    for f in items:
        f = f.get("frame", f)
        out.append(image.crop((f["x"], f["y"], f["x"] + f["w"], f["y"] + f["h"])))
    return out


def key_white(frame, threshold=236):
    """Clears pure white pixels (every channel above the threshold): leftovers of a white
    background that animation reveals, for example behind wings that spread."""
    rgba = frame.convert("RGBA")
    r, g, b, a = rgba.split()
    low = ImageChops.darker(ImageChops.darker(r, g), b)
    white = low.point(lambda v: 255 if v > threshold else 0)
    rgba.putalpha(ImageChops.subtract(a, white))
    return rgba


def load_clip(spec):
    kind, _, rest = spec.partition(":")
    if kind == "pack":
        return {short: atlas_frames(os.path.join(rest, name + ".png"), os.path.join(rest, name + ".json"))
                for short, name in PACK_DIRS}
    if kind == "sheet":
        sheet, atlas = rest.rsplit(":", 1)
        return {"": atlas_frames(sheet, atlas)}
    raise SystemExit("clip must be pack:<dir> or sheet:<png>:<json>")


def sample(frames, count):
    if count <= 0 or count >= len(frames):
        return frames
    return [frames[round(i * len(frames) / count)] for i in range(count)]


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    size = p.add_mutually_exclusive_group(required=True)
    size.add_argument("--height", type=float, help="world height of the shared cell, in tiles")
    size.add_argument("--object-height", type=float,
                      help="world height of the object in the first idle frame; motion above or below it (smoke, wings) adds to the cell")
    p.add_argument("--idle", required=True)
    p.add_argument("--fire")
    p.add_argument("--idle-frames", type=int, default=16)
    p.add_argument("--fire-frames", type=int, default=12)
    p.add_argument("--idle-fps", type=float, default=8)
    p.add_argument("--fire-fps", type=float, default=14)
    p.add_argument("--release", type=float, default=.3, help="seconds into the fire clip when the shot leaves")
    p.add_argument("--max-cell", type=int, default=176, help="cell height cap in pixels (mobile memory)")
    p.add_argument("--cols", type=int, default=8)
    p.add_argument("--key-white", action="store_true", help="remove pure white background leftovers")
    a = p.parse_args()

    clips = {"idle": (load_clip(a.idle), a.idle_frames, a.idle_fps, True)}
    if a.fire:
        clips["fire"] = (load_clip(a.fire), a.fire_frames, a.fire_fps, False)
    clips = {name: ({d: sample(f, n) for d, f in views.items()}, fps, loop) for name, (views, n, fps, loop) in clips.items()}
    if a.key_white:
        clips = {name: ({d: [key_white(f) for f in frames] for d, frames in views.items()}, fps, loop)
                 for name, (views, fps, loop) in clips.items()}

    # One box around the opaque pixels of every frame in every clip and direction.
    box = None
    for views, _, _ in clips.values():
        for frames in views.values():
            for frame in frames:
                b = frame.split()[-1].point(lambda v: 255 if v > 8 else 0).getbbox()
                if b:
                    box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
    if box is None:
        raise SystemExit("every frame is empty")
    w, h = box[2] - box[0], box[3] - box[1]
    height = a.height
    if height is None:
        first = next(iter(clips["idle"][0].values()))[0]
        b = first.split()[-1].point(lambda v: 255 if v > 8 else 0).getbbox()
        height = a.object_height * h / max(1, b[3] - b[1])
    scale = min(1.0, a.max_cell / h)
    cw, ch = max(1, round(w * scale)), max(1, round(h * scale))

    os.makedirs(a.out, exist_ok=True)
    defs = []
    for name, (views, fps, loop) in clips.items():
        count = len(next(iter(views.values())))
        cols = min(a.cols, count)
        rows = (count + cols - 1) // cols
        for d, frames in views.items():
            sheet = Image.new("RGBA", (cols * cw, rows * ch))
            for i, frame in enumerate(frames):
                cell = frame.crop(box).resize((cw, ch), Image.LANCZOS)
                sheet.paste(cell, ((i % cols) * cw, (i // cols) * ch))
            # AutoSprite already delivers 256-colour sheets; quantizing again keeps the repository small.
            sheet = sheet.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
            sheet.save(os.path.join(a.out, name + ("_" + d if d else "") + ".png"), optimize=True)
        defs.append({"name": name, "dirs": len(views), "count": count, "cols": cols, "fps": fps, "loop": loop})
    meta = {"cellW": cw, "cellH": ch, "height": round(height, 3), "release": a.release, "clips": defs}
    with open(os.path.join(a.out, "anim.json"), "w") as f:
        json.dump(meta, f, indent=2)
    print(a.out, meta)


if __name__ == "__main__":
    main()
