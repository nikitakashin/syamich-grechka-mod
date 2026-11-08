#!/usr/bin/env python3
"""Render the actual shipping ASCII layouts for offline review; no game control."""
from pathlib import Path
import math
import xml.etree.ElementTree as ET
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
files = sorted((root / 'mods/GrechkaTreasures/Config/Sites').glob('*.xml'))
assert files, 'No prepared maps'
columns, width, height = 5, 340, 300
sheet = Image.new('RGB', (columns * width, math.ceil(len(files) / columns) * height + 60), '#141b22')
draw = ImageDraw.Draw(sheet)
font_path = '/System/Library/Fonts/Supplemental/Arial Unicode.ttf'
font = ImageFont.truetype(font_path, 15)
small = ImageFont.truetype(font_path, 12)
palette = {'#': '#829098', '+': '#bc874e', '.': '#303c45', ',': '#495342',
           '~': '#285e80', '=': '#a18c68', 'C': '#ffd45b', 'H': '#e6b7ff',
           'D': '#c063c0', 'T': '#f45660', 'G': '#ff9a4d', 'W': '#f2f0d7',
           'S': '#47cbb5', 'M': '#8bf4ec', 'O': '#61a5cc', 'B': '#796951',
           'V': '#47cbb5', 'L': '#47cbb5', 'R': '#47cbb5'}
draw.text((16, 12), 'GRECHKA TREASURES — 26 prepared layouts / walls · doors · gold chests · purple decoys · red traps · orange guards', font=font, fill='white')
for index, file in enumerate(files):
    data = ET.parse(file).getroot()
    rows = [r.text or '' for r in data.findall('rows/row')]
    x0, y0 = (index % columns) * width, (index // columns) * height + 60
    draw.rounded_rectangle((x0 + 6, y0 + 4, x0 + width - 6, y0 + height - 6), 8, fill='#202b33')
    title = data.findtext('label', file.stem)
    draw.text((x0 + 16, y0 + 12), title[:36], font=font, fill='white')
    draw.text((x0 + 16, y0 + 34), f"T{data.findtext('tier')} / {data.findtext('gem')} / {file.stem}", font=small, fill='#abbac4')
    scale = min((width - 34) // len(rows[0]), (height - 75) // len(rows))
    left, top = x0 + (width - scale * len(rows[0])) // 2, y0 + 60
    for z, row in enumerate(rows):
        for x, symbol in enumerate(row):
            if symbol == ' ':
                continue
            xx, yy = left + x * scale, top + z * scale
            draw.rectangle((xx, yy, xx + scale - 1, yy + scale - 1), fill=palette.get(symbol, '#ff00ff'))
            if symbol not in '#+.,~= ' and scale >= 7:
                draw.rectangle((xx + 2, yy + 2, xx + scale - 3, yy + scale - 3), outline='#101820')
output = root / 'internal docs/art/grechka-treasures-2026-11-04/prepared-maps.png'
output.parent.mkdir(parents=True, exist_ok=True)
sheet.save(output)
print(output)
