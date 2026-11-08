#!/usr/bin/env python3
"""Verify Industry's actual graphics bindings, direction sets and prepared atlas format."""
from pathlib import Path
import struct
from workbench_art import directional_sources
import xml.etree.ElementTree as ET

repo = Path(__file__).resolve().parents[1]
module = repo / 'mods/GrechkaIndustry'
textures = module / 'Textures'
originals = repo / 'assets/GrechkaIndustry/Textures'
defs = {d.findtext('defName'): d for p in (module / 'Defs').rglob('*.xml') for d in ET.parse(p).getroot() if d.findtext('defName')}
verified = set()

work_sides = directional_sources(module.name, originals)

def png(path):
    file = textures / (path + '.png')
    assert file.is_file(), 'Missing texture: ' + path
    data = file.read_bytes()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', file
    relative = file.relative_to(textures)
    source = originals / work_sides.get(relative, relative)
    if source.is_file():
        assert source.read_bytes() == data, 'User artwork was modified: ' + path
    verified.add(path)
    return struct.unpack('>II', data[16:24])

for d in defs.values():
    graphic = d.find('graphicData')
    if graphic is None:
        continue
    path = graphic.findtext('texPath', '')
    if path.startswith(('Buildings/', 'Items/')):
        if graphic.findtext('graphicClass') == 'Graphic_Multi':
            for direction in ('north', 'east', 'south', 'west'):
                png(path + '_' + direction)
        else:
            png(path)
    for node in d.findall("comps/li[@Class='Grechka.Industry.CompProperties_OperationalGraphics']"):
        for state in ('offPath', 'idlePath', 'activePath'):
            png(node.findtext(state))

for tier in (1, 2, 3, 4, 5):
    d = defs[f'Grechka_AssemblyBenchT{tier}_Building']
    path = f'Buildings/Map/AssemblyBenchT{tier}/AssemblyBenchT{tier}'
    assert d.findtext('graphicData/texPath') == path
    assert d.findtext('size') == '(3,1)' and d.findtext('graphicData/drawSize') == '(3.5,1.5)'
    assert d.findtext('graphicData/color', '(1,1,1)') == '(1,1,1)', 'Full-colour bench must not use old tint'
    assert png(path + '_south') == (1024, 512) and png(path + '_east') == (512, 1024)
    assert d.findtext('interactionCellOffset') == '(0,0,-1)'
    assert d.findtext('uiIconPath') == path + '_north'  # Retains the original front-facing icon.

# These had vanilla placeholders or borrowed another item/tier's image. Keep exact identity.
exact = {
    'Grechka_Mechanites': 'Items/Mechanites',
    'Grechka_ArchoSubstrate': 'Items/ArchoSubstrate',
    'Grechka_Matter': 'Items/Matter',
    'Grechka_GrechkaOre': 'Items/GrechkaOre',
    'Grechka_ArchoGeneratorT5_Building': 'Buildings/ArchotechGeneratorT5',
    'Grechka_SolarEltexT5_Building': 'Buildings/Solar/Optional/Psycasts/PsyMatrixT5',
    'Grechka_SolarFortressT4_Building': 'Buildings/Solar/Optional/Fortifications/FortArchotechT4',
    'Grechka_SolarFortressT5_Building': 'Buildings/Solar/Optional/Fortifications/FortGrechkaT5',
}
for name in ('Ionizer', 'MechaniteExtractor'):
    for tier in (4, 5):
        exact[f'Grechka_{name}T{tier}_Building'] = f'Buildings/{name}T{tier}'
for name, path in exact.items():
    d = defs[name]
    assert d.findtext('graphicData/texPath') == path, name
    assert d.findtext('graphicData/graphicClass') == 'Graphic_Single', name
    assert d.findtext('graphicData/color', '(1,1,1)') == '(1,1,1)', name
    png(path)

recycler = defs['Grechka_MatterRecyclerT4_Building']
assert recycler.findtext('graphicData/graphicClass') == 'Graphic_Multi'
state = recycler.find("comps/li[@Class='Grechka.Industry.CompProperties_OperationalGraphics']")
assert state is not None and state.findtext('singleView') == '1', 'Unsuffixed state is the west operator side for Rotation.East'

main = [defs[f'Grechka_SolarS{i}_Building'].findtext('graphicData/texPath') for i in range(1, 7)]
assert len(set(main)) == 6, 'Solar ladder must use distinct prepared artwork'
# Cost lists draw a packed ThingDef without an InnerThing. MinifiedThing has no
# graphic of its own, so each solar wrapper needs an explicit ingredient icon.
packed_panels = 0
for name, d in defs.items():
    building = d.findtext("modExtensions/li[@Class='Grechka.Industry.PackedBuildingProperties']/building")
    if building != 'SolarGenerator' and not (building or '').startswith('Grechka_Solar'):
        continue
    expected = ('Things/Building/Power/SolarCollector' if building == 'SolarGenerator'
                else defs[building].findtext('graphicData/texPath'))
    assert d.findtext('uiIconPath') == expected, 'Missing/mismatched packed solar icon: ' + name
    if building != 'SolarGenerator':
        png(expected)
    packed_panels += 1
assert packed_panels == 20, 'Every vanilla/main/optional packed solar panel needs an icon'
ore = defs['Grechka_MineableGrechka'].find('graphicData')
assert ore.findtext('texPath') == 'Things/Building/Linked/RockFlecked_Atlas'
assert ore.findtext('linkType') == 'CornerFiller' and ore.findtext('shaderType') == 'CutoutComplex'
assert ore.findtext('color') == '(128,122,116)' and ore.findtext('colorTwo') == '(150,97,47)'
for tier in (3, 4, 5):
    d = defs[f'Grechka_SolarBiotechT{tier}_Building']
    assert d.findtext('size') == '(2,2)'
    assert d.findtext('graphicData/shadowData/volume') == '(1.75,0.5,1.7)'
print(f'PASS Industry art bindings: {len(verified)} PNGs, four-direction benches, unique solar ladder, tinted vanilla ore and matching Biotech footprint/shadow.')
