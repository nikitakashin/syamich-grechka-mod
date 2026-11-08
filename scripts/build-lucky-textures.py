#!/usr/bin/env python3
"""Собрать все игровые кубы Lucky Blocks из квадратных граней (нужен Pillow).

python3 scripts/build-lucky-textures.py
Перед заменой PNG сохраняются в игнорируемой internal docs/art/backups/.
Исходные грани не изменяются. Игра не запускается и не управляется.
"""

import hashlib
import importlib.util
import json
from datetime import datetime
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "assets/lucky-block-faces-2026-11-03"
FACES = ART / "faces"
TEXTURES = [ROOT / "assets/GrechkaLuckyBlocks/Textures/Items",
            ROOT / "mods/GrechkaLuckyBlocks/Textures/Items"]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def save_png(image, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_suffix(".png.tmp")
    image.save(temporary, format="PNG")
    temporary.replace(target)


def main():
    spec = importlib.util.spec_from_file_location("lucky_cube", ROOT / "scripts/render-lucky-cube.py")
    renderer = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(renderer)
    definitions = ET.parse(ROOT / "mods/GrechkaLuckyBlocks/Defs/LuckyBlocks.xml").getroot()
    entries = []
    for definition in definitions:
        texture_path = Path(definition.findtext("graphicData/texPath"))
        if texture_path.parent != Path("Items"):
            raise ValueError(f"Неожиданный путь: {texture_path}")
        name = texture_path.name
        source = FACES / (name + ".png")
        with Image.open(source) as image:
            if image.width != image.height or image.width < 256:
                raise ValueError(f"Ожидается квадратная грань не меньше 256px: {source}")
            if image.convert("RGBA").getchannel("A").getextrema() != (255, 255):
                raise ValueError(f"Грань должна быть полностью непрозрачной: {source}")
        entries.append({"defName": definition.findtext("defName"),
                        "label": definition.findtext("label"), "name": name,
                        "face": str(source.relative_to(ROOT)), "face_sha256": digest(source)})
    if len(entries) != 55 or len({e["face_sha256"] for e in entries}) != 55:
        raise ValueError("Нужны все 55 разных граней из действующих определений")

    # Нетирные файлы — старые образцы; сохранить их имена, обновив до своего T1.
    by_name = {e["name"]: e for e in entries}
    aliases = []
    for folder in TEXTURES:
        for path in sorted(folder.glob("LuckyBlock*.png")):
            match = re.fullmatch(r"LuckyBlock([A-Za-z]*)", path.stem)
            if match and path.stem + "T1" in by_name:
                aliases.append((path, path.stem + "T1"))

    backup = ROOT / "internal docs/art/backups" / ("lucky-build-" + datetime.now().strftime("%Y%m%d-%H%M%S-%f"))
    destinations = [folder / (e["name"] + ".png") for folder in TEXTURES for e in entries]
    destinations += [path for path, _ in aliases]
    backed_up = {}
    for path in destinations:
        if path.exists():
            relative = path.relative_to(ROOT)
            copy = backup / relative
            copy.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, copy)
            backed_up[str(relative)] = digest(copy)
    backup.mkdir(parents=True, exist_ok=True)
    (backup / "manifest.json").write_text(json.dumps(backed_up, indent=2) + "\n")

    # Порядок рядов в обзоре: семейство, колонки T1–T5.
    families = ["", "Resources", "Industry", "Energy", "Medical", "Combat", "Farm", "Zoo", "Mechs", "Chaos", "Storage"]
    labels = ["Общий", "Сырьё", "Промышленность", "Энергетика", "Медицина", "Арсенал", "Фермерский", "Зоопарк", "Мехи повсюду", "Гречневое безумие", "Цифровой"]
    font_path = Path("/System/Library/Fonts/Supplemental/Arial.ttf")
    font = ImageFont.truetype(str(font_path), 18) if font_path.exists() else ImageFont.load_default()
    sheet = Image.new("RGB", (1100, len(families) * 230 + 40), (35, 38, 43))
    draw = ImageDraw.Draw(sheet)
    for tier in range(1, 6):
        draw.text(((tier - 1) * 220 + 95, 10), f"T{tier}", fill="white", font=font)
    for entry in entries:
        with Image.open(ROOT / entry["face"]) as face:
            # Два независимых рендера исходника — игровой PNG не масштабируется повторно.
            game = renderer.render_cube(face, size=256)
            preview = renderer.render_cube(face, size=512)
            thumbnail = renderer.render_cube(face, size=192)
        game_path = ART / "cubes-256" / (entry["name"] + ".png")
        preview_path = ART / "cubes-512" / (entry["name"] + ".png")
        save_png(game, game_path)
        save_png(preview, preview_path)
        for folder in TEXTURES:
            folder.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(game_path, folder / game_path.name)
        entry.update({"cube": str(game_path.relative_to(ROOT)), "cube_sha256": digest(game_path),
                      "preview": str(preview_path.relative_to(ROOT))})
        match = re.fullmatch(r"LuckyBlock([A-Za-z]*?)T([1-5])", entry["name"])
        family, tier = match.group(1), int(match.group(2))
        row, column = families.index(family), tier - 1
        x, y = column * 220 + 14, row * 230 + 40
        sheet.paste(thumbnail, (x, y), thumbnail)
        draw.text((column * 220 + 10, y + 195), labels[row], fill=(235, 235, 235), font=font)
    for target, name in aliases:
        shutil.copyfile(ART / "cubes-256" / (name + ".png"), target)
    sheet.save(ART / "ALL-LUCKY-CUBES.png")
    manifest = {"generator": "built-in image_gen; prompts in internal docs/art/LUCKY-REDESIGN-*.json",
                "renderer": "scripts/render-lucky-cube.py", "size": 256, "preview_size": 512,
                "yaw": 25, "pitch": 20, "side_shade": 0.68, "top_shade": 1.08,
                "backup": str(backup.relative_to(ROOT)), "entries": entries,
                "legacy_aliases": {str(path.relative_to(ROOT)): name for path, name in aliases}}
    (ART / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    print(f"Готово: {len(entries)} кубов в двух размерах; {len(aliases)} старых образцов обновлено")
    print(f"Резервная копия: {backup}")
    print(f"Обзор: {ART / 'ALL-LUCKY-CUBES.png'}")


if __name__ == "__main__":
    main()
