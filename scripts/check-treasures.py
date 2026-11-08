#!/usr/bin/env python3
"""Validate Treasures shipping content and art without launching RimWorld."""
from pathlib import Path
import json
import xml.etree.ElementTree as ET
from PIL import Image

repo = Path(__file__).resolve().parents[1]
module = repo / 'mods/GrechkaTreasures'
checks = 0


def require(value, message):
    global checks
    if not value:
        raise SystemExit('FAIL Treasures: ' + message)
    checks += 1


defs = [node for path in (module / 'Defs').rglob('*.xml') for node in ET.parse(path).getroot()]
named = [node for node in defs if node.findtext('defName')]
identities = [(n.tag, n.findtext('defName')) for n in named]
require(len(identities) == len(set(identities)), 'unique definitions per type')
things = {n.findtext('defName'): n for n in named if n.tag == 'ThingDef'}
gems = ('Diamond', 'Ruby', 'Sapphire', 'Emerald', 'Amethyst', 'Topaz')
for gem in gems:
    for form in ('Raw', 'Cut', 'Monolith', 'Ore', 'Knife', 'Sword', 'Spear', 'Mace',
                 'Statue', 'Obelisk', 'Vase', 'Throne', 'Table', 'Bed', 'Sarcophagus', 'Lamp'):
        name = 'GrechkaTreasures_' + gem + form
        require(name in things, 'missing content ' + name)
    raw, cut, monolith, ore = (things['GrechkaTreasures_' + gem + s] for s in ('Raw', 'Cut', 'Monolith', 'Ore'))
    require(float(cut.findtext('statBases/MarketValue')) > float(raw.findtext('statBases/MarketValue')), gem + ' gains value through cutting')
    require(float(monolith.findtext('statBases/Mass')) >= 20, gem + ' monolith is physical caravan cargo')
    require(ore.findtext('building/mineableThing') == raw.findtext('defName'), gem + ' mineable ore yields real raw gems')
    require(float(ore.findtext('building/mineableScatterCommonality')) > 0, gem + ' ore has a natural generation route')

for name in things:
    require(not name[-1].isdigit(), 'ThingDef IDs cannot end in digits: ' + name)
paths = set()
for node in defs:
    for field in node.iter():
        if field.tag in {'texPath', 'uiIconPath', 'siteTexture', 'expandingIconTexture', 'texture'} and field.text:
            relative = Path(field.text.strip())
            require(not relative.is_absolute() and '..' not in relative.parts, 'safe texture path')
            # Explicit use of the native ore atlas is intentional; never copy game textures.
            if str(relative).startswith('Things/'):
                require(str(relative) == 'Things/Building/Linked/RockFlecked_Atlas', 'only the native ore atlas may use a vanilla path')
                continue
            paths.add(module / 'Textures' / relative.with_suffix('.png'))
for path in sorted(paths):
    require(path.is_file(), 'missing texture ' + str(path.relative_to(repo)))
    with Image.open(path) as image:
        require(image.mode == 'RGBA', 'RGBA artwork ' + path.name)
        alpha = image.getchannel('A')
        require(alpha.getextrema()[0] == 0 and alpha.getbbox() is not None, 'visible transparent cutout ' + path.name)

site_part = next(n for n in named if n.tag == 'SitePartDef')
require(site_part.findtext('disallowsAutomaticDetectionTimerStart') == 'true', 'no inherited surprise detection deadline')

maps = [ET.parse(p).getroot() for p in (module / 'Config/Sites').glob('*.xml')]
require(len(maps) >= 24, 'at least 24 prepared maps')
require(len({m.findtext('id') for m in maps}) == len(maps), 'stable unique map IDs')
layouts = [tuple(row.text or '' for row in m.findall('rows/row')) for m in maps]
require(len(set(layouts)) == len(maps), 'different maps must have different physical layouts')
require({m.findtext('gem') for m in maps} == set(gems), 'expeditions cover all six gems')
require({int(m.findtext('tier')) for m in maps} == set(range(1, 6)), 'five expedition difficulty levels')
for m, rows in zip(maps, layouts):
    name = m.findtext('id')
    require(bool(m.findtext('clue')), name + ' readable advance warning')
    require(len(rows) >= 9 and len({len(row) for row in rows}) == 1, name + ' rectangular plan')
    require(any('C' in row for row in rows), name + ' genuine treasure exists')

manifest = json.loads((repo / 'profiles/GrechkaWorld/mods.json').read_text())
entries = [e for e in manifest if e['package'] == 'grechka.treasures']
require(len(entries) == 1 and entries[0]['repositoryPath'] == 'mods/GrechkaTreasures', 'one portable local module in manifest')
active = [n.text for n in ET.parse(repo / 'profiles/GrechkaWorld/ModsConfig.xml').findall('activeMods/li')]
require(active.count('grechka.treasures') == 1, 'module enabled exactly once in repository profile')
require(active.index('grechka.treasures') < active.index('grechka.world.integration'), 'integration sees Treasures at definition load')
book = json.loads((repo / 'mods/GrechkaQuests/QuestPacks/grechka-world.json').read_text())
require(book['id'] == 'grechka.world', 'existing World book identity preserved')
require(sum(c['id'] == 'treasures' for c in book['chapters']) == 1, 'one Treasures learning chapter')
quests = [q for q in book['quests'] if q['id'].startswith('treasures.')]
require(len(quests) == 12, 'twelve learning quests')
all_ids = {q['id'] for q in book['quests']}
# Expedition preparations also use these exact Core supplies. The common
# check-lucky-blocks.py validates all of them against the installed source data.
learning_targets = set(things) | {'Steel', 'MealSurvivalPack', 'MedicineIndustrial'}
for quest in quests:
    require(quest['chapter'] == 'treasures', quest['id'] + ' belongs to the learning chapter')
    require(all(dep in all_ids for dep in quest['requires']), quest['id'] + ' valid dependencies')
    require(bool(quest['objectives']) and bool(quest['rewards'] or quest.get('alternatives')), 'practical objective and reward')
    for goal in quest['objectives']:
        require(goal['type'] in {'have', 'build', 'produce'}, 'only automatic non-consuming objective types')
        for target in [goal.get('target', '')] + goal.get('variants', []):
            if target:
                require(target in learning_targets, 'learning target exists: ' + target)
    for reward in quest['rewards']:
        require(reward['count'] > 0, 'positive physical learning reward')
        if reward['def'].startswith('GrechkaTreasures_'):
            require(reward['def'] in things, 'learning reward exists: ' + reward['def'])
print(f'Treasures: {checks} checks passed; {len(things)} ThingDefs, {len(maps)} prepared maps, {len(paths)} bound textures. No game launched.')
