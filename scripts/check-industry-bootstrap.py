#!/usr/bin/env python3
"""Static bootstrap check for the standalone Industry T1 to T2 route."""

from pathlib import Path
import sys
import xml.etree.ElementTree as ET


root = Path(__file__).resolve().parents[1] / "mods" / "GrechkaIndustry"
files = list(root.rglob("*.xml"))
if not files:
    sys.exit("GrechkaIndustry XML is missing")

trees = {path: ET.parse(path) for path in files}
defs = {}
for tree in trees.values():
    for node in tree.getroot():
        name = node.findtext("defName")
        if name:
            if name in defs:
                sys.exit(f"duplicate defName: {name}")
            defs[name] = node


def require(condition, message):
    if not condition:
        sys.exit(message)


def ingredients(recipe):
    result = {}
    for ingredient in recipe.findall("./ingredients/li"):
        names = [li.text for li in ingredient.findall("./filter/thingDefs/li")]
        require(len(names) == 1, "T1 ingredient must name exactly one item")
        result[names[0]] = int(ingredient.findtext("count"))
    return result


hand = defs["Grechka_MakeComponentByHandT1"]
housing = defs["Grechka_MakeHousingT1"]
batch = defs["Grechka_MakeComponentBatchT1"]
bench = defs["Grechka_AssemblyBenchT1"]
t2_housing = defs["Grechka_MakeHousingT2"]

require(hand.findtext("./products/ComponentIndustrial") == "1" and ingredients(hand) == {"Steel": 12},
        "manual component route must start from 12 steel")
require(housing.findtext("./products/Grechka_HousingT1") == "1"
        and ingredients(housing) == {"Steel": 80, "ComponentIndustrial": 4},
        "housing must be reachable after manual components")
require(batch.findtext("./products/ComponentIndustrial") == "5" and ingredients(batch) == {"Steel": 60},
        "batch route must yield five components from 60 steel")
require(bench.findtext("./costList/Grechka_HousingT1") == "1"
        and bench.findtext("./costList/Steel") is not None,
        "assembly bench must use the reachable T1 housing")

patch = trees[root / "Patches" / "CraftingSpot.xml"].getroot()
require(patch.findtext("./Operation/xpath") == '/Defs/ThingDef[defName="CraftingSpot"]/recipes',
        "crafting spot patch is missing")
available = {item.text for item in patch.findall("./Operation/value/li")}
require({hand.findtext("defName"), housing.findtext("defName")} <= available,
        "crafting spot must expose the manual bootstrap recipes")
require(batch.findtext("defName") in [item.text for item in bench.findall("./recipes/li")],
        "T1 bench must expose the batch recipe")
require((root / "Textures" / "Items" / "HousingT1.png").is_file(),
        "housing texture is missing")
require(t2_housing.findtext("./products/Grechka_HousingT2") == "1"
        and ingredients(t2_housing) == {
            "Grechka_HousingT1": 1, "Steel": 40, "Plasteel": 20, "ComponentSpacer": 4
        }, "T2 housing must be reachable from T1 materials and advanced components")
bench_recipes = {item.text for item in bench.findall("./recipes/li")}
require({"Make_ComponentSpacer", "Grechka_MakeHousingT2"} <= bench_recipes,
        "T1 bench must make the first T2 component and housing")
advanced_patch = trees[root / "Patches" / "AdvancedComponent.xml"].getroot()
operations = advanced_patch.findall("./Operation/operations/li")
require(len(operations) == 3, "advanced component patch must update both costs and research gate")
require(operations[0].findtext("./value/count") == "2"
        and "ComponentIndustrial" in operations[0].findtext("xpath", ""),
        "advanced component must cost two T1 components")
require(operations[1].findtext("./value/count") == "3"
        and "Gold" in operations[1].findtext("xpath", ""),
        "advanced component must cost three gold")
require(operations[2].get("Class") == "PatchOperationRemove"
        and operations[2].findtext("xpath", "").endswith("/researchPrerequisite"),
        "standalone Industry must not research-gate its first T2 component")
require((root / "Textures" / "Items" / "HousingT2.png").is_file(),
        "T2 housing texture is missing")

print("GrechkaIndustry T1 to T2 bootstrap XML and recipe path: OK")
