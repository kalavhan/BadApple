#!/usr/bin/env python3
"""Deterministic 2x IMGUI kit from the supplied haunted-hotel SVG/CSS mockup.

Run: python3 tools/art/export_haunted_ui.py [--chrome /usr/bin/google-chrome]
Requires Pillow and Chromium. SVG sources and the audit atlas go to ignored ArtSource/.
The supplied source paths are transcribed unchanged; CSS pieces use matching SVG
fills. Fonts must already exist under Assets/Resources/Fonts (see their sources file).
"""
from pathlib import Path
import argparse, base64, io, json, math, shutil, subprocess, tempfile, uuid
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Assets/Resources/Art/UI/Haunted'
SOURCE = ROOT / 'ArtSource/UI/Haunted'
FONT = ROOT / 'Assets/Resources/Fonts/SpecialElite-Regular.ttf'
DEFS = '''<defs>
<linearGradient id="brass" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#f0dc98"/><stop offset=".22" stop-color="#b8923f"/><stop offset=".5" stop-color="#4a3410"/><stop offset=".72" stop-color="#b8923f"/><stop offset=".88" stop-color="#dcc077"/><stop offset="1" stop-color="#8e6a26"/></linearGradient>
<linearGradient id="wood" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#2e1420"/><stop offset=".5" stop-color="#1c0a12"/><stop offset="1" stop-color="#12060b"/></linearGradient>
<linearGradient id="woodh"><stop stop-color="#1c0a12"/><stop offset=".3" stop-color="#2e1420"/><stop offset=".6" stop-color="#24101a"/><stop offset=".85" stop-color="#2e1420"/><stop offset="1" stop-color="#1c0a12"/></linearGradient>
<linearGradient id="paper" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#e6d9bb"/><stop offset="1" stop-color="#cfbc95"/></linearGradient>
<linearGradient id="tagp" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#eadfc4"/><stop offset="1" stop-color="#cbb991"/></linearGradient>
<linearGradient id="gb"><stop stop-color="#4a3410"/><stop offset=".35" stop-color="#e6d08c"/><stop offset=".6" stop-color="#a07e34"/><stop offset="1" stop-color="#3a280c"/></linearGradient>
<radialGradient id="rv" cx=".35" cy=".35" r=".7"><stop stop-color="#fff3c4"/><stop offset="1" stop-color="#6b4a16"/></radialGradient>
<radialGradient id="tarnish"><stop stop-color="#50826e" stop-opacity=".35"/><stop offset="1" stop-color="#50826e" stop-opacity="0"/></radialGradient>
<radialGradient id="cubby" cx=".5" cy=".35" r=".75"><stop stop-color="#24121f"/><stop offset="1" stop-color="#0b0409"/></radialGradient>
<radialGradient id="vignette"><stop offset=".45" stop-color="#000" stop-opacity="0"/><stop offset="1" stop-color="#000" stop-opacity=".85"/></radialGradient>
<radialGradient id="mintGlow"><stop stop-color="#7ef5d2" stop-opacity=".25"/><stop offset="1" stop-color="#7ef5d2" stop-opacity="0"/></radialGradient>
<radialGradient id="violetGlow"><stop stop-color="#a98bff" stop-opacity=".3"/><stop offset="1" stop-color="#a98bff" stop-opacity="0"/></radialGradient>
<radialGradient id="glow"><stop stop-color="#fff"/><stop offset=".35" stop-color="#fff" stop-opacity=".4"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>
<radialGradient id="amber" cx=".45" cy=".38" r=".7"><stop stop-color="#fff6d6"/><stop offset=".35" stop-color="#ffd27a"/><stop offset=".75" stop-color="#c27a1f"/><stop offset="1" stop-color="#6b3a08"/></radialGradient>
<radialGradient id="button" cx=".4" cy=".35" r=".7"><stop stop-color="#3a2c26"/><stop offset="1" stop-color="#120b08"/></radialGradient>
<radialGradient id="unlit" cx=".5" cy=".35" r=".8"><stop stop-color="#2a3a38"/><stop offset="1" stop-color="#121a1a"/></radialGradient>
<radialGradient id="knob" cx=".4" cy=".35" r=".7"><stop stop-color="#3a1626"/><stop offset="1" stop-color="#14060c"/></radialGradient>
<radialGradient id="pin" cx=".35" cy=".35" r=".7"><stop stop-color="#ff8a8a"/><stop offset="1" stop-color="#7a0d14"/></radialGradient>
<radialGradient id="nail" cx=".35" cy=".35" r=".7"><stop stop-color="#ddd"/><stop offset="1" stop-color="#333"/></radialGradient>
<radialGradient id="face" cx=".5" cy=".9" r=".9"><stop stop-color="#ece0bf"/><stop offset=".8" stop-color="#cdb98c"/><stop offset="1" stop-color="#a48d5e"/></radialGradient>
<radialGradient id="mint" cx=".5" cy=".35" r=".8"><stop stop-color="#e6fff7"/><stop offset=".3" stop-color="#7ef5d2"/><stop offset=".64" stop-color="#33a088"/><stop offset="1" stop-color="#1c3f4a"/></radialGradient>
<linearGradient id="shade" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#000" stop-opacity=".15"/><stop offset="1" stop-color="#000" stop-opacity=".55"/></linearGradient>
</defs>'''
ASSETS = {}
def add(name, size, shape, view=None):
    w,h=size; vw,vh=view or size
    ASSETS[name] = (w,h,f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {vw} {vh}">{DEFS}{shape}</svg>')
def screw(x,y,r=4.5):
    return f'<circle cx="{x}" cy="{y}" r="{r}" fill="url(#rv)"/><path d="M{x-r*.7} {y-r*.45}l{r*1.4} {r*.9}" stroke="#281905" stroke-width="1.3"/>'
def tarnish(w,h):
    return f'<ellipse cx="{w*.12}" cy="{h*.8}" rx="{w*.18}" ry="{h*.3}" fill="url(#tarnish)"/><ellipse cx="{w*.85}" cy="{h*.2}" rx="{w*.14}" ry="{h*.24}" fill="url(#tarnish)"/>'
def plate(w,h,rx=3):
    return f'<rect x=".5" y=".5" width="{w-1}" height="{h-1}" rx="{rx}" fill="url(#brass)" stroke="#241706"/>'+tarnish(w,h)+f'<rect x="1.5" y="1.5" width="{w-3}" height="{h-3}" rx="{rx}" fill="none" stroke="#fff5c8" stroke-opacity=".5"/>'

def halo(size, inset, sigma, radius=6):
    """White rounded rectangle with a Gaussian falloff outside its solid silhouette."""
    image = Image.new('RGBA', (size, size))
    pixels = []
    half = size / 2 - inset
    for y in range(size):
        for x in range(size):
            qx = abs(x + .5 - size / 2) - half + radius
            qy = abs(y + .5 - size / 2) - half + radius
            distance = math.hypot(max(qx, 0), max(qy, 0)) + min(max(qx, qy), 0) - radius
            alpha = 255 if distance <= 0 else round(255 * math.exp(-.5 * (distance / sigma) ** 2))
            pixels.append((255, 255, 255, alpha))
    image.putdata(pixels)
    return image

def build():
    for name,horizontal in [('wood_v',False),('wood_h',True)]:
        grain=''.join(f'<path d="M{x} 0v256" stroke="#000" stroke-opacity=".22" stroke-width="2"/><path d="M{x+14} 0v256" stroke="#ffbec8" stroke-opacity=".03" stroke-width="2"/>' for x in range(0,256,38))
        grain+=''.join(f'<path d="M{x} 0v256" stroke="#000" stroke-opacity=".2" stroke-width="3"/>' for x in range(0,256,86))
        add(name,(256,256),f'<rect width="256" height="256" fill="url(#{"woodh" if horizontal else "wood"})"/><g transform="{ "rotate(90 128 128)" if horizontal else ""}">{grain}</g>')
    add('damask',(144,192),'''<rect width="72" height="96" fill="#1a0c1c"/><g fill="#3a1830" fill-opacity=".7"><path d="M36 6c7 9 17 13 17 27 0 10-8 16-17 25-9-9-17-15-17-25 0-14 10-18 17-27z"/><path d="M0 54c7 9 15 13 15 25S7 92 0 96zM72 54c-7 9-15 13-15 25s8 13 15 17z"/></g><path fill="#1a0c1c" d="M36 22a8 8 0 0 0-8 8c0 3 1 5 3 6v4h10v-4c2-1 3-3 3-6a8 8 0 0 0-8-8z"/><g fill="#3a1830"><circle cx="32.6" cy="30" r="2.2"/><circle cx="39.4" cy="30" r="2.2"/><path d="M35 34h2l-1 2z"/></g>''',(72,96))
    add('gate_tile',(116,128),'''<rect width="58" height="64" fill="#07020a"/><g stroke="#08020a" stroke-width="7" stroke-linecap="round" opacity=".6"><path d="M9 2L49 62M49 2L9 62"/></g><path d="M9 0L49 64M49 0L9 64" stroke="url(#gb)" stroke-width="5"/><circle cx="29" cy="32" r="4.2" fill="url(#rv)" stroke="#2a1d08" stroke-width=".8"/><g fill="url(#rv)"><circle cx="9" cy="0" r="3.6"/><circle cx="49" cy="0" r="3.6"/><circle cx="9" cy="64" r="3.6"/><circle cx="49" cy="64" r="3.6"/></g><rect x="5" width="8" height="64" fill="url(#gb)"/><rect x="45" width="8" height="64" fill="url(#gb)"/><rect x="27" width="4" height="64" fill="url(#gb)" opacity=".7"/>''',(58,64))
    add('brass_9',(64,64),plate(64,64))
    add('brass_rim_9',(64,64),'<defs><mask id="rim"><rect width="64" height="64" fill="white"/><rect x="8" y="8" width="48" height="48" rx="2" fill="black"/></mask></defs><g mask="url(#rim)">'+plate(64,64)+'</g><rect x="8" y="8" width="48" height="48" rx="2" fill="none" stroke="#251806"/>')
    add('cubby_9',(96,128),'<rect width="96" height="128" rx="6" fill="url(#cubby)"/><rect x="2" y="2" width="92" height="124" rx="4" fill="url(#vignette)"/><path d="M5 124h86" stroke="#ffbec8" stroke-opacity=".1"/><path d="M5 5h86" stroke="#000" stroke-opacity=".7" stroke-width="8"/>')
    add('cubby_vignette',(128,128),'<rect width="128" height="128" fill="url(#vignette)"/>')
    add('tag',(244,108),'''<path d="M2 14L18 2H118Q122 2 122 6V48Q122 52 118 52H18L2 40Z" fill="url(#tagp)" stroke="#6b5240" stroke-width="1"/><circle cx="14" cy="27" r="5" fill="#120609" stroke="#b8923f" stroke-width="2"/><path d="M26 24H116" stroke="#6b5240" stroke-opacity=".35" stroke-width="1" stroke-dasharray="3 2"/>''',(122,54))
    add('plate_9',(48,28),plate(48,28,2))
    add('plaque_9',(96,64),plate(96,64)+screw(8,18)+screw(88,18))
    add('paper_9',(128,128),'<rect x="1" y="1" width="126" height="126" rx="2" fill="url(#paper)" stroke="#5a3c14" stroke-opacity=".3"/><rect x="1" y="1" width="126" height="126" rx="2" fill="url(#vignette)" opacity=".21"/>')
    add('coffee_ring',(64,64),'<path d="M56 30C58 48 44 58 29 57C12 56 5 44 7 29C9 12 23 6 36 8C52 10 57 22 56 30Z" fill="none" stroke="#6e3c14" stroke-opacity=".28" stroke-width="3"/><path d="M53 13C61 24 57 41 51 47" fill="none" stroke="#6e3c14" stroke-opacity=".12" stroke-width="2"/>')
    font=base64.b64encode(FONT.read_bytes()).decode()
    for name,title,sub in [('stamp_checked_in','CHECKED IN','never out'),('stamp_no_vacancy','NO VACANCY','')]:
        style=f'<style>@font-face{{font-family:Typed;src:url(data:font/ttf;base64,{font})}}</style>'
        add(name,(200,84),style+'<rect x="3" y="5" width="194" height="74" rx="6" fill="none" stroke="#e0505a" stroke-opacity=".9" stroke-width="3"/>'+f'<text x="100" y="{38 if sub else 50}" fill="#e0505a" font-family="Typed" text-anchor="middle" font-size="25">{title}</text>'+ (f'<text x="100" y="66" fill="#e0505a" font-family="Typed" text-anchor="middle" font-size="17" letter-spacing="2">{sub}</text>' if sub else '')+'<path d="M8 5h10M110 5h5M181 79h9M3 24v4" stroke="#e0505a" stroke-opacity=".3" stroke-width="2"/>')
    add('tack',(24,24),'<circle cx="12" cy="13" r="10" fill="#000" opacity=".5"/><circle cx="12" cy="11" r="10" fill="url(#pin)"/>')
    add('portrait_frame',(480,380),'<defs><mask id="oval"><rect width="480" height="380" fill="black"/><ellipse cx="240" cy="190" rx="240" ry="190" fill="white"/><ellipse cx="240" cy="190" rx="224" ry="174" fill="black"/></mask></defs><g mask="url(#oval)"><ellipse cx="240" cy="190" rx="239" ry="189" fill="url(#brass)"/>'+tarnish(480,380)+'</g><ellipse cx="240" cy="190" rx="223" ry="173" fill="none" stroke="#2a1a08" stroke-width="4"/><ellipse cx="240" cy="190" rx="238" ry="188" fill="none" stroke="#e6d08c" stroke-opacity=".65" stroke-width="2"/>')
    add('portrait_back',(480,380),'<defs><clipPath id="portrait"><ellipse cx="240" cy="190" rx="224" ry="174"/></clipPath></defs><g clip-path="url(#portrait)"><rect width="480" height="380" fill="#0a0408"/><ellipse cx="240" cy="350" rx="205" ry="210" fill="url(#violetGlow)"/><ellipse cx="240" cy="305" rx="195" ry="160" fill="url(#mintGlow)"/><rect width="480" height="380" fill="url(#vignette)"/></g>')
    add('wire',(240,80),'<path d="M10 38L60 4L110 38" stroke="#9a9a9a" stroke-width="1.3" fill="none"/><circle cx="60" cy="4" r="4" fill="url(#nail)"/>',(120,40))
    # Crest silhouette matches the supplied source. The hole is mapped from the
    # spec assembly (86,37,158,79 inside330x134), avoiding a3px gap below the face.
    add('dial_base',(880,356),'''<g fill="#14060c" stroke="url(#brass)" stroke-width="2"><path d="M110 128C80 96 40 92 4 104C20 112 24 122 22 132C34 124 44 126 52 136C58 126 70 124 80 130C86 122 98 122 110 128Z"/><path d="M330 128C360 96 400 92 436 104C420 112 416 122 418 132C406 124 396 126 388 136C382 126 370 124 360 130C354 122 342 122 330 128Z"/></g><defs><mask id="dialHole"><rect width="440" height="178" fill="white"/><path d="M86 116A79 79 0 0 1 244 116Z" transform="scale(1.3333333333333333 1.328358208955224)" fill="black"/></mask></defs><g mask="url(#dialHole)"><path d="M60 178L60 148Q60 136 72 136L100 136Q220 0 340 136L368 136Q380 136 380 148L380 178Z" fill="url(#brass)"/><path d="M68 178L68 150Q68 144 74 144L106 144Q220 14 334 144L366 144Q372 144 372 150L372 178Z" fill="#1c0a12"/></g>''',(440,178))
    add('dial_face',(420,210),'<path d="M1 104A104 104 0 0 1 209 104Z" fill="url(#face)" stroke="url(#brass)" stroke-width="2"/><path d="M135 17L147 43L141 51L157 75" stroke="#3c2814" stroke-opacity=".55" stroke-width="1" fill="none"/>',(210,105))
    add('needle_finger',(24,132),'<path d="M5 131L8 24Q12 8 16 24L19 131Z" fill="#e8dcc0" stroke="#22140a" stroke-width="2.8"/><path d="M5 78h14M7 46h10" stroke="#22140a" stroke-width="2.4"/><ellipse cx="12" cy="12" rx="6" ry="8.4" fill="#e8dcc0" stroke="#22140a" stroke-width="2.4"/>')
    add('skull_hub',(44,48),'<g transform="translate(22 21) scale(1.8)"><path d="M-10 0a10 10 0 0 1 20 0c0 4-2 6-4 7v4h-12v-4c-2-1-4-3-4-7z" fill="#e8dcc0" stroke="#22140a" stroke-width="1.4"/><circle cx="-4" cy="0" r="2.8" fill="#22140a"/><circle cx="4" cy="0" r="2.8" fill="#22140a"/><path d="M-1 5h2l-1 2z" fill="#22140a"/></g>')
    add('bat_wings_close',(312,120),'<g fill="#14060c" stroke="#b8923f" stroke-width="1.5"><path d="M52 30C40 12 20 8 2 14C10 18 12 24 10 30C18 26 24 28 28 34C32 28 40 28 52 30Z"/><path d="M104 30C116 12 136 8 154 14C146 18 144 24 146 30C138 26 132 28 128 34C124 28 116 28 104 30Z"/></g>',(156,60))
    add('knob',(112,112),'<circle cx="56" cy="56" r="55" fill="url(#brass)"/><circle cx="56" cy="56" r="48" fill="url(#knob)" stroke="#000" stroke-width="2"/><circle cx="56" cy="56" r="43" fill="none" stroke="#b8923f" stroke-opacity=".3" stroke-width="3"/>')
    for name,lit in [('call_button',False),('call_button_lit',True)]:
        add(name,(104,104),'<circle cx="52" cy="52" r="51" fill="url(#brass)"/><circle cx="52" cy="52" r="44" fill="url(#'+('amber' if lit else 'button')+')" stroke="#211304" stroke-width="2"/><path d="M17 43A36 36 0 0 1 86 35" fill="none" stroke="#fff" stroke-opacity="'+('.5' if lit else '.12')+'" stroke-width="2"/>')
    add('rail_9',(64,128),plate(64,128,6)+'<rect x="6" y="6" width="52" height="116" rx="4" fill="#211609" stroke="#000"/><rect x="10" y="10" width="44" height="108" rx="2" fill="none" stroke="#b8923f" stroke-opacity=".22"/>'+screw(32,10,4)+screw(32,118,4))
    add('crack',(96,60),'<path d="M2 4L14 10L10 16L24 20L20 26L34 28M14 10L26 6L40 10" stroke="#000" stroke-opacity=".7" stroke-width="1.4" fill="none"/>',(48,30))
    for name,col in [('jar_mint','#7ef5d2'),('jar_violet','#a98bff')]:
        add(name,(44,56),f'<rect x="5" y="1" width="12" height="4" rx="1" fill="#b8923f"/><path d="M4 6h14c2 3 2 6 2 10 0 7-3 11-9 11S2 23 2 16c0-4 0-7 2-10z" fill="{col}" fill-opacity=".25" stroke="#d9cfb8" stroke-width="1.2"/><path d="M4 15c4-2 10 2 14 0v2c0 6-3 9-7 9s-7-3-7-9z" fill="{col}"/>',(22,28))
    add('bell',(52,52),'<path d="M12 5a1.5 1.5 0 0 1 1.5 1.5v.3A7 7 0 0 1 19 14v1H5v-1a7 7 0 0 1 5.5-7.2v-.3A1.5 1.5 0 0 1 12 5zM3 16h18v2H3z" fill="#05231c"/>',(24,24))
    add('spider',(80,80),'<g transform="translate(40 30) scale(2)" fill="#1a0c14" stroke="#6b4a5a" stroke-width="1.4" stroke-linecap="round"><path d="M-4 -2L-12 -9L-15 -2M-4 1L-13 0L-16 7M-3 3L-10 8L-11 14M4 -2L12 -9L15 -2M4 1L13 0L16 7M3 3L10 8L11 14" fill="none"/><ellipse cy="2" rx="6" ry="7"/><circle cy="-6" r="4"/><circle cx="-1.6" cy="-6.5" r="1" fill="#ff4b4b" stroke="none"/><circle cx="1.6" cy="-6.5" r="1" fill="#ff4b4b" stroke="none"/></g>')
    add('cobweb',(240,240),'<g stroke="#e6e1f0" stroke-opacity=".45" stroke-width="1" fill="none"><path d="M0 0L120 6M0 0L110 60M0 0L80 100M0 0L40 118M0 0L4 120"/><path d="M24 1Q22 12 20 22Q12 22 4 24M50 2Q44 22 40 44Q22 46 6 50M80 4Q70 34 62 66Q36 72 10 78M108 6Q94 46 82 90Q50 98 14 104"/></g>',(120,120))
    add('drop_glow',(64,64),'<circle cx="32" cy="32" r="32" fill="url(#glow)"/>')
    add('nail',(16,16),'<circle cx="8" cy="8" r="7.5" fill="url(#nail)"/>')
    add('body_shade',(64,128),'<rect width="64" height="128" fill="url(#shade)"/>')
    add('summon_unlit',(128,64),'<rect x="1" y="1" width="126" height="62" rx="8" fill="url(#unlit)" stroke="#b8923f" stroke-opacity=".25"/>')
    add('summon_core',(128,64),'<rect x="1" y="1" width="126" height="62" rx="8" fill="url(#mint)" stroke="#e6fff7" stroke-opacity=".6"/><path d="M10 60h108" stroke="#392050" stroke-opacity=".6" stroke-width="3"/>')
    # Permanent lift housing: radius 84 arch and 200px sill, matched to the round 2 mockup.
    add('lift_housing',(400,192),'''<defs><linearGradient id="liftwood" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#2e1420"/><stop offset="1" stop-color="#1c0a12"/></linearGradient><linearGradient id="liftbrass" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#f0dc98"/><stop offset=".45" stop-color="#b8923f"/><stop offset=".8" stop-color="#8e6a26"/><stop offset="1" stop-color="#4a3410"/></linearGradient><clipPath id="liftarch"><path d="M16 84A84 84 0 0 1 184 84Z"/></clipPath><pattern id="liftgrain" width="19" height="128" patternUnits="userSpaceOnUse"><path d="M1 0v128" stroke="#000" stroke-opacity=".22"/><path d="M8 0v128" stroke="#ffbec8" stroke-opacity=".04"/></pattern></defs><path d="M16 84A84 84 0 0 1 184 84Z" fill="url(#liftwood)"/><rect width="200" height="84" fill="url(#liftgrain)" clip-path="url(#liftarch)"/><path d="M19 84A81 81 0 0 1 181 84" fill="none" stroke="url(#liftbrass)" stroke-width="6"/><path d="M19 84A81 81 0 0 1 181 84" fill="none" stroke="#50826e" stroke-opacity=".35" stroke-width="6" stroke-dasharray="2 9 1 14 3 22"/><rect x="0" y="84" width="200" height="8" rx="1.5" fill="url(#liftbrass)"/><circle cx="10" cy="88" r="2" fill="#4a3410"/><circle cx="190" cy="88" r="2" fill="#4a3410"/>''',(200,96))
    add('lift_wing',(200,84),'''<g transform="translate(-2 -94)"><path d="M110 128C80 96 40 92 4 104C20 112 24 122 22 132C34 124 44 126 52 136C58 126 70 124 80 130C86 122 98 122 110 128Z" fill="#14060c" stroke="url(#brass)" stroke-width="2"/></g>''',(110,44))

    # Append new exports so adding art never changes the raster origins of older SVGs.
    ASSETS['halo_tight_9'] = (96, 96, halo(96, 24, 8))
    ASSETS['halo_wide_9'] = (160, 160, halo(160, 48, 16))
    add('summon_bezel_lit_9',(76,76),'''<defs><linearGradient id="litbrass" x1="0" x2="0" y1="0" y2="1"><stop stop-color="#e6fff7"/><stop offset=".22" stop-color="#b8923f"/><stop offset=".5" stop-color="#4a3410"/><stop offset=".72" stop-color="#b8923f"/><stop offset=".88" stop-color="#e6fff7"/><stop offset="1" stop-color="#8e6a26"/></linearGradient><filter id="bleed" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="2"/></filter><mask id="litbezel"><rect width="76" height="76" fill="black"/><rect x="6" y="6" width="64" height="64" rx="6" fill="white"/><rect x="10" y="10" width="56" height="56" rx="3" fill="black"/></mask></defs><rect x="6" y="6" width="64" height="64" rx="6" fill="none" stroke="#7ef5d2" stroke-width="3" opacity=".8" filter="url(#bleed)"/><rect x="6" y="6" width="64" height="64" rx="6" fill="url(#litbrass)" mask="url(#litbezel)"/><rect x="10" y="10" width="56" height="56" rx="3" fill="none" stroke="#7ef5d2" stroke-width="1.5"/><path d="M12 7H64" stroke="#e6fff7" stroke-width="1" opacity=".7"/>''')

def metadata(path, folder=False):
    meta=Path(str(path)+'.meta')
    if meta.exists(): return
    guid=uuid.uuid5(uuid.NAMESPACE_URL,'badapple-haunted/'+str(path.relative_to(ROOT))).hex
    if folder: data=f'fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
    elif path.suffix=='.png':
        repeat=0 if path.stem in ['wood_v','wood_h','damask','gate_tile'] else 1
        data=f'''fileFormatVersion: 2
guid: {guid}
TextureImporter:
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    wrapU: {repeat}
    wrapV: {repeat}
    wrapW: {repeat}
  nPOTScale: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  textureType: 0
  textureShape: 1
  maxTextureSize: 1024
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 1024
    textureFormat: -1
    textureCompression: 0
    overridden: 0
  userData:
  assetBundleName:
  assetBundleVariant:
'''
    else: data=f'fileFormatVersion: 2\nguid: {guid}\n'
    meta.write_text(data)

def main():
    p=argparse.ArgumentParser();p.add_argument('--chrome',default=shutil.which('google-chrome') or shutil.which('chromium'));args=p.parse_args()
    if not args.chrome: raise SystemExit('Chromium is required')
    OUT.mkdir(parents=True,exist_ok=True); SOURCE.mkdir(parents=True,exist_ok=True);build()
    x=y=row=0; layout={};html=[]
    for name,(w,h,svg) in ASSETS.items():
        if x+w>2048: x=0;y+=row+8;row=0
        layout[name]=(x,y,w,h);row=max(row,h)
        if isinstance(svg, Image.Image):
            # Keep analytic alpha fields exact instead of rasterizing them a second time.
            encoded = io.BytesIO(); svg.save(encoded, format='PNG', optimize=True)
            (SOURCE/(name+'.png')).write_bytes(encoded.getvalue())
            url='data:image/png;base64,'+base64.b64encode(encoded.getvalue()).decode()
        else:
            (SOURCE/(name+'.svg')).write_text(svg)
            # Data URLs isolate SVG ids (including masks) from other assets.
            url='data:image/svg+xml;base64,'+base64.b64encode(svg.encode()).decode()
        html.append(f'<img style="position:absolute;left:{x}px;top:{y}px;width:{w}px;height:{h}px" src="{url}">')
        x+=w+8
    height=y+row
    page=SOURCE/'atlas.html';page.write_text('<html><body style="margin:0;background:transparent">'+''.join(html)+'</body></html>')
    atlas=SOURCE/'atlas.png'
    with tempfile.TemporaryDirectory(prefix='haunted-chrome-') as profile:
        command=[args.chrome,'--headless','--no-sandbox','--disable-gpu','--hide-scrollbars','--allow-file-access-from-files','--default-background-color=00000000',f'--user-data-dir={profile}',f'--window-size=2048,{height+100}','--force-device-scale-factor=1','--virtual-time-budget=3000',f'--screenshot={atlas}',page.as_uri()]
        subprocess.run(command,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.PIPE)
    im=Image.open(atlas).convert('RGBA')
    for name,(x,y,w,h) in layout.items():
        output=OUT/(name+'.png')
        source = ASSETS[name][2]
        rendered = source if isinstance(source, Image.Image) else im.crop((x,y,x+w,y+h))
        rendered.save(output,optimize=True);metadata(output)
    metadata(OUT,True); metadata(FONT.parent,True)
    for f in FONT.parent.iterdir():
        if f.suffix!='.meta':metadata(f)
    (SOURCE/'layout.json').write_text(json.dumps(layout,indent=2))
    print(f'Exported {len(layout)} deterministic RGBA textures to {OUT}; audit atlas: {atlas}')

if __name__=='__main__':main()
