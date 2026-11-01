#!/usr/bin/env python3
"""Check the T2 -> mechanites -> component T3 -> housing T3 startup path."""
from pathlib import Path
import sys
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1] / "mods" / "GrechkaIndustry"
defs = {node.findtext("defName"): node for path in (root / "Defs").rglob("*.xml")
        for node in ET.parse(path).getroot() if node.findtext("defName")}

def require(ok, message):
    if not ok:
        sys.exit(message)

def inputs(node):
    return {li.findtext("./filter/thingDefs/li"): int(li.findtext("count"))
            for li in node.findall("./ingredients/li")}

# These are actual ThingDef.ConfigErrors reported by the first in-game load.
# BenchBase supplies its category/mass; our direct BuildingBase children do not.
for name, node in defs.items():
    if node.tag != "ThingDef":
        continue
    require(not name[-1].isdigit(), "ThingDef names must not end with digits: " + name)
    if node.findtext("minifiedDef") and node.get("ParentName") == "BuildingBase":
        require(node.find("./thingCategories/li") is not None
                and node.findtext("./statBases/Mass") is not None,
                "minifiable buildings need an authored category and mass: " + name)
    if node.findtext("passability") == "Impassable" and node.findtext("designationCategory"):
        require(float(node.findtext("fillPercent", "0")) >= 1,
                "impassable player buildings must block sight/shots: " + name)
for folder in (root / "Defs", root / "Patches"):
    for path in folder.rglob("*.xml"):
        for node in ET.parse(path).iter():
            for candidate in (node.tag, (node.text or "").strip()):
                if re.fullmatch(r"Grechka_[A-Za-z0-9_]+", candidate):
                    require(candidate in defs, "Unresolved internal def reference: " + candidate)

extractor = defs["Grechka_MechaniteExtractorT3_Building"]
cost = {li.tag: int(li.text) for li in extractor.findall("./costList/*")}
require(cost == {"Grechka_HousingT2_Item": 1, "ComponentSpacer": 8, "Plasteel": 40, "Uranium": 20},
        "first extractor must be reachable with T2 materials, without its own product")
require(extractor.findtext("tickerType") == "Normal"
        and extractor.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "2000",
        "extractor must tick and consume its configured 2 kW")
producer = extractor.find("./comps/li[@Class='Grechka.Industry.CompProperties_MechaniteExtractor']")
require(producer is not None and producer.findtext("product") == "Grechka_Mechanites"
        and producer.findtext("cycleTicks") == "2500" and producer.findtext("batchCount") == "5",
        "extractor startup balance or product is missing")
component = defs["Grechka_MakeComponentT3"]
housing = defs["Grechka_MakeHousingT3"]
require(inputs(component) == {"ComponentSpacer": 4, "Plasteel": 20, "Uranium": 10, "Grechka_Mechanites": 25}
        and component.findtext("./products/Grechka_ComponentT3_Item") == "1", "accepted T3 component route changed")
require(inputs(housing) == {"Grechka_HousingT2_Item": 1, "Plasteel": 60, "Grechka_ComponentT3_Item": 8, "Grechka_Mechanites": 20}
        and housing.findtext("./products/Grechka_HousingT3_Item") == "1", "T3 housing chain is broken")
available = {li.text for li in defs["Grechka_AssemblyBenchT2_Building"].findall("./recipes/li")}
require({"Grechka_MakeComponentT3", "Grechka_MakeHousingT3"} <= available,
        "first T3 parts must be produced on T2 equipment")
require(all(not node.findall("researchPrerequisite") for node in (component, housing)),
        "T3 startup must not acquire a new research gate")
bench = defs["Grechka_AssemblyBenchT3_Building"]
require(bench.findtext("designatorDropdown") == "Grechka_AssemblyBenchGroup"
        and bench.findtext("tickerType") == "Normal"
        and bench.findtext("./statBases/WorkTableWorkSpeedFactor") == "5"
        and bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/inputDiscount") == "0.2"
        and bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/outputBonus") == "0.2"
        and bench.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "840",
        "T3 workbench must keep the accepted global tier multipliers")
upgrade = defs["Grechka_UpgradeAssemblyBenchT3"]
expected = {li.tag: int(li.text) for li in bench.findall("./costList/*") if li.tag != "Grechka_HousingT3_Item"}
for name, count in inputs(housing).items():
    if name != "Grechka_HousingT2_Item":
        expected[name] = expected.get(name, 0) + count
require(inputs(upgrade) == expected and "Grechka_UpgradeAssemblyBenchT3" in available,
        "T2 to T3 upgrade must credit its installed housing once")
require(defs["Grechka_AssemblyBenchT2_Building"].findtext(
    "./comps/li[@Class='Grechka.Industry.CompProperties_AssemblyUpgrade']/targetBenchName")
        == "Grechka_AssemblyBenchT3_Building", "T2 upgrade must target the real T3 workbench")
mechaponics = defs["Grechka_MechaponicsT3_Building"]
require(mechaponics.findtext("thingClass") == "Grechka.Industry.Building_Mechaponics"
        and mechaponics.findtext("size") == "(1,1)"
        and mechaponics.findtext("tickerType") == "Rare"
        and mechaponics.findtext("fertility") == "1"
        and mechaponics.findtext("./building/sowTag") == "Ground",
        "mechaponics must use one cell and neutral fertility with normal ground crops")
require(mechaponics.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "500"
        and mechaponics.findtext("./modExtensions/li[@Class='Grechka.Industry.MechaponicsProperties']/growthMultiplier") == "5"
        and mechaponics.find("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']") is None
        and mechaponics.find("./comps/li[@Class='CompProperties_Glower']") is None,
        "T3 must apply exactly x5/500W with internal light, without another tier or room-light bonus")
require({li.tag: int(li.text) for li in mechaponics.findall("./costList/*")}
        == {"Grechka_HousingT3_Item": 1, "Grechka_ComponentT3_Item": 2, "Plasteel": 30, "Grechka_Mechanites": 10}
        and mechaponics.findtext("designatorDropdown") == "Grechka_MechaponicsGroup"
        and not mechaponics.findall("researchPrerequisites"),
        "mechaponics must be reachable from the T3 route without a new research gate")
require((root / "Textures" / "Buildings" / "MechaponicsT3.png").is_file(), "mechaponics texture missing")
print("GrechkaIndustry reachable T3 mechanite startup: OK")
