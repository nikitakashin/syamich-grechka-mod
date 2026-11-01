#!/usr/bin/env python3
"""Validate standalone Storage startup and the opt-in World controller recipe."""
from decimal import Decimal
from pathlib import Path
import copy
import json
import re
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
require(cost(inscriber) == {"Steel": 80, "ComponentIndustrial": 5, "Grechka_Quartz": 10}
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
            and processor.findtext(f"./products/Grechka_{kind}Processor") == "1"
            and {circuit.findtext("defName"), processor.findtext("defName")} <= available,
            f"{kind} circuit/processor path must be complete without templates or presses")

cable = defs["Grechka_QuartzCable"]
controller = defs["Grechka_StorageController"]
standalone = {"Steel": 150, "ComponentSpacer": 6, "Grechka_QuartzProcessor": 8,
              "Grechka_SilverProcessor": 4, "Grechka_GoldProcessor": 4}
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

patch = ET.parse(repo / "mods/GrechkaWorldIntegration/Patches/StorageController.xml").getroot()
outer = patch.find("Operation")
inner = outer.find("match")
sequence = inner.find("match")
operations = sequence.findall("./operations/li")
replace = operations[0]
require(outer.get("Class") == inner.get("Class") == "PatchOperationFindMod"
        and outer.findtext("./mods/li") == "Grechka Storage"
        and inner.findtext("./mods/li") == "Grechka Industry"
        and sequence.get("Class") == "PatchOperationSequence" and len(operations) == 2
        and replace.get("Class") == "PatchOperationReplace"
        and replace.findtext("xpath") == '/Defs/ThingDef[defName="Grechka_StorageController"]/costList',
        "World controller patch must require both optional content modules")
world = cost(replace.find("value"))
require(world == {"Grechka_HousingT3_Item": 1, "Grechka_ComponentT3_Item": 4, "Plasteel": 100,
                  "Grechka_QuartzProcessor": 8, "Grechka_SilverProcessor": 4, "Grechka_GoldProcessor": 4},
        "World controller must use the T3 Industry route and all three processors")
require(operations[1].findtext("xpath") == '/Defs/ThingDef[defName="Grechka_Inscriber"]/costList'
        and cost(operations[1].find("value")) == {"Grechka_HousingT1_Item": 1, "Steel": 80,
                                                "ComponentIndustrial": 5, "Grechka_Quartz": 10},
        "World inscriber must use a reachable T1 housing without demanding its own processors")

def profile_costs(installed):
    targets = {node.findtext("defName"): copy.deepcopy(node) for node in (controller, inscriber)}
    def apply(operation):
        kind = operation.get("Class")
        if kind == "PatchOperationFindMod":
            branch = "match" if any(li.text in installed for li in operation.findall("./mods/li")) else "nomatch"
            selected = operation.find(branch)
            if selected is not None:
                apply(selected)
        elif kind == "PatchOperationSequence":
            for child in operation.findall("./operations/li"):
                apply(child)
        elif kind == "PatchOperationReplace":
            name = re.fullmatch(r'/Defs/ThingDef\[defName="([^"]+)"\]/costList', operation.findtext("xpath")).group(1)
            target = targets[name]
            target.remove(target.find("costList"))
            target.append(copy.deepcopy(operation.find("./value/costList")))
        else:
            raise AssertionError("Unsupported profile patch: " + str(kind))
    if "Grechka World Integration" in installed:
        apply(outer)
    return {name: cost(node) for name, node in targets.items()}

for industry, integration in [(False, False), (True, False), (False, True), (True, True)]:
    installed = {"Grechka Storage"}
    if industry:
        installed.add("Grechka Industry")
    if integration:
        installed.add("Grechka World Integration")
    result = profile_costs(installed)
    require(result["Grechka_StorageController"] == (world if industry and integration else standalone)
            and ("Grechka_HousingT1_Item" in result["Grechka_Inscriber"]) == (industry and integration),
            "Optional-mod patch traversal changes standalone recipes incorrectly")

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
for name in ["SiliconSubstrate", "PrintedSilicon", "QuartzCircuit", "SilverCircuit", "GoldCircuit", "QuartzProcessor", "SilverProcessor", "GoldProcessor"]:
    require((root / "Textures/Items" / (name + ".png")).is_file(), "Prepared processor texture missing: " + name)
print("Grechka Storage standalone startup, processors, network definitions and opt-in World recipe: OK")
