# Turns Autosprite generations (white background, 1024px) into the transparent sprites in Assets/Resources/Art.
# Paths below point at the folders where the raw images were saved (ArtSource/ is gitignored); adjust before re-running.
# Door recolor (wood -> brown) was applied afterwards by hand: hue 0.07, saturation x1.5, value x1.25 on purple pixels.
import numpy as np, colorsys, os, sys
from PIL import Image
from scipy import ndimage as ndi

SRC_B = '/mnt/user-data/uploads/Documents/BadApple/ArtSource/Burton/'
SRC_BEDS = '/mnt/user-data/uploads/Documents/BadApple/Assets/Art/Source/Beds/'
OUT = '/home/claude/badapple/Assets/Resources/Art/'
os.makedirs(OUT, exist_ok=True)

def key_white(im, pockets=False):
    a = np.asarray(im.convert('RGB')).astype(np.float32)
    mn = a.min(axis=2)
    light = mn >= 170
    lab, n = ndi.label(light)
    border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
    border = border[border != 0]
    outside = np.isin(lab, border)
    if pockets:
        pure = mn >= 250
        areas = ndi.sum(np.ones_like(mn), lab, index=np.arange(1, n + 1))
        purec = ndi.sum(pure.astype(np.float32), lab, index=np.arange(1, n + 1))
        for i in range(1, n + 1):
            if i in set(border.tolist()): continue
            if areas[i-1] > 150 and purec[i-1] / areas[i-1] > 0.9:
                outside |= (lab == i)
    alpha = np.ones_like(mn)
    al = np.clip((245.0 - mn) / (245.0 - 170.0), 0, 1)
    alpha = np.where(outside, al, 1.0)
    # unpremultiply against white for translucent pixels
    out = a.copy()
    m = (alpha < 1.0) & (alpha > 0.0)
    for c in range(3):
        ch = a[..., c]
        out[..., c] = np.where(m, np.clip((ch - 255.0 * (1 - alpha)) / np.maximum(alpha, 1e-3), 0, 255), ch)
    rgba = np.dstack([out, alpha * 255.0]).astype(np.uint8)
    return Image.fromarray(rgba, 'RGBA')

def crop_fit(img, box_w=None, box_h=None, max_px=256, pad=2, square=False):
    bb = img.getchannel('A').point(lambda v: 255 if v > 8 else 0).getbbox()
    img = img.crop(bb)
    w, h = img.size
    if square:
        img = img.resize((max_px, max_px), Image.LANCZOS)
    else:
        s = max_px / max(w, h)
        img = img.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    # pad transparent border to avoid bilinear bleed
    out = Image.new('RGBA', (img.width + pad * 2, img.height + pad * 2), (0, 0, 0, 0))
    out.paste(img, (pad, pad))
    return out

def save(img, name):
    img.save(OUT + name + '.png', optimize=True)
    print(name, img.size)

def load(path): return Image.open(path)

def sprite(src, name, max_px=256, square=False, pockets=False):
    save(crop_fit(key_white(load(src), pockets), max_px=max_px, square=square), name)

# ---- towers
for n, s in [('gun_turret','t_gun_1'),('missile_launcher','t_missile_0'),('electric_tower','t_electric_2'),
             ('dragon_statue','t_dragon_1'),('slow_totem','t_slow_2'),('faith_tower','t_faith_3'),('crystal_ball','t_crystal_1')]:
    sprite(SRC_B + s + '.png', 'tower_' + n, 256, pockets=n in ('gun_turret','electric_tower','slow_totem','crystal_ball','dragon_statue'))

# ---- doors (square-ish tiles); keep aspect for open door
sprite(SRC_B + 'd_wood_0.png', 'door_wood', 256, square=True)
sprite(SRC_B + 'd_reinf_1.png', 'door_reinforced', 256, square=True)
sprite(SRC_B + 'd_iron_0.png', 'door_iron', 256, square=True)
sprite(SRC_B + 'd_open_3.png', 'door_open', 256, square=True)
sprite(SRC_B + 'd_broken_0.png', 'door_broken', 256, square=True)
sprite(SRC_B + 'plate_0.png', 'build_plate', 192, square=True)

# ---- beds
for i, n in enumerate(['bed1_paper','bed2_cardboard','bed3_straw','bed4_spring','bed5_raised','bed6_haunted'], 1):
    sprite(SRC_BEDS + n + '.png', 'bed_%d' % i, 384)

# ---- monsters, ghost, parts
sprite(SRC_B + 'c_chef_0.png', 'monster_stitchwork_chef', 320)
sprite(SRC_B + 'c_matron_1.png', 'monster_moldy_matron', 320)
sprite(SRC_B + 'c_wraith_1.png', 'monster_bellhop_wraith', 320)
sprite(SRC_B + 'c_ghost_1.png', 'ghost', 192)
sprite(SRC_B + 'p_arm_0.png', 'part_arm', 128)
sprite(SRC_B + 'p_leg_0.png', 'part_leg', 128)
sprite(SRC_B + 'p_torso_2.png', 'part_torso', 128)
sprite(SRC_B + 'p_eye_0.png', 'part_eye', 128)

# ---- residents: recolor pajamas (magenta) into 6 hues
res = crop_fit(key_white(load(SRC_B + 'c_res_1.png')), max_px=288)
arr = np.asarray(res).astype(np.float32) / 255.0
r, g, b, a = arr[..., 0], arr[..., 1], arr[..., 2], arr[..., 3]
mx = arr[..., :3].max(axis=2); mn = arr[..., :3].min(axis=2)
sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
# hue
d = np.maximum(mx - mn, 1e-6)
h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) / 6.0
mask = (h > 0.78) & (h < 0.94) & (sat > 0.35) & (mx > 0.2) & (a > 0.5)
print('pajama pixels', int(mask.sum()))
targets = [0.86, 0.52, 0.30, 0.07, 0.62, 0.14]  # magenta, teal, moss, orange, blue, mustard
import matplotlib.colors as mc
hsv = mc.rgb_to_hsv(arr[..., :3])
for i, th in enumerate(targets):
    hh = hsv.copy()
    hh[..., 0] = np.where(mask, th, hh[..., 0])
    rgb = mc.hsv_to_rgb(hh)
    outa = np.dstack([rgb, a])
    save(Image.fromarray((outa * 255).astype(np.uint8), 'RGBA'), 'resident_%d' % i)

# ---- tileable floors
def seamless(img, size):
    a = np.asarray(img.convert('RGB').resize((size, size), Image.LANCZOS)).astype(np.float32)
    t = np.linspace(0, 1, size)
    tent = 1 - np.abs(t * 2 - 1)           # 0 at edges, 1 in the middle
    m = np.clip(tent * 1.6, 0, 1)
    m2 = (m[:, None] * m[None, :])[..., None]
    shifted = np.roll(np.roll(a, size // 2, axis=0), size // 2, axis=1)
    out = a * m2 + shifted * (1 - m2)
    return Image.fromarray(out.astype(np.uint8), 'RGB')

def center_crop(path, frac, dx=0, dy=0):
    im = load(path).convert('RGB'); w, h = im.size; c = int(w * frac)
    x0 = (w - c) // 2 + dx; y0 = (h - c) // 2 + dy
    return im.crop((x0, y0, x0 + c, y0 + c))

save(seamless(center_crop(SRC_B + 'f_room_1.png', 0.33), 128).convert('RGBA'), 'floor_room')
save(seamless(center_crop(SRC_B + 'f_corr_1.png', 0.5), 128).convert('RGBA'), 'floor_corridor')
save(seamless(center_crop(SRC_B + 'f_wall_1.png', 0.28), 128).convert('RGBA'), 'wall')
