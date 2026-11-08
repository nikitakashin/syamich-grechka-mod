#!/usr/bin/env python3
"""Validate standalone Storage startup and the opt-in World controller recipe."""
from decimal import Decimal
from pathlib import Path
import copy
import json
import re
import struct
import sys
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
root = repo / "mods/GrechkaStorage"

def require(ok, message):
    if not ok:
        sys.exit(message)

defs = {}
for path in (root / "Defs").rglob("*.xml"):
    for node in ET.parse(path).getroot():
        name = node.findtext("defName")
        if not name:
            continue
        require(name not in defs, "Duplicate Storage def: " + name)
        defs[name] = node
        if node.tag == "ThingDef":
            require(not name[-1].isdigit(), "ThingDef name ends in a digit: " + name)
            if node.get("ParentName") == "BuildingBase" and node.findtext("minifiedDef"):
                require(node.findtext("./statBases/Mass") and node.findtext("./thingCategories/li"),
                        "Portable building requires mass/category: " + name)
# The standalone category is supplied by a conditional XML patch rather than a duplicate Def.
fallback = ET.parse(root / "Patches/ArchitectCategory.xml").find("Operation/nomatch/value/DesignationCategoryDef")
require(fallback is not None and fallback.findtext("defName") == "Grechka_IndustryCategory",
        "Standalone Storage must supply the shared Architect category")
require(fallback.findtext("defName") not in defs, "Shared Architect category must not be duplicated")
defs[fallback.findtext("defName")] = fallback
for node in defs.values():
    require(not node.findall("researchPrerequisite") and not node.findall("researchPrerequisites"),
            "Storage startup cannot depend on a new research gate")
    for ref in node.iter():
        for value in (ref.tag, (ref.text or "").strip()):
            if re.fullmatch(r"Grechka_[A-Za-z0-9_]+", value):
                require(value in defs, "Standalone module references missing/Industry content: " + value)

def inputs(recipe):
    return {li.findtext("./filter/thingDefs/li"): Decimal(li.findtext("count"))
            for li in recipe.findall("./ingredients/li")}

def cost(node):
    return {li.tag: int(li.text) for li in node.findall("./costList/*")}

ore = defs["Grechka_MineableQuartz"]
require(ore.get("ParentName") == "RockBase"
        and ore.findtext("./building/mineableThing") == "Grechka_Quartz"
        and Decimal(ore.findtext("./building/mineableScatterCommonality")) < 1
        and ore.findtext("./building/mineableYield") == "35"
        and ore.findtext("./building/isResourceRock") == "true",
        "Quartz veins must yield real quartz and be less common than vanilla steel")
require(inputs(defs["Grechka_CrushQuartz"]) == {"Grechka_Quartz": Decimal(5)}
        and inputs(defs["Grechka_SmeltSilicon"]) == {"Grechka_QuartzDust": Decimal(5)}
        and "ElectricSmelter" in [li.text for li in defs["Grechka_SmeltSilicon"].findall("./recipeUsers/li")],
        "Silicon must be reachable from mined quartz without an existing network")

inscriber = defs["Grechka_Inscriber"]
available = {li.text for li in inscriber.findall("./recipes/li")}
require(cost(inscriber) == {"Steel": 40, "ComponentIndustrial": 2, "Grechka_Quartz": 5}
        and inscriber.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "200",
        "First inscriber must be built without its own processors")
require(inputs(defs["Grechka_MakeSiliconSubstrate"]) == {"Grechka_Silicon": Decimal(5)}
        and inputs(defs["Grechka_MakePrintedSilicon"]) == {"Grechka_SiliconSubstrate": Decimal(1)}
        and {"Grechka_MakeSiliconSubstrate", "Grechka_MakePrintedSilicon"} <= available,
        "Inscriber must make both of its silicon intermediates")
for kind, material, amount in [("Quartz", "Grechka_Quartz", "1"), ("Silver", "Silver", "0.5"), ("Gold", "Gold", "0.3")]:
    circuit = defs[f"Grechka_Make{kind}Circuit"]
    processor = defs[f"Grechka_Make{kind}Processor"]
    require(inputs(circuit) == {material: Decimal(amount), "Grechka_SiliconSubstrate": Decimal(1)}
            and inputs(processor) == {f"Grechka_{kind}Circuit": Decimal(1), "Grechka_PrintedSilicon": Decimal(1), "ComponentIndustrial": Decimal(1)}
            and circuit.findtext(f"./products/Grechka_{kind}Circuit") == "1"
            and processor.findtext(f"./products/Grechka_{kind}Processor") == "2"
            and {circuit.findtext("defName"), processor.findtext("defName")} <= available,
            f"{kind} circuit/processor path must be complete without templates or presses")

cable = defs["Grechka_QuartzCable"]
controller = defs["Grechka_StorageController"]
standalone = {"Steel": 80, "ComponentSpacer": 2, "Grechka_QuartzProcessor": 2,
              "Grechka_SilverProcessor": 1, "Grechka_GoldProcessor": 1}
require(cost(controller) == standalone
        and controller.findtext("./comps/li[@Class='Grechka.Storage.CompProperties_SignalNode']/controller") == "true"
        and controller.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "400",
        "Standalone controller must be reachable without Industry")
require(cost(cable) == {"Grechka_Quartz": 1, "Steel": 1}
        and cable.findtext("./building/isEdifice") == "false"
        and cable.findtext("./building/isPowerConduit", "false") == "false"
        and cable.findtext("drawerType") == "RealtimeOnly"
        and cable.findtext("tickerType") == "Never"
        and cable.find("./comps/li[@Class='CompProperties_Power']") is None
        and cable.findtext("./comps/li[@Class='Grechka.Storage.CompProperties_SignalNode']/cable") == "true",
        "Signal cable must coexist under buildings without joining the electrical grid or ticking")

profile_rules = ET.parse(repo / "mods/GrechkaWorldIntegration/Defs/MachineProfile.xml").getroot()
storage_rules = {d.findtext("targetDefName"): d for d in profile_rules if d.findtext("packageId") == "grechka.storage"}
require(ET.parse(repo / "mods/GrechkaWorldIntegration/Patches/StorageController.xml").getroot().find("Operation") is None,
        "World prices must not be applied unconditionally to old saves")
def profile_price(name):
    rule = storage_rules[name]
    require(rule.findtext("addHousing") == "true", "Storage profile lost its housing gate")
    result = {c.findtext("thingDefName"): int(c.findtext("count")) for c in rule.findall("replacementCost/li")}
    result["Grechka_HousingT" + rule.findtext("minimumTier") + "_Item"] = 1
    return result
world = profile_price("Grechka_StorageController")
require(world == {"Grechka_HousingT3_Item": 1, "Grechka_ComponentT3_Item": 1, "Steel": 80,
                  "Grechka_QuartzProcessor": 2, "Grechka_SilverProcessor": 1, "Grechka_GoldProcessor": 1},
        "World controller must use the T3 Industry route and all three processors")
require(profile_price("Grechka_Inscriber") == {"Grechka_HousingT1_Item": 1, "Steel": 40,
                                             "ComponentIndustrial": 2, "Grechka_Quartz": 5},
        "World inscriber needs reachable T1 housing without demanding its own processors")
for industry, integration, approved in [(False,False,False),(True,False,True),(False,True,True),(True,True,False),(True,True,True)]:
    effective = world if industry and integration and approved else standalone
    require(("Grechka_HousingT3_Item" in effective) == (industry and integration and approved),
            "Profile prices require Industry, Integration and per-save explicit/new-game approval")

about = ET.parse(root / "About/About.xml").getroot()
require([li.findtext("packageId") for li in about.findall("./modDependencies/li")] == ["brrainz.harmony"],
        "Storage must not acquire a hard dependency on Industry or Quests")
profile = repo / "profiles/GrechkaWorld"
active = [li.text for li in ET.parse(profile / "ModsConfig.xml").findall("./activeMods/li")]
require(active.count("grechka.storage") == 1 and active.index("grechka.storage") < active.index("grechka.world.integration"),
        "Storage must be enabled once, before World Integration")
manifest = json.loads((profile / "mods.json").read_text())
require(len([e for e in manifest if e.get("package") == "grechka.storage" and e.get("repositoryPath") == "mods/GrechkaStorage"]) == 1,
        "Storage module installer entry is missing")
for name in ["StorageController", "Natural/QuartzOreSource"]:
    require((root / "Textures/Buildings" / (name + ".png")).is_file(), "Prepared building texture missing: " + name)
source = defs["Grechka_CertusSource"]
require(source.findtext("minifiedDef") == "MinifiedThing"
        and source.findtext("./building/alwaysUninstallable") == "true"
        and source.findtext("./building/deconstructible") == "false",
        "Crystal source must be removable intact rather than dismantled")
require(defs["Grechka_CertusHarvestWork"].findtext("workType") == "Growing"
        and defs["Grechka_CertusHarvestWork"].findtext("giverClass") == "Grechka.Storage.WorkGiver_HarvestCertus"
        and defs["Grechka_HarvestCertus"].findtext("driverClass") == "Grechka.Storage.JobDriver_HarvestCertus",
        "Ripe crystals must have a registered pawn harvest job")
for stage in range(5):
    frame = root / f"Textures/Buildings/Map/CertusGrowthSource/CertusGrowthSource_{stage}.png"
    data = frame.read_bytes()
    require(data[:8] == b'\x89PNG\r\n\x1a\n' and struct.unpack('>II', data[16:24]) == (256, 256)
            and data[25] == 6, "Crystal growth stage must be a 256x256 RGBA PNG: " + str(stage))
require((root / "Textures/Buildings/Map/CertusGrowthSource/CertusGrowthSource_4.png").read_bytes()
        == (root / "Textures/Buildings/CertusGrowthSource.png").read_bytes(),
        "Mature crystal stage must preserve the user's original texture")
for name in ["SiliconSubstrate", "PrintedSilicon", "QuartzCircuit", "SilverCircuit", "GoldCircuit", "QuartzProcessor", "SilverProcessor", "GoldProcessor"]:
    require((root / "Textures/Items" / (name + ".png")).is_file(), "Prepared processor texture missing: " + name)
sizes = [("1K", 1024), ("4K", 4096), ("16K", 16384), ("64K", 65536), ("256K", 262144),
         ("512K", 524288), ("1M", 1048576), ("2M", 2097152), ("4M", 4194304), ("8M", 8388608),
         ("16M", 16777216), ("64M", 67108864), ("256M", 268435456)]
disks = [n for n in defs.values() if n.findtext("thingClass") == "Grechka.Storage.StorageDisk"]
require(len(disks) == 26, "Every accepted capacity needs both ordinary and permanent-freezing disks")
for size, capacity in sizes:
    core = f"Grechka_DataCore_{size}_Item"
    require(core in defs and f"Grechka_MakeDataCore_{size}" in available,
            "Disk hardware core is unreachable: " + size)
    for frozen in (False, True):
        name = ("Grechka_FrozenDisk_" if frozen else "Grechka_Disk_") + size + "_Item"
        node = defs[name]
        recipe = defs[("Grechka_MakeFrozenDisk_" if frozen else "Grechka_MakeDisk_") + size]
        props = node.find("./modExtensions/li[@Class='Grechka.Storage.StorageDiskProperties']")
        require(node.findtext("stackLimit") == "1" and node.findtext("tickerType") == "Never"
                and props.findtext("capacityBytes") == str(capacity)
                and props.findtext("freezing") == str(frozen).lower()
                and recipe.findtext("defName") in available and recipe.findtext("./products/" + name) == "1"
                and core in inputs(recipe), "Physical disk capacity/recipe/variant is incorrect: " + name)
        texture = node.findtext("./graphicData/texPath")
        require((root / "Textures" / (texture + ".png")).is_file()
                and node.find("./graphicData/color") is None,
                "Prepared disk art is missing or recolored: " + name)
for node in defs.values():
    if node.tag == "RecipeDef":
        require(not any(li.text in {d.findtext("defName") for d in disks}
                        for li in node.findall("./ingredients/li/filter/thingDefs/li")),
                "A recipe could consume a loaded disk and destroy its contents: " + node.findtext("defName"))
rack = defs["Grechka_DiskRack"]
require(all(node.findtext("size") == "(1,1)" and node.findtext("./graphicData/drawSize") == "(1,1)"
            for node in (controller, rack)), "Controller and disk rack must both occupy and draw one cell")
require(controller.findtext("thingClass") == "Grechka.Storage.Building_StorageController"
        and rack.findtext("thingClass") == "Grechka.Storage.Building_DiskRack"
        and rack.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "120"
        and rack.findtext("./statBases/Flammability") == "0"
        and rack.findtext("minifiedDef") == controller.findtext("minifiedDef") == "Grechka_PackedStorageDevice"
        and defs["Grechka_PackedStorageDevice"].findtext("thingClass") == "Grechka.Storage.MinifiedStorageDevice",
        "Disk rack and its loaded minified wrapper must preserve the accepted protection")
require(profile_price("Grechka_DiskRack") == {"Grechka_HousingT3_Item": 1,
        "Steel": 60, "Grechka_QuartzProcessor": 2, "Grechka_SilverProcessor": 1},
        "World rack must preserve its complete T3 price")

for name in ("Grechka_ImportStorage", "Grechka_ExportStorage", "Grechka_RemoveDisk", "Grechka_StorageHauling"):
    require(name in defs, "Physical pawn transfer job is missing: " + name)
print("Grechka Storage startup, 26 physical disks, protected rack and optional World recipes: OK")
