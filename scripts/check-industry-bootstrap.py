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
t2_bench = defs["Grechka_AssemblyBenchT2"]
upgrade = defs["Grechka_UpgradeAssemblyBenchT2"]

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
require(t2_bench.findtext("./costList/Grechka_HousingT2") == "1"
        and t2_bench.findtext("./costList/ComponentSpacer") is not None,
        "T2 bench must require its reachable T2 housing and component")
require(t2_bench.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "350",
        "T2 bench must use electricity")
require(t2_bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/inputDiscount") == "0.1"
        and t2_bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/outputBonus") == "0.1",
        "T2 bench must use the accepted 10% input and output bonuses")
require(upgrade.find("products") is None and ingredients(upgrade) == {
    "Steel": 120, "Plasteel": 20, "ComponentIndustrial": 4, "ComponentSpacer": 6
}, "in-place upgrade must credit the installed T1 housing exactly once")
require(upgrade.findtext("defName") in bench_recipes
        and upgrade.findtext("defName") not in {item.text for item in t2_bench.findall("./recipes/li")},
        "only a T1 bench may run the T2 upgrade bill")
require(bench.findtext("tickerType") == "Normal"
        and bench.find("./comps/li[@Class='Grechka.Industry.CompProperties_AssemblyUpgrade']") is not None,
        "T1 bench must tick and have the upgrade component")
require((root / "Source" / "AssemblyUpgrade.cs").is_file(),
        "assembly upgrade runtime is missing")
require((root / "Source" / "TierBonuses.cs").is_file(),
        "tier bonus runtime is missing")
group = defs["Grechka_AssemblyBenchGroup"]
require(group.tag == "DesignatorDropdownGroupDef"
        and bench.findtext("designatorDropdown") == group.findtext("defName")
        and t2_bench.findtext("designatorDropdown") == group.findtext("defName"),
        "T1 and T2 benches must share one construction dropdown")

print("GrechkaIndustry T1 to T2 bootstrap, upgrade and tier bonus XML: OK")
