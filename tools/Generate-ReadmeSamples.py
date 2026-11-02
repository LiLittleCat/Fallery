"""Create original, deterministic demo art. Never reads personal media or metadata.

Requires Pillow. Output stays under work/readme-demo; only reviewed UI captures
belong in docs/screenshots. Run from any working directory.
"""
from pathlib import Path
import math
import random
import hashlib
import json

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1] / "work" / "readme-demo" / "Sample Library"


def gradient(size, top, bottom):
    w, h = size
    image = Image.new("RGB", size)
    draw = ImageDraw.Draw(image)
    for y in range(h):
        t = y / max(1, h - 1)
        color = tuple(round(a * (1 - t) + b * t) for a, b in zip(top, bottom))
        draw.line((0, y, w, y), fill=color)
    return image


def landscape(size, seed, colors, scene):
    rng = random.Random(seed)
    w, h = size
    sky, horizon, far, middle, near, sun = colors
    image = gradient(size, sky, horizon)
    draw = ImageDraw.Draw(image)
    cx, cy, radius = w * (.65 if seed % 2 else .3), h * .27, min(w, h) * .105
    draw.ellipse((cx-radius, cy-radius, cx+radius, cy+radius), fill=sun)
    if scene == "ocean":
        draw.rectangle((0, h*.55, w, h), fill=far)
        for layer, color in enumerate((middle, near)):
            points = [(x, h*(.69+layer*.14) + math.sin(x/w*7+seed+layer)*h*.035) for x in range(0, w+8, 8)]
            draw.polygon(points+[(w,h),(0,h)], fill=color)
        for i in range(30):
            y = h*(.57+i*.009)
            half = w*(.016+i*.003)
            draw.line((cx-half, y, cx+half, y), fill=horizon, width=2)
    else:
        for layer, color in enumerate((far, middle, near)):
            points = [(0, h)]
            for i in range(9):
                x = i*w/8
                y = h*(.52+layer*.16) + rng.uniform(-.18,.09)*h/(1+layer*.3)
                points.append((x,y))
            points.extend([(w,h),(0,h)])
            if scene == "dunes":
                points = [(0,h)] + [(x, h*(.52+layer*.16)+math.sin(x/w*4+seed+layer)*h*.09) for x in range(0,w+8,8)] + [(w,h)]
            draw.polygon(points, fill=color)
    return image


def geometry(size, seed, palette):
    rng = random.Random(seed)
    w, h = size
    image = Image.new("RGB", size, palette[0])
    draw = ImageDraw.Draw(image)
    for i in range(7):
        x, y = rng.uniform(-.2,.85)*w, rng.uniform(-.15,.85)*h
        r = rng.uniform(.18,.48)*min(w,h)
        color = palette[1+i%(len(palette)-1)]
        if i % 3:
            draw.ellipse((x,y,x+2*r,y+2*r),fill=color)
        else:
            draw.rectangle((x,y,x+r,y+2*r),fill=color)
    return image


def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    palettes = [
        ((22,44,66),(122,171,180),(64,113,133),(37,77,101),(15,42,66),(244,224,174)),
        ((243,190,158),(255,227,181),(204,140,111),(161,91,86),(99,58,75),(255,244,211)),
        ((53,55,94),(170,143,174),(103,98,151),(65,72,119),(30,43,80),(248,215,182)),
        ((224,230,200),(243,239,208),(151,177,153),(82,136,123),(37,88,87),(255,252,224)),
        ((232,160,111),(252,219,153),(197,114,77),(144,72,66),(80,48,58),(255,239,185)),
    ]
    names = ["Alpine morning", "Apricot dunes", "Indigo coast", "Quiet valley", "Golden hour",
             "Blue horizon", "Rose canyon", "Moonlit ridge", "Sage hills", "Amber shore",
             "Coastal rhythm", "Sunset study", "Northern light", "Soft peaks", "Desert evening"]
    sizes = [(1000,740),(760,1080),(1100,720),(820,1000),(1000,820)]
    generated = []
    for i, name in enumerate(names):
        scene = "ocean" if i in (2,5,9,10) else "dunes" if i in (1,4,6,14) else "mountains"
        image = landscape(sizes[i%len(sizes)], i+12, palettes[i%len(palettes)], scene)
        file = ROOT/f"{i+1:02d} - {name}.png"
        image.save(file, optimize=True)
        generated.append(file)
    for j, folder in enumerate(("Coastlines", "Color studies", "Mountain sketches")):
        directory = ROOT/folder
        directory.mkdir(exist_ok=True)
        for i in range(3):
            image = geometry((900,900), j*10+i, palettes[(j+i)%5]) if j == 1 else landscape((960,720),j*10+i,palettes[(j+i)%5],"ocean" if j == 0 else "mountains")
            file = directory/f"Study {i+1:02d}.png"
            image.save(file, optimize=True)
            generated.append(file)
    # Stable file dates keep the list-view screenshot reproducible.
    import os
    for file in generated:
        os.utime(file, (1767225600, 1767225600))
    manifest = {file.relative_to(ROOT).as_posix(): hashlib.sha256(file.read_bytes()).hexdigest() for file in generated}
    (ROOT.parent/"manifest.json").write_text(json.dumps(manifest, indent=2)+"\n", encoding="utf-8")
    print(f"Created 24 original PNGs under {ROOT}")


if __name__ == "__main__":
    main()
