#!/usr/bin/env python3
"""Cross-check authored Lucky Blocks definitions and quest book against available data."""
from pathlib import Path
import json, xml.etree.ElementTree as ET, os
ROOT=Path(__file__).resolve().parents[1]
config=json.loads((ROOT/'mods/GrechkaLuckyBlocks/Config/pools.json').read_text())
pools={p['id']:p for p in config['pools']}
defs=ET.parse(ROOT/'mods/GrechkaLuckyBlocks/Defs/LuckyBlocks.xml').getroot()
assert len(defs)==55
for d in defs:
 assert d.findtext('stackLimit')=='1'
 assert not d.findtext('defName')[-1].isdigit()
 assert d.findtext('thingClass')=='Grechka.LuckyBlocks.LuckyBlock'
 assert d.findtext('modExtensions/li/pool') in pools
 assert (ROOT/'mods/GrechkaLuckyBlocks/Textures'/(d.findtext('graphicData/texPath')+'.png')).exists()
 assert 'ExoticMisc' in [t.text for t in d.findall('tradeTags/li')]
 if 'LuckyBlockStorage' in d.findtext('defName'): assert d.get('MayRequire')=='grechka.storage'
book=json.loads((ROOT/'mods/GrechkaQuests/QuestPacks/grechka-world.json').read_text())
assert len(book['quests'])<60 and len(book['chapters'])==10
ids={q['id'] for q in book['quests']}
assert len(ids)==len(book['quests'])
for q in book['quests']:
 assert set(q['requires'])<=ids
 assert all(o['type'] in ('have','build','produce','mine','harvest','manual') for o in q['objectives'])
 assert all(r['count']>0 for r in q['rewards'])
things=set(); pawnkinds=set()
roots=[ROOT/'mods']
game=Path(os.environ.get('RIMWORLD_DATA_DIR',str(Path.home()/'Library/Application Support/Steam/steamapps/common/RimWorld/Data')))
if game.exists(): roots.append(game)
for root in roots:
 for path in root.rglob('*.xml'):
  if 'Defs' not in path.parts: continue
  try: tree=ET.parse(path).getroot()
  except ET.ParseError: continue
  for d in tree:
   if d.tag=='ThingDef' and d.findtext('defName'): things.add(d.findtext('defName'))
   if d.tag=='PawnKindDef' and d.findtext('defName'): pawnkinds.add(d.findtext('defName'))
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
  for r in q['rewards']:
   if r['def'] not in things:missing.append((q['id'],r['def']))
 assert not missing,missing
print(f'PASS {len(defs)} block defs, textures, pools, trade tags; {len(book["quests"])} quests and source references')
