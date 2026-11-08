#!/usr/bin/env python3
"""Check that explicit World rules preserve the accepted G01 catalogue and exceptions."""
from pathlib import Path
import xml.etree.ElementTree as E, re
root=Path(__file__).resolve().parents[1]
r=E.parse(root/'mods/GrechkaWorldIntegration/Defs/MachineProfile.xml').getroot()
rules={d.findtext('targetDefName'):d for d in r};assert len(rules)==len(r)==334
catalog=[d for d in r if d.findtext('defName','').startswith('GrechkaWorld_G01_')]
assert len(catalog)==328
for d in r:
 assert d.findtext('packageId') and d.findtext('targetDefName')
 tier=int(d.findtext('minimumTier'));assert 0<=tier<=5
 if d.findtext('addHousing')=='true':assert tier>0
for name in ['Campfire','CraftingSpot','ButcherSpot','SolarGenerator','Vent','PlantPot','TorchLamp','HiTechResearchBench','SimpleResearchBench','MultiAnalyzer','AdvancedMultiAnalyzer']:
 assert rules[name].findtext('addHousing')=='false',name
assert rules['VFEFactory_ConveyorOven'].findtext('addHousing')=='true'
for name in ['VFEC_Turret_Scorpion','VFES_Turret_Ballista','VFES_Turret_Catapult']:assert rules[name].findtext('minimumTier')=='1'
assert rules['VQE_Genetron_Nuclear'].findtext('minimumTier')=='4'
assert rules['LightBall'].findtext('retired')=='true','LightBall should remain vanilla; legacy Def names are retained for saves'
assert not E.parse(root/'mods/GrechkaWorldIntegration/Patches/StorageController.xml').getroot().findall('Operation'),'Unconditional Storage cost patch returned'
m=E.parse(root/'mods/GrechkaWorldIntegration/Defs/Materials.xml').getroot()
groups={d.findtext('defName'):d for d in m if d.tag.endswith('MaterialGroupDef')}
assert len(groups)==9
keys=[]
for d in m:
 if d.tag.endswith('RecipeMaterialRuleDef'):
  assert d.findtext('materialGroup') in groups
  key=(d.findtext('packageId'),d.findtext('recipeDefName'),d.findtext('ingredientIndex'));assert key not in keys;keys.append(key)
# Use authored docs only for a local consistency check, never as a runtime dependency.
p=root/'internal docs/research/machines/G01-REVIEW.md'
if p.exists():
 names=[]
 for line in p.read_text().split('## Принятые исправления')[0].splitlines():
  if line.startswith('| G01-'):names.append(re.search(r'`([^`]+)`',line).group(1))
 assert set(names)=={d.findtext('targetDefName') for d in catalog}
print(f'PASS World profile: all 328 G01 entries + 6 Storage prices, exceptions, {len(groups)} material groups, {len(keys)} explicit recipe positions.')
