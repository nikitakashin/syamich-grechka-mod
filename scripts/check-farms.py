#!/usr/bin/env python3
"""Validate the standalone Farms module and collection registration without starting the game."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET
repo = Path(__file__).resolve().parents[1]
mod = repo / 'mods/GrechkaFarms'
about = ET.parse(mod / 'About/About.xml').getroot()
assert about.findtext('packageId') == 'grechka.farms'
assert about.findtext('name') == 'Grechka Farms'
assert not about.findall('modDependencies/li')
terrain = ET.parse(mod / 'Defs/Terrain.xml').find('TerrainDef')
assert terrain.findtext('defName') == 'Grechka_Farm'
assert float(terrain.findtext('fertility')) == 2.28
assert terrain.findtext('designationCategory') == 'Grechka_IndustryCategory'
assert terrain.findtext('constructionSkillPrerequisite') == terrain.findtext('artisticSkillPrerequisite') == '0'
assert 0 < float(terrain.findtext('statBases/WorkToBuild')) < 100
assert not terrain.findall('costList/*') and not terrain.findall('researchPrerequisites/*')
assert terrain.findtext('affordances/li') == 'GrowSoil'
for locale in ('Russian', 'English'):
 keys = ET.parse(mod / f'Languages/{locale}/Keyed/Farms.xml').getroot()
 assert {key.tag for key in keys} == {'GrechkaFarms_Fertility', 'GrechkaFarms_Help', 'GrechkaFarms_Reset'}
 assert '{0}' in keys.findtext('GrechkaFarms_Fertility')
base = ET.parse(repo/'mods/GrechkaIndustry/Defs/DesignationCategories.xml').find('DesignationCategoryDef')
fallback = ET.parse(mod/'Patches/ArchitectCategory.xml').find('Operation/nomatch/value/DesignationCategoryDef')
assert ET.canonicalize(ET.tostring(base,encoding='unicode'),strip_text=True) == ET.canonicalize(ET.tostring(fallback,encoding='unicode'),strip_text=True)
profile = repo / 'profiles/GrechkaWorld'
active = [e.text for e in ET.parse(profile/'ModsConfig.xml').findall('activeMods/li')]
assert active.count('grechka.farms') == 1
entries = [e for e in json.loads((profile/'mods.json').read_text()) if e['package'] == 'grechka.farms']
assert len(entries) == 1 and entries[0]['repositoryPath'] == 'mods/GrechkaFarms'
assert not entries[0]['dependencies']
print('PASS Farms metadata, terrain, translations, standalone category and collection profile.')
