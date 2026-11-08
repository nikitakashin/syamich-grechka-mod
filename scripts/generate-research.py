#!/usr/bin/env python3
"""Deterministic Grechka research catalogue. --check detects uncovered/new content."""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PREFIX = "Grechka_Research_"
BRANCHES = {
    "Industry": (1, 5, "Промышленность", "Корпуса, компоненты и сборочные верстаки"),
    "Solar": (2, 5, "Солнечная энергетика", "Солнечные ячейки, панели и их интеграции"),
    "Mecha": (3, 5, "Механитные технологии", "Экстракторы механитов, ионизаторы и мехапоника"),
    "Archo": (4, 5, "Архотехнологии", "Архогенераторы и облучатели"),
    "Matter": (4, 5, "Управление материей", "Переработка и синтез материи"),
    "Storage": (1, 5, "Цифровое хранение", "Сеть, процессоры и диски хранения соответствующей ёмкости"),
    "Autocraft": (3, 5, "Автокрафт", "Сетевые крафтеры"),
    "Equipment": (2, 5, "Оснащение", "Броня, шлемы и боевое оснащение"),
    "Bionics": (4, 5, "Бионика", "Производство собственных искусственных органов"),
    "Chips": (4, 5, "Импланты", "Производство чипов"),
    "Drones": (1, 5, "Робототехника", "Доки дронов-МЧСников, воинов и караванных носильщиков"),
    "Pharmacy": (1, 5, "Фармацевтика", "Лечебные препараты, стимуляторы и фармацевтические реакторы"),
}
PACKAGES = {"GrechkaIndustry": "grechka.industry", "GrechkaStorage": "grechka.storage",
            "GrechkaEquipment": "grechka.equipment", "GrechkaDrones": "grechka.drones",
            "GrechkaPharmacy": "grechka.pharmacy"}


def project_name(branch, tier):
    # Pharmacy keeps the same IDs when a standalone colony adopts this profile.
    return "GrechkaPharmacy_ResearchT" + str(tier) if branch == "Pharmacy" else PREFIX + branch + "T" + str(tier)


def classify(module, name):
    match = re.search(r"T([1-5])(?:_|$)", name)
    tier = int(match[1]) if match else 1
    if module == "GrechkaIndustry":
        if "Solar" in name:
            stage = re.search(r"SolarS([0-6])(?:_|$)", name)
            return "Solar", [2, 2, 2, 3, 3, 4, 5][int(stage[1])] if stage else tier
        if any(x in name for x in ("Mechaponics", "Mechanite", "Ionizer")):
            return "Mecha", max(3, tier)
        if any(x in name for x in ("Emitter", "ArchoGenerator")):
            return "Archo", max(4, tier)
        if "Matter" in name:
            return "Matter", max(4, tier)
        if "ArchoSubstrate" in name:
            return "Industry", 4
        if "Concentrate" in name:
            return "Industry", 5
        if any(x in name for x in ("Housing", "Component", "AssemblyBench")):
            return "Industry", tier
    elif module == "GrechkaStorage":
        if "StorageCrafter" in name:
            return "Autocraft", tier
        capacity = re.search(r"_(1K|4K|16K|64K|256K|512K|1M|2M|4M|8M|16M|64M|256M)(?:_|$)", name)
        if capacity:
            index = ["1K", "4K", "16K", "64K", "256K", "512K", "1M", "2M", "4M", "8M", "16M", "64M", "256M"].index(capacity[1])
            tier = 1 if index <= 2 else 2 if index <= 4 else 3 if index <= 7 else 4 if index <= 10 else 5
        return "Storage", tier
    elif module == "GrechkaEquipment":
        if "Chip" in name:
            return "Chips", tier
        if "Mythic" in name or "Grechka_Grechka" in name:
            return "Bionics", tier
        return "Equipment", 5 if "Cryo" in name or "GoldenWings" in name else tier
    elif module == "GrechkaDrones":
        return "Drones", tier
    elif module == "GrechkaPharmacy":
        trainer = re.search(r"Trainer(Interest|Passion|Strong|Talent|Calling)(?:_Item)?$", name)
        if trainer:
            return "Pharmacy", {"Interest": 2, "Passion": 3, "Strong": 4, "Talent": 5, "Calling": 5}[trainer[1]]
        return "Pharmacy", 4 if name == "GrechkaPharmacy_MakeUltratechMedicine" else tier
    raise ValueError(f"Unclassified content: {module}/{name}")


def catalogue():
    groups = {}
    for module, package in PACKAGES.items():
        for path in sorted((ROOT / "mods" / module / "Defs").rglob("*.xml")):
            for node in ET.parse(path).getroot():
                name = node.findtext("defName")
                if not name or node.tag not in ("ThingDef", "RecipeDef"):
                    continue
                if node.tag == "RecipeDef" and (name.startswith("Install_") or name.startswith("Remove_")):
                    continue  # Found/bought implants remain usable, including removal.
                if node.tag == "ThingDef" and (node.find("projectile") is not None
                        or node.find("race") is not None or "Mineable" in name
                        or name in ("Grechka_GrechkaOre", "Grechka_Quartz", "Grechka_CertusQuartz")):
                    continue  # Natural resources and pawn/projectile internals are not inventions.
                branch, tier = classify(module, name)
                first, last, _, _ = BRANCHES[branch]
                if not first <= tier <= last:
                    raise ValueError(f"Invalid research tier: {name} -> {branch}/{tier}")
                groups.setdefault((branch, tier, package), {"ThingDef": [], "RecipeDef": []})[node.tag].append(name)
    return groups


def add(node, name, text):
    ET.SubElement(node, name).text = str(text)


def render():
    root = ET.Element("Defs")
    tab = ET.SubElement(root, "ResearchTabDef")
    add(tab, "defName", "Grechka_ResearchTab")
    add(tab, "label", "Grechka")
    for row, (branch, (first, last, label, description)) in enumerate(BRANCHES.items()):
        for tier in range(first, last + 1):
            project = ET.SubElement(root, "ResearchProjectDef")
            add(project, "defName", project_name(branch, tier))
            add(project, "label", label + " T" + str(tier))
            content = "Одиночная винтовка «Оса»" if branch == "Equipment" and tier == 2 else description
            add(project, "description", content + f" T{tier}. Открывает производство и строительство; найденные вещи и квестовые награды доступны без исследования.")
            add(project, "baseCost", [0, 400, 1500, 3000, 6000, 10000][tier] if branch == "Industry" else [0, 500, 1000, 2000, 4000, 7000][tier])
            add(project, "techLevel", "Neolithic" if branch == "Industry" and tier == 1 else "Industrial" if tier <= 2 else "Spacer" if tier <= 4 else "Ultra")
            add(project, "tab", "Grechka_ResearchTab")
            # Native coordinates are 190px horizontally / 100px vertically.
            # Keep labels at normal size, with ~40px tier gaps / >=9px row gaps.
            # Archo has only T4/T5; share the unused left side of Mecha's row.
            # This leaves a full Pharmacy row inside the native fixed-height canvas.
            view_x = (tier - 1) * .95 + (0 if branch == "Industry" else .85)
            view_row = row if row < 3 else row - 1
            if branch == "Archo":
                view_x = .85 + (tier - 4) * .95
                view_row = 2
            add(project, "researchViewX", round(view_x, 2))
            add(project, "researchViewY", round(view_row * .65, 2))
            deps = []
            # Equipment T2 is a new side entry; preserve the existing T3 prerequisite path.
            if tier > first and not (branch == "Equipment" and tier == 3):
                deps.append(project_name(branch, tier - 1))
            if branch != "Industry":
                deps.append(PREFIX + "IndustryT" + str(tier))
            if branch == "Industry" and tier == 2:
                deps.append("Electricity")
            if branch == "Industry" and tier == 3:
                deps.append("Fabrication")
            if branch == "Storage" and tier == 1:
                deps.append("MicroelectronicsBasics")
            if branch == "Drones" and tier == 1:
                deps.append("MicroelectronicsBasics")
            if branch == "Autocraft":
                deps.append(PREFIX + "StorageT" + str(tier))
            if branch in ("Chips", "Bionics"):
                deps.append(PREFIX + "EquipmentT" + str(tier))
            if deps:
                parent = ET.SubElement(project, "prerequisites")
                for dep in deps:
                    add(parent, "li", dep)
    for (branch, tier, package), entries in sorted(catalogue().items()):
        binding = ET.SubElement(root, "Grechka.WorldIntegration.ResearchUnlockDef")
        add(binding, "defName", PREFIX + "Unlock_" + branch + "T" + str(tier))
        add(binding, "packageId", package)
        add(binding, "research", project_name(branch, tier))
        for tag, field in (("ThingDef", "things"), ("RecipeDef", "recipes")):
            if entries[tag]:
                parent = ET.SubElement(binding, field)
                for name in sorted(entries[tag]):
                    add(parent, "li", name)
    ET.indent(root, space="  ")
    return '<?xml version="1.0" encoding="utf-8"?>\n' + ET.tostring(root, encoding="unicode") + "\n"


if __name__ == "__main__":
    path = ROOT / "mods/GrechkaWorldIntegration/Defs/Research.xml"
    content = render()
    if "--check" in sys.argv:
        if not path.exists() or path.read_text() != content:
            sys.exit("Research catalogue is stale: run scripts/generate-research.py")
        print("Grechka research catalogue covers current content: OK")
    else:
        path.write_text(content)
        print(path)
