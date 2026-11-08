#!/usr/bin/env python3
"""Keep natural ore veins on the tinted vanilla linked-rock renderer."""
from pathlib import Path
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
ores = [
    ('GrechkaIndustry', 'Buildings_GrechkaOre.xml', 'Grechka_MineableGrechka', '(150,97,47)', 'GrechkaOreSource', 'Grechka_GrechkaOre', '25'),
    ('GrechkaStorage', 'Buildings_QuartzOre.xml', 'Grechka_MineableQuartz', '(237,231,216)', 'QuartzOreSource', 'Grechka_Quartz', '35'),
    ('GrechkaStorage', 'Buildings_Certus.xml', 'Grechka_MineableCertus', '(100,190,245)', 'CertusOreSource', 'Grechka_CertusQuartz', '15'),
]
for module, filename, name, fleck, icon, product, amount in ores:
    ore = next(n for n in ET.parse(repo / 'mods' / module / 'Defs/ThingDefs' / filename).getroot()
               if n.findtext('defName') == name)
    graphic = ore.find('graphicData')
    assert graphic.get('Inherit') == 'False', name
    assert graphic.findtext('texPath') == 'Things/Building/Linked/RockFlecked_Atlas', name
    assert graphic.findtext('graphicClass') == 'Graphic_Single', name
    assert graphic.findtext('linkType') == 'CornerFiller', 'Native ore needs native corner fill: ' + name
    assert {n.text for n in graphic.findall('linkFlags/li')} == {'Rock', 'MapEdge'}, name
    assert graphic.findtext('shaderType') == 'CutoutComplex', 'Native mask needs two-colour shader: ' + name
    assert graphic.findtext('color') == '(128,122,116)' and graphic.findtext('colorTwo') == fleck, name
    assert ore.findtext('uiIconPath') == 'Buildings/Natural/' + icon, name
    assert ore.findtext('building/mineableThing') == product and ore.findtext('building/mineableYield') == amount, name
print('Grechka, quartz and certus veins: tinted vanilla ore, natural-rock/map-edge links, original yields and IDs: OK')
