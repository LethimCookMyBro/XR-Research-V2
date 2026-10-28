"""Second destination for verified PC bundles: copy to an approved cloud-synced folder, read back, write receipts.

Local is always first: research_export.py writes immutable verified bundles; this tool only reads them and never
writes inside a bundle. The target is a folder kept in sync by the provider's own desktop client (OneDrive,
Google Drive for desktop) or a network share, so authentication stays with that client and no token ever reaches
this tool or its receipts. 'cloud_folder_verified' means every file was read back from the target folder with a
matching SHA-256; it does not prove the provider finished its upload.

Google Drive: set drive_folder_id to the pinned folder ID and target_root to the local Drive-for-desktop folder that maps
to it (the pinned folder is the research_s1 root, so no second research_s1 level is added). 'provider-verify' then reads
each file's server ID from the Drive client's metadata, downloads the bytes from Google's servers and compares SHA-256.
Downloads use the folder's link access, so after the owner restricts sharing this reports provider_pending, never a pass.

usage: research_cloud_sync.py sync|status|provider-verify --data-root DIR [--config FILE] [--force]
       research_cloud_sync.py withdraw --data-root DIR --config FILE --participant CODE [--execute]
       research_cloud_sync.py retention --data-root DIR --before YYYY-MM-DD
"""
import argparse,hashlib,json,os,re,shutil,sqlite3,sys,urllib.request,uuid
from datetime import datetime,timezone,timedelta
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import research_export as rx

CONFIG_SCHEMA='research.cloud-sync-config.1'
RECEIPT_SCHEMA='research.cloud-receipt.1'
APPROVAL_FIELDS=('approved_by','ethics_ref','approved_utc','region','access_scope')
SECRET_KEYS=re.compile(r'token|secret|password|passwd|credential|api_?key|client_?id|refresh',re.I)
MAX_BACKOFF_S=3600

def now():return datetime.now(timezone.utc)
def stamp(t):return t.isoformat(timespec='seconds').replace('+00:00','Z')

def load_config(path):
    if not path:return None
    c=rx.load(path)
    def walk(v):
        if isinstance(v,dict):
            for k,x in v.items():
                if SECRET_KEYS.search(k):raise ValueError('Credentials do not belong in the sync config: '+k)
                walk(x)
    walk(c)
    if c.get('schema')!=CONFIG_SCHEMA:raise ValueError('Cloud config schema mismatch')
    rx.token(c.get('target_id'));rx.token(c.get('site_code'))
    if not isinstance(c.get('target_root'),str) or not Path(c['target_root']).is_absolute():raise ValueError('target_root must be an absolute folder path')
    if 'drive_folder_id' in c and not re.fullmatch(r'[A-Za-z0-9_-]{10,}',str(c['drive_folder_id'])):raise ValueError('drive_folder_id is not a Drive folder ID')
    return c

def study_approved(c):
    a=c.get('approval') or {}
    return c.get('allow_study') is True and all(isinstance(a.get(k),str) and a[k] for k in APPROVAL_FIELDS) and a.get('encryption_at_rest') is True

def bundles(root):
    for marker in sorted(Path(root).rglob('COMPLETE.json')):
        b=marker.parent
        if any(part.startswith('.') for part in b.relative_to(root).parts) or b.parent.name=='raw':continue
        yield b

def device_folder(manifest):
    # Each headset numbers its own participants (TH01...), so two headsets at one site need separate trees (owner, 28 Oct).
    serial=((manifest.get('transport_provenance') or {}).get('profile') or {}).get('serial')
    return 'device_'+rx.token(serial) if serial else 'device_unbound'

def remote_dir(c,manifest):
    i=manifest['identity'];group=i['group']
    cohort='TH' if group=='Thai' else 'JP' if group=='Japanese' else group.upper()
    return Path(*(() if c.get('drive_folder_id') else ('research_s1',)),'site_'+c['site_code'],device_folder(manifest),'cohort_'+cohort,'participant_'+rx.token(i['participant_code']),
                'session_'+rx.token(i['session_id']),'run_'+rx.token(i['run_id']),'bundle_'+rx.token(manifest['bundle_id']))

def bundle_remote_dir(root,c,manifest):
    # A bundle already in the cloud keeps the path its immutable receipt recorded (pre-device-folder layout included).
    local=state_dir(root,c)/(manifest['bundle_id']+'.json')
    return Path(rx.load(local)['remote_path']) if local.exists() else remote_dir(c,manifest)

def target_root(c):
    # Real synced folders plus session/run/bundle ids pass Windows' 260-char path limit (WinError 206 on a DEV bundle).
    root=Path(c['target_root']).resolve();prefix='\\\\?\\'
    return Path(prefix+str(root)) if os.name=='nt' and not str(root).startswith(prefix) else root

def state_dir(root,c):
    d=Path(root)/'.cloud-sync'/c['target_id'];d.mkdir(parents=True,exist_ok=True);return d

def atomic_write(path,data):
    temp=path.with_name(path.name+'.'+uuid.uuid4().hex+'.partial')
    try:
        temp.write_bytes(data)
        if temp.read_bytes()!=data:raise OSError('Readback mismatch '+path.name)
        os.replace(temp,path)
    finally:temp.unlink(missing_ok=True)

def copy_verified(src,dst,meta):
    if dst.is_file() and dst.stat().st_size==meta['bytes'] and rx.file_digest(dst)==meta['sha256']:return False
    dst.parent.mkdir(parents=True,exist_ok=True)
    temp=dst.with_name(dst.name+'.'+uuid.uuid4().hex+'.partial')
    try:
        shutil.copyfile(src,temp)
        if rx.file_digest(temp)!=meta['sha256']:raise OSError('Cloud copy hash mismatch '+dst.name)
        os.replace(temp,dst)
    finally:temp.unlink(missing_ok=True)
    if rx.file_digest(dst)!=meta['sha256']:raise OSError('Cloud readback hash mismatch '+dst.name)
    return True

def sync_bundle(root,c,bundle,force,t):
    manifest=rx.verify(bundle);i=manifest['identity']
    base=dict(bundle_id=manifest['bundle_id'],participant=i['participant_code'],session=i['session_id'],run=i['run_id'],test_data=i['test_data'])
    if not c.get('enabled'):return dict(base,status='cloud_disabled')
    if not i['test_data'] and not study_approved(c):return dict(base,status='cloud_pending_approval')
    if i['test_data'] and c.get('allow_technical') is not True:return dict(base,status='cloud_disabled_for_technical')
    state=state_dir(root,c);pending=state/(manifest['bundle_id']+'.pending.json')
    if pending.exists() and not force:
        p=rx.load(pending)
        if datetime.fromisoformat(p['next_attempt_utc'].replace('Z','+00:00'))>t:return dict(base,status='cloud_queued_backoff',**p)
    target=target_root(c)
    if not target.is_dir():return dict(base,status='cloud_queued_offline',reason='target folder unavailable')
    rel=bundle_remote_dir(root,c,manifest);dest=target/rel
    files=dict(manifest['files'],**{n:dict(bytes=(bundle/n).stat().st_size,sha256=rx.file_digest(bundle/n)) for n in ('manifest.json','COMPLETE.json')})
    try:
        copied=[n for n,meta in sorted(files.items(),key=lambda kv:(kv[0]=='COMPLETE.json',kv[1]['bytes'],kv[0])) if copy_verified(bundle/n,dest/n,meta)]
        receipt=dict(schema_version=RECEIPT_SCHEMA,bundle_id=manifest['bundle_id'],manifest_sha256=files['manifest.json']['sha256'],
                     target_id=c['target_id'],**({'target_folder_id':c['drive_folder_id']} if c.get('drive_folder_id') else {}),remote_path=rel.as_posix(),file_count=len(files),bytes=sum(m['bytes'] for m in files.values()),
                     status='cloud_folder_verified',verified_utc=stamp(t),provider_upload_confirmed=False)
        local=state/(manifest['bundle_id']+'.json')
        if local.exists():
            previous=rx.load(local)
            if previous.get('manifest_sha256')!=receipt['manifest_sha256'] or previous.get('remote_path')!=receipt['remote_path']:raise ValueError('Immutable cloud receipt identity changed')
            if not copied:
                remote=dest.parent/(dest.name+'.cloud_receipt.json')
                try:held=json.loads(remote.read_bytes())
                except (OSError,ValueError):held=None
                # Another PC's valid receipt for the same bundle differs only in verified_utc; repair damage, never ping-pong.
                if not isinstance(held,dict) or any(held.get(k)!=receipt[k] for k in receipt if k!='verified_utc'):atomic_write(remote,local.read_bytes())
                pending.unlink(missing_ok=True)
                return dict(base,status='already_cloud_verified',remote_path=receipt['remote_path'])
        data=rx.canonical(receipt)
        atomic_write(dest.parent/(dest.name+'.cloud_receipt.json'),data);atomic_write(local,data);pending.unlink(missing_ok=True)
        return dict(base,status='cloud_folder_verified',remote_path=receipt['remote_path'],files_copied=len(copied))
    except OSError as ex:
        attempts=(rx.load(pending)['attempts'] if pending.exists() else 0)+1
        p=dict(attempts=attempts,reason=str(ex),next_attempt_utc=stamp(t+timedelta(seconds=min(MAX_BACKOFF_S,30*2**(attempts-1)))))
        atomic_write(pending,rx.canonical(p))
        return dict(base,status='cloud_queued_retry',**p)

def sync(root,config,force=False,t=None):
    c=load_config(config) or dict(enabled=False,target_id='none',site_code='none');t=t or now();results=[]
    for b in bundles(root):
        try:results.append(sync_bundle(root,c,b,force,t))
        except ValueError as ex:results.append(dict(bundle=str(b),status='local_invalid_not_uploaded',reason=str(ex)))
    return dict(target_id=c['target_id'],enabled=bool(c.get('enabled')),bundles=results)

DRIVEFS_DIR=Path(os.environ.get('LOCALAPPDATA','~'),'Google','DriveFS').expanduser()
DOWNLOAD_URL='https://drive.usercontent.google.com/download?id={}&export=download&confirm=t'

def drivefs_db(c):
    dbs=[Path(c['drivefs_account_dir'])/'metadata_sqlite_db'] if c.get('drivefs_account_dir') else sorted(DRIVEFS_DIR.glob('*/metadata_sqlite_db'))
    if len(dbs)!=1 or not dbs[0].is_file():raise ValueError('Set drivefs_account_dir: found %d Google Drive for desktop accounts'%len(dbs))
    return sqlite3.connect('file:'+dbs[0].as_posix()+'?mode=ro',uri=True,timeout=10)

def drive_children(db,stable_id,title):
    return db.execute('select i.stable_id,i.id from stable_parents p join items i on i.stable_id=p.item_stable_id '
                      'where p.parent_stable_id=? and i.local_title=? and i.trashed=0 and i.is_tombstone=0',(stable_id,title)).fetchall()

def drive_mapping(db,c):
    """Folder titles from My Drive down to drive_folder_id; target_root must end with them."""
    row=db.execute('select stable_id from items where id=? and is_folder=1 and trashed=0',(c['drive_folder_id'],)).fetchone()
    if not row:raise ValueError('drive_folder_id is not visible to this Drive for desktop account')
    titles=[];s=row[0]
    while True:
        parent=db.execute('select parent_stable_id from stable_parents where item_stable_id=?',(s,)).fetchone()
        if not parent:break
        titles.append(db.execute('select local_title from items where stable_id=?',(s,)).fetchone()[0]);s=parent[0]
    titles.reverse()
    if list(Path(c['target_root']).parts[-len(titles):])!=titles:
        raise ValueError('target_root does not map to drive_folder_id (expected a path ending in %s)'%'/'.join(titles))
    return row[0]

def fetch(file_id):
    with urllib.request.urlopen(DOWNLOAD_URL.format(file_id),timeout=120) as r:
        if 'text/html' in r.headers.get('Content-Type',''):raise OSError('Drive returned a sign-in/interstitial page, not file bytes')
        return r.read()

def provider_verify_bundle(root,c,db,folder,bundle,t):
    manifest=rx.verify(bundle);state=state_dir(root,c);local=state/(manifest['bundle_id']+'.json')
    base=dict(bundle_id=manifest['bundle_id'],participant=manifest['identity']['participant_code'])
    if not local.exists():return dict(base,status='local_saved',reason='not copied to the Drive folder yet')
    receipt=rx.load(local);proof=state/(manifest['bundle_id']+'.provider.json')
    if proof.exists() and rx.load(proof).get('manifest_sha256')==receipt['manifest_sha256']:return dict(base,status='already_provider_verified')
    files=dict(manifest['files'],**{n:dict(bytes=(bundle/n).stat().st_size,sha256=rx.file_digest(bundle/n)) for n in ('manifest.json','COMPLETE.json')})
    rel=Path(receipt['remote_path']);ids={}
    try:
        for name,meta in sorted(files.items()):
            s=folder
            for part in [*rel.parts,*Path(name).parts]:
                hits=drive_children(db,s,part)
                if len(hits)!=1:return dict(base,status='cloud_folder_verified',reason='provider_pending: %s has %d server entries'%((rel/name).as_posix(),len(hits)))
                s,file_id=hits[0]
            if file_id.startswith('local'):return dict(base,status='cloud_folder_verified',reason='provider_pending: upload not finished for '+name)
            data=fetch(file_id)
            if len(data)!=meta['bytes'] or hashlib.sha256(data).hexdigest()!=meta['sha256']:
                return dict(base,status='provider_mismatch',reason='server bytes differ for '+name,drive_file_id=file_id)
            ids[name]=file_id
    except OSError as ex:return dict(base,status='cloud_folder_verified',reason='provider_pending: '+str(ex))
    out=dict(schema_version=RECEIPT_SCHEMA,status='provider_verified',bundle_id=manifest['bundle_id'],manifest_sha256=receipt['manifest_sha256'],
             target_id=c['target_id'],target_folder_id=c['drive_folder_id'],remote_path=receipt['remote_path'],algorithm='sha256',
             file_count=len(ids),bytes=sum(files[n]['bytes'] for n in ids),drive_file_ids=ids,verified_utc=stamp(t),
             method='server download by Drive file ID; IDs resolved from Drive for desktop metadata')
    data=rx.canonical(out);atomic_write(proof,data)
    dest=target_root(c)/rel;atomic_write(dest.parent/(dest.name+'.provider_receipt.json'),data)
    return dict(base,status='provider_verified',remote_path=receipt['remote_path'],files=len(ids))

def provider_verify(root,config,t=None):
    c=load_config(config);t=t or now()
    if not c or not c.get('drive_folder_id'):raise ValueError('provider-verify needs a config with drive_folder_id')
    db=drivefs_db(c)
    try:
        folder=drive_mapping(db,c);results=[]
        for b in bundles(root):
            try:results.append(provider_verify_bundle(root,c,db,folder,b,t))
            except ValueError as ex:results.append(dict(bundle=str(b),status='local_invalid_not_uploaded',reason=str(ex)))
        return dict(target_id=c['target_id'],target_folder_id=c['drive_folder_id'],bundles=results)
    finally:db.close()

def status(root,config):
    c=load_config(config);out=[]
    for b in bundles(root):
        try:m=rx.verify(b)
        except ValueError as ex:out.append(dict(bundle=str(b),local='invalid',reason=str(ex)));continue
        cloud='cloud_disabled'
        if c:
            d=state_dir(root,c);r=d/(m['bundle_id']+'.json');p=d/(m['bundle_id']+'.pending.json')
            v=d/(m['bundle_id']+'.provider.json')
            cloud='provider_verified' if v.exists() else rx.load(r)['status'] if r.exists() else 'cloud_queued_retry' if p.exists() else 'not_synced'
        out.append(dict(bundle_id=m['bundle_id'],participant=m['identity']['participant_code'],local='pc_verified',cloud=cloud))
    return out

def withdraw(root,config,participant,execute=False):
    c=load_config(config);rx.token(participant);targets=[]
    for b in bundles(root):
        m=rx.load(b/'manifest.json')
        if m['identity']['participant_code']!=participant:continue
        rel=bundle_remote_dir(root,c,m);dest=target_root(c)/rel
        targets.append(dict(bundle_id=m['bundle_id'],local=str(b),remote=str(dest),remote_exists=dest.exists()))
        if execute and dest.exists():
            shutil.rmtree(dest);(dest.parent/(dest.name+'.cloud_receipt.json')).unlink(missing_ok=True)
            (state_dir(root,c)/(m['bundle_id']+'.json')).unlink(missing_ok=True)
    audit=dict(utc=stamp(now()),action='withdraw',participant=participant,executed=execute,remote_removed=[t['remote'] for t in targets if execute and t['remote_exists']],
               local_kept_for_researcher=[t['local'] for t in targets])
    with (state_dir(root,c)/'_audit.jsonl').open('a',encoding='utf-8') as f:f.write(json.dumps(audit,ensure_ascii=False)+'\n')
    return dict(audit,targets=targets)

def retention(root,before):
    cutoff=datetime.fromisoformat(before).replace(tzinfo=timezone.utc);due=[]
    for b in bundles(root):
        made=datetime.fromtimestamp((b/'COMPLETE.json').stat().st_mtime,timezone.utc)
        if made<cutoff:due.append(dict(bundle=str(b),created_utc=stamp(made)))
    return dict(dry_run=True,before=before,due=due,note='Deletion is a researcher action after confirming publication date (Proposal 8.7).')

def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__);s=p.add_subparsers(dest='command',required=True)
    for name in ('sync','status','provider-verify','withdraw','retention'):
        q=s.add_parser(name);q.add_argument('--data-root',required=True)
        if name!='retention':q.add_argument('--config')
    s.choices['sync'].add_argument('--force',action='store_true')
    s.choices['withdraw'].add_argument('--participant',required=True);s.choices['withdraw'].add_argument('--execute',action='store_true')
    s.choices['retention'].add_argument('--before',required=True)
    a=p.parse_args(argv)
    try:
        r=(sync(a.data_root,a.config,a.force) if a.command=='sync' else status(a.data_root,a.config) if a.command=='status'
           else provider_verify(a.data_root,a.config) if a.command=='provider-verify'
           else withdraw(a.data_root,a.config,a.participant,a.execute) if a.command=='withdraw' else retention(a.data_root,a.before))
        print(json.dumps(r,ensure_ascii=False,indent=1));return 0
    except (OSError,ValueError,KeyError,sqlite3.Error) as ex:
        print(json.dumps(dict(status='failed',reason=str(ex)),ensure_ascii=False),file=sys.stderr);return 1

if __name__=='__main__':sys.exit(main())
