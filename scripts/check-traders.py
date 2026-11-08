#!/usr/bin/env python3
"""Validate GrechkaTraders catalogue, translations and profile without loading RimWorld."""
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / "mods/GrechkaTraders"
SPECIALIST_TYPE = "Grechka.Traders.SpecialistDef"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def parse(path):
    return ET.parse(path).getroot()


def unique(values, context):
    require(all(values), context + ": empty value")
    require(len(values) == len(set(values)), context + ": duplicate value")


def fields_for(class_name):
    """Read declared XML field names, without asserting runtime method implementation."""
    source = (MODULE / "Source/Specialists.cs").read_text()
    match = re.search(r"\bclass\s+" + re.escape(class_name) + r"\b[^{}]*\{", source)
    require(match is not None, "Missing runtime XML class: " + class_name)
    start = match.end()
    depth = 1
    end = start
    while depth and end < len(source):
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    require(depth == 0, "Unclosed runtime class: " + class_name)
    # Public instance fields are the XML surface. Properties and methods do not match.
    return set(re.findall(r"\bpublic\s+[\w.]+(?:<[^>]+>)?\s+(\w+)\s*(?:=[^;]+)?;", source[start:end - 1]))


def child_tags(node, allowed, context):
    tags = [child.tag for child in node]
    unique(tags, context + " fields")
    unknown = set(tags) - allowed
    require(not unknown, context + ": unsupported XML fields " + str(sorted(unknown)))


def text_list(node, field, context):
    group = node.find(field)
    if group is None:
        return []
    require(all(child.tag == "li" and not list(child) for child in group), context + ": malformed " + field)
    values = [(child.text or "").strip() for child in group]
    unique(values, context + " " + field)
    return values


def boolean(node, field, context):
    value = node.findtext(field)
    require(value in ("true", "false"), context + ": invalid " + field)
    return value == "true"


def translations(folder):
    entries = {}
    for path in sorted((MODULE / "Languages/Russian/DefInjected" / folder).glob("*.xml")):
        root = parse(path)
        require(root.tag == "LanguageData", str(path) + ": expected LanguageData")
        for child in root:
            require(child.tag not in entries, "Duplicate Russian translation: " + child.tag)
            require((child.text or "").strip(), "Empty Russian translation: " + child.tag)
            entries[child.tag] = child.text
    return entries


def check():
    manifest = json.loads((ROOT / "profiles/GrechkaWorld/mods.json").read_text())
    packages = [entry["package"].lower() for entry in manifest]
    unique(packages, "Profile manifest packages")
    active = [node.text.strip().lower() for node in parse(ROOT / "profiles/GrechkaWorld/ModsConfig.xml").findall("activeMods/li")]
    unique(active, "Active profile packages")
    require([package for package in active if not package.startswith("ludeon.")] == packages,
            "Profile ModsConfig and manifest order/content differ")
    own_entry = next((entry for entry in manifest if entry["package"] == "grechka.traders"), None)
    require(own_entry is not None and own_entry.get("source") == "local" and own_entry.get("repositoryPath") == "mods/GrechkaTraders",
            "GrechkaTraders local profile entry is missing or malformed")
    about = parse(MODULE / "About/About.xml")
    require(about.findtext("packageId") == "grechka.traders", "About package mismatch")
    required = [node.text.strip().lower() for node in about.findall("modDependencies/li/packageId")]
    require(required == ["brrainz.harmony"], "Specialist content mods must remain optional dependencies")
    manifest_required = [entry["package"].lower() for entry in own_entry.get("dependencies", [])]
    require(manifest_required == required, "Manifest and About dependencies differ")
    require(about.findtext("supportedVersions/li") == "1.7", "RimWorld 1.7 support is missing")

    spec_root = parse(MODULE / "Defs/Specialists.xml")
    require(spec_root.tag == "Defs" and all(node.tag == SPECIALIST_TYPE for node in spec_root), "Malformed specialist root/type")
    require(len(spec_root) == 16, "Expected 16 accepted specialist profiles")
    allowed = fields_for("SpecialistDef") | {"defName", "label", "description"}
    extension_fields = fields_for("SpecialistExtension")
    stock_fields = fields_for("StockGenerator_Specialist")
    require("specialist" in extension_fields and "specialist" in stock_fields, "Trader XML reference field is missing from runtime classes")
    names = [node.findtext("defName") for node in spec_root]
    unique(names, "Specialist IDs")
    specs = dict(zip(names, spec_root))
    covered = set()
    curated_assignments = []
    for name, spec in specs.items():
        child_tags(spec, allowed, name)
        require(name.startswith("GrechkaTrader_"), "Unexpected specialist ID: " + name)
        require(spec.findtext("label") and spec.findtext("description"), name + ": missing English text")
        owners = text_list(spec, "packageIds", name)
        require(owners and all(owner == owner.lower() and owner in packages for owner in owners), name + ": owner absent from current profile or package casing mismatch")
        covered.update(owners)
        curated = text_list(spec, "curatedPackageIds", name)
        require(all(owner == owner.lower() and owner in packages for owner in curated), name + ": curated owner absent from current profile or package casing mismatch")
        covered.update(curated)
        curated_assignments.extend((name, owner) for owner in curated)
        text_list(spec, "extraThingDefs", name)
        require(1 <= int(spec.findtext("offers")) <= 50, name + ": offers outside runtime bounds")
        require(0 <= float(spec.findtext("discount")) <= .3, name + ": discount outside runtime bounds")
        require(boolean(spec, "caravan", name) and boolean(spec, "orbital", name), name + ": catalogue requires both trader variants")

    # A curated module has explicit thematic goods, rather than donating its whole
    # catalogue to every specialist. Validate the real module's patches as well as
    # inline stock so an empty classification cannot conceal unsupported content.
    for name, owner in curated_assignments:
        module_entry = next(entry for entry in manifest if entry["package"] == owner)
        require(module_entry.get("source") == "local" and module_entry.get("repositoryPath"),
                name + ": curated module needs its canonical local source")
        source = ROOT / module_entry["repositoryPath"]
        owned_defs = {node.findtext("defName") for path in (source / "Defs").rglob("*.xml")
                      for node in parse(path) if node.tag == "ThingDef" and node.findtext("defName")}
        whitelist = set(text_list(specs[name], "extraThingDefs", name))
        target = "Defs/Grechka.Traders.SpecialistDef[defName='" + name + "']/extraThingDefs"
        for path in (source / "Patches").rglob("*.xml"):
            for operation in parse(path).iter():
                if operation.findtext("xpath") == target:
                    whitelist.update(node.text for node in operation.findall("value/li"))
        selected = whitelist & owned_defs
        require(selected, name + ": curated module " + owner + " has no real explicit stock")
        require(selected != owned_defs, name + ": curated ownership must preserve a selective thematic catalogue")

    # Every local Grechka module must be classified. New item modules cannot silently
    # miss support; UI/quest/profile modules intentionally have no stock of their own.
    no_stock = {"grechka.kite", "grechka.quests", "grechka.world.integration", "grechka.cameraplus", "grechka.speedplus", "grechka.traders",
                "grechka.achivements", "grechka.destroyitem", "grechka.factions", "grechka.farms", "grechka.tweaks"}
    own_modules = {entry["package"] for entry in manifest if entry["package"].startswith("grechka.")}
    require(own_modules <= covered | no_stock, "Unclassified Grechka modules: " + str(sorted(own_modules - covered - no_stock)))
    require({"grechka.industry", "grechka.storage", "grechka.equipment", "grechka.luckyblocks"} <= covered,
            "A Grechka item module is missing its specialist")

    trader_root = parse(MODULE / "Defs/TraderKinds.xml")
    require(trader_root.tag == "Defs" and len(trader_root) == 32 and all(node.tag == "TraderKindDef" for node in trader_root),
            "Expected 32 TraderKindDefs")
    trader_names = [node.findtext("defName") for node in trader_root]
    unique(trader_names, "Trader IDs")
    expected_names = {name + suffix for name in names for suffix in ("_Caravan", "_Orbital")}
    require(set(trader_names) == expected_names, "Trader variants do not match specialist IDs")
    for trader in trader_root:
        name = trader.findtext("defName")
        child_tags(trader, {"defName", "label", "orbital", "commonality", "requestable", "stockGenerators", "modExtensions"}, name)
        orbital = boolean(trader, "orbital", name)
        require(orbital == name.endswith("_Orbital"), name + ": wrong orbital flag")
        require(trader.findtext("label"), name + ": missing label")
        require(0 < float(trader.findtext("commonality")) <= 1, name + ": unreasonable commonality")
        if orbital:
            require(trader.findtext("requestable", "false") == "false", name + ": orbital trader cannot be requested as a caravan")
        else:
            require(boolean(trader, "requestable", name), name + ": caravan request disabled")
        stock = trader.findall("stockGenerators/li")
        require(len(stock) == 2, name + ": expected silver and thematic stock generators")
        silver, thematic = stock
        require(silver.attrib.get("Class") == "StockGenerator_SingleDef" and silver.findtext("thingDef") == "Silver", name + ": missing silver generator")
        child_tags(silver, {"thingDef", "countRange"}, name + " silver")
        low, high = [int(part) for part in silver.findtext("countRange").split("~")]
        require(0 < low <= high, name + ": invalid silver range")
        require(thematic.attrib.get("Class") == "Grechka.Traders.StockGenerator_Specialist", name + ": wrong stock runtime class")
        child_tags(thematic, stock_fields, name + " stock")
        specialist = thematic.findtext("specialist")
        require(specialist in specs and name.startswith(specialist + "_"), name + ": stock specialist reference mismatch")
        extensions = trader.findall("modExtensions/li")
        require(len(extensions) == 1 and extensions[0].attrib.get("Class") == "Grechka.Traders.SpecialistExtension", name + ": wrong extension class")
        child_tags(extensions[0], extension_fields, name + " extension")
        require(extensions[0].findtext("specialist") == specialist, name + ": extension and stock point to different specialists")

    spec_translations = translations(SPECIALIST_TYPE)
    expected_spec_keys = {name + suffix for name in names for suffix in (".label", ".description")}
    require(set(spec_translations) == expected_spec_keys, "Russian specialist translations do not match catalogue")
    trader_translations = translations("TraderKindDef")
    require(set(trader_translations) == {name + ".label" for name in trader_names}, "Russian trader translations do not match catalogue")
    print(f"PASS GrechkaTraders: {len(specs)} specialists, {len(trader_names)} trader kinds; XML fields/references, RU text, optional dependencies, {len(covered)} supported packages and profile agree.")


if __name__ == "__main__":
    try:
        check()
    except (ValueError, TypeError, KeyError, ET.ParseError, OSError) as error:
        print("FAIL GrechkaTraders:", error, file=sys.stderr)
        sys.exit(1)
