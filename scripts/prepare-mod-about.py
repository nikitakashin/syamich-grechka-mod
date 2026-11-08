#!/usr/bin/env python3
"""Render Pharmacy/Treasures About images with the existing Grechka card layout."""
import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
FONTS = Path('/System/Library/Fonts/Supplemental')
MODULES = {
    'GrechkaPharmacy': {
        'title': 'Pharmacy', 'subtitle': 'Лекарства и сыворотки T1–T5',
        'code': 'PHR  /  T1–T5', 'accent': (130, 203, 171),
        'emblem': 'Textures/Items/GrechkaSerumT5.png',
    },
    'GrechkaTreasures': {
        'title': 'Treasures', 'subtitle': 'Самоцветы, сокровищницы и роскошь',
        'code': 'TRS  /  GEMS', 'accent': (226, 181, 99),
        'emblem': 'Textures/Buildings/TreasureChest.png',
    },
}


def font(size, bold=False):
    return ImageFont.truetype(str(FONTS / ('Arial Bold.ttf' if bold else 'Arial.ttf')), size)


def fit(image, width, height):
    image = image.convert('RGBA')
    box = image.getchannel('A').getbbox()
    if box is None:
        raise ValueError('Empty emblem')
    image = image.crop(box)
    image.thumbnail((width, height), Image.Resampling.LANCZOS)
    out = Image.new('RGBA', (width, height))
    out.alpha_composite(image, ((width-image.width)//2, (height-image.height)//2))
    return out


def background(width, height):
    # Same charcoal gradient as the existing Factions About renderer.
    out = Image.new('RGBA', (width, height))
    draw = ImageDraw.Draw(out)
    for x in range(width):
        t = x / (width-1)
        color = (int(19+17*t), int(24+16*t), int(25+10*t), 255)
        draw.line((x, 0, x, height), fill=color)
    return out


def render(module):
    spec = MODULES[module]
    source = ROOT / 'mods' / module / spec['emblem']
    emblem = Image.open(source).convert('RGBA')
    accent = spec['accent']
    muted, white = (194, 201, 200), (245, 245, 239)
    about = ROOT / 'mods' / module / 'About'
    icon = background(160, 160)
    mask = Image.new('L', icon.size)
    ImageDraw.Draw(mask).rounded_rectangle((5, 5, 154, 154), radius=17, fill=255)
    icon.putalpha(mask)
    icon.alpha_composite(fit(emblem, 132, 132), (14, 10))
    ImageDraw.Draw(icon).line((12, 146, 148, 146), fill=accent, width=4)
    icon.save(about / 'ModIcon.png')

    card = background(800, 450)
    grid = Image.new('RGBA', card.size)
    draw = ImageDraw.Draw(grid)
    for x in range(0, 800, 40):
        draw.line((x, 0, x, 450), fill=(44, 49, 45, 95))
    for y in range(0, 450, 40):
        draw.line((0, y, 800, y), fill=(44, 49, 45, 95))
    card.alpha_composite(grid)
    draw = ImageDraw.Draw(card)
    for y in (78, 381):
        draw.line((36, y, 764, y), fill=(69, 74, 70))
    grain = Image.open(ROOT / 'mods/GrechkaIndustry/Textures/UI/GrechkaBrand/Grain.png')
    card.alpha_composite(fit(grain, 32, 24), (36, 31))
    draw.text((83, 32), 'GRECHKA WORLD', font=font(18, True), fill=(220, 182, 126))
    draw.text((635, 34), 'RIMWORLD 1.7', font=font(16), fill=muted)
    draw.text((36, 153), 'GRECHKA', font=font(31, True), fill=accent)
    draw.text((36, 201), spec['title'], font=font(62, True), fill=white)
    draw.text((37, 294), spec['subtitle'], font=font(18), fill=muted)
    for radius, strength in ((123, .58), (113, .36)):
        color = tuple(round(c*strength) for c in accent)
        draw.ellipse((612-radius, 230-radius, 612+radius, 230+radius), outline=color)
    for x, y in ((612, 107), (612, 353), (489, 230), (735, 230)):
        draw.line((x-5, y, x+5, y), fill=accent)
        draw.line((x, y-5, x, y+5), fill=accent)
    card.alpha_composite(fit(emblem, 222, 222), (501, 119))
    draw.line((36, 407, 60, 407), fill=accent, width=3)
    draw.text((72, 398), spec['code'], font=font(14, True), fill=(177, 188, 186))
    card.convert('RGB').save(about / 'Preview.png')

    entries = []
    for name in ('ModIcon.png', 'Preview.png'):
        path = about / name
        with Image.open(path) as im:
            entries.append({'path': str(path.relative_to(ROOT)), 'size': im.size,
                            'mode': im.mode, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    output = ROOT / 'assets' / module / 'about-manifest.json'
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps({
        'pipeline': 'scripts/prepare-mod-about.py',
        'layout': 'Existing Grechka Factions card and rounded icon tile',
        'emblem': str(source.relative_to(ROOT)),
        'supersedes': 'Earlier About-only output entries; source artwork remains unchanged',
        'files': entries,
    }, ensure_ascii=False, indent=2)+'\n')
    print(f'{module}: Preview 800×450 RGB; ModIcon 160×160 RGBA')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('modules', nargs='*', choices=list(MODULES))
    args = parser.parse_args()
    for name in args.modules or MODULES:
        render(name)
