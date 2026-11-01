#!/usr/bin/env python3
"""Check the T2 -> mechanites -> component T3 -> housing T3 startup path."""
from pathlib import Path
import sys
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

extractor = defs["Grechka_MechaniteExtractorT3"]
cost = {li.tag: int(li.text) for li in extractor.findall("./costList/*")}
require(cost == {"Grechka_HousingT2": 1, "ComponentSpacer": 8, "Plasteel": 40, "Uranium": 20},
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
        and component.findtext("./products/Grechka_ComponentT3") == "1", "accepted T3 component route changed")
require(inputs(housing) == {"Grechka_HousingT2": 1, "Plasteel": 60, "Grechka_ComponentT3": 8, "Grechka_Mechanites": 20}
        and housing.findtext("./products/Grechka_HousingT3") == "1", "T3 housing chain is broken")
available = {li.text for li in defs["Grechka_AssemblyBenchT2"].findall("./recipes/li")}
require({"Grechka_MakeComponentT3", "Grechka_MakeHousingT3"} <= available,
        "first T3 parts must be produced on T2 equipment")
require(all(not node.findall("researchPrerequisite") for node in (component, housing)),
        "T3 startup must not acquire a new research gate")
bench = defs["Grechka_AssemblyBenchT3"]
require(bench.findtext("designatorDropdown") == "Grechka_AssemblyBenchGroup"
        and bench.findtext("tickerType") == "Normal"
        and bench.findtext("./statBases/WorkTableWorkSpeedFactor") == "5"
        and bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/inputDiscount") == "0.2"
        and bench.findtext("./comps/li[@Class='Grechka.Industry.CompProperties_TierBonuses']/outputBonus") == "0.2"
        and bench.findtext("./comps/li[@Class='CompProperties_Power']/basePowerConsumption") == "840",
        "T3 workbench must keep the accepted global tier multipliers")
upgrade = defs["Grechka_UpgradeAssemblyBenchT3"]
expected = {li.tag: int(li.text) for li in bench.findall("./costList/*") if li.tag != "Grechka_HousingT3"}
for name, count in inputs(housing).items():
    if name != "Grechka_HousingT2":
        expected[name] = expected.get(name, 0) + count
require(inputs(upgrade) == expected and "Grechka_UpgradeAssemblyBenchT3" in available,
        "T2 to T3 upgrade must credit its installed housing once")
require(defs["Grechka_AssemblyBenchT2"].findtext(
    "./comps/li[@Class='Grechka.Industry.CompProperties_AssemblyUpgrade']/targetBenchName")
        == "Grechka_AssemblyBenchT3", "T2 upgrade must target the real T3 workbench")
print("GrechkaIndustry reachable T3 mechanite startup: OK")
