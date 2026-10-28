import json,shutil,sqlite3,sys,tempfile,unittest,uuid
from datetime import timedelta
from pathlib import Path
from unittest.mock import patch
TOOLS=Path(__file__).resolve().parent;sys.path.insert(0,str(TOOLS))
import research_export as rx
import research_cloud_sync as cs
from test_research_export import fixture

def study_authorization(path,record):
    path.write_text(json.dumps(dict(schema='research.operator-authorization.1',participant_id=record['participant_id'],operator_id='OPERATOR_FIXTURE',
        consent_approved=True,content_approved=True,language_approved=True,consent_approval_ref='fixture',content_approval_ref='fixture',
        language_approval_ref='fixture',link_status='linked',session_id=record['session_id'])),encoding='utf-8')

class CloudSyncChecks(unittest.TestCase):
    def setUp(self):
        self.td=tempfile.TemporaryDirectory();self.base=Path(self.td.name);self.data=self.base/'ResearchData';self.cloud=self.base/'OneDrive'/'S1';self.cloud.mkdir(parents=True)
        record=fixture(self.base/'dev','AB')
        self.dev=Path(rx.ingest(self.base/'dev',self.data,record['participant_id'],record['session_id'])['bundle_path'])

    def tearDown(self):
        # TemporaryDirectory cannot remove trees past 260 chars on Windows; remove via the extended-length prefix first.
        shutil.rmtree(cs.target_root(dict(target_root=str(self.base))),ignore_errors=True);self.td.cleanup()

    def config(self,**extra):
        c=dict(schema=cs.CONFIG_SCHEMA,target_id='fixture_drive',site_code='TMU',target_root=str(self.cloud),enabled=True,allow_technical=True,**extra)
        path=self.base/('config_'+str(len(list(self.base.glob('config_*'))))+'.json');path.write_text(json.dumps(c),encoding='utf-8');return path

    def remote(self,result):return self.cloud/result['remote_path']

    def study(self):
        record=fixture(self.base/'study','BA');record.update(participant_id='TH001',group='Thai',test_data=False)
        (self.base/'study'/'research-session.json').write_text(json.dumps(record),encoding='utf-8')
        meta=json.loads((self.base/'study'/'session_metadata.json').read_text());meta.update(technical_test=False,test_data=False)
        (self.base/'study'/'session_metadata.json').write_text(json.dumps(meta),encoding='utf-8')
        auth=self.base/'auth.json';study_authorization(auth,record)
        return Path(rx.ingest(self.base/'study',self.data,'TH001',record['session_id'],authorization=auth)['bundle_path'])

    def test_without_config_nothing_leaves_the_pc(self):
        r=cs.sync(self.data,None)
        self.assertEqual([b['status'] for b in r['bundles']],['cloud_disabled']);self.assertEqual(list(self.cloud.iterdir()),[]);self.assertFalse((self.data/'.cloud-sync').exists())
        self.assertEqual(cs.status(self.data,None)[0]['local'],'pc_verified')

    def test_upload_reads_back_every_hash_and_repeat_is_idempotent(self):
        c=self.config();r=cs.sync(self.data,c)['bundles'][0]
        self.assertEqual(r['status'],'cloud_folder_verified')
        self.assertTrue(r['remote_path'].startswith('research_s1/site_TMU/device_unbound/cohort_DEV/participant_DEV900/session_fixture_session_AB/run_fixture_run_AB/bundle_v1_'))
        manifest=rx.verify(self.remote(r));self.assertEqual(manifest,rx.verify(self.dev))
        receipt=json.loads((self.remote(r).parent/(self.remote(r).name+'.cloud_receipt.json')).read_text())
        self.assertFalse(receipt['provider_upload_confirmed']);self.assertNotRegex(json.dumps(receipt),cs.SECRET_KEYS)
        self.assertEqual(cs.sync(self.data,c)['bundles'][0]['status'],'already_cloud_verified')
        self.assertEqual(cs.status(self.data,c)[0]['cloud'],'cloud_folder_verified')
        self.assertEqual(list(self.cloud.rglob('*.partial')),[])

    def test_each_headset_has_its_own_cloud_tree(self):
        c=dict(site_code='TMU',drive_folder_id='folder')
        def m(serial):return dict(bundle_id='b',identity=dict(group='Thai',participant_code='TH01',session_id='s',run_id='r'),
                                  transport_provenance=dict(profile=dict(serial=serial)) if serial else None)
        one,two=cs.remote_dir(c,m('2G0YC5ZHBF00FJ')),cs.remote_dir(c,m('2G0YC5ZHBF0077'))
        self.assertEqual(one.parts[:2],('site_TMU','device_2G0YC5ZHBF00FJ'))
        self.assertNotEqual(one.parent.parent,two.parent.parent,'TH01 on two headsets never shares a participant folder')
        self.assertEqual(cs.remote_dir(c,m(None)).parts[1],'device_unbound')

    def test_bundle_uploaded_before_device_folders_keeps_its_path(self):
        c=self.config();r=cs.sync(self.data,c)['bundles'][0]
        legacy=Path(*[p for p in Path(r['remote_path']).parts if p!='device_unbound'])
        (self.cloud/legacy).parent.mkdir(parents=True,exist_ok=True);shutil.move(str(self.remote(r)),str(self.cloud/legacy))
        receipt=cs.state_dir(self.data,cs.load_config(c))/(rx.verify(self.dev)['bundle_id']+'.json')
        old=json.loads(receipt.read_text());old['remote_path']=legacy.as_posix();receipt.write_bytes(rx.canonical(old))
        again=cs.sync(self.data,c)['bundles'][0]
        self.assertEqual((again['status'],again['remote_path']),('already_cloud_verified',legacy.as_posix()))
        self.assertFalse(self.remote(r).exists(),'not copied a second time under the new layout')

    def test_long_synced_folder_path_still_copies(self):
        self.cloud=self.base/('OneDrive - Tokyo Metropolitan University Research Data Workspace '+'x'*120)/'S1';self.cloud.mkdir(parents=True)
        r=cs.sync(self.data,self.config())['bundles'][0]
        self.assertEqual(r['status'],'cloud_folder_verified',r.get('reason'))
        self.assertGreater(len(str(self.cloud/r['remote_path']/'task_A'/'movement.csv')),260)

    def test_offline_target_queues_then_delivers(self):
        self.cloud.rmdir();c=self.config();self.assertEqual(cs.sync(self.data,c)['bundles'][0]['status'],'cloud_queued_offline')
        self.cloud.mkdir(parents=True);self.assertEqual(cs.sync(self.data,c)['bundles'][0]['status'],'cloud_folder_verified')

    def test_partial_remote_file_is_replaced_not_trusted(self):
        c=self.config();first=cs.sync(self.data,c)['bundles'][0]
        broken=self.remote(first)/'raw'/'events.jsonl';broken.write_bytes(b'truncated')
        again=cs.sync(self.data,c)['bundles'][0]
        self.assertEqual(again['status'],'cloud_folder_verified');self.assertEqual(again['files_copied'],1);rx.verify(self.remote(again))

    def test_damaged_remote_receipt_is_restored_from_local_record(self):
        c=self.config();first=cs.sync(self.data,c)['bundles'][0]
        receipt=self.remote(first).parent/(self.remote(first).name+'.cloud_receipt.json');original=receipt.read_bytes()
        receipt.write_bytes(original.replace(b'DEV900',b'DEV9O0'))
        self.assertEqual(cs.sync(self.data,c)['bundles'][0]['status'],'already_cloud_verified')
        self.assertEqual(receipt.read_bytes(),original)

    def test_valid_receipt_from_another_pc_is_left_alone(self):
        c=self.config();first=cs.sync(self.data,c)['bundles'][0]
        receipt=self.remote(first).parent/(self.remote(first).name+'.cloud_receipt.json')
        other=json.loads(receipt.read_text());other['verified_utc']='2099-01-01T00:00:00Z'
        receipt.write_text(json.dumps(other))
        self.assertEqual(cs.sync(self.data,c)['bundles'][0]['status'],'already_cloud_verified')
        self.assertEqual(json.loads(receipt.read_text())['verified_utc'],'2099-01-01T00:00:00Z')

    def test_failed_copy_backs_off_and_force_retries(self):
        c=self.config();t=cs.now()
        with patch.object(cs.shutil,'copyfile',side_effect=OSError('auth expired / server denied')):r=cs.sync(self.data,c,t=t)['bundles'][0]
        self.assertEqual((r['status'],r['attempts']),('cloud_queued_retry',1))
        self.assertEqual(cs.sync(self.data,c,t=t+timedelta(seconds=5))['bundles'][0]['status'],'cloud_queued_backoff')
        self.assertEqual(cs.status(self.data,c)[0]['cloud'],'cloud_queued_retry')
        self.assertEqual(cs.sync(self.data,c,force=True)['bundles'][0]['status'],'cloud_folder_verified')
        self.assertFalse(list((self.data/'.cloud-sync').rglob('*.pending.json')))

    def test_study_data_waits_for_complete_approval(self):
        self.study()
        r={b['participant']:b['status'] for b in cs.sync(self.data,self.config(allow_study=True))['bundles']}
        self.assertEqual(r['TH001'],'cloud_pending_approval')
        approval=dict(approved_by='PI',ethics_ref='TMU-EC-fixture',approved_utc='2026-10-16T00:00:00Z',region='JP',access_scope='research team',encryption_at_rest=True)
        r={b['participant']:b for b in cs.sync(self.data,self.config(allow_study=True,approval=approval))['bundles']}
        self.assertEqual(r['TH001']['status'],'cloud_folder_verified');self.assertIn('/cohort_TH/participant_TH001/',r['TH001']['remote_path'])

    def test_credentials_in_config_are_refused(self):
        with self.assertRaisesRegex(ValueError,'Credentials'):cs.sync(self.data,self.config(provider=dict(access_token='x')))

    def test_tampered_local_bundle_is_not_uploaded(self):
        (self.dev/'task_A'/'metrics.json').write_bytes(b'{}')
        r=cs.sync(self.data,self.config())['bundles'][0]
        self.assertEqual(r['status'],'local_invalid_not_uploaded');self.assertFalse(any(self.cloud.rglob('*.jsonl')) if self.cloud.exists() else False)

    def test_withdrawal_dry_run_then_remote_removal_keeps_local(self):
        c=self.config();first=cs.sync(self.data,c)['bundles'][0]
        dry=cs.withdraw(self.data,c,'DEV900')
        self.assertTrue(dry['targets'][0]['remote_exists']);self.assertTrue(self.remote(first).exists())
        done=cs.withdraw(self.data,c,'DEV900',execute=True)
        self.assertFalse(self.remote(first).exists());self.assertTrue(self.dev.exists());self.assertEqual(len(done['remote_removed']),1)
        audit=(self.data/'.cloud-sync'/'fixture_drive'/'_audit.jsonl').read_text().splitlines();self.assertEqual(len(audit),2)
        self.assertEqual(cs.sync(self.data,c)['bundles'][0]['status'],'cloud_folder_verified')

    def test_retention_is_a_dry_run(self):
        r=cs.retention(self.data,'2999-01-01');self.assertTrue(r['dry_run']);self.assertEqual(len(r['due']),1);self.assertTrue(self.dev.exists())
        self.assertEqual(cs.retention(self.data,'2000-01-01')['due'],[])

class DriveFolderChecks(CloudSyncChecks):
    """Pinned Drive folder: a fake Drive-for-desktop metadata DB stands in for the client and fetch() for Google's servers."""
    FOLDER='1SyntheticDriveFolderIdForTests0'
    def setUp(self):
        super().setUp();self.cloud=self.base/'G'/'My Drive'/'VR_Research_Data'/'research_s1';self.cloud.mkdir(parents=True)
        acct=self.base/'DriveFS'/'1234';acct.mkdir(parents=True);self.db=sqlite3.connect(acct/'metadata_sqlite_db')
        self.db.executescript('create table items(stable_id integer primary key,id text,local_title text,is_folder int,trashed int default 0,is_tombstone int default 0);'
                              'create table stable_parents(item_stable_id int,parent_stable_id int);')
        self.add(None,'root_id','My Drive',1);self.add(1,'parent_id','VR_Research_Data',1);self.add(2,self.FOLDER,'research_s1',1);self.db.commit()
        self.acct=acct
    def tearDown(self):self.db.close();super().tearDown()
    def add(self,parent,cid,title,folder):
        sid=self.db.execute('insert into items(id,local_title,is_folder) values(?,?,?)',(cid,title,folder)).lastrowid
        if parent:self.db.execute('insert into stable_parents values(?,?)',(sid,parent))
        return sid
    def config(self,**extra):return super().config(drive_folder_id=self.FOLDER,drivefs_account_dir=str(self.acct),**extra)
    def mirror(self,result,server=None):
        """Record every synced file under the pinned folder as the Drive client would after upload; returns id->bytes."""
        server={} if server is None else server
        def walk(local,sid):
            for child in sorted(local.iterdir()):
                cid='srv_'+uuid.uuid4().hex;new=self.add(sid,cid,child.name,child.is_dir())
                if child.is_dir():walk(child,new)
                else:server[cid]=child.read_bytes()
        walk(cs.target_root(dict(target_root=str(self.cloud))),3);self.db.commit();return server

    def test_pinned_folder_has_no_second_research_s1_level(self):
        r=cs.sync(self.data,self.config())['bundles'][0]
        self.assertTrue(r['remote_path'].startswith('site_TMU/device_unbound/cohort_DEV/participant_DEV900/'),r['remote_path'])
        receipt=json.loads((self.remote(r).parent/(self.remote(r).name+'.cloud_receipt.json')).read_text())
        self.assertEqual(receipt['target_folder_id'],self.FOLDER)

    def test_upload_reads_back_every_hash_and_repeat_is_idempotent(self):
        c=self.config();r=cs.sync(self.data,c)['bundles'][0];self.assertEqual(r['status'],'cloud_folder_verified')
        self.assertEqual(cs.sync(self.data,c)['bundles'][0]['status'],'already_cloud_verified')

    def test_provider_pending_until_server_ids_exist_then_verified_by_download(self):
        c=self.config();cs.sync(self.data,c)
        self.assertIn('provider_pending',cs.provider_verify(self.data,c)['bundles'][0]['reason'])
        server=self.mirror(None)
        with patch.object(cs,'fetch',side_effect=lambda i:server[i]):v=cs.provider_verify(self.data,c)['bundles'][0]
        self.assertEqual(v['status'],'provider_verified');self.assertEqual(cs.status(self.data,c)[0]['cloud'],'provider_verified')
        with patch.object(cs,'fetch',side_effect=AssertionError):self.assertEqual(cs.provider_verify(self.data,c)['bundles'][0]['status'],'already_provider_verified')

    def test_corrupt_server_bytes_and_revoked_access_never_pass(self):
        c=self.config();cs.sync(self.data,c);server=self.mirror(None)
        with patch.object(cs,'fetch',side_effect=lambda i:server[i]+b'x'):self.assertEqual(cs.provider_verify(self.data,c)['bundles'][0]['status'],'provider_mismatch')
        with patch.object(cs,'fetch',side_effect=OSError('HTTP Error 403')):r=cs.provider_verify(self.data,c)['bundles'][0]
        self.assertEqual(r['status'],'cloud_folder_verified');self.assertIn('403',r['reason'])

    def test_target_root_must_map_to_the_folder_id(self):
        self.cloud=self.base/'G'/'My Drive'/'elsewhere';self.cloud.mkdir(parents=True);c=self.config();cs.sync(self.data,c)
        with self.assertRaisesRegex(ValueError,'does not map'):cs.provider_verify(self.data,c)

if __name__=='__main__':unittest.main()
