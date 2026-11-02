#!/usr/bin/env python3
"""Validate Industry late-game reachability, upgrade costs and asset references."""
from pathlib import Path
import xml.etree.ElementTree as ET
import re
root = Path(__file__).resolve().parents[1]
mod = root / 'mods/GrechkaIndustry'
defs = {}
for path in (mod / 'Defs').rglob('*.xml'):
    for node in ET.parse(path).getroot():
        name = node.findtext('defName')
        if name:
            assert name not in defs, f'duplicate: {name}'
            defs[name] = node
for path in (mod / 'Defs').rglob('*.xml'):
    for n in ET.parse(path).iter():
        for text in [n.tag, (n.text or '').strip()]:
            if re.fullmatch(r'Grechka_[A-Za-z0-9_]+', text):
                assert text in defs or (path.name == 'SolarIntegrations.xml' and text in {'Grechka_CertusQuartz','Grechka_QuartzProcessor','Grechka_SilverProcessor','Grechka_GoldProcessor'}), f'unresolved {text} in {path}'
        if n.tag in ('texPath', 'uiIconPath') and (n.text or '').startswith(('Items/', 'Buildings/')):
            assert (mod / 'Textures' / (n.text + '.png')).exists() or all((mod / 'Textures' / (n.text + '_' + direction + '.png')).exists() for direction in ['north','east','south','west']), f'missing texture: {n.text}'
# Start from vanilla resources and T3 manufacturing. Every higher component and
# housing must become reachable without spending an item of its own output tier.
known = {'Plasteel','Uranium','Grechka_Mechanites','Grechka_ComponentT3_Item','Grechka_HousingT3_Item','Grechka_GrechkaOre'}
for _ in range(10):
    for name, recipe in defs.items():
        if recipe.tag != 'RecipeDef': continue
        inputs = {n.text for n in recipe.findall('./ingredients/li/filter/thingDefs/li')}
        if inputs <= known:
            known.update(n.tag for n in recipe.findall('./products/*'))
assert {'Grechka_ArchoSubstrate','Grechka_ComponentT4_Item','Grechka_HousingT4_Item','Grechka_GrechkaConcentrate','Grechka_ComponentT5_Item','Grechka_HousingT5_Item'} <= known
for t, speed, watts, growth, growth_watts in [(4,15,2800,20,3000),(5,60,11200,50,10000)]:
    b = defs[f'Grechka_AssemblyBenchT{t}_Building']
    assert float(b.findtext('statBases/WorkTableWorkSpeedFactor')) == speed
    assert float(b.findtext("comps/li[@Class='CompProperties_Power']/basePowerConsumption")) == watts
    b = defs[f'Grechka_MechaponicsT{t}_Building']
    assert b.findtext('size') == '(1,1)'
    assert float(b.findtext('modExtensions/li/growthMultiplier')) == growth
    assert float(b.findtext("comps/li[@Class='CompProperties_Power']/basePowerConsumption")) == growth_watts
    assert b.findtext('graphicData/texPath') == f'Buildings/MechaponicsT{t}'
    for family in ['SuppressionEmitter','AccelerationEmitter','WellbeingEmitter','ArchoGenerator','MatterRecycler','MatterSynthesizer']:
        assert f'Grechka_{family}T{t}_Building' in defs
for t in (4,5):
    recipe = defs[f'Grechka_UpgradeAssemblyBenchT{t}']
    housing = defs[f'Grechka_MakeHousingT{t}']
    target = defs[f'Grechka_AssemblyBenchT{t}_Building']
    expected = {n.tag:float(n.text) for n in target.findall('costList/*') if n.tag != f'Grechka_HousingT{t}_Item'}
    for n in housing.findall('ingredients/li'):
        name=n.findtext('filter/thingDefs/li')
        if name != f'Grechka_HousingT{t-1}_Item':expected[name]=expected.get(name,0)+float(n.findtext('count'))
    actual={n.findtext('filter/thingDefs/li'):float(n.findtext('count')) for n in recipe.findall('ingredients/li')}
    assert actual == expected, f'upgrade overcharges or duplicates its existing housing: T{t}'
base_panels=1
for stage, ratio in [(1,1),(2,2),(3,3),(4,6),(5,6),(6,8)]:
    cost=defs[f'Grechka_SolarS{stage}_Building'].findtext(f'costList/Grechka_PackedSolarS{stage-1}_Item')
    assert int(cost)==ratio
    base_panels*=ratio
assert base_panels==1728
# The accepted one/three-panel load envelope, explicit rather than guessed.
solar_power = -float(defs['Grechka_SolarS6_Building'].findtext("comps/li[@Class='CompProperties_Power']/basePowerConsumption"))
base_load=40_000_000
matter_load=90_000_000
assert solar_power >= base_load and solar_power * 3 >= base_load + matter_load
assert defs['Grechka_SolarS6_Building'].findtext('modExtensions/li/nightFraction')=='1'
print('Industry T4/T5: references, textures, bootstrap, upgrade credit, solar chain and 40MW/130MW load envelopes OK')
