import sys, os, json, zipfile, io
import numpy as np
from PIL import Image

ZIPS = os.environ.get('ANIM_ZIPS', 'ArtSource/Anim/zips/')            # downloaded Autosprite isometric packs
OUT = os.environ.get('CHARS_OUT', 'Assets/Resources/Art/Chars/')
os.makedirs(OUT, exist_ok=True)
DIRS = [('right',0),('northeast',1),('up',2),('northwest',3),('left',4),('southwest',5),('down',6),('southeast',7)]  # E,NE,N,NW,W,SW,S,SE
ANIMS = ['idle','run','attack']
SCALE = float(os.environ.get('SCALE', '0.6'))

def load_frames(zpath):
    z = zipfile.ZipFile(zpath)
    res = {}
    for name, idx in DIRS:
        sheet = Image.open(io.BytesIO(z.read(name + '.png'))).convert('RGBA')
        at = json.loads(z.read(name + '.json'))['frames']
        frames = []
        for i in range(len(at)):
            f = at[str(i)]
            frames.append(sheet.crop((f['x'], f['y'], f['x'] + f['w'], f['y'] + f['h'])))
        res[idx] = frames
    return res

def bbox_of(frames):
    bb = None
    for fs in frames.values():
        for f in fs:
            b = f.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
            if b is None: continue
            bb = b if bb is None else (min(bb[0], b[0]), min(bb[1], b[1]), max(bb[2], b[2]), max(bb[3], b[3]))
    return bb

def build(char, prefix=None, quant=True):
    prefix = prefix or char
    data = {}
    for a in ANIMS:
        zp = ZIPS + f'{prefix}_{a}.zip'
        if not os.path.exists(zp):
            print('missing', zp); return False
        data[a] = load_frames(zp)
    bb = None
    for a in ANIMS:
        b = bbox_of(data[a])
        bb = b if bb is None else (min(bb[0], b[0]), min(bb[1], b[1]), max(bb[2], b[2]), max(bb[3], b[3]))
    pad = 3
    bb = (max(0, bb[0]-pad), max(0, bb[1]-pad), min(256, bb[2]+pad), min(256, bb[3]+pad))
    cw, ch = bb[2]-bb[0], bb[3]-bb[1]
    fw0, fh0 = round(cw*SCALE), round(ch*SCALE)
    fw, fh = (fw0 + 3)//4*4, (fh0 + 3)//4*4
    info = {'id': char, 'frameW': fw, 'frameH': fh, 'dirs': 8}
    # pivot: horizontal = centre of the source frame (128), vertical = bottom of the crop
    info['pivotX'] = round((128 - bb[0]) * SCALE / fw, 4)
    info['pivotY'] = round(max(0.0, (bb[3] - 250) * SCALE / fh), 4)
    info['contentH'] = fh0
    hb = [f.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox() for f in data['idle'][6]]
    info['heightPx'] = round(sum(b[3]-b[1] for b in hb)/len(hb) * SCALE, 1)
    for a in ANIMS:
        n = len(data[a][0])
        atlas = Image.new('RGBA', (fw*n, fh*8), (0,0,0,0))
        for d in range(8):
            for i, f in enumerate(data[a][d]):
                c = f.crop(bb).resize((fw0, fh0), Image.LANCZOS)
                atlas.paste(c, (i*fw, d*fh + (fh - fh0)))
        if quant:
            q = atlas.quantize(colors=255, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE)
            atlas = q
        fn = f'{char}_{a}.png'
        atlas.save(OUT + fn, optimize=True)
        info[a + 'Frames'] = n
        print(fn, atlas.size, os.path.getsize(OUT + fn)//1024, 'KB')
    json.dump(info, open(OUT + char + '.json', 'w'), indent=1)
    print(char, 'frame', fw, fh, 'pivotX', info['pivotX'])
    return True

if __name__ == '__main__':
    for c in sys.argv[1:]:
        build(c)
