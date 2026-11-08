#!/usr/bin/env python3
"""Validate the standalone moving-fire module and collection integration."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
mod = root / 'mods/GrechkaKite'
about = ET.parse(mod / 'About/About.xml').getroot()
assert about.findtext('name') == 'Grechka Kite'
assert about.findtext('packageId') == 'grechka.kite'
assert [n.text for n in about.findall('supportedVersions/li')] == ['1.7']
assert [n.findtext('packageId') for n in about.findall('modDependencies/li')] == ['brrainz.harmony']
conflicts = {n.text for n in about.findall('incompatibleWith/li')}
assert {'roolo.runandgun', 'memegoddess.runandgun'} <= conflicts
profile = root / 'profiles/GrechkaWorld'
active = [n.text for n in ET.parse(profile / 'ModsConfig.xml').findall('activeMods/li')]
assert active.count('grechka.kite') == 1
assert active.index('brrainz.harmony') < active.index('grechka.kite')
assert not conflicts.intersection(active), 'Two moving-fire implementations are active'
entries = [e for e in json.loads((profile / 'mods.json').read_text()) if e['package'] == 'grechka.kite']
assert len(entries) == 1 and entries[0]['repositoryPath'] == 'mods/GrechkaKite'
assert set(entries[0]['incompatible']) == conflicts
languages = [ET.parse(mod / f'Languages/{language}/Keyed/Kite.xml').getroot() for language in ('Russian', 'English')]
assert {n.tag for n in languages[0]} == {n.tag for n in languages[1]}
assert all(n.text and n.text.strip() for language in languages for n in language)
assert len({n.tag for n in languages[0]}) == len(languages[0]) == 4
assert 'GrechkaKite' in (root / 'scripts/build.sh').read_text()
assert 'Kite/KiteTests' in (root / 'scripts/test-native.sh').read_text()
print('PASS Kite metadata, standalone Harmony dependency, RU/EN settings, collection profile and build/test integration.')
