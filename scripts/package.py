#!/usr/bin/env python3
"""Create local prerelease archives from an explicit allowlist; never publish/install."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path, default=repo / "work" / "packages")
parser.add_argument("--module", help="Package a single module without the collection profile")
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
modules = sorted(p for p in (repo / "mods").iterdir() if (p / "About" / "About.xml").is_file())
if args.module:
    modules = [module for module in modules if module.name == args.module]
    if not modules:
        parser.error("Unknown module: " + args.module)
allowed_folders = {"About", "Defs", "Patches", "Textures", "Languages", "Sounds", "QuestPacks", "Config"}
allowed_suffixes = {".xml", ".png", ".jpg", ".jpeg", ".ogg", ".wav", ".json"}
revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
dirty = bool(subprocess.check_output(["git", "status", "--porcelain", "--", "mods", "scripts", "profiles"], cwd=repo, text=True).strip())
for module in modules:
    about = ET.parse(module / "About" / "About.xml").getroot()
    version = about.findtext("modVersion", "0.1.0")
    assembly = module / "Assemblies" / (module.name + ".dll")
    if not assembly.is_file():
        raise SystemExit("Build module before packaging: " + module.name)
    files = [assembly]
    for path in module.rglob("*"):
        relative = path.relative_to(module)
        if path.is_file() and relative.parts[0] in allowed_folders and path.suffix.lower() in allowed_suffixes:
            files.append(path)
    if (module / "LoadFolders.xml").is_file():
        files.append(module / "LoadFolders.xml")
    manifest = {
        "packageId": about.findtext("packageId"), "version": version,
        "status": "prerelease-awaiting-game-acceptance", "commit": revision, "uncommittedSources": dirty,
        "gameLibrariesIncluded": False,
        "files": {str(p.relative_to(module)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(files)},
    }
    destination = args.output / f"{module.name}-{version}-prerelease.zip"
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(files):
            archive.write(path, str(Path(module.name) / path.relative_to(module)))
        archive.writestr(module.name + "/release-manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None, destination
        assert all("internal docs" not in name and "/Source/" not in name for name in archive.namelist())
        assert sum(name.endswith(".dll") for name in archive.namelist()) == 1
    print(destination)

# A portable load-order/profile artifact is separate from the actual mod content.
# External Workshop packages are listed, never copied into our archives.
profile = repo / "profiles" / "GrechkaWorld"
profile_files = [profile / "ModsConfig.xml", profile / "mods.json"]
if not args.module and all(path.is_file() for path in profile_files):
    config = ET.parse(profile_files[0]).getroot()
    profile_manifest = {
        "status": "prerelease-awaiting-game-acceptance",
        "gameVersion": config.findtext("version"),
        "commit": revision, "uncommittedSources": dirty,
        "activePackages": [node.text for node in config.findall("activeMods/li")],
        "externalWorkshopContentIncluded": False,
        "files": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in profile_files},
    }
    destination = args.output / "GrechkaWorld-profile-prerelease.zip"
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in profile_files:
            archive.write(path, "GrechkaWorldProfile/" + path.name)
        archive.writestr("GrechkaWorldProfile/release-manifest.json", json.dumps(profile_manifest, ensure_ascii=False, indent=2))
    with zipfile.ZipFile(destination) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == 3
    print(destination)
