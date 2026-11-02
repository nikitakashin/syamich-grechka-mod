#!/usr/bin/env python3
"""Sync user-authored referenced PNGs from assets; never modify source artwork."""
import argparse
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--apply", action="store_true")
args = parser.parse_args()
changed = []
for module in sorted((repo / "mods").iterdir()):
    source = repo / "assets" / module.name / "Textures"
    destination = module / "Textures"
    if not source.is_dir():
        continue
    candidates = set()
    for old in destination.rglob("*.png"):
        original = source / old.relative_to(destination)
        if original.is_file():
            candidates.add(original)
    for xml in (module / "Defs").rglob("*.xml"):
        for node in ET.parse(xml).iter():
            if node.tag not in {"texPath", "uiIconPath", "wornGraphicPath"} or not node.text:
                continue
            relative = Path(node.text.strip())
            if relative.is_absolute() or ".." in relative.parts:
                raise SystemExit("Unsafe texture path in " + str(xml))
            for match in (source / relative.parent).glob(relative.name + "*.png"):
                candidates.add(match)
    for original in sorted(candidates):
        target = destination / original.relative_to(source)
        if target.is_file() and original.read_bytes() == target.read_bytes():
            continue
        changed.append(str(target.relative_to(repo)))
        if args.apply:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
print(("Updated" if args.apply else "Pending") + " PNG files: " + str(len(changed)))
for name in changed:
    print(name)
