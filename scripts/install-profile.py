#!/usr/bin/env python3
"""Validate and install the repository profile; never starts or controls RimWorld."""
import argparse
from datetime import datetime
import json
from pathlib import Path
import shutil
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--game-dir", type=Path, required=True)
parser.add_argument("--config-dir", type=Path, required=True)
parser.add_argument("--apply", action="store_true", help="Write links/config after validation; default is read-only")
args = parser.parse_args()
game = args.game_dir.resolve()
config = args.config_dir.resolve()
mods_dir = game / "Mods"
profile = repo / "profiles" / "GrechkaWorld"
source_config = profile / "ModsConfig.xml"
active = [node.text.strip().lower() for node in ET.parse(source_config).findall("./activeMods/li")]
manifest = json.loads((profile / "mods.json").read_text())

def require(condition, message):
    if not condition:
        sys.exit(message)

def package_at(folder):
    about = folder / "About" / "About.xml"
    if not about.is_file():
        return None
    try:
        return ET.parse(about).findtext("packageId", "").strip().lower()
    except ET.ParseError:
        return None

require(mods_dir.is_dir() and config.is_dir(), "Game Mods and config directories must already exist")
require(len(active) == len(set(active)), "Profile contains duplicate package IDs")
installed = set()
for folder in (mods_dir, game.parent.parent / "workshop" / "content" / "294100"):
    if folder.is_dir():
        installed.update(package_at(child) for child in folder.iterdir() if child.is_dir())

planned = []
for entry in manifest:
    if entry.get("source") != "local":
        continue
    module = (repo / entry["repositoryPath"]).resolve()
    require(module.is_relative_to(repo / "mods"), "Local module path escapes repository mods")
    package = entry["package"].lower()
    require(package_at(module) == package, "Local module metadata mismatch: " + str(module))
    require((module / "Assemblies" / (module.name + ".dll")).is_file(), "Build local module first: " + module.name)
    link = mods_dir / module.name
    require(not link.exists() and not link.is_symlink()
            or link.is_symlink() and link.resolve() == module,
            "Existing installation has another target; resolve it before applying: " + str(link))
    planned.append((link, module))
    installed.add(package)

missing = [package for package in active if not package.startswith("ludeon.") and package not in installed]
require(not missing, "Required packages not installed: " + ", ".join(missing))
print("Validated active entries:", len(active), "(including Core/DLC)")
for link, module in planned:
    print("Local module:", link.name, "->", module)
print("Config:", config / "ModsConfig.xml")
if not args.apply:
    print("Read-only validation. Add --apply to install.")
    sys.exit(0)

backup = repo / "work" / "profile-install-backups" / datetime.now().astimezone().strftime("%Y%m%d-%H%M%S-%f")
backup.mkdir(parents=True)
destination = config / "ModsConfig.xml"
if destination.exists():
    shutil.copy2(destination, backup / "ModsConfig.xml")
created = []
for link, module in planned:
    if not link.is_symlink():
        link.symlink_to(module, target_is_directory=True)
        created.append(str(link))
(backup / "installation.json").write_text(json.dumps({"destination": str(destination), "newLinks": created}, indent=2))
temporary = config / "ModsConfig.xml.grechka-tmp"
temporary.write_bytes(source_config.read_bytes())
temporary.replace(destination)
require(destination.read_bytes() == source_config.read_bytes(), "Installed config differs from repository profile")
print("Installed. Previous config backup:", backup)
print("Restart RimWorld manually to load the profile. This command does not touch saves or preferences.")
