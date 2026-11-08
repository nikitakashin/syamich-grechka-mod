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
bench = defs["Grechka_AssemblyBenchT1_Building"]
t2_housing = defs["Grechka_MakeHousingT2"]
t2_bench = defs["Grechka_AssemblyBenchT2_Building"]
upgrade = defs["Grechka_UpgradeAssemblyBenchT2"]

require(hand.findtext("./products/ComponentIndustrial") == "1" and ingredients(hand) == {"Steel": 12},
        "manual component route must start from 12 steel")
require(housing.findtext("./products/Grechka_HousingT1_Item") == "1"
        and ingredients(housing) == {"Steel": 40, "ComponentIndustrial": 2},
        "housing must be reachable after manual components")
require(batch.findtext("./products/ComponentIndustrial") == "5" and ingredients(batch) == {"Steel": 60},
        "batch route must yield five components from 60 steel")
require(bench.findtext("./costList/Grechka_HousingT1_Item") == "1"
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
require(t2_housing.findtext("./products/Grechka_HousingT2_Item") == "1"
        and ingredients(t2_housing) == {
            "Grechka_HousingT1_Item": 1, "Steel": 20, "Plasteel": 10, "ComponentSpacer": 2
        }, "T2 housing must be reachable from T1 materials and advanced components")
bench_recipes = {item.text for item in bench.findall("./recipes/li")}
require({"Make_ComponentSpacer", "Grechka_MakeHousingT2"} <= bench_recipes,
        "T1 bench must make the first T2 component and housing")
advanced_patch = trees[root / "Patches" / "AdvancedComponent.xml"].getroot()
operations = advanced_patch.findall("./Operation/operations/li")
require(len(operations) == 6, "advanced component patch must synchronize all physical input prices")
require([op.findtext("value/count") for op in operations[:4]] == ["1", "12", "5", "0.2"],
        "advanced component must use one ordinary, 12 steel, 5 plasteel and two physical gold")
require(operations[4].get("Class") == "PatchOperationRemove"
        and operations[4].findtext("xpath", "").endswith("/researchPrerequisite"),
        "standalone Industry must not research-gate its first T2 component")
require((root / "Textures" / "Items" / "HousingT2.png").is_file(),
        "T2 housing texture is missing")
require(t2_bench.findtext("./costList/Grechka_HousingT2_Item") == "1"
        and t2_bench.findtext("./costList/ComponentSpacer") is not None,
        "T2 bench must require its reachable T2 housing and component")
require(t2_bench.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "350",
        "T2 bench must use electricity")
require(t2_bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/inputDiscount") == "0.1"
        and t2_bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/outputBonus") == "0.1",
        "T2 bench must use the accepted 10% input and output bonuses")
require(upgrade.find("products") is None and ingredients(upgrade) == {
    "Steel": 20, "Plasteel": 10, "ComponentSpacer": 4
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

solar = defs["Grechka_SolarS1_Building"]
solar_cells = defs["Grechka_SolarCellT2_Item"]
solar_cell_recipe = defs["Grechka_MakeSolarCellT2"]
solar_recipe = defs["Grechka_MakeSolarS1"]
solar_patch = trees[root / "Patches" / "SolarGenerator.xml"].getroot()
solar_values = solar_patch.find("./Operation/operations/li/value")
solar_comp_patch = solar_patch.findall("./Operation/operations/li")
require(solar_patch.findtext("./Operation/operations/li/xpath") == '/Defs/ThingDef[defName="SolarGenerator"]'
        and solar_values.findtext("minifiedDef") == "Grechka_PackedSolarS0_Item"
        and solar_values.find("designatorDropdown") is None
        and solar_patch.findtext('./Operation/operations/li[@Class="PatchOperationReplace"]/value/designationCategory') == "Grechka_SolarCategory",
        "vanilla solar generator must gain packaging and move to the standalone solar tab")
require(len(solar_comp_patch) == 6
        and solar_comp_patch[2].findtext("xpath") == '/Defs/ThingDef[defName="SolarGenerator"]/comps'
        and solar_comp_patch[2].find("./value/li[@Class='Grechka.Industry.CompProperties_SolarUpgrade']") is not None
        and solar_comp_patch[2].findtext("./value/li/targetDefName") == "Grechka_SolarS1_Building"
        and (root / "Source" / "SolarUpgrade.cs").is_file(),
        "installed S0 needs an in-place upgrade button")
require(solar_values.findtext("./thingCategories/li") == "BuildingsSpecial"
        and solar_comp_patch[1].findtext("./value/Mass") == "50",
        "making vanilla solar minifiable requires a category and authored mass")
require(all(defs[name].get("ParentName") == "MinifiedThing"
            for name in ("Grechka_PackedSolarS0_Item", "Grechka_PackedSolarS1_Item")),
        "both solar generations need distinct minified definitions")
require(solar.findtext("minifiedDef") == "Grechka_PackedSolarS1_Item"
        and solar.find("designatorDropdown") is None
        and solar.findtext("./comps/li[@Class='CompProperties_Power']/compClass") == "CompPowerPlantSolar"
        and solar.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "-6000",
        "S1 must be a packable six-kilowatt vanilla-style solar plant")
require(solar_cells.findtext("stackLimit") == "100"
        and solar_cell_recipe.findtext("./products/Grechka_SolarCellT2_Item") == "8",
        "T2 solar cells must be a stackable eight-piece product")
cell_inputs = {
    li.findtext("./filter/thingDefs/li"): Decimal(li.findtext("count"))
    for li in solar_cell_recipe.findall("./ingredients/li")
}
require(cell_inputs == {"ComponentIndustrial": Decimal("2"), "Silver": Decimal("0.4"), "Gold": Decimal("0.2")},
        "solar cell recipe must use 2 components, 4 silver and 2 gold at vanilla smallVolume=0.1")
require(solar_recipe.findtext("./products/Grechka_SolarS1_Building") == "1"
        and solar_recipe.findtext("allowMixingIngredients") == "true"
        and {li.text for li in solar_recipe.findall("./ingredients/li[1]/filter/thingDefs/li")}
            == {"SolarGenerator", "Grechka_PackedSolarS0_Item"},
        "S1 recipe must consume a whole packed vanilla solar panel")
other_recipe_inputs = {
    li.findtext("./filter/thingDefs/li"): li.findtext("count")
    for li in solar_recipe.findall("./ingredients/li")[1:]
}
require(other_recipe_inputs == {"Grechka_HousingT2_Item": "1", "ComponentSpacer": "2", "Grechka_SolarCellT2_Item": "8"}
        and {li.tag: li.text for li in solar.findall("./costList/*")}
            == {"Grechka_PackedSolarS0_Item": "1", **other_recipe_inputs},
        "direct S1 construction and workbench assembly must have the same costs")
require({"Grechka_MakeSolarCellT2", "Grechka_MakeSolarS1"}
        <= {li.text for li in t2_bench.findall("./recipes/li")},
        "T2 bench must expose both solar recipes")

solar_s2 = defs["Grechka_SolarS2_Building"]
solar_s2_recipe = defs["Grechka_MakeSolarS2"]
require(defs["Grechka_PackedSolarS2_Item"].get("ParentName") == "MinifiedThing"
        and solar_s2.findtext("minifiedDef") == "Grechka_PackedSolarS2_Item"
        and solar_s2.find("designatorDropdown") is None
        and solar_s2.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "-18000",
        "S2 must be packable, have its own build command and produce up to 18 kW")
require(solar.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_SolarUpgrade']/targetDefName")
        == "Grechka_SolarS2_Building", "installed S1 must offer S2 upgrade")
require(solar_s2_recipe.findtext("./products/Grechka_SolarS2_Building") == "1"
        and solar_s2_recipe.findtext("allowMixingIngredients") == "true"
        and {li.text for li in solar_s2_recipe.findall("./ingredients/li[1]/filter/thingDefs/li")}
            == {"Grechka_SolarS1_Building", "Grechka_PackedSolarS1_Item"},
        "S2 recipe must consume one whole S1 panel")
s2_inputs = {
    li.findtext("./filter/thingDefs/li"): li.findtext("count")
    for li in solar_s2_recipe.findall("./ingredients/li")[1:]
}
require(s2_inputs == {"ComponentSpacer": "2", "Grechka_SolarCellT2_Item": "16", "Plasteel": "20"}
        and {li.tag: li.text for li in solar_s2.findall("./costList/*")}
            == {"Grechka_PackedSolarS1_Item": "1", **s2_inputs}
        and "Grechka_MakeSolarS2" in {li.text for li in t2_bench.findall("./recipes/li")},
        "S2 construction and workbench assembly must share accepted costs")

# New stages must preserve one previous device and credit its housing in both construction
# and bill assembly, with no accidentally free shortcut or new research gate.
t3_recipes = {li.text for li in defs["Grechka_AssemblyBenchT3_Building"].findall("./recipes/li")}
t3_cells = defs["Grechka_MakeSolarCellT3"]
require(ingredients(t3_cells) == {"Grechka_SolarCellT2_Item": 8, "Grechka_Mechanites": 8, "Grechka_ComponentT3_Item": 1}
        and t3_cells.findtext("./products/Grechka_SolarCellT3_Item") == "8"
        and "Grechka_MakeSolarCellT3" in t3_recipes,
        "T3 photoelements must be reachable from the existing T2 cells and mechanite route")
require(defs["Grechka_SolarCellT3_Item"].findtext("stackLimit") == "100",
        "T3 photoelements must remain ordinary stackable production")
base_panels = 1  # One initial S0 is retained throughout the upgrade chain.
for stage, count, housings, components, cells, power, plasteel in [
        (3, 1, 0, 2, 16, 72000, 20), (4, 1, 0, 2, 24, 576000, 40)]:
    panel = defs[f"Grechka_SolarS{stage}_Building"]
    recipe = defs[f"Grechka_MakeSolarS{stage}"]
    packed = f"Grechka_PackedSolarS{stage}_Item"
    previous_packed = f"Grechka_PackedSolarS{stage-1}_Item"
    previous_building = f"Grechka_SolarS{stage-1}_Building"
    expected = {previous_packed: str(count), "Grechka_ComponentT3_Item": str(components), "Grechka_SolarCellT3_Item": str(cells)}
    if stage == 3:
        expected["Grechka_Mechanites"] = "10"
    if plasteel:
        expected["Plasteel"] = str(plasteel)
    actual = {li.findtext("./filter/thingDefs/li"): li.findtext("count")
              for li in recipe.findall("./ingredients/li")[1:]}
    require({li.tag: li.text for li in panel.findall("./costList/*")} == expected
            and {previous_packed: recipe.findtext("./ingredients/li/count"), **actual} == expected
            and {li.text for li in recipe.findall("./ingredients/li[1]/filter/thingDefs/li")}
                == {previous_packed, previous_building},
            f"S{stage} must consume the accepted previous panels and T3 parts through both paths")
    require(panel.findtext("minifiedDef") == packed and defs[packed].get("ParentName") == "MinifiedThing"
            and panel.find("designatorDropdown") is None
            and panel.findtext("./comps/li[@Class='CompProperties_Power']/compClass") == "CompPowerPlantSolar"
            and panel.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == str(-power)
            and panel.findtext("size") == "(4,4)"
            and recipe.findtext("allowMixingIngredients") == "true"
            and recipe.findtext(f"./products/Grechka_SolarS{stage}_Building") == "1"
            and recipe.findtext("defName") in t3_recipes,
            f"S{stage} must be packable, solar-powered, upgradable in the same footprint and buildable on T3")
    require(defs[previous_building].findtext(
        "./comps/li[@Class='Grechka.Industry.CompProperties_SolarUpgrade']/targetDefName") == panel.findtext("defName"),
        f"installed S{stage-1} must offer the next stage while retaining its whole previous panel")
    require(not any(node.findall(tag) for node in (panel, recipe, t3_cells)
                    for tag in ("researchPrerequisite", "researchPrerequisites")),
            f"S{stage} must not acquire a research gate")
    base_panels *= count
require(base_panels == 1, "S4 must retain exactly one original vanilla panel")

print("GrechkaIndustry T1 to T3 and solar S0 to S4 XML routes: OK")
