#!/usr/bin/env python3
"""Research DAG, explicit coverage, progression and vanilla bootstrap invariants."""
import importlib.util
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("research_catalogue", ROOT / "scripts/generate-research.py")
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)
path = ROOT / "mods/GrechkaWorldIntegration/Defs/Research.xml"
assert path.read_text() == generator.render(), "Research catalogue is stale; regenerate it"
tree = ET.parse(path).getroot()
projects = {n.findtext("defName"): n for n in tree.findall("ResearchProjectDef")}
assert len(projects) == 42
visited, active = set(), set()


def visit(name):
    if name in visited:
        return
    assert name not in active, f"Research cycle at {name}"
    active.add(name)
    for dep in projects[name].findall("./prerequisites/li"):
        if dep.text in projects:
            visit(dep.text)
        else:
            assert dep.text in {"Electricity", "MicroelectronicsBasics", "Fabrication"}
    active.remove(name)
    visited.add(name)


for name, node in projects.items():
    assert float(node.findtext("baseCost")) > 0
    assert node.findtext("tab") == "Grechka_ResearchTab"
    visit(name)
# Native Research scrolls horizontally but has a fixed-height canvas. Keep full-size
# 140x56px labels and fit every branch into the pane of a 1600x900 UI.
rects = [(float(n.findtext('researchViewX')) * 190, float(n.findtext('researchViewY')) * 100, name)
         for name, n in projects.items()]
assert max(x + 140 for x, _, _ in rects) + 40 <= 1100, 'Research tree exceeds the target pane width'
assert max(y + 56 for _, y, _ in rects) + 40 <= 750, 'Research branches are clipped below the fixed-height canvas'
for i, (x, y, name) in enumerate(rects):
    for other_x, other_y, other_name in rects[i+1:]:
        assert abs(x-other_x) >= 148 or abs(y-other_y) >= 64, f'Research labels overlap: {name}/{other_name}'
bindings = {}
for node in tree.findall("Grechka.WorldIntegration.ResearchUnlockDef"):
    project = node.findtext("research")
    assert project in projects
    for kind in ("things", "recipes"):
        for target in node.findall(kind + "/li"):
            key = (node.findtext("packageId"), kind, target.text)
            assert key not in bindings, f"Duplicate research binding: {key}"
            bindings[key] = project
for name, tier in (("SolarS1", 2), ("SolarS2", 2), ("SolarS3", 3), ("SolarS4", 3), ("SolarS5", 4), ("SolarS6", 5)):
    assert bindings[("grechka.industry", "things", "Grechka_" + name + "_Building")] == generator.PREFIX + "SolarT" + str(tier)
assert bindings[("grechka.storage", "things", "Grechka_Disk_256M_Item")] == generator.PREFIX + "StorageT5"
assert bindings[("grechka.equipment", "things", "Grechka_CryoRifle")] == generator.PREFIX + "EquipmentT5"
assert [n.text for n in projects[generator.PREFIX + "EquipmentT3"].findall("prerequisites/li")] == [generator.PREFIX + "IndustryT3"], "New T2 weapon must not add a prerequisite to the existing T3 armor path"
for name, tier in (("Grechka_SingleRifleT2_Item", 2), ("Grechka_BurstLauncherT4_Item", 4)):
    assert bindings[("grechka.equipment", "things", name)] == generator.PREFIX + "EquipmentT" + str(tier)
for tier, suffix in ((1, "HemostaticT1"), (2, "ImmunoT2"), (3, "MechaniteRegenT3"), (4, "LimbSerumT4"), (5, "GrechkaSerumT5")):
    assert bindings[("grechka.pharmacy", "recipes", "GrechkaPharmacy_Make" + suffix)] == generator.project_name("Pharmacy", tier)
for suffix, tier in (("Interest", 2), ("Passion", 3), ("Strong", 4), ("Talent", 5), ("Calling", 5)):
    assert bindings[("grechka.pharmacy", "recipes", "GrechkaPharmacy_MakeTrainer" + suffix)] == generator.project_name("Pharmacy", tier)
    assert bindings[("grechka.pharmacy", "things", "GrechkaPharmacy_Trainer" + suffix + "_Item")] == generator.project_name("Pharmacy", tier)
standalone = ET.parse(ROOT / "mods/GrechkaPharmacy/Defs/Research.xml").getroot()
assert {n.findtext("defName") for n in standalone.findall("ResearchProjectDef")} == {
    generator.project_name("Pharmacy", tier) for tier in range(1, 6)
}, "Standalone and profile Pharmacy research IDs must remain identical for saved progress"
profile = ET.parse(ROOT / "mods/GrechkaWorldIntegration/Defs/MachineProfile.xml").getroot()
for bench in ("SimpleResearchBench", "HiTechResearchBench", "MultiAnalyzer"):
    rule = next(n for n in profile if n.findtext("targetDefName") == bench)
    assert rule.findtext("addHousing") == "false", f"Research bootstrap requires its own future housing: {bench}"
print(f"Grechka research: {len(projects)} projects, {len(bindings)} explicit unlocks; DAG, coverage and research benches: OK")
