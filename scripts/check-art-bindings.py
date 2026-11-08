#!/usr/bin/env python3
"""Validate prepared art bindings without opening RimWorld or rewriting source PNGs."""
from pathlib import Path
import struct
from workbench_art import directional_sources
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
MODULES = ['GrechkaIndustry', 'GrechkaEquipment', 'GrechkaStorage', 'GrechkaQuests', 'GrechkaLuckyBlocks', 'GrechkaWorldIntegration']
TEXTURES = [ROOT / 'mods' / name / 'Textures' for name in MODULES]
checked = 0

def png_size(path):
    data = path.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    return struct.unpack('>II', data[16:24])

def find(path):
    return next((root / (path + '.png') for root in TEXTURES if (root / (path + '.png')).is_file()), None)

for mod in MODULES:
    for path in (ROOT / 'mods' / mod / 'Defs').rglob('*.xml'):
        tree = ET.parse(path)
        for graphic in tree.iter('graphicData'):
            texture = graphic.findtext('texPath')
            if not texture or texture.startswith('Things/'):
                continue  # Explicit engine assets are checked by native configuration checks.
            kind = graphic.findtext('graphicClass')
            if kind == 'Graphic_Multi':
                for direction in ['south', 'east', 'north', 'west']:
                    assert find(texture + '_' + direction), (path, texture, direction)
                    checked += 1
            elif kind == 'Graphic_Single':
                image = find(texture)
                assert image, (path, texture)
                checked += 1
                if texture.startswith('Items/'):
                    width, height = png_size(image)
                    raw = graphic.findtext('drawSize', '(1,1)').strip('()')
                    sizes = [float(x) for x in raw.split(',')]
                    x, y = (sizes * 2)[:2] if len(sizes) == 1 else sizes
                    assert abs(x / y - width / height) < 0.00002, (path, texture, 'aspect ratio', raw, (width, height))
        for field in ['uiIconPath', 'iconPath']:
            for element in tree.iter(field):
                if element.text and element.text.startswith(('Items/', 'Buildings/', 'UI/')):
                    assert find(element.text), (path, element.text)
                    checked += 1

# The planet map and its zoomed-out overlay load different fields on the Certus site.
storage = ROOT / 'mods' / 'GrechkaStorage'
meteor = ET.parse(storage / 'Defs/CertusEvents.xml').find("./SitePartDef[defName='Grechka_CertusMeteor']")
for field in ('siteTexture', 'expandingIconTexture'):
    texture = meteor.findtext(field)
    assert texture and (storage / 'Textures' / (texture + '.png')).is_file(), (meteor.findtext('defName'), field, texture)
    checked += 1

# Every configured state renderer must have its three prepared frames.
for path in (ROOT / 'mods/GrechkaStorage/Defs').rglob('*.xml'):
    for comp in ET.parse(path).iter('li'):
        if comp.get('Class') != 'Grechka.Storage.CompProperties_StorageMachineArt':
            continue
        base = comp.findtext('texture')
        for state in ['Off', 'Idle', 'Active']:
            assert find(base + '_' + state), (path, base, state)
            checked += 1
# Directional states use a prepared explicit side or the known legacy single view.
for path in (ROOT / 'mods/GrechkaIndustry/Defs').rglob('*.xml'):
    for comp in ET.parse(path).iter('li'):
        if comp.get('Class') != 'Grechka.Industry.CompProperties_OperationalGraphics' or comp.findtext('directionalStates') != 'true':
            continue
        legacy = int(comp.findtext('singleView', '-1'))
        for field in ['offPath', 'idlePath', 'activePath']:
            base = comp.findtext(field)
            for i, side in enumerate(['north', 'east', 'south', 'west']):
                frame = find(base + '_' + side) or (find(base) if legacy == i else None)
                assert frame and png_size(frame) == (512, 512), (path, field, side)
                checked += 1

# Full tier bindings address complete sets; no masks or drafts are accepted here.
art_xml = ROOT / 'mods/GrechkaWorldIntegration/Defs/TierArtwork.xml'
if art_xml.exists():
    for record in ET.parse(art_xml).getroot():
        kind, prefix = record.findtext('kind'), record.findtext('texPath')
        assert kind in ('Single', 'Multi') and prefix.startswith('TierSprites/'), record.findtext('defName')
        variants = [n.text for n in record.findall('stateVariants/li')]
        for suffix in (['_north', '_east', '_south', '_west'] if kind == 'Multi' else ['']):
            assert find(prefix + suffix), (prefix, suffix)
            checked += 1
            for variant in variants:
                state = prefix + '__' + variant + suffix if kind == 'Multi' else prefix.removesuffix('_single') + '__' + variant + '_single'
                assert find(state), state
                checked += 1
    # Runtime files can have approved distribution dimensions and different native state names.
    import subprocess
    import sys
    subprocess.run([sys.executable, str(ROOT / 'scripts/sync-tier-artwork.py'), '--check-installed'], check=True)
inscriber = next(n for n in ET.parse(ROOT / 'mods/GrechkaStorage/Defs/ThingDefs/Buildings_Network.xml').getroot()
                 if n.findtext('defName') == 'Grechka_Inscriber')
assert inscriber.findtext('size') == '(3,1)'
assert inscriber.findtext('graphicData/drawSize') == '(1.4,1.4)'

certus = next(n for n in ET.parse(ROOT / 'mods/GrechkaStorage/Defs/ThingDefs/Buildings_Certus.xml').getroot()
               if n.findtext('defName') == 'Grechka_MineableCertus')
graphic = certus.find('graphicData')
assert graphic.findtext('texPath') == 'Things/Building/Linked/RockFlecked_Atlas'
assert graphic.findtext('linkType') == 'CornerFiller' and graphic.findtext('shaderType') == 'CutoutComplex'
assert graphic.findtext('color') == '(128,122,116)' and graphic.findtext('colorTwo') == '(100,190,245)'
for block in ET.parse(ROOT / 'mods/GrechkaLuckyBlocks/Defs/LuckyBlocks.xml').getroot():
    assert len(block.findall('tradeTags')) == 1, block.findtext('defName')
    assert set(n.text for n in block.findall('tradeTags/li')) == {'ExoticMisc', 'GrechkaLuckyBlocks'}

# Body types and explicit west frames are part of our authored renderer contract.
for tier in [3, 4, 5]:
    for body in ['Male', 'Female', 'Thin', 'Fat', 'Hulk']:
        for facing in ['south', 'east', 'north', 'west']:
            assert find(f'Worn/ArmorT{tier}/ArmorT{tier}_{body}_{facing}')
            checked += 1
for name in ['GoldenWings', 'HelmetT3', 'HelmetT4', 'HelmetT5', 'CombatDroneT4', 'CombatDroneT5']:
    for facing in ['south', 'east', 'north', 'west']:
        assert find(f'Worn/{name}/{name}_{facing}')
        checked += 1
for mod in MODULES:
    src = ROOT / 'assets' / mod / 'Textures'
    work_sides = directional_sources(mod, src)
    for target in (ROOT / 'mods' / mod / 'Textures').rglob('*.png'):
        if mod == 'GrechkaWorldIntegration' and 'TierSprites' in target.parts:
            continue  # Verified against the addressed registry above.
        relative = target.relative_to(ROOT / 'mods' / mod / 'Textures')
        original = src / work_sides.get(relative, relative)
        if original.exists():
            assert original.read_bytes() == target.read_bytes(), ('altered prepared PNG', target)
print(f'PASS: {checked} texture bindings/directions; item drawSize preserves PNG proportions; packaged prepared art unchanged.')
