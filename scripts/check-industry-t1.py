#!/usr/bin/env python3
"""Static bootstrap check for the standalone Industry T1 route."""

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

print("GrechkaIndustry T1 bootstrap XML and recipe path: OK")
