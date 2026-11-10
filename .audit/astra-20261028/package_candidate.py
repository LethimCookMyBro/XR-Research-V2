"""Package the exact locally verified S1 engineering candidate, excluding secrets, data and raw font files."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = Path(r'D:\VR_TMU\S1_Delivery_Work_20261028\astra_2019')
BUILD = EVIDENCE / 'build_2111'
DEST = Path(r'D:\VR_TMU\S1_Delivery_Candidate_20261028_Astra')
APK = ROOT / 'Logs/QuestFinalQA/build/ResearchFlowQuest3_20261028_141132.apk'
EXPECTED_APK = 'ba79f9c74a68c17b1f5100a2c8796479b84d829d93f44ea6d9c88aba4fd66d08'
EXCLUDED_EXTENSIONS = {'.ttf','.otf','.woff','.woff2','.ttc','.keystore','.jks','.pfx','.p12','.pem','.key'}


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def jsonfile(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')


if digest(APK) != EXPECTED_APK:
    raise SystemExit('Candidate bytes changed; packaging refused')
DEST.mkdir(exist_ok=False)
for sub in ('APK','Source','HostTools','Verification','Guides'):
    (DEST/sub).mkdir()
shutil.copy2(APK, DEST/'APK/VRMaintenanceLab_20261028_Astra_Candidate.apk')
frozen=json.loads((BUILD/'source-inputs.json').read_text('utf-8-sig'))
source_files={}
excluded=[]
for rel,meta in frozen['files'].items():
    path=ROOT/rel
    if digest(path)!=meta['sha256']:
        raise SystemExit('Frozen source mismatch: '+rel)
    if path.suffix.lower() in EXCLUDED_EXTENSIONS:
        excluded.append({'path':rel,'reason':'Raw font or credential material is not distributed.'})
        continue
    source_files[rel]=path
for directory in ('Tools','Documentation/S1'):
    for path in (ROOT/directory).rglob('*'):
        if not path.is_file() or any(part in {'bin','obj','__pycache__','.git','node_modules'} for part in path.parts):
            continue
        if path.suffix.lower() not in {'.py','.ps1','.sh','.bat','.cmd','.md','.txt','.json','.cs','.csproj','.yml','.yaml'}:
            continue
        lower=path.name.lower()
        if any(s in lower for s in ('credential','token','secret','authorization.json','registry.json')):
            excluded.append({'path':path.relative_to(ROOT).as_posix(),'reason':'Potential live authorization/credential configuration; excluded conservatively.'})
            continue
        rel=path.relative_to(ROOT).as_posix()
        source_files[rel]=path
        if directory=='Tools':
            target=DEST/'HostTools'/path.relative_to(ROOT/'Tools')
            target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(path,target)
for name in ('AGENTS.md','CLAUDE.md','design_rule.md','README.md','.gitignore','.gitattributes'):
    path=ROOT/name
    if path.is_file():source_files[name]=path
manifest=[]
with zipfile.ZipFile(DEST/'Source/source_snapshot_without_raw_fonts.zip','w',zipfile.ZIP_DEFLATED,compresslevel=3,allowZip64=True) as archive:
    for rel,path in sorted(source_files.items()):
        archive.write(path,rel)
        manifest.append({'path':rel,'bytes':path.stat().st_size,'sha256':digest(path)})
    archive.writestr('SOURCE_SNAPSHOT_NOTICE.txt','Source snapshot includes the frozen Unity inputs and current host tools. Four original raw font files are intentionally not distributed. This is not independently rebuild-verified on a recipient PC. Obtain licensed originals through the project owner and retain their original .meta GUIDs. No researcher registry, logs, credentials or signing key is included. See excluded-source-files.json.\n')
jsonfile(DEST/'Source/source-files.json',manifest)
jsonfile(DEST/'Source/excluded-source-files.json',excluded)
jsonfile(DEST/'Source/provenance.json',{'head_at_build':frozen['head'],'branch':frozen['branch'],'uncommitted_inputs':True,'frozen_manifest':'../Verification/build/source-inputs.json','recipient_rebuild_verified':False,'source_archive_excludes_raw_fonts':True})
shutil.copytree(BUILD,DEST/'Verification/build',ignore=shutil.ignore_patterns('preserved-inputs','prior-pipeline-reports','*.unity','*.unity.meta'))
for sub,files in {
 'th_AB_P_no_runtime_probes':['run.json','cleanup.json','isolation-receipt.json'],
 'th_AB_P':['run.json','cleanup.json'],
 'ja_BA_Q_no_runtime_probes':['run.json','cleanup.json'],
 'reader_2045':['reader-production.json'],
 'operator_2045':['operator.json'],
}.items():
    for name in files:
        src=EVIDENCE/sub/name
        if src.is_file():
            target=DEST/'Verification'/sub/name
            target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(src,target)
for folder in ('reader_2045','operator_2045'):
    for path in (EVIDENCE/folder).glob('*.png'):
        target=DEST/'Verification'/folder/path.name
        target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(path,target)
# Preserve a reviewable patch, without raw binary media/font payloads or unrelated historical records.
paths=['Assets/VRMaintenanceResearch/ResearchFlow/Core/ResearchS1FanKinematics.cs',
       'Assets/VRMaintenanceResearch/ResearchFlow/Tests/ResearchS1FanKinematicsChecks.cs',
       'Tools/research_operator_authorize.py','Tools/test_research_operator_authorize.py']
patch=subprocess.check_output(['git','diff','--no-ext-diff','--',*paths],cwd=ROOT)
(DEST/'Source/reviewed_fix_diff_from_HEAD.patch').write_bytes(patch)
jsonfile(DEST/'HostTools/drive.config.example.json',{'schema':'research.cloud-config.1','target_root':'SET_LOCAL_GOOGLE_DRIVE_RESEARCH_S1_DIRECTORY','site':'SET_SITE','transport':'drive_desktop','target_label':'Google Drive research_s1','target_folder_id':'1FUh0hKzUguaG5Es4UN4FRxqRaBC76W3E','root_is_research_s1':True})
jsonfile(DEST/'CANDIDATE_MANIFEST.json',{'created_utc':datetime.now(timezone.utc).isoformat(),'status':'ENGINEERING_CANDIDATE_NOT_ACCEPTED_FOR_STUDY','apk_sha256':EXPECTED_APK,'apk_bytes':APK.stat().st_size,'package':'th.ac.tmu.vr.researchflow.quest3.technical','version_name':'3.4.0','version_code':1,'installed':False,'native_verified':False,'study_approved':False,'build_result':'Succeeded','build_errors':0,'build_warnings':671,'source_files_in_archive':len(source_files),'raw_fonts_in_archive':0,'editor_complete_session':'d046d33fcdbe49c7a8e95bb5dd34957b','drive_verified_bundle':'v1_4ba8f3d79f725ddaa23c1194','drive_bundle_folder_id':'151AUZ05QbgMhRMZMDeKENKWxH9mnxTEg','drive_scope':'New Editor simulated DEV session, 21 files; not native Quest data.'})
files=[]
for path in sorted(DEST.rglob('*')):
    if path.is_file():files.append({'path':path.relative_to(DEST).as_posix(),'bytes':path.stat().st_size,'sha256':digest(path)})
jsonfile(DEST/'FILES_SHA256.json',files)
with zipfile.ZipFile(DEST/'Source/source_snapshot_without_raw_fonts.zip') as archive:
    problem=archive.testzip()
    if problem:raise SystemExit('Archive CRC failure: '+problem)
    if any(Path(n).suffix.lower() in EXCLUDED_EXTENSIONS for n in archive.namelist()):raise SystemExit('Forbidden raw font/credential suffix in archive')
print(json.dumps({'folder':str(DEST),'files':len(files),'source_entries':len(source_files),'excluded':len(excluded),'archive_sha256':digest(DEST/'Source/source_snapshot_without_raw_fonts.zip'),'apk_sha256':EXPECTED_APK,'archive_crc':'PASS'},ensure_ascii=True))
