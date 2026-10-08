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
# Monster windows: stat tracks, kit ranks, Fear and the minion lines (white, tinted in GameHUD; Fear carries its pink).
icons.update({
 'stat_vitality': stroke('<path d="M12 20.5s-7.5-4.6-7.5-10.2A4.2 4.2 0 0 1 12 7.6a4.2 4.2 0 0 1 7.5 2.7c0 5.6-7.5 10.2-7.5 10.2z"/><path d="M9 11.5h2l1-2 1.5 4 1-2H15"/>'),
 'stat_hide': stroke('<path d="M12 3 5 6v5.5c0 4.5 3 7.7 7 9.5 4-1.8 7-5 7-9.5V6z"/><path d="M12 7v10M8.5 10.5h7"/>'),
 'stat_stride': stroke('<path d="M7 4h5l1 7 6 2.5c1.2.5 1.5 1.5 1.5 2.5v1H7z"/><path d="M7 17h13.5"/><path d="M2 8h3M1.5 11.5h3.5M2 15h3"/>'),
 'stat_frenzy': stroke('<path d="M5 18 13 4"/><path d="M10 20 18 6"/><path d="M15 21 21 10"/><path d="M2 9h3M3 13h2.5"/>'),
 'stat_maw': stroke('<path d="M3 8c4-3 14-3 18 0-1 6-5 10-9 10S4 14 3 8z"/><path d="M6.5 8.6 8 12l1.6-3.2L11 12l1-3.4L13.5 12l1.4-3.2L16.5 12l1-3.4"/>'),
 'stat_horde': stroke('<path d="M4 20v-6a3 3 0 0 1 6 0v6l-1-1-1 1-1-1-1 1-1-1z"/><path d="M14 20v-6a3 3 0 0 1 6 0v6l-1-1-1 1-1-1-1 1-1-1z"/><path d="M9 11V7a3 3 0 0 1 6 0v4"/><circle cx="6.3" cy="15" r=".4"/><circle cx="7.9" cy="15" r=".4"/><circle cx="16.3" cy="15" r=".4"/><circle cx="17.9" cy="15" r=".4"/><circle cx="11.3" cy="7.6" r=".4"/><circle cx="12.9" cy="7.6" r=".4"/>'),
 'kit_attack': stroke('<path d="M4 20 15 9"/><path d="M13 5l6 6-3 3-6-6z"/><path d="M5.5 15.5l3 3"/>'),
 'kit_area': stroke('<circle cx="12" cy="12" r="3"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1"/>'),
 'kit_special': stroke('<path d="M12 2.5l2.4 5.6 6.1.6-4.6 4 1.4 6-5.3-3.2-5.3 3.2 1.4-6-4.6-4 6.1-.6z"/><circle cx="12" cy="11.5" r="1.6"/>'),
 'trick': stroke('<path d="M12 3v4M12 17v4M3 12h4M17 12h4"/><path d="M12 8.5 13.2 11 15.5 12l-2.3 1-1.2 2.5-1.2-2.5L8.5 12l2.3-1z"/>'),
 'minion_cloche': stroke('<path d="M3.5 17.5h17"/><path d="M5 17.5a7 7 0 0 1 14 0"/><circle cx="12" cy="9.3" r="1"/><path d="M6.5 17.5l1.2 2 1.3-2 1.2 2 1.3-2 1.2 2 1.3-2 1.2 2 1.3-2"/><circle cx="9.5" cy="14" r=".6"/><circle cx="14.5" cy="14" r=".6"/>'),
 'minion_puffcap': stroke('<path d="M3.5 12a8.5 6.5 0 0 1 17 0z"/><path d="M9 12v5.5a3 3 0 0 0 6 0V12"/><circle cx="7.5" cy="9" r=".9"/><circle cx="12" cy="7.3" r=".9"/><circle cx="16.5" cy="9" r=".9"/><path d="M10.5 15h.01M13.5 15h.01"/><path d="M9.5 19.5l-2 1.5M14.5 19.5l2 1.5"/>'),
 'minion_mimic': stroke('<rect x="3.5" y="7" width="17" height="12" rx="2"/><path d="M9 7V5h6v2"/><path d="M3.5 12.5h17"/><path d="M5.5 12.5l1 1.8 1-1.8 1 1.8 1-1.8 1 1.8 1-1.8 1 1.8 1-1.8 1 1.8 1-1.8 1 1.8 1-1.8"/><circle cx="8" cy="9.8" r=".6"/><circle cx="16" cy="9.8" r=".6"/>'),
 'fear': '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><defs><radialGradient id="g" cx="45%" cy="40%" r="65%"><stop offset="0" stop-color="#ffd6e8"/><stop offset=".5" stop-color="#ff8fc1"/><stop offset="1" stop-color="#e2457f"/></radialGradient></defs><path d="M12 2c3 4.5 7 8.3 7 12.3A7 7 0 0 1 5 14.3C5 10.3 9 6.5 12 2z" fill="url(#g)"/><ellipse cx="9.6" cy="14" rx="1.2" ry="1.6" fill="#2a0f1c"/><ellipse cx="14.4" cy="14" rx="1.2" ry="1.6" fill="#2a0f1c"/><ellipse cx="12" cy="18.2" rx="1.3" ry="1.6" fill="#2a0f1c"/></svg>',
})
# Monster controls and the horde tray: one icon per ability, the three minion roles, lock and the hotel view.
icons.update({
 'ab_flambe': stroke('<path d="M12 3c1 4 6 6 6 11a6 6 0 0 1-12 0c0-3 2-5 3-6 0 2 1 3 2 3 0-3-1-5 1-8z"/><path d="M3 21h18"/>'),
 'ab_spores': stroke('<circle cx="12" cy="12" r="3"/><circle cx="5" cy="7" r="1.6"/><circle cx="19" cy="6.5" r="1.4"/><circle cx="18" cy="17" r="1.8"/><circle cx="6" cy="17.5" r="1.3"/><circle cx="12" cy="3.5" r="1"/><circle cx="12" cy="20.5" r="1"/>'),
 'ab_bell': stroke('<path d="M6 16V11a6 6 0 0 1 12 0v5l2 2H4z"/><path d="M10 20.5a2 2 0 0 0 4 0"/><path d="M12 3v2"/><path d="M2.5 9a8 8 0 0 1 2-4M21.5 9a8 8 0 0 0-2-4"/>'),
 'ab_hook': stroke('<path d="M12 2v10"/><path d="M12 12a5 5 0 1 1-5 5"/><path d="M7 17l-2-2.5M7 17l2.6-.8"/><circle cx="12" cy="2.5" r=".6"/>'),
 'ab_shrine': stroke('<path d="M5 11a7 5 0 0 1 14 0z"/><path d="M10 11v6h4v-6"/><path d="M4 21h16"/><path d="M8 17.5c-2 1-3 2-4 3.5M16 17.5c2 1 3 2 4 3.5"/>'),
 'ab_door': stroke('<rect x="6" y="3" width="12" height="18" rx="1"/><circle cx="15" cy="12" r=".9"/><path d="M2 8c2 0 2 2 4 2M2 14c2 0 2 2 4 2"/>'),
 'ab_jam': stroke('<path d="M3 13h10l2-3h5v4h-5l-2 3H6z"/><path d="M4 4l16 16"/>'),
 'ab_rampage': stroke('<path d="M6 11V7a1.5 1.5 0 0 1 3 0v3M9 10V5.5a1.5 1.5 0 0 1 3 0V10M12 10V6.5a1.5 1.5 0 0 1 3 0V11M15 11V8.5a1.5 1.5 0 0 1 3 0V14a7 7 0 0 1-7 7h-1a6 6 0 0 1-6-6v-2a2 2 0 0 1 2-2h2"/>'),
 'ab_blackout': stroke('<path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5z"/><path d="M15 4l1 2 2 1-2 1-1 2-1-2-2-1 2-1z"/>'),
 'ab_lunge': stroke('<path d="M4 12h12"/><path d="M12 6l6 6-6 6"/><path d="M2 8h4M2 16h4"/>'),
 'ab_cloak': stroke('<path d="M2.5 12s3.5-6.5 9.5-6.5S21.5 12 21.5 12s-3.5 6.5-9.5 6.5S2.5 12 2.5 12z"/><path d="M4 20 20 4"/>'),
 'ab_slime': stroke('<path d="M12 3c2.5 3.5 6 6.5 6 10a6 6 0 0 1-12 0c0-3.5 3.5-6.5 6-10z"/><path d="M4 21c2-1.5 3-1.5 5 0s3 1.5 5 0 3-1.5 5 0"/>'),
 'ab_gaze': stroke('<path d="M2.5 12s3.5-6.5 9.5-6.5S21.5 12 21.5 12s-3.5 6.5-9.5 6.5S2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/><path d="M12 2v2M5 4l1.3 1.6M19 4l-1.3 1.6"/>'),
 'eye': stroke('<path d="M2.5 12s3.5-6.5 9.5-6.5S21.5 12 21.5 12s-3.5 6.5-9.5 6.5S2.5 12 2.5 12z"/><circle cx="12" cy="12" r="3"/>'),
 'role_swarm': stroke('<circle cx="6" cy="8" r="2.4"/><circle cx="13" cy="6" r="2.4"/><circle cx="18" cy="12" r="2.4"/><circle cx="8" cy="15" r="2.4"/><circle cx="14" cy="18" r="2.4"/>'),
 'role_breacher': stroke('<rect x="11" y="4" width="9" height="16" rx="1"/><path d="M3 12h7"/><path d="M7 9l3 3-3 3"/><path d="M14 8l2 3-2 2 2 3"/>'),
 'role_escort': stroke('<path d="M12 3 5 6v5.5c0 4.5 3 7.7 7 9.5 4-1.8 7-5 7-9.5V6z"/><path d="M9 12l2 2 4-4"/>'),
 'lock': stroke('<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/><circle cx="12" cy="15.5" r="1.2"/>'),
 'weak': stroke('<path d="M12 3 5 6v5.5c0 4.5 3 7.7 7 9.5 4-1.8 7-5 7-9.5V6z"/><path d="M13 6l-3 6h4l-3 6"/>'),
})
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
ap.add_argument('--only', nargs='*', help='render just these icons')
args = ap.parse_args()
os.makedirs(args.out, exist_ok=True)
tmp = tempfile.mkdtemp()
for name, svg in icons.items():
    if args.only and name not in args.only: continue
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
