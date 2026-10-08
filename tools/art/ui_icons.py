#!/usr/bin/env python3
"""Render the HUD window icons (tower types, close, level up, currencies, the evolve shadows) to PNG.

The icons are small SVGs drawn here; headless Chrome rasterises them with a transparent background.
White icons are tinted in GameHUD; the evolve shadows and currency icons carry their own colours.

  python3 tools/art/ui_icons.py --out Assets/Resources/Art/UI
"""
import argparse, os, subprocess, tempfile
from PIL import Image
W = '#ffffff'
def stroke(paths, sw=1.8, vb='0 0 24 24'):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}" fill="none" stroke="{W}" stroke-width="{sw}" stroke-linecap="round" stroke-linejoin="round">{paths}</svg>'
icons = {
 'type_bullets': stroke('<path d="M9 21h6v-9c0-4-1.5-7-3-9-1.5 2-3 5-3 9z"/><path d="M9 16h6"/>'),
 'type_fire': stroke('<path d="M12 3c1 4 6 6 6 11a6 6 0 0 1-12 0c0-3 2-5 3-6 0 2 1 3 2 3 0-3-1-5 1-8z"/>'),
 'type_electric': stroke('<path d="M13 2 5 14h6l-1 8 8-12h-6z"/>'),
 'type_effects': stroke('<path d="M7 3h10M7 21h10M8 3c0 5 8 6 8 9s-8 4-8 9M16 3c0 5-8 6-8 9s8 4 8 9"/>'),
 'type_resources': stroke('<path d="M15 4a8 8 0 1 0 5 13 7 7 0 0 1-5-13z"/><path d="M18.5 2.5v3M17 4h3"/>'),
 'close': stroke('<path d="M6 6l12 12M18 6 6 18"/>', 2.2),
 'levelup': stroke('<path d="M6 19l6-5 6 5"/><path d="M6 13.5l6-5 6 5"/><path d="M6 8l6-5 6 5"/>', 2.2),
 'faith': '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><radialGradient id="g" cx="50%" cy="50%" r="50%"><stop offset="0" stop-color="#fff"/><stop offset=".45" stop-color="#d9c8ff"/><stop offset="1" stop-color="#b99cff"/></radialGradient></defs><path d="M12 1.5 14.2 9.8 22.5 12 14.2 14.2 12 22.5 9.8 14.2 1.5 12 9.8 9.8Z" fill="url(#g)"/></svg>',
 'dream': '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><radialGradient id="g" cx="42%" cy="40%" r="60%"><stop offset="0" stop-color="#fff"/><stop offset=".4" stop-color="#cdf6e6"/><stop offset="1" stop-color="#9fe3c8"/></radialGradient></defs><path d="M5.5 19.5C3 15 6 12.5 8.5 13.5" fill="none" stroke="#9fe3c8" stroke-width="1.6" stroke-linecap="round" opacity=".8"/><circle cx="13" cy="11" r="7.5" fill="url(#g)"/><path d="M9.5 9.5a4 4 0 0 1 4-3" fill="none" stroke="#fff" stroke-width="1.4" stroke-linecap="round" opacity=".9"/></svg>',
}
SMALL='M5 37C5 27 6 17 11.5 15.5 17 17 18 27 18 37 16 34 14 38 11.5 35 9 38 7 34 5 37Z'
BIG='M38 39C37 25 39 6 50 3.5 61 6 63 25 62 39 59 35 57 40 54 36 51 40 49 35 46 39 43 35 41 40 38 39Z'
EYES='<circle cx="9.4" cy="22" r="1.3"/><circle cx="13.6" cy="22" r="1.3"/><ellipse cx="45" cy="15.5" rx="2.3" ry="1.5" transform="rotate(14 45 15.5)"/><ellipse cx="55" cy="15.5" rx="2.3" ry="1.5" transform="rotate(-14 55 15.5)"/>'
GRINS='<path d="M8.8 26Q11.5 28.4 14.2 26"/><path d="M41.5 23Q50 31.5 58.5 23"/>'
TEETH='<path d="M43.5 25.4 45.3 27.6 47.2 26.3 49 28.7 51 26.6 52.8 28.6 54.7 26.2 56.5 25.2" fill="none" stroke="#07050b" stroke-width=".9"/>'
ARROW='<path d="M22 24.5H33.5M29.5 20.5 33.5 24.5 29.5 28.5" fill="none" stroke="{c}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'
def evo(rim, ro, arrow, eyes, grins):
    s=f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 42">'
    s+=f'<g fill="#07050b" stroke="{rim}" stroke-opacity="{ro}" stroke-width="1.2"><path d="{SMALL}"/><path d="{BIG}"/></g>'
    s+=ARROW.format(c=arrow)
    if grins: s+=f'<g fill="none" stroke="#9fe3c8" stroke-width="1.3" stroke-linecap="round">{GRINS}</g>'+TEETH
    if eyes: s+=f'<g fill="#c9fbe8">{EYES}</g>'
    return s+'</svg>'
icons['evolve_body']=evo('#9fe3c8',.8,'#b99cff',False,True)
icons['evolve_eyes']='<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 42"><g fill="#c9fbe8">'+EYES+'</g></svg>'
icons['evolve_dim']=evo('#e8dcc0',.28,'rgba(232,220,192,.25)',False,False)
sizes={'evolve_body':(256,168),'evolve_eyes':(256,168),'evolve_dim':(256,168)}

ap = argparse.ArgumentParser()
ap.add_argument('--out', required=True)
ap.add_argument('--chrome', default='google-chrome')
args = ap.parse_args()
os.makedirs(args.out, exist_ok=True)
tmp = tempfile.mkdtemp()
for name, svg in icons.items():
    w, h = sizes.get(name, (128, 128))
    sized = svg.replace('<svg ', '<svg width="%d" height="%d" ' % (w, h), 1)
    page = os.path.join(tmp, name + '.html')
    shot = os.path.join(tmp, name + '.png')
    open(page, 'w').write('<html><body style="margin:0;background:transparent">' + sized + '</body></html>')
    # Headless Chrome keeps part of a small window for itself, so render big and crop.
    subprocess.run([args.chrome, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--default-background-color=00000000',
                    '--window-size=600,600', '--screenshot=' + shot, 'file://' + page], capture_output=True, check=True)
    Image.open(shot).convert('RGBA').crop((0, 0, w, h)).save(os.path.join(args.out, name + '.png'))
    print(name, w, h)
