#!/usr/bin/env python3
"""Validate dedicated Architect groups without touching the running game."""
from pathlib import Path
import xml.etree.ElementTree as ET
repo=Path(__file__).resolve().parents[1]
categories={}
buildables=[]
for module in (repo/'mods').iterdir():
 for path in (module/'Defs').rglob('*.xml'):
  for node in ET.parse(path).getroot():
   if node.tag=='DesignationCategoryDef':
    name=node.findtext('defName')
    assert name not in categories,name
    categories[name]=node
   if node.findtext('designationCategory'):
    buildables.append((node.findtext('defName'),node.findtext('designationCategory')))
for name,category in buildables:
 assert name.startswith(('Grechka_', 'GrechkaPharmacy_', 'GrechkaTreasures_')),name
 assert category in categories,(name,category)
 assert category.startswith('Grechka_'),(name,category)
for name,node in categories.items():
 label=node.findtext('label')
 assert 'Grechka' not in label and len(label)<=16,(name,label)
 assert node.find('icon') is None and node.find('iconPath') is None,'Unsupported DesignationCategoryDef icon field'
assert set(categories)=={'Grechka_IndustryCategory','Grechka_SolarCategory'},set(categories)
solar_category=categories['Grechka_SolarCategory']
assert solar_category.findtext('label')=='Солнечные панели'
assert solar_category.findtext('showPowerGrid')=='true'
assert int(solar_category.findtext('order'))==int(categories['Grechka_IndustryCategory'].findtext('order'))+1
category=categories['Grechka_IndustryCategory']
assert category.findtext('label')=='Гречка'
assert category.findtext('showPowerGrid')=='true'
for path in ('GrechkaStorage/Patches/ArchitectCategory.xml','GrechkaQuests/Patches/RewardMarker.xml','GrechkaPharmacy/Patches/ArchitectCategory.xml','GrechkaTreasures/Patches/ArchitectCategory.xml'):
 fallback=ET.parse(repo/'mods'/path).find('Operation/nomatch/value/DesignationCategoryDef')
 assert fallback is not None,path
 assert ET.canonicalize(ET.tostring(fallback,encoding='unicode'),strip_text=True)==ET.canonicalize(ET.tostring(category,encoding='unicode'),strip_text=True),path
marker=ET.parse(repo/'mods/GrechkaQuests/Patches/RewardMarker.xml').getroot()[-1]
assert marker.findtext('nomatch/value/li')=='Grechka.Quests.Designator_RewardMarker'
assert marker.findtext('nomatch/xpath')=="Defs/DesignationCategoryDef[defName='Grechka_IndustryCategory']/specialDesignatorClasses"
for module in ('GrechkaIndustry','GrechkaStorage','GrechkaQuests','GrechkaWorldIntegration'):
 assert (repo/'mods'/module/'Textures/UI/GrechkaBrand/Grain.png').is_file()
extractors={}
for path in (repo/'mods/GrechkaIndustry/Defs').rglob('*.xml'):
 for node in ET.parse(path).getroot():
  name=node.findtext('defName','')
  if name in {f'Grechka_MechaniteExtractorT{tier}_Building' for tier in (3,4,5)}:
   extractors[name]=node.findtext('designatorDropdown')
assert len(extractors)==3 and set(extractors.values())=={'Grechka_MechaniteExtractorGroup'},extractors
groups=ET.parse(repo/'mods/GrechkaIndustry/Defs/DesignatorDropdownGroups.xml')
assert groups.find("DesignatorDropdownGroupDef[defName='Grechka_MechaniteExtractorGroup']") is not None
panels=[]
for path in (repo/'mods/GrechkaIndustry/Defs').rglob('*.xml'):
 for node in ET.parse(path).getroot():
  name=node.findtext('defName','')
  if node.tag!='ThingDef' or not name.startswith('Grechka_Solar') or not name.endswith('_Building'):
   continue
  assert node.findtext('designationCategory')=='Grechka_SolarCategory',name
  assert node.find('designatorDropdown') is None,name
  power=-int(node.findtext("comps/li[@Class='CompProperties_Power']/basePowerConsumption"))
  panels.append((int(node.findtext('uiOrder')),power,name))
assert len(panels)==19,len(panels)
assert len({order for order,_,_ in panels})==19,'Solar commands need a stable total order'
assert [power for _,power,_ in sorted(panels)]==sorted(power for _,power,_ in panels),'Solar power must increase across the tab'
assert min(order for order,_,_ in panels)>0,'Vanilla S0 reserves the first position'
print(f'PASS Architect: {len(buildables)} explicit own buildings, {len(categories)} concise branded groups, standalone icon resources and reward marker routing.')
