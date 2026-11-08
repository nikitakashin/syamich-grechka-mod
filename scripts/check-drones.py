#!/usr/bin/env python3
"""Check GrechkaDrones data/profile consistency; does not load or control RimWorld."""
from copy import deepcopy
import json
import math
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = "grechka.drones"
DEPENDENCIES = {"brrainz.harmony", "haplo.miscellaneous.robots"}
ROLES = ("Rescue", "Warrior", "Caravan")
TIERS = range(1, 6)
NAMESPACE = "Grechka.Drones."


def require(condition, message):
    if not condition:
        raise ValueError(message)


def parsed(path, expected):
    result = ET.parse(path).getroot()
    require(result.tag == expected, f"{path}: expected {expected} root")
    return result


def unique(values, context):
    require(all(values), context + ": empty value")
    require(len(values) == len(set(values)), context + ": duplicate values")


def positive(value, context):
    try:
        number = float(value)
    except (ValueError, TypeError):
        raise ValueError(context + ": missing or invalid number") from None
    require(math.isfinite(number) and number > 0, context + ": must be finite and positive")
    return number


def class_body(source, class_name):
    match = re.search(r"\bclass\s+" + re.escape(class_name) + r"\b[^{}]*\{", source)
    require(match is not None, "Missing source class: " + class_name)
    start = end = match.end()
    depth = 1
    while depth and end < len(source):
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    require(depth == 0, "Unclosed source class: " + class_name)
    return source[start:end - 1]


def validate(root):
    """Raise ValueError on inconsistent inputs; root may be an isolated fixture copy."""
    root = Path(root)
    module = root / "mods/GrechkaDrones"
    about = parsed(module / "About/About.xml", "ModMetaData")
    require(about.findtext("packageId") == PACKAGE, "Wrong GrechkaDrones package ID")
    require("1.7" in [n.text for n in about.findall("supportedVersions/li")], "Missing RimWorld 1.7 support")
    dependencies = [n.text.strip().lower() for n in about.findall("modDependencies/li/packageId")]
    unique(dependencies, "About dependencies")
    require(set(dependencies) == DEPENDENCIES, "Only Misc. Robots and Harmony are required; Industry must remain optional")
    after = [n.text.strip().lower() for n in about.findall("loadAfter/li")]
    require(DEPENDENCIES | {"grechka.industry"} <= set(after), "Load order must follow dependencies and optional Industry")

    definitions = ET.Element("Defs")
    by_key = {}
    abstracts = {}
    for path in sorted((module / "Defs").rglob("*.xml")):
        for definition in parsed(path, "Defs"):
            require(isinstance(definition.tag, str), "Invalid definition in " + str(path))
            name = definition.findtext("defName")
            if definition.attrib.get("Abstract", "false").lower() == "true":
                abstract_name = definition.attrib.get("Name")
                require(abstract_name and abstract_name not in abstracts, "Missing or duplicate abstract name")
                abstracts[abstract_name] = definition
            else:
                require(name and re.fullmatch(r"Grechka_[A-Za-z0-9_]+", name), "Missing or invalid stable ID")
                require(not name[-1].isdigit(), "Def IDs must not end with a number: " + name)
                key = (definition.tag, name)
                require(key not in by_key, "Duplicate definition: " + str(key))
                by_key[key] = definition
            definitions.append(definition)

    def inherited(node, path):
        visited = set()
        while node is not None:
            found = node.find(path)
            if found is not None:
                return found
            parent = node.attrib.get("ParentName")
            require(parent not in visited, "Cyclic XML inheritance")
            visited.add(parent)
            node = abstracts.get(parent)
        return None

    def text(node, path):
        found = inherited(node, path)
        return None if found is None else found.text

    def require_node(kind, name):
        node = by_key.get((kind, name))
        require(node is not None, f"Missing {kind}: {name}")
        return node

    source_files = list((module / "Source").rglob("*.cs"))
    require(source_files, "Missing addon C# source")
    source = "\n".join(path.read_text() for path in source_files)
    require(re.search(r"\bnamespace\s+Grechka\.Drones\b", source), "Missing Grechka.Drones namespace")
    classes = set(re.findall(r"\bclass\s+(\w+)\b", source))
    for definition in definitions:
        for node in definition.iter():
            values = [node.attrib.get("Class", "")]
            if node.tag in {"thingClass", "driverClass"}:
                values.append(node.text or "")
            for value in values:
                if value.startswith(NAMESPACE):
                    require(value[len(NAMESPACE):] in classes, "XML references missing C# class: " + value)
    extension_body = class_body(source, "DroneProperties")
    extension_fields = set(re.findall(r"\bpublic\s+[\w.]+\s+(\w+)\s*(?:=[^;]+)?;", extension_body))
    require({"role", "tier", "foamRadius", "foamCooldownTicks"} <= extension_fields,
            "DroneProperties XML fields do not match C# source")
    enum = re.search(r"\benum\s+DroneRole\s*\{([^}]+)\}", source)
    require(enum is not None and all(re.search(r"\b" + role + r"\b", enum.group(1)) for role in ROLES),
            "DroneRole source enum does not contain all three roles")

    expected_units = {f"Grechka_{role}DroneT{tier}_Unit" for role in ROLES for tier in TIERS}
    expected_docks = {f"Grechka_{role}DroneStationT{tier}_Dock" for role in ROLES for tier in TIERS}
    require({name for kind, name in by_key if kind == "ThingDef"} == expected_units | expected_docks,
            "Expected exactly Rescue/Warrior/Caravan T1–T5 units and matching docks")
    require({name for kind, name in by_key if kind == "PawnKindDef"} == expected_units,
            "PawnKind IDs must exactly match unit IDs for Misc. Robots spawning")
    job_ids = {name for kind, name in by_key if kind == "JobDef"}
    required_jobs = {"Grechka_DroneFoam", "Grechka_DroneRetreat"}
    require(required_jobs <= job_ids <= required_jobs | {"Grechka_DroneRescueFloor"},
            "Unexpected or missing custom job IDs")
    require({name for kind, name in by_key if kind == "ThinkTreeDef"} == {"Grechka_DroneMain", "Grechka_DroneConstant", "Grechka_CaravanDroneMain", "Grechka_CaravanDroneConstant"},
            "Unexpected think tree IDs")

    health = {}
    for role in ROLES:
        previous = {}
        for tier in TIERS:
            name = f"Grechka_{role}DroneT{tier}_Unit"
            pawn = require_node("ThingDef", name)
            dock_name = f"Grechka_{role}DroneStationT{tier}_Dock"
            dock = require_node("ThingDef", dock_name)
            kind = require_node("PawnKindDef", name)
            require(text(pawn, "thingClass") == NAMESPACE + ("CaravanDrone" if role == "Caravan" else "Drone"), name + ": wrong pawn class")
            require(text(dock, "thingClass") == NAMESPACE + "DroneStation", dock_name + ": wrong dock class")
            require(kind.findtext("race") == name and dock.findtext("spawnThingDef") == name,
                    name + ": race/PawnKind/dock spawn references differ")
            for item, context in [(pawn, name), (dock, dock_name)]:
                require(text(item, "label") and text(item, "description"), context + ": missing visible text")
                require(float(text(item, "statBases/Flammability")) == 0, context + ": must be fireproof")
            require(text(pawn, "race/thinkTreeMain") == ("Grechka_CaravanDroneMain" if role == "Caravan" else "Grechka_DroneMain") and text(pawn, "race/thinkTreeConstant") == ("Grechka_CaravanDroneConstant" if role == "Caravan" else "Grechka_DroneConstant"),
                    name + ": wrong think tree")
            work = inherited(pawn, "robotWorkTypes")
            require(work is not None and ((role == "Caravan" and [n.findtext("workTypeDef") for n in work] == ["Hauling"]) or (role != "Caravan" and not list(work))), name + ": ordinary work must match role")
            require(work.attrib.get("Inherit", "").lower() == "false", name + ": ordinary work override missing")
            require(not pawn.findall("verbs/li"), name + ": ranged verbs are outside this module's scope")
            exts = pawn.findall("modExtensions/li")
            require(len(exts) == 1 and exts[0].attrib.get("Class") == NAMESPACE + "DroneProperties", name + ": wrong drone extension")
            extension = exts[0]
            tags = [child.tag for child in extension]
            unique(tags, name + " extension fields")
            require(set(tags) <= extension_fields, name + ": unsupported drone extension fields")
            require(extension.findtext("role") == role and int(extension.findtext("tier")) == tier, name + ": role/tier mismatch")
            values = {
                "health": positive(text(pawn, "race/baseHealthScale"), name + " health"),
                "speed": positive(text(pawn, "statBases/MoveSpeed"), name + " speed"),
                "damage": positive(text(pawn, "tools/li/power"), name + " melee damage"),
                "foam": positive(extension.findtext("foamRadius"), name + " foam radius"),
                "power": positive(text(dock, "comps/li/basePowerConsumption"), dock_name + " power"),
            }
            health[(role, tier)] = values["health"]
            positive(text(pawn, "tools/li/cooldownTime"), name + " melee cooldown")
            positive(extension.findtext("foamCooldownTicks"), name + " foam cooldown")
            positive(text(dock, "rechargeEfficiency"), dock_name + " charging efficiency")
            require(text(dock, "minifiedDef") == "AIRobot_MinifiedRechargeStation", dock_name + ": native packed station missing")
            require(text(dock, "recipeMaker/recipeUsers/li") == "FabricationBench", dock_name + ": standalone crafting bench missing")
            for field in ["costList", "robotRepairCosts"]:
                costs = inherited(dock, field)
                require(costs is not None and list(costs), dock_name + ": missing " + field)
                unique([cost.tag for cost in costs], dock_name + " " + field)
                for cost in costs:
                    require(not cost.tag.startswith("Grechka_"), dock_name + ": standalone recipe requires optional Industry")
                    positive(cost.text, dock_name + " " + cost.tag)
            for field, number in values.items():
                if field in previous:
                    require(number >= previous[field], name + ": tier progression decreases " + field)
                previous[field] = number
    require(all(health[("Warrior", tier)] > health[("Rescue", tier)] for tier in TIERS), "Warrior must have more health than corresponding rescue drone")

    for tier in TIERS:
        pawn = require_node("ThingDef", f"Grechka_CaravanDroneT{tier}_Unit")
        require(text(pawn, "race/allowedOnCaravan") == "true" and text(pawn, "race/packAnimal") == "true", "Caravan drone must be caravan-eligible cargo pawn")
        require(float(text(pawn, "statBases/CarryingCapacity")) == 50*tier, "Caravan drone carrying weight must be 50 kg per tier")
        require(text(pawn, "race/foodType") == "OmnivoreHuman", "Caravan drone needs real food")
    caravan_nodes = require_node("ThinkTreeDef", "Grechka_CaravanDroneMain").findall("thinkRoot/subNodes/li")
    require(any(n.findtext("treeDef") == "LordDuty" for n in caravan_nodes), "Caravan assembly must retain native Lord duties")
    require(any(n.attrib.get("Class") == NAMESPACE + "JobGiver_CaravanHaul" for n in caravan_nodes), "Caravan drone must haul")
    main_nodes = require_node("ThinkTreeDef", "Grechka_DroneMain").findall("thinkRoot/subNodes/li")
    require([n.attrib.get("Class") for n in main_nodes] == ["ThinkNode_Subtree", "ThinkNode_Subtree", "Grechka.Drones.JobGiver_DroneRecharge", NAMESPACE + "JobGiver_Drone", "AIRobot.X2_JobGiver_RechargeEnergyIdle", "JobGiver_IdleForever"],
            "Main think tree order differs from dock/role controller")
    require([main_nodes[i].findtext("treeDef") for i in range(2)] == ["Despawned", "Downed"], "Native safety subtrees missing")
    require(not require_node("ThinkTreeDef", "Grechka_DroneConstant").findall("thinkRoot/subNodes/li"), "Constant tree must not override controlled combat/retreat")
    for name, class_name in [("Grechka_DroneFoam", "JobDriver_Foam"), ("Grechka_DroneRetreat", "JobDriver_Retreat")]:
        require(require_node("JobDef", name).findtext("driverClass") == NAMESPACE + class_name, name + ": wrong job driver")

    # Apply the authored optional replacements to a copy. This checks their actual
    # targets and resulting recipes, without loading the game's patch engine.
    patched = deepcopy(definitions)
    patch = parsed(module / "Patches/Industry.xml", "Patch")
    require(len(patch) == 1 and patch[0].attrib.get("Class") == "PatchOperationConditional", "Industry patch must remain conditional")
    conditional = patch[0]
    require(conditional.findtext("xpath") == "Defs/ThingDef[defName='Grechka_HousingT1_Item']", "Industry conditional must test actual optional content")
    require(conditional.find("nomatch") is None, "Standalone branch must not mutate definitions")
    sequence = conditional.find("match")
    require(sequence is not None and sequence.attrib.get("Class") == "PatchOperationSequence", "Industry patch sequence missing")
    changed = set()
    for operation in sequence.find("operations"):
        require(operation.attrib.get("Class") == "PatchOperationReplace", "Unexpected Industry patch operation")
        path = operation.findtext("xpath", "")
        require(path.startswith("Defs/ThingDef[defName='Grechka_"), "Industry patch targets external definitions")
        path = path.removeprefix("Defs/")
        targets = patched.findall(path)
        require(len(targets) == 1, "Industry patch must match one target: " + path)
        require(path not in changed, "Duplicate Industry patch target: " + path)
        changed.add(path)
        replacement = operation.find("value")
        require(replacement is not None and len(replacement) == 1, "Malformed Industry replacement")
        parent = patched.find(path.rsplit("/", 1)[0])
        position = list(parent).index(targets[0]);parent.remove(targets[0]);parent.insert(position, deepcopy(replacement[0]))
    expected_patch_targets = {
        f"ThingDef[defName='{name}']/{field}"
        for name in expected_docks
        for field in ("costList", "robotRepairCosts", "recipeMaker/recipeUsers", "recipeMaker/researchPrerequisites")
    }
    require(changed == expected_patch_targets, "Industry patch must only replace the four recipe/repair fields of every dock")
    industry_names = set()
    for path in (root / "mods/GrechkaIndustry/Defs").rglob("*.xml"):
        industry_names.update(n.findtext("defName") for n in ET.parse(path).getroot() if n.tag == "ThingDef")
    for role in ROLES:
        for tier in TIERS:
            name = f"Grechka_{role}DroneStationT{tier}_Dock"
            dock = patched.find(f"ThingDef[defName='{name}']")
            housing = f"Grechka_HousingT{tier}_Item"
            component = "ComponentIndustrial" if tier == 1 else "ComponentSpacer" if tier == 2 else f"Grechka_ComponentT{tier}_Item"
            bench = f"Grechka_AssemblyBenchT{tier}_Building"
            require(dock.find("costList/" + housing) is not None and dock.find("costList/" + component) is not None,
                    name + ": optional recipe lacks tier housing/component")
            require(dock.findtext("recipeMaker/recipeUsers/li") == bench, name + ": wrong tier workbench")
            require(not dock.findall("recipeMaker/researchPrerequisites/li"), name + ": optional recipe must use material progression")
            for cost in dock.findall("costList/*") + dock.findall("robotRepairCosts/*"):
                positive(cost.text, name + " " + cost.tag)
                require(not cost.tag.startswith("Grechka_") or cost.tag in industry_names, "Unknown optional Industry ingredient: " + cost.tag)
            require(bench in industry_names, "Unknown optional Industry bench: " + bench)

    profile = root / "profiles/GrechkaWorld"
    manifest = json.loads((profile / "mods.json").read_text())
    packages = [entry["package"].lower() for entry in manifest]
    unique(packages, "Profile packages")
    active = [node.text.strip().lower() for node in parsed(profile / "ModsConfig.xml", "ModsConfigData").findall("activeMods/li")]
    unique(active, "Active packages")
    require([package for package in active if not package.startswith("ludeon.")] == packages, "Manifest and active profile differ")
    own = next((entry for entry in manifest if entry["package"].lower() == PACKAGE), None)
    require(own is not None and own.get("source") == "local" and own.get("repositoryPath") == "mods/GrechkaDrones", "Missing/malformed local GrechkaDrones profile entry")
    require({entry["package"].lower() for entry in own.get("dependencies", [])} == DEPENDENCIES, "Profile dependencies differ from About")
    require(all(active.index(dependency) < active.index(PACKAGE) for dependency in DEPENDENCIES), "Addon must load after Misc. Robots/Harmony")
    if "grechka.industry" in active:
        require(active.index("grechka.industry") < active.index(PACKAGE), "Optional Industry must load before addon")
    return "PASS GrechkaDrones: three roles × five tiers; XML IDs/source references, fireproof stats, dock spawning, optional Industry recipes and profile agree. Runtime behavior is not tested by this check."


if __name__ == "__main__":
    try:
        print(validate(ROOT))
    except (ValueError, TypeError, KeyError, ET.ParseError, OSError, AttributeError) as error:
        print("FAIL GrechkaDrones:", error, file=sys.stderr)
        sys.exit(1)
