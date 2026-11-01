#!/usr/bin/env python3
"""Check that the research rule is confined to the explicit World profile."""

import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


root = Path(__file__).resolve().parents[1]
mod = root / "mods" / "GrechkaWorldIntegration"
package = "grechka.world.integration"


def require(ok, message):
    if not ok:
        sys.exit(message)


about = ET.parse(mod / "About" / "About.xml").getroot()
require(about.findtext("packageId") == package, "Integration package ID changed")
require(any(item.findtext("packageId") == "brrainz.harmony"
            for item in about.findall("./modDependencies/li")), "Harmony dependency is missing")

patch = ET.parse(mod / "Patches" / "ResearchTab.xml").getroot()
operations = patch.findall("./Operation/operations/li")
require(len(operations) == 2, "Research button needs visibility and key patches")
require(operations[0].get("Class") == "PatchOperationAdd"
        and operations[0].findtext("xpath") == '/Defs/MainButtonDef[defName="Research"]'
        and operations[0].findtext("./value/buttonVisible") == "false",
        "Research button is not hidden")
require(operations[1].get("Class") == "PatchOperationReplace"
        and operations[1].findtext("xpath") == '/Defs/MainButtonDef[defName="Research"]/defaultHotKey'
        and operations[1].findtext("./value/defaultHotKey") == "None",
        "Research default hotkey is not removed")

profile = root / "profiles" / "GrechkaWorld"
active = [item.text for item in ET.parse(profile / "ModsConfig.xml").findall("./activeMods/li")]
require(active.count(package) == 1, "Integration must be enabled once in the repository profile")
manifest = json.loads((profile / "mods.json").read_text())
entries = [item for item in manifest if item.get("package") == package]
require(len(entries) == 1 and entries[0].get("repositoryPath") == "mods/GrechkaWorldIntegration",
        "Integration manifest entry is missing or duplicated")

print("Grechka World research profile, menu patch and metadata: OK")
