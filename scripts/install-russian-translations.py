#!/usr/bin/env python3
"""Validate/install Grechka Translations Rus without editing foreign mods or controlling the game."""
from datetime import datetime
from pathlib import Path
import argparse
import hashlib
import json
import xml.etree.ElementTree as E

ROOT = Path(__file__).resolve().parents[1]
PACKAGE = 'grechka.translations.rus'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-mods-dir',type=Path,required=True)
parser.add_argument('--config-dir',type=Path,required=True)
parser.add_argument('--apply',action='store_true')
args = parser.parse_args()
module = ROOT/'mods/GrechkaTranslationsRus'
profile = ROOT/'profiles/GrechkaWorld'
config = args.config_dir/'ModsConfig.xml'
link = args.game_mods_dir/'GrechkaTranslationsRus'
assert args.game_mods_dir.is_dir() and args.config_dir.is_dir()
assert not link.exists() and not link.is_symlink() or link.is_symlink() and link.resolve()==module
meta = E.parse(module/'About/About.xml').getroot()
assert meta.findtext('packageId')==PACKAGE
assert (module/'Assemblies/GrechkaTranslationsRus.dll').is_file()
assert (module/'Translations/UiMappings.xml').is_file()
for folder in ('Defs','Patches','Textures','Sounds'):
    assert not any(p.is_dir() for p in module.rglob(folder)), 'Translation must not contain game content: '+folder
assert [p.name for p in module.rglob('*.dll')]==['GrechkaTranslationsRus.dll']
xml_files=list(module.rglob('*.xml'))
for p in xml_files: E.parse(p)
load=E.parse(module/'LoadFolders.xml').find('v1.7')
assert load is not None and list(load)[0].text=='/'
for n in load:
    assert (module/n.text.strip('/')).is_dir(), n.text

paths=[profile/'ModsConfig.xml',profile/'mods.json',config]
before={p:p.read_bytes() for p in paths}
repo_active=[n.text.lower() for n in E.fromstring(before[paths[0]]).findall('activeMods/li')]
live_active=[n.text.lower() for n in E.fromstring(before[config]).findall('activeMods/li')]
assert repo_active==live_active, 'Live profile differs from repository; refusing to overwrite unrelated changes'
assert repo_active.count(PACKAGE)<=1
assert not any(p.startswith(('ow.ru.','mewn.ru.','ru.pashka.')) for p in live_active), 'Separate translation packs need conflict review'
active_without=[p for p in repo_active if p!=PACKAGE]
first_grechka=next(p for p in active_without if p.startswith('grechka.'))
new_config={}
for p in (paths[0],config):
    text=before[p].decode('utf-8')
    if PACKAGE not in repo_active:
        anchor='    <li>'+first_grechka+'</li>'
        assert text.count(anchor)==1
        text=text.replace(anchor,'    <li>'+PACKAGE+'</li>\n'+anchor)
    new_config[p]=text.encode('utf-8')
    result=[n.text.lower() for n in E.fromstring(new_config[p]).findall('activeMods/li')]
    assert result.count(PACKAGE)==1
    assert [p for p in result if p!=PACKAGE]==active_without

manifest=json.loads(before[paths[1]])
entry={
    'id':'local:'+PACKAGE,'name':'Grechka Translations Rus','package':PACKAGE,
    'versions':['1.7'],'dependencies':[{'package':'brrainz.harmony','name':'Harmony','url':'steam://url/CommunityFilePage/2009463077'}],
    'after':[n.text.lower() for n in meta.findall('loadAfter/li')],
    'before':[n.text.lower() for n in meta.findall('loadBefore/li')],
    'incompatible':[],'source':'local','version':'0.1.0','group':'Русские переводы',
    'summary':'Отдельные русские локали и адаптер текстов настроек 1.7. Исходные моды не изменяются. Сторонние каталоги хранятся локально и не публикуются.',
    'url':'','repositoryPath':'mods/GrechkaTranslationsRus'}
if not any(m['package']==PACKAGE for m in manifest):
    index=next(i for i,m in enumerate(manifest) if m['package']==first_grechka)
    manifest.insert(index,entry)
for order,m in enumerate(manifest,1):m['order']=order
new_config[paths[1]]=(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
expected=[n.text.lower() for n in E.fromstring(new_config[paths[0]]).findall('activeMods/li')]
assert [m['package'].lower() for m in manifest]==[p for p in expected if not p.startswith('ludeon.')]
proof=json.loads((ROOT/'work/ru-install-20261106/preparation.json').read_text())
for file,digest in proof['source_metadata_hashes'].items():
    assert hashlib.sha256(Path(file).read_bytes()).hexdigest()==digest, 'Original metadata changed since preparation: '+file
print('Validated',len(xml_files),'XML files;',len(expected),'active entries; no foreign binaries, Defs or Patches')
print('Installation:',link,'->',module)
if not args.apply:
    print('Read-only validation; add --apply to install')
    raise SystemExit(0)

backup=ROOT/'work/profile-install-backups'/('translations-rus-'+datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
backup.mkdir(parents=True)
backup_names=['repository-ModsConfig.xml','repository-mods.json','installed-ModsConfig.xml']
for p,name in zip(paths,backup_names):(backup/name).write_bytes(before[p])
created=False
def atomic(path,data):
    temp=path.with_name(path.name+'.grechka-translations-tmp')
    temp.write_bytes(data)
    temp.replace(path)
try:
    assert all(p.read_bytes()==before[p] for p in paths), 'Profile changed during preparation'
    if not link.is_symlink():
        link.symlink_to(module,target_is_directory=True);created=True
    for p in paths:atomic(p,new_config[p])
    assert all(p.read_bytes()==new_config[p] for p in paths)
    assert link.resolve()==module
except BaseException:
    for p in paths:atomic(p,before[p])
    if created:link.unlink()
    raise
installation={'packageId':PACKAGE,'module':str(module),'link':str(link),'createdLink':created,'backup':str(backup),'profiles':[str(p) for p in paths],'oldActiveCount':len(repo_active),'newActiveCount':len(expected),'foreignModsModified':False,'gameControlled':False,'generatedXmlCount':len(xml_files),'assemblySha256':hashlib.sha256((module/'Assemblies/GrechkaTranslationsRus.dll').read_bytes()).hexdigest()}
(backup/'installation.json').write_text(json.dumps(installation,ensure_ascii=False,indent=2)+'\n')
(ROOT/'work/ru-install-20261106/installation.json').write_text(json.dumps(installation,ensure_ascii=False,indent=2)+'\n')
print('Installed Grechka Translations Rus. Backup:',backup)
print('RimWorld was not controlled. Restart manually to load the translation module.')
