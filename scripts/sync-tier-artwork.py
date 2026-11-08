#!/usr/bin/env python3
"""Validate reviewed full tier sprites; explicitly --apply to copy them and emit addressed bindings."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
from PIL import Image

MODULE = 'GrechkaWorldIntegration'
DIRECTIONS = ('north', 'east', 'south', 'west')

# Author filenames keep the source-state name. Game Graphic_Multi requires the facing suffix last.
STATE_LAYOUTS = {
    ('oskarpotocki.vfe.classical', 'VFEC_MeatDryingRack'): ('base', ('in-progress', 'finished')),
    ('oskarpotocki.vfe.classical', 'VFEC_TyrianFarm'): ('empty-base', ('in-progress', 'finished')),
    ('vanillaexpanded.vcooke', 'VCE_CheesePress'): ('empty', ('full',)),
    ('vanillaexpanded.vcooke', 'VCE_ElectricPot'): ('base', ('full',)),
    ('vanillaexpanded.vcookestews', 'VCE_StewPot'): ('base', ('full',)),
}


def frame_paths(prefix, view, variant, is_base):
    source_suffix = '' if variant == 'base' else '__' + variant
    target_suffix = '' if is_base else '__' + variant
    return Path(f'{prefix}_{view}{source_suffix}.png'), Path(f'{prefix}{target_suffix}_{view}.png')


def verify_frame(source, expected_hash, scale):
    payload = source.read_bytes()
    if hashlib.sha256(payload).hexdigest() != expected_hash:
        raise ValueError(f'Changed/unreviewed PNG: {source}')
    with Image.open(io.BytesIO(payload)) as image:
        if image.format != 'PNG' or image.mode != 'RGBA':
            raise ValueError(f'Expected RGBA PNG: {source}')
        if scale == 1:
            image.verify()
            return payload
        size = tuple(max(1, (n + 1) // 2) for n in image.size)
        stream = io.BytesIO()
        image.resize(size, Image.Resampling.LANCZOS).save(stream, format='PNG')
        return stream.getvalue()


def prepare(repo, registry, staging=None, dry_run=False, check_installed=False):
    data = json.loads(registry.read_text())
    if data.get('version') != 1 or not isinstance(data.get('records'), list):
        raise ValueError('Expected version=1 and records array')
    scale = data.get('runtimeScale', 1)
    if isinstance(scale, bool) or scale not in (1, 0.5):
        raise ValueError('runtimeScale must be 1 or 0.5')
    rules = {}
    for rule in ET.parse(repo / 'mods' / MODULE / 'Defs/MachineProfile.xml').getroot():
        if rule.findtext('addHousing') == 'true':
            rules[(rule.findtext('packageId', '').lower(), rule.findtext('targetDefName'))] = int(rule.findtext('minimumTier'))
    copies, seen, definitions = [], set(), ET.Element('Defs')
    for record in data['records']:
        package, name, tier = record['packageId'], record['sourceDefName'], record['tier']
        if not re.fullmatch(r'[a-z0-9_.-]+', package) or not re.fullmatch(r'[A-Za-z0-9_]+', name):
            raise ValueError('Unsafe packageId/sourceDefName')
        minimum = rules.get((package, name))
        if type(tier) is not int or minimum is None or not minimum <= tier <= 5 or package.startswith('grechka.'):
            raise ValueError(f'Not an approved external profile tier: {package}/{name}/T{tier}')
        key = (package, name, tier)
        if key in seen: raise ValueError(f'Duplicate binding: {key}')
        seen.add(key)
        if record.get('reviewed') is not True: raise ValueError(f'Artwork not reviewed: {key}')
        kind = record['kind']
        if kind not in ('Single', 'Multi'): raise ValueError(f'Custom renderer needs an adapter: {key}')
        views = DIRECTIONS if kind == 'Multi' else ('single',)
        hashes = record['sha256']
        if set(hashes) != set(views): raise ValueError(f'Incomplete/extraneous view set: {key}')
        prefix = Path('TierSprites') / package / name / f'{name}_T{tier}'
        base, additional = STATE_LAYOUTS.get((package, name), ('base', ()))
        extras = record.get('stateHashes', {})
        if set(extras) != set(additional):
            raise ValueError(f'Incomplete/extraneous native states: {key}')
        for variant, frame_hashes in [(base, hashes), *extras.items()]:
            if set(frame_hashes) != set(views):
                raise ValueError(f'Incomplete native state directions: {key}/{variant}')
            for view in views:
                source_rel, target_rel = frame_paths(prefix, view, variant, variant == base)
                asset_root = repo / 'assets' / MODULE / 'Textures'
                source = asset_root / source_rel
                if not source.resolve().is_relative_to(asset_root.resolve()):
                    raise ValueError('Source escapes artwork directory')
                payload = verify_frame(source, frame_hashes[view], scale)
                target = repo / 'mods' / MODULE / 'Textures' / target_rel
                if check_installed and (not target.is_file() or target.read_bytes() != payload):
                    raise ValueError(f'Installed sprite differs from the reviewed delivery: {target}')
                if dry_run:
                    saved = None
                elif staging is not None:
                    saved = staging / str(len(copies))
                    saved.write_bytes(payload)
                else:
                    saved = payload
                copies.append((target, saved))
        node = ET.SubElement(definitions, 'Grechka.WorldIntegration.TierArtworkDef')
        for tag, value in [('defName', f'GrechkaWorld_Artwork_{name}_T{tier}'), ('packageId', package),
                           ('sourceDefName', name), ('tier', str(tier)), ('kind', kind),
                           ('texPath', prefix.as_posix() + ('_single' if kind == 'Single' else ''))]:
            ET.SubElement(node, tag).text = value
        if additional:
            variants = ET.SubElement(node, 'stateVariants')
            for variant in additional:
                ET.SubElement(variants, 'li').text = variant
    names = [n.findtext('defName') for n in definitions]
    if len(names) != len(set(names)): raise ValueError('Duplicate generated DefName across source packages')
    ET.indent(definitions, space='  ')
    return copies, ET.tostring(definitions, encoding='utf-8', xml_declaration=True) + b'\n'

def write_atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.is_file() and path.read_bytes() == data: return
    import tempfile, os
    fd, tmp = tempfile.mkstemp(prefix=path.name+'.', suffix='.tmp', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as out: out.write(data)
        os.replace(tmp, path)
    finally:
        if Path(tmp).exists(): Path(tmp).unlink()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--registry', type=Path)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--apply', action='store_true')
    mode.add_argument('--check-installed', action='store_true', help='Verify all installed files and generated XML')
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    registry = args.registry or repo / 'assets' / MODULE / 'tier-artwork.json'
    # A production batch exceeds a gigabyte. Stage checked bytes on disk before publishing any changes.
    import tempfile
    with tempfile.TemporaryDirectory(prefix='grechka-tier-import-') as folder:
        copies, xml = prepare(repo, registry, staging=Path(folder), dry_run=not args.apply, check_installed=args.check_installed)
        target = repo / 'mods' / MODULE / 'Defs/TierArtwork.xml'
        if args.check_installed and (not target.is_file() or target.read_bytes() != xml):
            raise ValueError(f'Installed tier definitions differ from registry: {target}')
        if args.apply:
            for path, staged in copies:
                write_atomic(path, staged.read_bytes())
            write_atomic(target, xml)
    print(f'{"Applied" if args.apply else "Validated"}: {len(copies)} reviewed PNG files; {len(ET.fromstring(xml))} tier bindings.')
    print('No game process/config modified. Custom layered renderers require their own adapters.')

if __name__ == '__main__': main()
