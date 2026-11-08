#!/usr/bin/env python3
"""Check DestroyItem definitions, translations and collection registration without running the game."""
import json
import os
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
module = root / "mods/GrechkaDestroyItem"
package = "grechka.destroyitem"
about = ET.parse(module / "About/About.xml").getroot()
assert about.findtext("name") == "Grechka Destroy Item"
assert about.findtext("packageId") == package
assert [e.text for e in about.findall("supportedVersions/li")] == ["1.7"]
assert [e.text for e in about.findall("modDependencies/li/packageId")] == ["brrainz.harmony"]
defs = ET.parse(module / "Defs/DestroyItem.xml").getroot()
assert len(defs) == 4
for node in defs:
    assert node.findtext("defName")
thought = defs.find("ThoughtDef")
assert float(thought.findtext("durationDays")) == .25
assert thought.findtext("stackLimit") == "1"
assert thought.findtext("stages/li/baseMoodEffect") == "-3"
work_type = defs.findtext("WorkGiverDef/workType")
assert work_type == "BasicWorker"
game_data = Path(os.environ.get("RIMWORLD_DATA_DIR", Path.home() / "Library/Application Support/Steam/steamapps/common/RimWorld/Data"))
if game_data.is_dir():
    work_types = ET.parse(game_data / "Core/Defs/WorkTypeDefs/WorkTypes.xml")
    assert work_types.find(f"./WorkTypeDef[defName='{work_type}']") is not None, work_type
assert defs.findtext("WorkGiverDef/requiredCapacities") is None
source = (module / "Source/DestroyItem.cs").read_text()
keys = set(re.findall(r'"(GrechkaDestroy_\w+)"', source))
for language in ("English", "Russian"):
    strings = ET.parse(module / f"Languages/{language}/Keyed/DestroyItem.xml").getroot()
    assert len(strings) == len(keys) and {e.tag for e in strings} == keys
    assert all(e.text and e.text.strip() for e in strings)
for file in module.rglob("*.xml"):
    ET.parse(file)
injected = []
for file in (module / "Languages/Russian/DefInjected").rglob("*.xml"):
    for entry in ET.parse(file).getroot():
        definition, path = entry.tag.split(".", 1)
        target = next(e for e in defs if e.tag == file.parent.name and e.findtext("defName") == definition)
        for part in path.split("."):
            target = list(target)[int(part)] if part.isdigit() else target.find(part)
            assert target is not None, entry.tag
        assert entry.text.strip()
        injected.append(entry.tag)
assert len(injected) == 6 and len(set(injected)) == 6
profile = root / "profiles/GrechkaWorld"
active = [e.text for e in ET.parse(profile / "ModsConfig.xml").findall("activeMods/li")]
assert len(active) == len(set(active)) and active.count(package) == 1
assert active.index("brrainz.harmony") < active.index(package)
manifest = json.loads((profile / "mods.json").read_text())
entries = [e for e in manifest if e["package"] == package]
assert len(entries) == 1 and entries[0]["repositoryPath"] == "mods/GrechkaDestroyItem"
assert entries[0]["source"] == "local"
build_modules = re.search(r"for module in\s+(.*?);\s*do", (root / "scripts/build.sh").read_text(), re.S).group(1).split()
assert build_modules.count("GrechkaDestroyItem") == 1
assert "DestroyItem/DestroyItemTests" in (root / "scripts/test-native.sh").read_text()
print("PASS DestroyItem: metadata, four stable definitions, EN/RU strings, native-job registration and collection profile agree.")
