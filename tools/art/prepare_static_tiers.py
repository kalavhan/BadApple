#!/usr/bin/env python3
"""Slice AutoSprite static design sheets; no animation generation or paid API calls.
Input: ArtSource/PlanOct04/*.png; output: Resources/Art/Tiers and Resources/Art/Hotel.
Preserves ivory details by removing only white connected to each cell's edge.
"""
from pathlib import Path
from collections import deque
import json
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'ArtSource/PlanOct04'
OUT = ROOT / 'Assets/Resources/Art'

def cutout(image):
    image = image.convert('RGBA')
    w,h = image.size
    pixels = image.load()
    seen=set(); q=deque()
    def visit(x,y):
        if not (0 <= x < w and 0 <= y < h) or (x,y) in seen:return
        r,g,b,a=pixels[x,y]
        if a < 10 or min(r,g,b)>228 and max(r,g,b)-min(r,g,b)<24:
            seen.add((x,y));q.append((x,y))
    for x in range(w):visit(x,0);visit(x,h-1)
    for y in range(h):visit(0,y);visit(w-1,y)
    while q:
        x,y=q.popleft();pixels[x,y]=(0,0,0,0)
        for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):visit(x+dx,y+dy)
    bounds=image.getbbox()
    if bounds: image=image.crop(bounds)
    image.thumbnail((230,230),Image.Resampling.LANCZOS)
    canvas=Image.new('RGBA',(256,256))
    canvas.alpha_composite(image,((256-image.width)//2,(256-image.height)//2))
    return canvas

def cells(image):
    w,h=image.size
    return [image.crop((x*w//2,y*h//2,(x+1)*w//2,(y+1)*h//2)) for y in range(2) for x in range(2)]

def main():
    (OUT/'Tiers').mkdir(parents=True,exist_ok=True)
    (OUT/'Hotel').mkdir(parents=True,exist_ok=True)
    config=json.loads((ROOT/'Assets/StreamingAssets/Config/towers.json').read_text())
    previews=[]
    for tower in config['towers']:
        tiers=tower.get('tiers',[])
        if not tiers:continue
        src=SOURCE/(tower['id']+'.png')
        if not src.exists():continue
        for cell,tier in zip(cells(Image.open(src)),tiers):
            art=cutout(cell);art.save(OUT/(tier['sprite']+'.png'))
            previews.append((tier['name'],art))
    if (SOURCE/'hall_props.png').exists():
        for i,cell in enumerate(cells(Image.open(SOURCE/'hall_props.png'))):cutout(cell).save(OUT/f'Hotel/prop_{i}.png')
    if (SOURCE/'hall_walls.png').exists():
        for key,cell in zip(('wall_top','wall_face','wall_corner','baseboard'),cells(Image.open(SOURCE/'hall_walls.png'))):
            cell.convert('RGB').resize((128,128),Image.Resampling.LANCZOS).save(OUT/f'Hotel/{key}.png')
    if (SOURCE/'hall_floor.png').exists():
        Image.open(SOURCE/'hall_floor.png').convert('RGB').resize((256,256),Image.Resampling.LANCZOS).save(OUT/'Hotel/floor_corridor.png')
    if (SOURCE/'hall_doorway.png').exists():cutout(Image.open(SOURCE/'hall_doorway.png')).save(OUT/'Hotel/doorway.png')
    sheet=Image.new('RGB',(1024,280*((len(previews)+3)//4)),'#393041')
    draw=ImageDraw.Draw(sheet)
    for i,(name,art) in enumerate(previews):
        x=i%4*256;y=i//4*280
        sheet.paste(art,(x,y),art);draw.text((x+8,y+255),name,fill='#eadfc7')
    sheet.save(SOURCE/'static-tier-review.jpg')
    print(f'Prepared {len(previews)} static tier images; review: {SOURCE}/static-tier-review.jpg')

if __name__=='__main__':main()
