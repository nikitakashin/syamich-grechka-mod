#!/usr/bin/env python3
"""Static bootstrap check for the standalone Industry T1 to T2 route."""

from pathlib import Path
from decimal import Decimal
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
require(len(operations) == 2, "advanced component patch must update T1 cost and research gate only")
require(operations[0].findtext("./value/count") == "2"
        and "ComponentIndustrial" in operations[0].findtext("xpath", ""),
        "advanced component must cost two T1 components")
require(not any("Gold" in item.findtext("xpath", "") for item in operations),
        "vanilla gold amount 0.3 already means three physical gold")
require(operations[1].get("Class") == "PatchOperationRemove"
        and operations[1].findtext("xpath", "").endswith("/researchPrerequisite"),
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

solar = defs["Grechka_SolarS1"]
solar_cells = defs["Grechka_SolarCellT2"]
solar_cell_recipe = defs["Grechka_MakeSolarCellT2"]
solar_recipe = defs["Grechka_MakeSolarS1"]
solar_group = defs["Grechka_SolarPanelGroup"]
solar_patch = trees[root / "Patches" / "SolarGenerator.xml"].getroot()
solar_values = solar_patch.find("./Operation/operations/li/value")
solar_comp_patch = solar_patch.findall("./Operation/operations/li")
require(solar_patch.findtext("./Operation/operations/li/xpath") == '/Defs/ThingDef[defName="SolarGenerator"]'
        and solar_values.findtext("minifiedDef") == "Grechka_PackedSolarS0"
        and solar_values.findtext("designatorDropdown") == solar_group.findtext("defName"),
        "vanilla solar generator must gain packaging and the shared construction group")
require(len(solar_comp_patch) == 2
        and solar_comp_patch[1].findtext("xpath") == '/Defs/ThingDef[defName="SolarGenerator"]/comps'
        and solar_comp_patch[1].find("./value/li[@Class='Grechka.Industry.CompProperties_SolarUpgrade']") is not None
        and (root / "Source" / "SolarUpgrade.cs").is_file(),
        "installed S0 needs an in-place upgrade button")
require(all(defs[name].get("ParentName") == "MinifiedThing"
            for name in ("Grechka_PackedSolarS0", "Grechka_PackedSolarS1")),
        "both solar generations need distinct minified definitions")
require(solar.findtext("minifiedDef") == "Grechka_PackedSolarS1"
        and solar.findtext("designatorDropdown") == solar_group.findtext("defName")
        and solar.findtext("./comps/li[@Class='CompProperties_Power']/compClass") == "CompPowerPlantSolar"
        and solar.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "-6000",
        "S1 must be a packable six-kilowatt vanilla-style solar plant")
require(solar_cells.findtext("stackLimit") == "100"
        and solar_cell_recipe.findtext("./products/Grechka_SolarCellT2") == "8",
        "T2 solar cells must be a stackable eight-piece product")
cell_inputs = {
    li.findtext("./filter/thingDefs/li"): Decimal(li.findtext("count"))
    for li in solar_cell_recipe.findall("./ingredients/li")
}
require(cell_inputs == {"ComponentIndustrial": Decimal("2"), "Silver": Decimal("1"), "Gold": Decimal("0.5")},
        "solar cell recipe must use 2 components, 10 silver and 5 gold at vanilla smallVolume=0.1")
require(solar_recipe.findtext("./products/Grechka_SolarS1") == "1"
        and solar_recipe.findtext("allowMixingIngredients") == "true"
        and {li.text for li in solar_recipe.findall("./ingredients/li[1]/filter/thingDefs/li")}
            == {"SolarGenerator", "Grechka_PackedSolarS0"},
        "S1 recipe must consume a whole packed vanilla solar panel")
other_recipe_inputs = {
    li.findtext("./filter/thingDefs/li"): li.findtext("count")
    for li in solar_recipe.findall("./ingredients/li")[1:]
}
require(other_recipe_inputs == {"Grechka_HousingT2": "1", "ComponentSpacer": "4", "Grechka_SolarCellT2": "8"}
        and {li.tag: li.text for li in solar.findall("./costList/*")}
            == {"Grechka_PackedSolarS0": "1", **other_recipe_inputs},
        "direct S1 construction and workbench assembly must have the same costs")
require({"Grechka_MakeSolarCellT2", "Grechka_MakeSolarS1"}
        <= {li.text for li in t2_bench.findall("./recipes/li")},
        "T2 bench must expose both solar recipes")

print("GrechkaIndustry T1 to T2 and S1 solar XML routes: OK")
