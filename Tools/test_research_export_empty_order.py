"""Synthetic disk-fixture regression. Never modifies participant exports."""
import csv,json,tempfile,unittest
from pathlib import Path
import test_research_export as disk
import test_s1_metrics as f
import test_research_release_export as release

class EmptyOrderTests(unittest.TestCase):
 def make_source(self,root,tasks=()):
  source=root/'source';order='BA' if tasks and tasks[0]=='B' else 'AB';record=disk.fixture(source,order=order,interrupted=True)
  events=[json.loads(line) for line in (source/'events.jsonl').read_text(encoding='utf-8').splitlines()]
  events=[e for e in events if e.get('phase')!='Experiment']
  if tasks:
   events.append(release.protocol(release.V7,tasks=['A','B']))
   for index,task in enumerate(tasks):
    offset=1+20*index
    start=f.start(task);start['timestamp']=offset;start['after']['task_started_at']=offset
    state=start['after']['initial_state']
    state.update(pending_attempts=[],original_fault_active=True,user_created_issues=[],observation_accessible_targets=[])
    if task=='A':
     state['baseState']=dict(caseId='fixture-case',motherboardId='fixture-board',cpuId='fixture-cpu',coolerId='fixture-cooler',psuId='fixture-psu',caseIntact=True,motherboardSecured=True,cpuSeated=True,coolerSecured=True,psuSecured=True,atx24Connected=True,epsConnected=True,cpuFanConnected=True,frontPanelConnected=True,powerIsolated=True)
    end=f.end(time=10,task=task,reason='technical_failure');end['timestamp']+=offset
    end['after']['task_started_at']=offset;end['after']['task_ended_at']+=offset
    events.extend([start,end])
  events.sort(key=lambda e:float(e['timestamp']))
  for seq,event in enumerate(events,1):event['seq']=seq;event['run_id']=record['run_id']
  (source/'events.jsonl').write_text(''.join(json.dumps(e)+'\n' for e in events),encoding='utf-8')
  with (source/'movement.csv').open('w',newline='',encoding='utf-8') as stream:csv.DictWriter(stream,fieldnames=disk.HEADER).writeheader()
  return source,record
 def check_order(self,tasks):
  with tempfile.TemporaryDirectory() as folder:
   root=Path(folder);source,record=self.make_source(root,tasks);m=disk.module()
   hashes={name:m.file_digest(source/name) for name in m.RAW_FILES}
   result=m.ingest(source,root/'out',record['participant_id'],record['session_id'])
   qc=result['qc'];bundle=Path(result['bundle_path'])
   self.assertEqual(qc['actual_task_order'],list(tasks));self.assertEqual(qc['global_sequence_gaps'],[])
   self.assertEqual(qc['raw_movement_rows'],0);self.assertEqual(record['task_order'],'BA' if tasks and tasks[0]=='B' else 'AB')
   self.assertEqual(qc['s1_analysis_complete'],bool(tasks))
   self.assertEqual(qc['s1_warnings'],{'A':[],'B':[]})
   self.assertEqual(hashes,{name:m.file_digest(source/name) for name in m.RAW_FILES})
   self.assertEqual(hashes,{name:m.file_digest(bundle/'raw'/name) for name in m.RAW_FILES})
   self.assertEqual(m.load(bundle/'qc/report.json')['s1_analysis_complete'],bool(tasks))
   m.verify(bundle)
 def test_training_only_ab_record_is_not_analysis_complete(self):self.check_order(())
 def test_single_observed_a_retains_analysis_complete(self):self.check_order(('A',))
 def test_single_observed_b_retains_analysis_complete(self):self.check_order(('B',))
 def test_both_observed_ab_retain_analysis_complete(self):self.check_order(('A','B'))
 def test_both_observed_ba_retain_analysis_complete(self):self.check_order(('B','A'))
if __name__=='__main__':unittest.main(verbosity=2)
