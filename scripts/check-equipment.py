#!/usr/bin/env python3
"""Validate Equipment definitions against the installed game's data and own authored textures."""
from pathlib import Path
import os, xml.etree.ElementTree as E
root=Path(__file__).resolve().parents[1]
game=Path(os.environ.get('RIMWORLD_DATA_DIR',str(Path.home()/'Library/Application Support/Steam/steamapps/common/RimWorld/Data')))
own=root/'mods/GrechkaEquipment'
lookup={}
native_textures=set()
for folder in [game,root/'mods']:
 for p in folder.rglob('*.xml'):
  if not 'Defs' in p.parts:continue
  try:r=E.parse(p).getroot()
  except E.ParseError:continue
  if r.tag!='Defs':continue
  if folder == game:
   native_textures.update(e.text for e in r.findall('.//graphicData/texPath') if e.text)
  for d in r:
   name=d.findtext('defName')
   if name:lookup.setdefault(d.tag,set()).add(name)
def fail(text):raise AssertionError(text)
def ref(kind,value):
 if value not in lookup.get(kind,set()):fail(f'Missing {kind}: {value}')
body=E.parse(game/'Core/Defs/Bodies/Bodies_Humanlike.xml').getroot()
bodyparts={e.text for e in body.findall("BodyDef[defName='Human']//def")}
ours={}
for p in (own/'Defs').glob('*.xml'):
 for d in E.parse(p).getroot():
  name=d.findtext('defName')
  if name:ours[d.tag,name]=d
  if d.tag=='HediffDef':assert (d.findtext('description') or '').strip(),f'Missing Hediff description: {name}'
  for path,kind in [('costList/*','ThingDef'),('products/*','ThingDef'),('statBases/*','StatDef'),('equippedStatOffsets/*','StatDef'),('stages/li/statOffsets/*','StatDef'),('stages/li/statFactors/*','StatDef')]:
   for e in d.findall(path):ref(kind,e.tag)
  for path,kind in [('.//thingDefs/li','ThingDef'),('.//recipeUsers/li','ThingDef'),('.//spawnThingOnRemoved','ThingDef'),('.//projectile','ThingDef'),('.//defaultProjectile','ThingDef'),('.//addsHediff','HediffDef'),('.//removesHediff','HediffDef'),('.//appliedOnFixedBodyParts/li','BodyPartDef'),('.//thingCategories/li','ThingCategoryDef'),('.//bodyPartGroups/li','BodyPartGroupDef'),('.//layers/li','ApparelLayerDef'),('.//capacity','PawnCapacityDef'),('.//damageDef','DamageDef')]:
   for e in d.findall(path):
    if e.text and not list(e):ref(kind,e.text)
  for e in d.findall('graphicData/texPath'):
   assert (own/'Textures'/f'{e.text}.png').is_file() or e.text in native_textures,f'Missing own or installed vanilla texture: {e.text}'
  worn=d.findtext('apparel/wornGraphicPath')
  if worn:
   prefix=own/'Textures'/worn
   bodytype='ArmorT' in prefix.name
   for variant in (['Male','Female','Thin','Fat','Hulk'] if bodytype else ['']):
    for rot in ['north','south','east']:
     suffix=('_'+variant if variant else '')+'_'+rot+'.png'
     assert Path(str(prefix)+suffix).is_file(),f'Missing worn variant {prefix}{suffix}'
covered={e.text for d in ours.values() if d.tag=='RecipeDef' for e in d.findall('appliedOnFixedBodyParts/li')}
assert bodyparts<=covered, f'Uncovered Human anatomy: {bodyparts-covered}'
art_source=root/'assets/GrechkaEquipment/Textures'
if art_source.is_dir():
 for p in (own/'Textures').rglob('*.png'):
  source=art_source/p.relative_to(own/'Textures')
  assert source.is_file() and p.read_bytes()==source.read_bytes(), f'Art differs from prepared source: {p}'
else:
 print('SKIP source-art comparison: optional local assets are absent; packaged textures validated above.')
assert len([1 for kind,name in ours if kind=='ThingDef' and ('Mythic' in name or name.startswith('Grechka_Grechka'))])==64
print(f'PASS Equipment: {len(ours)} definitions, all reference targets, {len(bodyparts)} Human part types, worn variants.')

for module in ['GrechkaEquipment','GrechkaLuckyBlocks']:
 metadata=E.parse(root/'mods'/module/'About/About.xml').getroot()
 for dependency in metadata.findall('modDependencies/li'):
  assert dependency.findtext('downloadUrl') or dependency.findtext('steamWorkshopUrl'), f'Missing dependency URL: {module}/{dependency.findtext("packageId")}'
print('PASS Equipment Hediff descriptions and Equipment/Lucky dependency URLs.')
