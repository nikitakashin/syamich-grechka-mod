#!/usr/bin/env python3
"""Check Pharmacy content, safe IDs and prepared runtime artwork without starting RimWorld."""
from pathlib import Path
import xml.etree.ElementTree as ET
from PIL import Image

repo = Path(__file__).resolve().parents[1]
module = repo / 'mods/GrechkaPharmacy'
checks = 0

def require(ok, message):
    global checks
    if not ok:
        raise SystemExit('FAIL Pharmacy: ' + message)
    checks += 1

defs = [n for p in sorted((module / 'Defs').glob('*.xml')) for n in ET.parse(p).getroot()]
keys = [(n.tag, n.findtext('defName')) for n in defs if n.findtext('defName')]
require(len(keys) == len(set(keys)), 'definition IDs must be unique per type')
things = [n for n in defs if n.tag == 'ThingDef' and n.findtext('defName')]
require(all(not n.findtext('defName')[-1].isdigit() for n in things), 'ThingDef IDs must not end in digits in RimWorld 1.7')
preparations = [n for n in things if n.find('ingestible/outcomeDoers/li[@Class="Grechka.Pharmacy.IngestionOutcomeDoer_Pharmacy"]') is not None]
require(len(preparations) == 12, 'twelve distinct preparations required')
for item in preparations:
    name = item.findtext('defName')
    require(name.endswith('_Item'), name + ' stable item suffix')
    require(item.findtext('ingestible/drugCategory') == 'Medical', name + ' targeted Medical consumable')
    doer = item.find('ingestible/outcomeDoers/li')
    require(doer is not None and doer.findtext('action') in {'effect','detox','regeneration','comprehensive','scar','limb','youth'}, name + ' supported outcome')
    require(item.findtext('ingestible/joy') in {None, '0'}, name + ' has no recreational joy')
reactors = [n for n in things if n.findtext('thingClass') == 'Building_WorkTable']
require(len(reactors) == 3, 'three reactor tiers required')
for building in reactors:
    require(building.findtext('defName').endswith('_Building'), 'stable building ID')
    require(building.findtext('rotatable') == 'false', 'single reactor graphic must not rotate')
    require(building.findtext('size') == '(3,2)', 'reactor footprint is 3x2')
    require(float(building.findtext('comps/li[@Class="CompProperties_Power"]/basePowerConsumption')) > 0, 'reactor needs power')
trainers = [n for n in things if n.find('comps/li[@Class="Grechka.Tweaks.CompProperties_InterestTrainer"]') is not None]
require(len(trainers) == 5, 'five positive interest trainers required')
require(sorted(int(n.findtext('comps/li[@Class="Grechka.Tweaks.CompProperties_InterestTrainer"]/grade')) for n in trainers)
        == [2, 3, 4, 5, 6], 'one trainer for each positive interest grade, none for lowering')
for item in trainers:
    name = item.findtext('defName')
    require(set(item.get('MayRequire', '').split(',')) == {'grechka.treasures', 'grechka.tweaks'}, name + ' optional two-module integration')
    require(item.find('ingestible') is None, name + ' explicit use only, never recreational ingestion')
    require(item.findtext('comps/li[@Class="CompProperties_Usable"]/useJob') == 'UseItem', name + ' native use job')
    require(item.find('comps/li[@Class="CompProperties_UseEffectDestroySelf"]') is None, name + ' consumes only on success')
    recipes = [n for n in defs if n.tag == 'RecipeDef' and n.find('products/' + name) is not None]
    require(len(recipes) == 1, name + ' single crafting recipe')
    recipe = recipes[0]
    require(recipe.get('MayRequire') == item.get('MayRequire'), name + ' recipe and item have identical optional requirements')
    require(recipe.findtext('products/' + name) == '1', name + ' one item per craft')
    ingredients = {n.findtext('filter/thingDefs/li'): float(n.findtext('count')) for n in recipe.findall('ingredients/li')}
    require(all(v > 0 for v in ingredients.values()) and 'Neutroamine' in ingredients, name + ' positive pharmaceutical ingredients')
    require(any(k.startswith('GrechkaTreasures_') for k in ingredients), name + ' uses Treasures gems')
    require(any(k.startswith('GrechkaPharmacy_') or k == 'MedicineIndustrial' for k in ingredients), name + ' uses Pharmacy preparations or medicine')
    require(set(ingredients) == {n.text for n in recipe.findall('fixedIngredientFilter/thingDefs/li')}, name + ' fixed filter matches ingredients')
    tier = int(recipe.findtext('researchPrerequisites/li').split('T')[-1])
    expected = ({'DrugLab'} if tier == 2 else set()) | {f'GrechkaPharmacy_ReactorT{t}_Building' for t in range(max(tier, 3), 6)}
    require({n.text for n in recipe.findall('recipeUsers/li')} == expected, name + ' later reactors retain earlier recipes')
required = set()
for node in defs:
    for field in node.iter():
        if field.tag in {'texPath', 'uiIconPath'} and field.text:
            relative = Path(field.text.strip())
            require(not relative.is_absolute() and '..' not in relative.parts, 'safe runtime texture path')
            required.add(module / 'Textures' / relative.with_suffix('.png'))
require(len(required) == 20, 'each preparation, reactor and interest trainer has its own prepared texture')
for path in sorted(required):
    require(path.is_file(), 'missing artwork ' + str(path.relative_to(repo)))
    with Image.open(path) as image:
        require(image.mode == 'RGBA', 'RGBA artwork ' + path.name)
        require(image.width > 0 and image.height > 0, 'valid dimensions ' + path.name)
        alpha = image.getchannel('A')
        low, high = alpha.getextrema()
        require(low == 0 and high > 0, 'transparent cutout with visible pixels ' + path.name)
        require(alpha.getbbox() is not None, 'nonempty sprite ' + path.name)
# Optional integration data belongs in conditional patches, never hard references in standalone defs.
for node in defs:
    for field in node.iter():
        if field.text:
            require(not field.text.strip().startswith(('Grechka_Housing', 'Grechka_Component', 'Grechka_Mechanites', 'Grechka_ArchoSubstrate')), 'standalone definitions must not require Industry')
for path in (module / 'Patches').glob('*.xml'):
    ET.parse(path)
print(f'Pharmacy: {checks} content/art checks passed; no game launched.')
