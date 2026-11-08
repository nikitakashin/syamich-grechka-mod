#!/usr/bin/env python3
"""Sync user-authored referenced PNGs from assets; never modify source artwork."""
import argparse
from pathlib import Path
import shutil
from workbench_art import directional_sources
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
        # Full tier sprites are immutable to this general copier: use the SHA-reviewed registry.
        if "TierSprites" in old.relative_to(destination).parts or "TierMasks" in old.relative_to(destination).parts:
            continue
        original = source / old.relative_to(destination)
        if original.is_file():
            candidates.add(original)
    for xml in (module / "Defs").rglob("*.xml"):
        for node in ET.parse(xml).iter():
            if node.tag not in {"texPath", "uiIconPath", "wornGraphicPath", "offPath", "idlePath", "activePath", "siteTexture", "expandingIconTexture"} or not node.text:
                continue
            relative = Path(node.text.strip())
            if relative.parts and relative.parts[0] in {"TierSprites", "TierMasks"}:
                continue
            if relative.is_absolute() or ".." in relative.parts:
                raise SystemExit("Unsafe texture path in " + str(xml))
            for match in (source / relative.parent).glob(relative.name + "*.png"):
                candidates.add(match)
    overrides = directional_sources(module.name, source)
    # A partial art checkout can still update unrelated assets and available directions.
    outputs = {p.relative_to(source) for p in candidates} - overrides.keys()
    outputs.update(slot for slot, authored in overrides.items() if (source / authored).is_file())
    for relative in sorted(outputs):
        original = source / overrides.get(relative, relative)
        if not original.is_file():
            raise SystemExit(f"Missing prepared direction for {module.name}/{relative}: {original}")
        target = destination / relative
        if target.is_file() and original.read_bytes() == target.read_bytes():
            continue
        changed.append(str(target.relative_to(repo)))
        if args.apply:
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
print(("Updated" if args.apply else "Pending") + " referenced PNG updates: " + str(len(changed)))
print("This checks synchronization of existing references, not artwork completeness or visual correctness.")
for name in changed:
    print(name)
