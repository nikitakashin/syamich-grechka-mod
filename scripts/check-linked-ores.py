#!/usr/bin/env python3
"""Reject opaque source tiles wired as standalone natural ore graphics."""
from pathlib import Path
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
ore = ET.parse(repo / "mods/GrechkaStorage/Defs/ThingDefs/Buildings_QuartzOre.xml").getroot().find("ThingDef")
graphic = ore.find("graphicData")
assert graphic.findtext("texPath") == "Things/Building/Linked/RockFlecked_Atlas", "Quartz requires a real linked-rock atlas"
assert graphic.findtext("linkType") == "CornerFiller", "Natural ore must retain the rock corner filler"
assert {n.text for n in graphic.findall("./linkFlags/li")} == {"Rock", "MapEdge"}, "Quartz must join neighbouring natural rock and map edges"
assert graphic.findtext("shaderType") == "CutoutComplex" and graphic.findtext("colorTwo"), "Mineral flecks require the vanilla two-colour rock material"
assert ore.findtext("uiIconPath") == "Buildings/Natural/QuartzOreSource", "Keep the prepared source art as the menu icon"
print("Quartz uses a real connected ore atlas with natural-rock and map-edge links: OK")
