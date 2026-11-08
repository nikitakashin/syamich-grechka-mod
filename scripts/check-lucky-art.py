#!/usr/bin/env python3
"""Проверить полноту новой графики Lucky Blocks, синхронизацию и отсутствие щелей."""

import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "assets/lucky-block-faces-2026-11-03"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    definitions = ET.parse(ROOT / "mods/GrechkaLuckyBlocks/Defs/LuckyBlocks.xml").getroot()
    expected = {Path(d.findtext("graphicData/texPath")).name for d in definitions}
    manifest = json.loads((ART / "manifest.json").read_text())
    assert len(expected) == 55 and {e["name"] for e in manifest["entries"]} == expected
    assert len({e["face_sha256"] for e in manifest["entries"]}) == 55
    for entry in manifest["entries"]:
        name = entry["name"]
        assert sha(ROOT / entry["face"]) == entry["face_sha256"], name
        cube = ROOT / entry["cube"]
        assert sha(cube) == entry["cube_sha256"], name
        for folder in ("assets", "mods"):
            assert sha(ROOT / folder / "GrechkaLuckyBlocks/Textures/Items" / (name + ".png")) == sha(cube), name
        for path, size in ((cube, 256), (ROOT / entry["preview"], 512)):
            with Image.open(path) as image:
                assert image.mode == "RGBA" and image.size == (size, size), path
                alpha = image.getchannel("A")
                assert alpha.getextrema() == (0, 255), path
                pixels = alpha.tobytes()
                assert set(pixels) <= {0, 255}, path
                assert all(alpha.getpixel(p) == 0 for p in ((0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1))), path
                assert alpha.getpixel((size // 2, size // 2)) == 255, path
                # Выпуклый непрозрачный куб: между краями каждой строки нет дырок.
                for y in range(size):
                    row = pixels[y * size:(y + 1) * size]
                    if 255 in row:
                        first, last = row.index(255), size - row[::-1].index(255)
                        assert all(v == 255 for v in row[first:last]), (path, y)
    for relative, target in manifest["legacy_aliases"].items():
        assert sha(ROOT / relative) == sha(ART / "cubes-256" / (target + ".png")), relative
    print("PASS: 55 unique faces, 110 RGBA cubes, all bindings synchronized, no transparent seams")


if __name__ == "__main__":
    main()
