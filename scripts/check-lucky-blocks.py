#!/usr/bin/env python3
"""Cross-check authored Lucky Blocks definitions and quest book against available data."""
from pathlib import Path
import json, xml.etree.ElementTree as ET, os
ROOT=Path(__file__).resolve().parents[1]
config=json.loads((ROOT/'mods/GrechkaLuckyBlocks/Config/pools.json').read_text())
pools={p['id']:p for p in config['pools']}
defs=ET.parse(ROOT/'mods/GrechkaLuckyBlocks/Defs/LuckyBlocks.xml').getroot()
assert len(defs)==55
# Each shipped theme/tier now has its own authored image. Do not silently fall
# back to a generic tier cube with a tint when adding or rebinding definitions.
assert len({d.findtext('graphicData/texPath') for d in defs}) == 55
assert all(d.findtext('graphicData/color') in (None, '(1,1,1)') for d in defs)
for d in defs:
 assert d.findtext('stackLimit')=='1'
 assert not d.findtext('defName')[-1].isdigit()
 assert d.findtext('thingClass')=='Grechka.LuckyBlocks.LuckyBlock'
 assert d.findtext('modExtensions/li/pool') in pools
 assert (ROOT/'mods/GrechkaLuckyBlocks/Textures'/(d.findtext('graphicData/texPath')+'.png')).exists()
 assert 'ExoticMisc' in [t.text for t in d.findall('tradeTags/li')]
 if 'LuckyBlockStorage' in d.findtext('defName'): assert d.get('MayRequire')=='grechka.storage'
book=json.loads((ROOT/'mods/GrechkaQuests/QuestPacks/grechka-world.json').read_text())
assert 150 <= len(book['quests']) <= 500 and 20 <= len(book['chapters']) <= 100
ids={q['id'] for q in book['quests']}
assert len(ids)==len(book['quests'])
chapters={c['id'] for c in book['chapters']}
assert len(chapters)==len(book['chapters'])
positions=set()
for q in book['quests']:
 assert set(q['requires'])<=ids
 assert q['chapter'] in chapters
 assert all(o['type'] in ('have','build','produce','mine','harvest','manual','kill') for o in q['objectives'])
 assert all(r['count']>0 for r in q['rewards'])
 position=(q['chapter'],q['graphX'],q['graphY'])
 assert position not in positions, f'Overlapping quest nodes: {q["id"]}'
 positions.add(position)
by_id={q['id']:q for q in book['quests']}
# The introductory route is intentionally short. Optional farming and the machine
# catalog must never become an implicit build-everything gate for T3.
assert by_id['start.food']['chapter']=='farms' and not by_id['start.food']['requires']
assert by_id['start.steel']['objectives'][0]['count']==80
assert by_id['t1.components']['objectives'][0]['count']==5
assert by_id['t2.components']['requires']==['t1.bench']
assert by_id['t2.housing']['requires']==['t2.components']
assert by_id['t3.extractor']['requires']==['t2.bench']
assert {by_id[k]['chapter'] for k in ('start.guide','start.steel','t1.housing','t1.bench','t1.components')}=={'start'}
def ancestors(key):
 result=set();pending=list(by_id[key]['requires'])
 while pending:
  parent=pending.pop()
  if parent in result:continue
  result.add(parent);pending.extend(by_id[parent]['requires'])
 return result
assert not any(k.startswith(('route.','achievements.')) or k=='start.food' for k in ancestors('t3.extractor'))
for chapter in chapters:
 assert sum(q['chapter']==chapter for q in book['quests'])<=35, f'Overloaded chapter: {chapter}'
profile_rules=ET.parse(ROOT/'mods/GrechkaWorldIntegration/Defs/MachineProfile.xml').getroot()
catalog_targets={o['target'] for q in book['quests'] for o in q['objectives'] if o['type']=='build'}
for rule in profile_rules:
 if rule.findtext('addHousing')=='true' and rule.findtext('minimumTier') in ('1','2'):
  assert rule.findtext('targetDefName') in catalog_targets, f'Missing early machine: {rule.findtext("targetDefName")}'
for amount in (10,100,1000):
 q=by_id[f'achievements.cows.{amount}']
 assert q['objectives'][0]['type']=='kill' and q['objectives'][0]['target']=='Cow' and q['objectives'][0]['count']==amount
 assert all(r['def'].startswith('Grechka_LuckyBlock') for r in q['rewards'])
# No core progression quest may require a grind achievement.
assert all(not any(p.startswith('achievements.') for p in q['requires']) for q in book['quests'] if q['chapter']!='achievements')
visited=set()
def visit(key, active):
 assert key not in active, f'Quest cycle at {key}'
 if key in visited:return
 for parent in by_id[key]['requires']:visit(parent,active|{key})
 visited.add(key)
for key in by_id:visit(key,set())
things=set(); pawnkinds=set()
roots=[ROOT/'mods']
game=Path(os.environ.get('RIMWORLD_DATA_DIR',str(Path.home()/'Library/Application Support/Steam/steamapps/common/RimWorld/Data')))
if game.exists(): roots.append(game)
# Only the explicitly selected modpack is used as reference data; removed mods
# must not make a typo or missing quest target look valid.
workshop=Path(os.environ.get('RIMWORLD_WORKSHOP_DIR',str(Path.home()/'Library/Application Support/Steam/steamapps/workshop/content/294100')))
profile=json.loads((ROOT/'profiles/GrechkaWorld/mods.json').read_text())
external=[workshop/m['id'] for m in profile if m['id'].isdigit()]
roots.extend(p for p in external if p.exists())
for root in roots:
 for path in root.rglob('*.xml'):
  if 'Defs' not in path.parts: continue
  try: tree=ET.parse(path).getroot()
  except ET.ParseError: continue
  for d in tree:
   if d.tag=='ThingDef' and d.findtext('defName'): things.add(d.findtext('defName'))
   if d.tag=='PawnKindDef' and d.findtext('defName'): pawnkinds.add(d.findtext('defName'))
# Explicit generated variants are legitimate only for known, housing-enabled
# source families. Do not accept arbitrary names just because they share a prefix.
for rule in ET.parse(ROOT/'mods/GrechkaWorldIntegration/Defs/MachineProfile.xml').getroot():
 target=rule.findtext('targetDefName')
 if target in things and rule.findtext('addHousing')=='true':
  things.update(f'GrechkaWorld_{target}_T{t}_Building' for t in range(int(rule.findtext('minimumTier'))+1,6))
if game.exists():
 missing=[]
 for pool in pools.values():
  for o in pool['outcomes']:
   for item in o['items']:
    if item['def'] not in things: missing.append((pool['id'],item['def']))
   if o['effect']!='items' and o['pawnKind'] not in pawnkinds:missing.append((pool['id'],o['pawnKind']))
 assert not missing,missing
 missing=[]
 for q in book['quests']:
  for o in q['objectives']:
   if o['type']!='manual' and o['target'] not in things:missing.append((q['id'],o['target']))
   for variant in o.get('variants',[]):
    if variant not in things:missing.append((q['id'],variant))
  for r in q['rewards']:
   if r['def'] not in things:missing.append((q['id'],r['def']))
 assert not missing,missing
print(f'PASS {len(defs)} block defs, textures, pools, trade tags; {len(book["quests"])} quests and source references')
