#!/usr/bin/env python3
"""Audit explicit bills and the T2→T3 supply plan; never loads or controls a save.

The finite-batch model includes physical rounding, tier output carry and leftovers.
It assumes one work-speed-1 crafter at a T2 assembly bench for eight hours/day.
Hauling, construction, research, combat and power interruptions are not simulated.
"""
import argparse
from collections import Counter, defaultdict
from fractions import Fraction
import json
import math
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=Path)
parser.add_argument("--baseline", type=Path, help="balance snapshot directory for before/after comparison")
args = parser.parse_args()


class Economy:
    def __init__(self, baseline=None):
        self.things, self.recipes = {}, {}
        for module in sorted((ROOT / "mods").iterdir()):
            folder = module / "Defs"
            if baseline:
                snapshot = baseline / {"GrechkaIndustry": "industry-before", "GrechkaStorage": "consumers-before/mods/GrechkaStorage"}.get(module.name, "missing") / "Defs"
                if snapshot.is_dir():
                    folder = snapshot
            if not folder.is_dir():
                continue
            for path in sorted(folder.rglob("*.xml")):
                for node in ET.parse(path).getroot():
                    name = node.findtext("defName")
                    if name and node.tag == "RecipeDef":
                        self.recipes[name] = node
                    elif name and node.tag == "ThingDef":
                        self.things[name] = node
        # Exact native physical ingredients; our patch owns the ordinary count,
        # steel/plasteel/gold and work amount in Make_ComponentSpacer.
        patch = ROOT / "mods/GrechkaIndustry/Patches/AdvancedComponent.xml"
        if baseline:
            patch = baseline / "industry-before/Patches/AdvancedComponent.xml"
        advanced = {"ComponentIndustrial": 1, "Steel": 20, "Plasteel": 10, "Gold": 3}
        work = 10000
        for op in ET.parse(patch).getroot().iter("li"):
            xpath = op.findtext("xpath", "")
            if not xpath:
                continue
            if xpath.endswith("/workAmount"):
                work = int(op.findtext("value/workAmount"))
            for material in advanced:
                if f'li="{material}"' in xpath:
                    amount = Fraction(op.findtext("value/count"))
                    advanced[material] = amount * (10 if material in ("Gold", "Silver") else 1)
        self.native_advanced = (advanced, work)
        self.chosen = {"ComponentIndustrial": "Grechka_MakeComponentBatchT1", "ComponentSpacer": "Make_ComponentSpacer"}
        for name, recipe in self.recipes.items():
            if name.startswith("Grechka_Upgrade"):
                continue
            products = recipe.findall("products/*")
            if len(products) == 1:
                self.chosen.setdefault(products[0].tag, name)
        self.chosen.update({f"Grechka_HousingT{t}_Item": f"Grechka_MakeHousingT{t}" for t in range(1, 6)})
        self.chosen.update({f"Grechka_ComponentT{t}_Item": f"Grechka_MakeComponentT{t}" for t in range(3, 6)})
        profile = ROOT / "mods/GrechkaWorldIntegration/Defs/MachineProfile.xml"
        if baseline:
            profile = baseline / "world-before/mods/GrechkaWorldIntegration/Defs/MachineProfile.xml"
        self.world_costs = {}
        for rule in ET.parse(profile).getroot():
            replacement = rule.find("replacementCost")
            if replacement is None or rule.findtext("retired") == "true":
                continue
            cost = {c.findtext("thingDefName"): int(c.findtext("count")) for c in replacement}
            if rule.findtext("addHousing") == "true":
                cost[f"Grechka_HousingT{rule.findtext('minimumTier')}_Item"] = 1
            self.world_costs[rule.findtext("targetDefName")] = cost

    def recipe(self, name):
        if name == "Make_ComponentSpacer":
            return self.native_advanced[0], {"ComponentSpacer": 1}, self.native_advanced[1]
        recipe = self.recipes[name]
        ingredients = {}
        for item in recipe.findall("ingredients/li"):
            names = item.findall("filter/thingDefs/li")
            if not names:
                raise AssertionError(f"Unsupported semantic ingredient in audited chain: {name}")
            material = names[0].text
            ingredients[material] = Fraction(item.findtext("count")) * (10 if material in ("Gold", "Silver") else 1)
        return ingredients, {p.tag: int(p.text) for p in recipe.findall("products/*")}, int(recipe.findtext("workAmount", "0"))

    def cost(self, building):
        if building in self.world_costs:
            return self.world_costs[building]
        return {c.tag: int(c.text) for c in self.things[building].findall("costList/*")}

    def raw(self, material, amount=Fraction(1), path=()):
        assert material not in path, f"Cyclic recipe chain: {path} -> {material}"
        if material not in self.chosen:
            return Counter({material: amount})
        inputs, outputs, _ = self.recipe(self.chosen[material])
        result = Counter()
        for item, count in inputs.items():
            result.update(self.raw(item, amount * count / outputs[material], path + (material,)))
        return result

    def finite_transition(self):
        stock, carry, raw = Counter(), defaultdict(Fraction), Counter()
        work_ticks = Fraction()
        craft_tier = 2

        def make(material, amount, path=()):
            nonlocal work_ticks
            assert material not in path, f"Cyclic finite plan: {material}"
            if material not in self.chosen:
                raw[material] += amount
                return
            while stock[material] < amount:
                recipe_name = self.chosen[material]
                inputs, products, work = self.recipe(recipe_name)
                for item, quantity in inputs.items():
                    multiplier = Fraction(9, 10) if craft_tier == 2 else Fraction(4, 5)
                    make(item, max(1, math.ceil(quantity * multiplier)), path + (material,))
                work_ticks += Fraction(work, 2 if craft_tier == 2 else 5)
                for product, quantity in products.items():
                    key = recipe_name, product
                    bonus = carry[key] + Fraction(quantity, 10 if craft_tier == 2 else 5)
                    extra = math.floor(bonus)
                    carry[key] = bonus - extra
                    stock[product] += quantity + extra
            stock[material] -= amount

        def pay(cost):
            for material, amount in cost.items():
                make(material, amount)

        # Existing T2 bench and working T2 mining infrastructure are prerequisites.
        pay(self.cost("Grechka_MechaniteExtractorT3_Building"))
        ingredients, _, work = self.recipe("Grechka_UpgradeAssemblyBenchT3")
        pay(ingredients)
        work_ticks += Fraction(work, 2)
        first_t3 = {"raw": dict(raw), "crafter_days_8h": float(work_ticks / 20000)}
        craft_tier = 3
        for building in ("Grechka_StorageController", "Grechka_DiskRack", "Grechka_StorageCrafterT3_Building"):
            pay(self.cost(building))
        make("Grechka_Disk_1K_Item", 1)
        assert not any(name.startswith("Grechka_") and name not in {"Grechka_Mechanites", "Grechka_Quartz", "Grechka_CertusQuartz", "Grechka_GrechkaOre"} for name in raw), "A manufactured intermediate was incorrectly treated as raw supply"
        return first_t3, {"raw": dict(raw), "crafter_days_8h": float(work_ticks / 20000), "leftovers": dict(+stock)}


def plain(value):
    if isinstance(value, dict):
        return {key: plain(item) for key, item in value.items()}
    if isinstance(value, Fraction):
        return round(float(value), 4)
    return value


current = Economy()
for t in range(3, 6):
    inputs, outputs, _ = current.recipe(f"Grechka_MakeComponentT{t}")
    previous = "ComponentSpacer" if t == 3 else f"Grechka_ComponentT{t-1}_Item"
    assert inputs[previous] == 1 and outputs[f"Grechka_ComponentT{t}_Item"] == 1
for t in range(1, 6):
    inputs, _, _ = current.recipe(f"Grechka_MakeHousingT{t}")
    component = "ComponentIndustrial" if t == 1 else "ComponentSpacer" if t == 2 else f"Grechka_ComponentT{t}_Item"
    assert inputs[component] == 2
report = {"assumptions": "Explicit canonical recipes, physical rounding, T2 discounts/carry until first T3 bench, then T3 for storage. Existing T2 bench/mining. 8h crafter shift. No hauling/construction/research/power interruptions.",
          "housing_raw_base": {str(t): current.raw(f"Grechka_HousingT{t}_Item") for t in range(1, 6)}}
first, network = current.finite_transition()
report.update(first_t3=first, t3_with_storage=network)
assert first["crafter_days_8h"] <= 5, "The first T3 kit still exceeds five ideal crafter shifts"
processes = ET.parse(ROOT / "mods/GrechkaWorldIntegration/Defs/FactoryDrillProduction.xml").getroot()
report["targeted_t2_daily"] = {p.findtext("results/li/thing"): 60000 / int(p.findtext("ticks")) * 2 * 1.1 * int(p.findtext("results/li/count")) for p in processes}
for material in ("Steel", "Plasteel", "Uranium"):
    # Demand includes a larger storage rollout, all still crafted on T2 (conservative).
    demand = float(network["raw"].get(material, 0))
    report.setdefault("supply_days_one_dedicated_drill_each", {})[material] = demand / report["targeted_t2_daily"][material]
if args.baseline:
    old = Economy(args.baseline)
    report["before_housing_raw_base"] = {str(t): old.raw(f"Grechka_HousingT{t}_Item") for t in range(1, 6)}
    old_first, old_network = old.finite_transition()
    report["before_first_t3"], report["before_t3_with_storage"] = old_first, old_network
report = plain(report)
if args.output:
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n")
print(json.dumps(report, ensure_ascii=False, indent=2))
print("PASS economic chain, finite transition and targeted supply audit")
