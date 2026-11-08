#!/usr/bin/env python3
"""Check shipping bilingual catalogues without launching RimWorld."""
from pathlib import Path
from collections import Counter
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
errors = []
entries = 0

def catalogue(mod, language, category):
    result = {}
    folder = mod / 'Languages' / language / category
    for path in sorted(folder.rglob('*.xml')):
        try:
            data = ET.parse(path).getroot()
        except ET.ParseError as exc:
            errors.append(f'{path.relative_to(ROOT)}: {exc}')
            continue
        if data.tag != 'LanguageData':
            errors.append(f'{path.relative_to(ROOT)}: expected LanguageData')
        for item in data:
            key = (str(path.parent.relative_to(folder)), item.tag)
            if key in result:
                errors.append(f'{mod.name}/{language}: duplicate {key}')
            result[key] = ''.join(item.itertext())
    return result

def placeholders(value):
    return Counter(re.findall(r'(?<!\{)\{([A-Za-z0-9_]+)(?:[^{}]*)\}(?!\})', value))

for mod in sorted((ROOT / 'mods').iterdir()):
    if not (mod / 'About/About.xml').exists():
        continue
    keyed = {}
    for category in ('Keyed', 'DefInjected'):
        ru = catalogue(mod, 'Russian', category)
        en = catalogue(mod, 'English', category)
        if category == 'Keyed': keyed = {'Russian': ru, 'English': en}
        if not ru and not en and category == 'Keyed':
            continue  # Content-only modules may only have DefInjected entries.
        for key in ru.keys() - en.keys(): errors.append(f'{mod.name}/{category}: missing English {key}')
        for key in en.keys() - ru.keys(): errors.append(f'{mod.name}/{category}: missing Russian {key}')
        for key in ru.keys() & en.keys():
            if placeholders(ru[key]) != placeholders(en[key]):
                errors.append(f'{mod.name}/{category}: placeholder mismatch {key}')
            if re.search('[А-Яа-яЁё]', en[key]):
                errors.append(f'{mod.name}/{category}: Cyrillic in English {key}')
        entries += len(en)
    # Validate literal Translate calls. Vanilla keys may come from Core.
    known = {key for language in keyed.values() for _, key in language}
    for path in (mod / 'Source').rglob('*.cs'):
        source = path.read_text()
        used = re.findall(r'"(Grechka[^"\n]+)"\.Translate\s*\(', source)
        used += re.findall(r'(?:QuestText|StorageText|EquipmentText|LuckyText|IndustryText|WorldText)\.Get\(\s*"([^"]+)"', source)
        for key in used:
            if key not in known: errors.append(f'{path.relative_to(ROOT)}: missing keyed text {key}')

if errors:
    print('\n'.join(errors))
    raise SystemExit(f'FAIL localization: {len(errors)} issues')
print(f'PASS localization: {entries} bilingual entries, unique keys, matching placeholders and literal Translate references')
