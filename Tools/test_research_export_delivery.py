import importlib.util,json,pathlib,subprocess,sys,unittest,uuid,io,os
from datetime import datetime,timezone,timedelta
from contextlib import redirect_stdout,redirect_stderr
from unittest.mock import patch
TOOLS=pathlib.Path(__file__).resolve().parent;sys.path.insert(0,str(TOOLS))
spec=importlib.util.spec_from_file_location('test_research_export',TOOLS/'test_research_export.py');old=importlib.util.module_from_spec(spec);spec.loader.exec_module(old)
TARGET=pathlib.Path(os.environ.get('RESEARCH_EXPORT_TEST_TARGET',str(TOOLS/'research_export.py'))).resolve()
old.SCRIPT=TARGET
class WatchChecks(old.PullResumeChecks):
 def test_timeout_initial_connection_is_retryable(self):
  with patch.object(self.m,'profile_client',side_effect=subprocess.TimeoutExpired(['adb'],60)):
   r=self.m.pull_queue('fixture','fixture-serial',self.base/'out')
  self.assertEqual(r['status'],'queued_profile_unavailable');self.assertEqual(r['delivered'],[])
 def test_timeout_discovery_is_retryable(self):
  with patch.object(self.m,'profile_client',side_effect=subprocess.TimeoutExpired(['adb'],60)):
   r=self.m.pull_all_queues('fixture','fixture-serial',self.base/'out',self.m.REMOTE_ROOT,True)
  self.assertEqual(r['status'],'queued_profile_unavailable')
 def test_timeout_midfile_reconnect_reuses_exact_bundle(self):
  profile=self.Profile();read=profile.read
  def fail(reg,bundle,name,target):
   if name=='raw/movement.csv':raise subprocess.TimeoutExpired(['adb','content','read'],60)
   return read(reg,bundle,name,target)
  with patch.object(profile,'read',side_effect=fail):r=self.m.pull_queue('fixture','fixture-serial',self.base/'out',client=profile)
  self.assertEqual(r['status'],'partial');self.assertEqual(r['delivered'],[]);self.no_receipt(self.base/'out')
  resumed=self.pull(self.base/'out');self.assertEqual(resumed['status'],'verified')
  repeated=self.pull(self.base/'out');self.assertEqual(repeated['delivered'][0]['status'],'already_verified')
  self.assertEqual(resumed['delivered'][0]['bundle_path'],repeated['delivered'][0]['bundle_path'])
 def test_actual_watch_continues_after_timeout(self):
  now=[datetime.now(timezone.utc)]
  class Clock(datetime):
   @classmethod
   def now(cls,tz=None):return now[0]
  calls=[]
  def connect(*args):
   calls.append(args)
   if len(calls)==1:raise subprocess.TimeoutExpired(['adb'],60)
   return self.Profile()
  def sleep(seconds):now[0]+=timedelta(seconds=seconds)
  argv=['research_export.py','pull-queue','--adb','fixture','--serial','fixture-serial','--destination',str(self.base/'out'),'--watch','--lease-expires-utc',(now[0]+timedelta(seconds=6)).isoformat()]
  out=io.StringIO();err=io.StringIO()
  with patch.object(sys,'argv',argv),patch.object(self.m,'datetime',Clock),patch.object(self.m.time,'sleep',sleep),patch.object(self.m,'profile_client',side_effect=connect),redirect_stdout(out),redirect_stderr(err):status=self.m.main()
  self.assertEqual(status,0,err.getvalue());self.assertEqual(len(calls),2)
  rows=[json.loads(x) for x in out.getvalue().splitlines()]
  self.assertEqual([r['status'] for r in rows],['queued_profile_unavailable','verified','host_watch_lease_expired'])
 def test_retry_after_post_ingest_scope_loss_reuses_same_bundle(self):
  destination=self.base/'out';self.fail_scope_at=3
  def client():
   profile=self.Profile();scoped=profile.scoped
   def call(*args,**kwargs):
    value=scoped(*args,**kwargs);profile.request_log[-1]['request_id']=str(uuid.uuid4());return value
   profile.scoped=call;return profile
  first=self.m.pull_queue('fixture','fixture-serial',destination,client=client())
  self.assertEqual(first['status'],'partial');self.no_receipt(destination)
  created=list(destination.glob('quest/fixture-serial/user-10/technical/DEV900/*/v1_*'))
  self.assertEqual(len(created),1);before=(created[0]/'manifest.json').read_bytes()
  self.fail_scope_at=None;self.scope_checks=0
  second=self.m.pull_queue('fixture','fixture-serial',destination,client=client())
  self.assertEqual(second['status'],'verified')
  self.assertEqual(pathlib.Path(second['delivered'][0]['bundle_path']),created[0])
  self.assertEqual(len(list(destination.glob('quest/fixture-serial/user-10/technical/DEV900/*/v1_*'))),1)
  self.assertEqual((created[0]/'manifest.json').read_bytes(),before)
 def test_partial_receipt_write_never_publishes_final_receipt(self):
  original=pathlib.Path.write_bytes
  def diskfull(path,data):
   if '.delivery-receipts' in path.parts:
    original(path,b'{partial');raise OSError('fixture disk full during receipt')
   return original(path,data)
  destination=self.base/'out'
  with patch.object(pathlib.Path,'write_bytes',diskfull):r=self.pull(destination)
  self.assertEqual(r['status'],'partial');self.no_receipt(destination)
  resumed=self.pull(destination);self.assertEqual(resumed['status'],'verified')
 def test_ack_atomic_receipt_identity_hash_and_qc(self):
  destination=self.base/'out';profile=self.Profile();scoped=profile.scoped;seen=[]
  def acknowledge(method,**params):
   if method!='delivery_ack':return scoped(method,**params)
   path=destination/'quest/fixture-serial/user-10/.delivery-receipts/main/snapshot_fixture.json'
   receipt=self.m.load(path);ack=params['receipt'];seen.append(ack)
   self.assertEqual(params['registry'],'main');self.assertEqual(params['bundle_id'],'snapshot_fixture')
   self.assertEqual(ack['schema_version'],'researchflow.pc-receipt.1')
   self.assertEqual(ack['receipt_sha256'],self.m.file_digest(path))
   self.assertEqual(ack['source_manifest_sha256'],self.m.file_digest(self.base/'device/manifest.json'))
   self.assertEqual(ack['participant_code'],'DEV900');self.assertEqual(ack['session_id'],self.record['session_id']);self.assertEqual(ack['run_id'],self.record['run_id'])
   self.assertEqual(ack['pc_manifest_sha256'],self.m.file_digest(pathlib.Path(receipt['bundle_path'])/'manifest.json'))
   self.assertEqual(ack['qc_data_complete'],receipt['qc']['data_complete']);self.assertEqual(ack['qc_failure_count'],len(receipt['qc']['failures']))
   self.assertTrue(ack['received_utc'].endswith('Z'))
   return dict(status='received_on_device',bundle_id=params['bundle_id'],receipt_sha256=ack['receipt_sha256'])
  profile.scoped=acknowledge
  first=self.m.pull_queue('fixture','fixture-serial',destination,client=profile)
  self.assertEqual(first['delivered'][0].get('ack_status'),'received_on_device');self.assertEqual(len(seen),1)
  second=self.m.pull_queue('fixture','fixture-serial',destination,client=profile)
  self.assertEqual(second['delivered'][0]['status'],'already_verified');self.assertEqual(len(seen),2);self.assertEqual(seen[0],seen[1])
 def test_ack_timeout_retains_verified_receipt_then_reacks_without_transfer(self):
  destination=self.base/'out';profile=self.Profile();scoped=profile.scoped
  def unavailable(method,**params):
   if method=='delivery_ack':raise subprocess.TimeoutExpired(['adb'],60)
   return scoped(method,**params)
  profile.scoped=unavailable
  first=self.m.pull_queue('fixture','fixture-serial',destination,client=profile)
  self.assertEqual(first['status'],'verified');self.assertEqual(first['delivered'][0].get('ack_status'),'ack_pending')
  path=destination/'quest/fixture-serial/user-10/.delivery-receipts/main/snapshot_fixture.json';before=path.read_bytes()
  self.denied=set(self.manifest['files'])|{'manifest.json','COMPLETE.json'}
  second=self.pull(destination)
  self.assertEqual(second['delivered'][0].get('ack_status'),'received_on_device');self.assertEqual(self.reads,[]);self.assertEqual(path.read_bytes(),before)
  self.assertEqual(second['delivered'][0]['bundle_path'],first['delivered'][0]['bundle_path'])
 def test_wrong_ack_readback_does_not_claim_device_received(self):
  for reply in [None,{},dict(status='received_on_device',bundle_id='wrong',receipt_sha256='f'*64),dict(status='wrong',bundle_id='snapshot_fixture',receipt_sha256='f'*64)]:
   with self.subTest(reply=reply):
    profile=self.Profile();scoped=profile.scoped
    profile.scoped=lambda method,**params:reply if method=='delivery_ack' else scoped(method,**params)
    result=self.m.pull_queue('fixture','fixture-serial',self.base/str(uuid.uuid4()),client=profile)
    self.assertEqual(result['delivered'][0].get('ack_status'),'ack_pending')
 def test_corrupt_receipt_field_blocks_ack(self):
  for key,bad in [('pc_manifest_sha256','f'*64),('source_manifest_sha256','e'*64),('qc_failure_count',999),('received_utc','not-a-date')]:
   with self.subTest(key=key):
    destination=self.base/key;self.pull(destination)
    path=destination/'quest/fixture-serial/user-10/.delivery-receipts/main/snapshot_fixture.json';r=self.m.load(path);r[key]=bad;path.write_bytes(self.m.canonical(r))
    profile=self.Profile();scoped=profile.scoped;ack=[]
    def call(method,**params):
     if method=='delivery_ack':ack.append(params)
     return scoped(method,**params)
    profile.scoped=call
    result=self.m.pull_queue('fixture','fixture-serial',destination,client=profile)
    self.assertEqual(result['status'],'partial');self.assertEqual(result['delivered'],[]);self.assertEqual(ack,[])
 def test_legacy_verified_receipt_upgrades_and_acks(self):
  destination=self.base/'out';self.pull(destination)
  path=destination/'quest/fixture-serial/user-10/.delivery-receipts/main/snapshot_fixture.json';r=self.m.load(path)
  legacy={k:v for k,v in r.items() if k in ('job_id','queue_sha256','profile','status','bundle_path','bundle_id','actual_task_order','qc','reused')};path.write_bytes(self.m.canonical(legacy))
  result=self.pull(destination)
  self.assertEqual(result['delivered'][0].get('ack_status'),'received_on_device');self.assertEqual(result['delivered'][0]['bundle_path'],legacy['bundle_path'])
  self.assertEqual(self.m.load(path)['schema_version'],'researchflow.pc-delivery-receipt.1')
 def test_zero_microsecond_clock_emits_exact_provider_timestamp(self):
  instant=datetime(2026,10,15,12,0,0,0,tzinfo=timezone.utc)
  class Clock(datetime):
   @classmethod
   def now(cls,tz=None):return instant
  profile=self.Profile();scoped=profile.scoped;acks=[]
  def call(method,**params):
   if method=='delivery_ack':acks.append(params['receipt'])
   return scoped(method,**params)
  profile.scoped=call
  with patch.object(self.m,'datetime',Clock):result=self.m.pull_queue('fixture','fixture-serial',self.base/'out',client=profile)
  self.assertEqual(result['status'],'verified');self.assertEqual(len(acks),1)
  self.assertEqual(acks[0]['received_utc'],'2026-10-15T12:00:00.000000Z')
  path=self.base/'out/quest/fixture-serial/user-10/.delivery-receipts/main/snapshot_fixture.json'
  self.assertEqual(self.m.load(path)['received_utc'],'2026-10-15T12:00:00.000000Z')
if __name__=='__main__':unittest.main(verbosity=2)
