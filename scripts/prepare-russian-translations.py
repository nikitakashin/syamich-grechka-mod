#!/usr/bin/env python3
"""Build a private, language-only RU overlay; never changes source mods or game state.

Downloaded translations remain in ignored Translations/, with provenance in work/.
Only active LoadFolders branches are read. The output is gated per owning package.
"""
from collections import Counter, defaultdict
from pathlib import Path
import argparse
import copy
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--sources', type=Path, required=True)
parser.add_argument('--audit', type=Path, required=True)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args()
sources = json.loads(args.sources.read_text())
mods = json.loads(args.audit.read_text())
active = {n.text.lower() for n in E.parse(ROOT/'profiles/GrechkaWorld/ModsConfig.xml').findall('activeMods/li')}
out = ROOT/'mods/GrechkaTranslationsRus'
generated = out/'Translations'
if generated.exists():
    shutil.rmtree(generated)
generated.mkdir(parents=True)
issues = defaultdict(list)

def parse(path):
    try:
        return E.parse(path).getroot()
    except E.ParseError as exc:
        # Some upstream XML has duplicate/trailing UTF-8 BOMs. Repair in memory only.
        original = path.read_text(encoding='utf-8')
        cleaned = original.strip('\ufeff \t\r\n')
        if cleaned != original:
            try:
                result = E.fromstring(cleaned)
                issues['bom_repairs'].append(str(path))
                return result
            except E.ParseError:
                pass
        issues['malformed'].append({'file': str(path), 'error': str(exc)})
        return None

def condition(node):
    for key, value in node.attrib.items():
        names = [n.strip().lower() for n in value.split(',') if n.strip()]
        if key == 'IfModActive' and not any(n in active for n in names): return False
        if key == 'IfModActiveAll' and not all(n in active for n in names): return False
        if key == 'IfModNotActive' and any(n in active for n in names): return False
        if key == 'IfModNotActiveAll' and all(n in active for n in names): return False
    return True

def routes(root):
    lf = next((p for p in root.iterdir() if p.name.lower() == 'loadfolders.xml'), None)
    if lf:
        doc = parse(lf)
        assert doc is not None, lf
        choices = []
        for node in doc:
            match = re.fullmatch(r'v(\d+)\.(\d+)', node.tag)
            if match and tuple(map(int, match.groups())) <= (1, 7):
                choices.append((tuple(map(int, match.groups())), node))
        branch = max(choices, key=lambda x: x[0])[1] if choices else doc.find('default')
        assert branch is not None, str(lf)
        result = [(root/(n.text or '').strip().strip('/')) for n in branch if condition(n)]
    else:
        result = [root]
        versions = [(tuple(map(int,p.name.split('.'))),p) for p in root.iterdir()
                    if p.is_dir() and re.fullmatch(r'\d+\.\d+',p.name) and tuple(map(int,p.name.split('.'))) <= (1,7)]
        if versions: result.append(max(versions)[1])
    return list(dict.fromkeys(p for p in result if p.is_dir()))

owners = {}
def_sources = {}
english = {}
before_hashes = {}
for mod in mods:
    root = Path(mod['path'])
    for base in routes(root):
        for path in sorted((base/'Defs').rglob('*.xml')):
            doc = parse(path)
            if doc is None or doc.tag != 'Defs': continue
            for d in doc:
                name = d.findtext('defName')
                if name:
                    owners[name] = mod['package']
                    def_sources[name] = d
        for path in sorted((base/'Languages/English/Keyed').rglob('*.xml')):
            doc = parse(path)
            if doc is not None:
                for n in doc: english[n.tag] = ''.join(n.itertext())
    before_hashes[str(root/'About/About.xml')] = hashlib.sha256((root/'About/About.xml').read_bytes()).hexdigest()

entries = {}
counts = Counter()
credits = []

def add(kind, typ, key, value, owner, source, path):
    identity = (kind, typ, key)
    if not value.strip():
        issues['empty'].append([source, key]); return
    if key.endswith('.defName'):
        issues['identity_fields_omitted'].append([source, key]); return
    if kind == 'Keyed' and key in english:
        # Named grammar expressions may intentionally change. Positional arguments may not.
        pattern = r'(?<!\{)\{(\d+)(?=_[A-Za-z]|[,}: ?])'
        if set(re.findall(pattern, value)) != set(re.findall(pattern, english[key])):
            issues['placeholder_mismatch'].append({'source':source,'key':key,'english':english[key],'russian':value}); return
    if identity in entries:
        issues['duplicates_resolved'].append({'key': list(identity), 'previous': entries[identity]['source'], 'selected': source})
    entries[identity] = dict(owner=owner, value=value, source=source, path=str(path))
    counts[source] += 1

for source in sources['sources']:
    root = Path(source['source'])
    targets = source['targets']
    assert targets and set(targets) <= active, targets
    about_path = root/'About/About.xml'
    about = parse(about_path) if about_path.exists() else None
    credits.append({'source': source['key'], 'url': source['url'], 'author': about.findtext('author') if about is not None else 'Original project contributors', 'targets': targets})
    bases = [root/p for p in source['routes']] if 'routes' in source else routes(root)
    for base in bases:
        lang = base/'Languages/Russian'
        for path in sorted(lang.rglob('*.xml')):
            rel = path.relative_to(lang)
            if rel.parts[0] not in ('Keyed', 'DefInjected'): continue
            doc = parse(path)
            if doc is None: continue
            if doc.tag != 'LanguageData':
                issues['unexpected_root'].append(str(path)); continue
            kind = rel.parts[0]
            typ = rel.parts[1] if kind == 'DefInjected' else ''
            for n in doc:
                if not isinstance(n.tag, str): continue
                owner = targets[0]
                if kind == 'DefInjected':
                    name = n.tag.split('.')[0]
                    if name not in owners:
                        issues['absent_defs_omitted'].append([source['key'], typ, n.tag]); continue
                    owner = owners[name]
                add(kind, typ, n.tag, ''.join(n.itertext()), owner, source['key'], path)
        # Preserve native Strings/WordInfo data. They contain text, not game definitions.
        for folder in ('Strings', 'WordInfo'):
            for path in sorted((lang/folder).rglob('*.txt')):
                dest = generated/'Targets'/targets[0]/'Languages/Russian'/path.relative_to(lang)
                dest.parent.mkdir(parents=True, exist_ok=True)
                if dest.exists():
                    # WordInfo files are lists: retain each unique source line.
                    lines = list(dict.fromkeys(dest.read_text(encoding='utf-8-sig').splitlines()+path.read_text(encoding='utf-8-sig').splitlines()))
                    dest.write_text('\n'.join(lines)+'\n', encoding='utf-8')
                else: shutil.copy2(path, dest)
        # Some old translations replace entire BackstoryDefs. Import text fields only.
        for path in sorted((base/'Defs').rglob('*.xml')):
            doc = parse(path)
            if doc is None: continue
            for d in doc:
                name = d.findtext('defName')
                if not name or name not in owners: continue
                for field in ('title','titleFemale','titleShort','titleShortFemale','baseDesc','label','description'):
                    n = d.find(field)
                    if n is not None and n.text:
                        add('DefInjected', d.tag, name+'.'+field, n.text, owners[name], source['key'], path)

groups = defaultdict(list)
for (kind,typ,key), item in entries.items():
    groups[(item['owner'],kind,typ)].append((key,item))
for (owner,kind,typ), items in groups.items():
    directory = generated/'Targets'/owner/'Languages/Russian'/kind
    if typ: directory /= typ
    directory.mkdir(parents=True, exist_ok=True)
    doc = E.Element('LanguageData')
    for key,item in sorted(items): E.SubElement(doc,key).text=item['value']
    E.indent(doc)
    E.ElementTree(doc).write(directory/'GrechkaTranslations.xml',encoding='utf-8',xml_declaration=True)

load = E.Element('loadFolders')
branch = E.SubElement(load,'v1.7')
E.SubElement(branch,'li').text='/'
for directory in sorted((generated/'Targets').iterdir()):
    E.SubElement(branch,'li',{'IfModActive':directory.name}).text=str(directory.relative_to(out))
E.indent(load)
E.ElementTree(load).write(out/'LoadFolders.xml',encoding='utf-8',xml_declaration=True)

report = {'entries':len(entries),'sources':credits,'source_entry_counts':dict(counts),'target_entry_counts':dict(Counter(i['owner'] for i in entries.values())), 'issues':dict(issues), 'source_metadata_hashes':before_hashes, 'revision':sources.get('revision'), 'gameControlled':False}
args.report.parent.mkdir(parents=True,exist_ok=True)
args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
(generated/'Credits.json').write_text(json.dumps(credits,ensure_ascii=False,indent=2)+'\n')
print(json.dumps({'entries':len(entries),'target_packages':len({k[0] for k in groups}),'sources':len(credits),'issues':{k:len(v) for k,v in issues.items()}},ensure_ascii=False))
