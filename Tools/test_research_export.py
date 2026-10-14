"""Runnable disk-output checks. Fixtures are explicitly synthetic technical data."""
import csv
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from shutil import copyfile
from unittest.mock import patch
import importlib.util

SCRIPT = Path(__file__).with_name('research_export.py')
HEADER = 'timestamp,run_id,phase,seq,frame_id,task_state,handedness,input_source,runtime,execution_environment,source,target,pose_kind,provider,tracking_valid,reason,sample_timestamp,sample_age_seconds,px,py,pz,qx,qy,qz,qw'.split(',')

def fixture(root, order='AB', header_only=False, interrupted=False, dense=False):
    root.mkdir(parents=True)
    run, session = 'fixture_run_'+order, 'fixture_session_'+order
    record = dict(participant_id='DEV900', session_id=session, run_id=run, group='Dev', test_data=True, task_order=order, session_state='Completed' if not interrupted else 'InProgress')
    (root/'research-session.json').write_text(json.dumps(record), encoding='utf-8')
    (root/'session_metadata.json').write_text(json.dumps(dict(run_id=run, technical_test=True, test_data=True, hardware_verified=False, movement_configured_max_hz=20, schema_version='handslab.events.2', maximum_pose_age_seconds=.25)), encoding='utf-8')
    events, movement = [], []
    def event(t, name, task, target='task.A', **extra):
        events.append(dict(timestamp=t, seq=0, run_id=run, event=name, task_id=task, phase='Experiment' if task in 'AB' else 'Tutorial', source='User', target=target, result='Recorded', action_id=str(len(events)), **extra))
    event(0,'RUN_STARTED','session','session');events[-1].update(phase='ResearchSession',source='System',result='technical_test')
    event(0, 'INFORMATION_OPEN', 'training', 'reference.manual')
    event(.1, 'INFORMATION_CLOSE', 'training', 'reference.manual')
    event(.2, 'EXAMINE', 'training', 'training.target')
    for i, task in enumerate(order):
        t = 1+i*20
        event(t, 'TASK_STARTED', task, 'task.'+task)
        event(t+.1, 'MOVEMENT_CONTEXT', task, after=dict(context_schema='research.movement-context.1', task_id=task, phase='Experiment', segment_id=i+1, paused=False, focused=True))
        event(t+1, 'INFORMATION_OPEN', task, 'reference.manual', after=dict(visibility_verified=True, selection_kind='automatic_default'))
        event(t+2, 'INFORMATION_CLOSE', task, 'reference.manual', reason='system_concealment')
        event(t+3, 'FORMAT_SELECTED', task, 'reference.step_by_step', after=dict(selection_kind='deliberate'))
        event(t+3, 'INFORMATION_OPEN', task, 'reference.step_by_step', after=dict(visibility_verified=True, selection_kind='deliberate'))
        event(t+4, 'INFORMATION_CLOSE', task, 'reference.step_by_step')
        event(t+4.1, 'EXAMINE', task, 'component.one')
        event(t+4.2, 'EXAMINE', task, 'component.two')
        event(t+4.3, 'EXAMINE', task, 'component.one')
        event(t+5, 'REPAIR', task, 'component.one', after=dict(wrong_action=True, components=dict(one='wrong')))
        event(t+6, 'INSPECT', task, before=dict(components=dict(one='wrong', two='original')), after=dict(components=dict(one='wrong', two='original')))
        inspect_id=events[-1]['action_id']
        event(t+6.1, 'INSPECT_FAIL', task)
        events[-1]['action_id']=inspect_id
        event(t+6.2, 'RETRY', task, reason='return_from_result')
        event(t+6.3, 'UI_CLICK_REJECTED', task)
        event(t+7, 'ACTION', task, 'component.two', after=dict(components=dict(two='replaced')))
        event(t+8, 'SESSION_PAUSED', task)
        event(t+9, 'SESSION_RESUMED', task)
        event(t+10, 'INSPECT', task, before=dict(components=dict(one='fixed', two='replaced')))
        inspect_id=events[-1]['action_id']
        event(t+10.1, 'INSPECT_PASS', task)
        events[-1]['action_id']=inspect_id
        event(t+12, 'TASK_TERMINATED' if interrupted and i==1 else 'TASK_COMPLETED', task, reason='technical_failure' if interrupted and i==1 else 'passed')
        if not header_only:
            for j, x in enumerate([0, .1, .2]):
                for target, hand in [('handslab.head','None'),('handslab.left.provider','Left'),('handslab.right.provider','Right')]:
                    row={k:'' for k in HEADER}
                    row.update(timestamp=t+.2+j*.05, run_id=run, phase='ResearchSession', seq=0, frame_id=f'frame_{i}_{j}', task_state='Task1Active' if i==0 else 'Task2Active', handedness=hand, input_source='SimulatedController', runtime='Fixture', execution_environment='Fixture', source='Tester', target=target, pose_kind='ProviderReported', provider='SyntheticFixture', tracking_valid='True', sample_timestamp=t+.2+j*.05, sample_age_seconds=0, px=x, py=0, pz=0, qx=0,qy=0,qz=0,qw=1)
                    movement.append(row)
            # A world relocation and tracking-invalid row cannot inflate provider distance.
            event(t+.36, 'MOVEMENT_CONTEXT', task, after=dict(context_schema='research.movement-context.1', task_id=task, phase='Experiment', segment_id=20+i, paused=False, focused=True, boundary_reason='teleport'))
            for target, hand in [('handslab.head','None'),('handslab.left.provider','Left'),('handslab.right.provider','Right')]:
                row=dict(movement[-1]);row.update(timestamp=t+.4, frame_id=f'frame_{i}_3', target=target, handedness=hand, sample_timestamp=t+.4, px=100);movement.append(row)
            row=dict(movement[-1]);row.update(timestamp=t+.45, frame_id=f'frame_{i}_4', tracking_valid='False', sample_timestamp='', px='',py='',pz='',qx='',qy='',qz='',qw='',reason='tracking_unavailable');movement.append(row)
    if dense and not header_only:
        for context in events:
            if context['event']=='MOVEMENT_CONTEXT':context['after']['active_task']=True
        for i,task in enumerate(order):
            t=1+i*20
            for dt,paused in [(8,True),(9,False)]:
                event(t+dt,'MOVEMENT_CONTEXT',task,after=dict(context_schema='research.movement-context.1',task_id=task,phase='Experiment',active_task=True,segment_id=40+i*2+(1 if not paused else 0),paused=paused,focused=True,boundary_reason='controlled_fixture_pause_resume'))
            existing={(row['target'],round(float(row['timestamp']),6)) for row in movement}
            for centis in range(15,1200,5):
                ts=t+centis/100
                for target in ['handslab.head','handslab.left.provider','handslab.right.provider']:
                    if (target,round(ts,6)) in existing:continue
                    row=dict(next(row for row in movement if row['target']==target))
                    x=0 if centis<25 else .1 if centis<30 else .2 if centis<40 else 100
                    row.update(timestamp=ts,seq=0,frame_id=f'dense_{i}_{centis}',sample_timestamp=ts,sample_age_seconds=0,px=x,tracking_valid='True')
                    if 800<=centis<900:row.update(tracking_valid='False',px='',py='',pz='',qx='',qy='',qz='',qw='',reason='controlled_fixture_pause')
                    movement.append(row)
    event(34,'RUN_CLOSED_INCOMPLETE' if interrupted else 'RUN_COMPLETED','session','session',reason='technical_failure' if interrupted else 'synthetic_fixture_completed')
    events[-1].update(phase='ResearchSession',source='System',result='Incomplete' if interrupted else 'Completed')
    timeline=sorted(events+movement,key=lambda e:float(e['timestamp']))
    for seq, row in enumerate(timeline,1):row['seq']=seq
    events.sort(key=lambda e:e['seq']);movement.sort(key=lambda e:e['seq'])
    (root/'events.jsonl').write_text(''.join(json.dumps(e)+'\n' for e in events),encoding='utf-8')
    with (root/'movement.csv').open('w',newline='',encoding='utf-8') as f:
        w=csv.DictWriter(f,fieldnames=HEADER);w.writeheader();w.writerows(movement)
    return record

def module():
    spec=importlib.util.spec_from_file_location('research_export',SCRIPT);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def device_bundle(root,record):
    """Same byte/hash marker contract as device Snapshot; original source remains intact."""
    m=module();raw=root/'raw';raw.mkdir()
    for name in m.RAW_FILES:(root/name).rename(raw/name)
    files={p.relative_to(root).as_posix():dict(bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in raw.iterdir()}
    manifest=dict(schema_version='researchflow.device-bundle.1',bundle_id='snapshot_fixture',identity=dict(participant_code=record['participant_id'],session_id=record['session_id'],run_id=record['run_id']),lifecycle='finalized',files=files,provenance=dict(test_data=True,simulated_presence=False,hardware_verified=False))
    (root/'manifest.json').write_bytes(m.canonical(manifest))
    (root/'COMPLETE.json').write_bytes(m.canonical(dict(schema_version='researchflow.bundle-complete.1',bundle_id='snapshot_fixture',manifest_sha256=hashlib.sha256((root/'manifest.json').read_bytes()).hexdigest())))
    return manifest

class ExportChecks(unittest.TestCase):
    def run_cli(self, source, dest, record, *args, env=None):
        return subprocess.run([sys.executable,str(SCRIPT),'ingest','--source',str(source),'--destination',str(dest),'--participant',record['participant_id'],'--session',record['session_id'],*args],capture_output=True,text=True,env=env)

    def test_disk_reopened_metrics_ab_ba_and_idempotent_immutable_delivery(self):
        # Catches Tutorial contamination, default-as-choice, Return-as-retry and teleport distance.
        for order in ['AB','BA']:
            with tempfile.TemporaryDirectory() as td:
                base=Path(td);record=fixture(base/'source',order)
                p=self.run_cli(base/'source',base/'out',record)
                self.assertEqual(p.returncode,0,p.stderr)
                report=json.loads(p.stdout);bundle=Path(report['bundle_path'])
                self.assertEqual(report['actual_task_order'],list(order))
                a=json.loads((bundle/'task_A/metrics.json').read_text())
                self.assertEqual(a['duration']['elapsed_seconds'],12)
                self.assertEqual(a['duration']['active_seconds'],11)
                self.assertEqual(a['errors']['wrong_actions'],1)
                self.assertEqual(a['errors']['inspect_attempts'],2)
                self.assertEqual(a['errors']['additional_inspects_after_first_fail'],1)
                self.assertEqual(a['information']['first_deliberate_format'],'step_by_step')
                self.assertEqual(a['information']['opens'],2)
                self.assertEqual(a['information']['verified_visible_seconds'],2)
                self.assertEqual(a['exploration']['ordered_targets'],['component.one','component.two','component.one'])
                self.assertEqual(a['exploration']['distinct_targets'],2)
                self.assertEqual(a['progress']['first_inspect_state']['components']['two'],'original')
                self.assertEqual(a['post_failure'][0]['classification'],'switch_target')
                self.assertAlmostEqual(a['movement']['sources']['handslab.head']['distance_metres'],.2)
                self.assertEqual(a['movement']['sources']['handslab.right.provider']['invalid_rows'],1)
                self.assertEqual(json.loads((bundle/'questionnaires/linked.json').read_text())['nasa_tlx']['A']['status'],'missing')
                self.assertIn('missing_video_A',json.loads((bundle/'qc/report.json').read_text())['warnings'])
                self.assertEqual((bundle/'raw/movement.csv').read_bytes(),(base/'source/movement.csv').read_bytes())
                again=self.run_cli(base/'source',base/'out',record)
                self.assertEqual(again.returncode,0,again.stderr)
                self.assertEqual(json.loads(again.stdout)['bundle_path'],str(bundle))
                self.assertEqual(len(list((base/'out/technical/DEV900'/record['session_id']).iterdir())),1)
                verify=subprocess.run([sys.executable,str(SCRIPT),'verify','--bundle',str(bundle)],capture_output=True,text=True)
                self.assertEqual(verify.returncode,0,verify.stderr)
                (bundle/'raw/movement.csv').write_bytes(b'corruption')
                verify=subprocess.run([sys.executable,str(SCRIPT),'verify','--bundle',str(bundle)],capture_output=True,text=True)
                self.assertNotEqual(verify.returncode,0)
                print(json.dumps(dict(fixture=order,expected_duration=12,actual_duration=a['duration']['elapsed_seconds'],expected_distance=.2,actual_distance=a['movement']['sources']['handslab.head']['distance_metres'],bundle=str(bundle))))

    def test_ui_policy_versions_are_carried_and_legacy_is_attributed_not_pooled(self):
        # ADVISOR_UI_AMENDMENT_20261014: fixed-panel sessions must stay distinguishable from earlier movable-panel sessions.
        for recorded in [None,'S1_FIXED_UI_DEMO_20261014']:
            with tempfile.TemporaryDirectory() as td:
                base=Path(td);record=fixture(base/'source','AB')
                if recorded:
                    record.update(ui_policy=recorded,protocol_version='S1_LOCKED_TH_BASELINE+ADVISOR_UI_AMENDMENT_20261014',
                        training_version='S1_TRAINING_FIXED_UI_20261014',layout_version='S1_LAYOUT_ENV_V2_Q5_20261014',task_a_layout='P')
                    (base/'source/research-session.json').write_text(json.dumps(record),encoding='utf-8')
                p=self.run_cli(base/'source',base/'out',record)
                self.assertEqual(p.returncode,0,p.stderr)
                bundle=Path(json.loads(p.stdout)['bundle_path'])
                manifest=json.loads((bundle/'manifest.json').read_text());qc=json.loads((bundle/'qc/report.json').read_text())
                summaries=json.loads((bundle/'task_summary.json').read_text())
                expected=recorded or 'S1_BASELINE_MOVABLE_PANELS'
                self.assertEqual(manifest['protocol_versions']['ui_policy'],expected)
                self.assertEqual(manifest['protocol_versions']['ui_policy_recorded'],recorded is not None)
                self.assertEqual(qc['protocol_versions'],manifest['protocol_versions'])
                self.assertTrue(all(row['ui_policy']==expected for row in summaries))
                if recorded:self.assertEqual(manifest['protocol_versions']['layout_version'],'S1_LAYOUT_ENV_V2_Q5_20261014')
                else:self.assertIsNone(manifest['protocol_versions']['layout_version'])

    def test_missing_movement_fails_qc_and_interrupted_outcome_is_preserved(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source',header_only=True,interrupted=True)
            p=self.run_cli(base/'source',base/'out',r)
            self.assertEqual(p.returncode,0,p.stderr)
            bundle=Path(json.loads(p.stdout)['bundle_path'])
            qc=json.loads((bundle/'qc/report.json').read_text())
            self.assertFalse(qc['movement_complete'])
            self.assertIn('missing_required_movement_A',qc['failures'])
            b=json.loads((bundle/'task_B/metrics.json').read_text())
            self.assertEqual(b['duration']['termination_status'],'technical_failure')
            self.assertEqual(b['movement']['sources']['handslab.head']['distance_metres'],None)
            print(json.dumps(dict(fixture='header-only interrupted',expected_movement_complete=False,actual=qc['movement_complete'],task_B=b['duration'])))

    def test_wrong_identity_disk_failure_partial_tail_and_source_tamper_rejected(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source')
            wrong=dict(r,participant_id='DEV901');p=self.run_cli(base/'source',base/'out',wrong)
            self.assertNotEqual(p.returncode,0)
            self.assertFalse((base/'out/technical').exists())
            env=dict(os.environ,RESEARCH_EXPORT_TEST_DISK_FAIL='1')
            p=self.run_cli(base/'source',base/'out',r,env=env)
            self.assertNotEqual(p.returncode,0)
            self.assertEqual(list((base/'out/technical/DEV900'/r['session_id']).glob('v*')),[])
            p=self.run_cli(base/'source',base/'out',r)
            self.assertEqual(p.returncode,0,p.stderr)
            with (base/'source/events.jsonl').open('a') as f:f.write('{')
            p=self.run_cli(base/'source',base/'out',r)
            self.assertNotEqual(p.returncode,0)

    def test_questionnaire_original_scale_direction_and_missingness(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source')
            links=dict(schema_version='research.questionnaire-links.1',participant_code='DEV900',session_id=r['session_id'],source_form_version='1.0 2026-06-18',nasa_tlx=dict(A=dict(status='incomplete',items=dict(mental_demand=4,physical_demand=None,temporal_demand=5,performance=20,effort=2,frustration=0))),symptoms={},interviews=[])
            path=base/'links.json';path.write_text(json.dumps(links))
            p=self.run_cli(base/'source',base/'out',r,'--questionnaires',str(path))
            self.assertEqual(p.returncode,0,p.stderr)
            bundle=Path(json.loads(p.stdout)['bundle_path']);linked=json.loads((bundle/'questionnaires/linked.json').read_text())
            self.assertEqual(linked['nasa_tlx']['A']['items']['performance'],20)
            self.assertIsNone(linked['nasa_tlx']['A']['items']['physical_demand'])
            self.assertNotIn('score',linked['nasa_tlx']['A'])
            links['nasa_tlx']['A']['items']['effort']=21;path.write_text(json.dumps(links))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--questionnaires',str(path)).returncode,0)

    def test_host_disconnect_reconnect_verified_receipt_and_wrong_identity_queue(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'device');device_bundle(base/'device',r);m=module()
            job=dict(schema_version='researchflow.export_queue.1',bundle_id='snapshot_fixture',participant_code='DEV900',session_id=r['session_id'],run_id=r['run_id'],export_path='/storage/emulated/10/Android/data/th.ac.tmu.vr.researchflow.quest3.technical/files/ResearchSessionRegistry/Exports/snapshot_fixture')
            connected=False;user=10
            class Profile:
                def __init__(self):
                    if not connected:raise ConnectionError('fixture disconnected')
                    self.bound=dict(serial='2G0YC5ZHBF00FJ',android_user=user,package='th.ac.tmu.vr.researchflow.quest3.technical',uid=user*100000+10158,pid=27306 if user else 9372,process_start_ticks='fixture-birth',boot_id='fixture-boot',build_sha256='7'*64,apk_path='/data/app/fixture/base.apk',foreground_activity='fixture',instance_id='fixture',session_id=r['session_id'],run_id=r['run_id'])
                    self.request_log=[]
                def check(self):return None
                def scope_check(self):return None
                def scoped(self,method,**params):
                    self.request_log.append(dict(method=method,request_id='fixture-queue-request',binding=dict(self.bound)))
                    if method=='queues':return [job]
                    raise AssertionError(method)
                def read(self,registry,bundle,name,target):
                    self.request_log.append(dict(method='read_grant',request_id='fixture-'+name.replace('/','_'),binding=dict(self.bound)))
                    target=Path(target);target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes((base/'device'/name).read_bytes())
            with patch.object(m,'profile_client',side_effect=lambda *args:Profile()):
                disconnected=m.pull_queue('fixture','2G0YC5ZHBF00FJ',base/'out')
                self.assertEqual(disconnected['status'],'queued_profile_unavailable')
                connected=True;job['run_id']='wrong_initial_run'
                wrong_run=m.pull_queue('fixture','2G0YC5ZHBF00FJ',base/'out')
                self.assertEqual(wrong_run['status'],'partial',wrong_run);self.assertEqual(wrong_run['delivered'],[])
                job['run_id']=r['run_id'];job['bundle_id']='wrong_initial_bundle'
                wrong_bundle=m.pull_queue('fixture','2G0YC5ZHBF00FJ',base/'out')
                self.assertEqual(wrong_bundle['status'],'partial',wrong_bundle)
                job['bundle_id']='snapshot_fixture'
                reconnected=m.pull_queue('fixture','2G0YC5ZHBF00FJ',base/'out')
                self.assertEqual(reconnected['status'],'verified',reconnected)
                self.assertEqual(reconnected['delivered'][0]['status'],'verified_on_pc')
                bundle10=Path(reconnected['delivered'][0]['bundle_path'])
                self.assertIn('user-10',bundle10.parts)
                manifest10=m.verify(bundle10);self.assertEqual(manifest10['transport_provenance']['binding']['android_user'],10)
                for name in m.RAW_FILES:self.assertEqual((bundle10/'raw'/name).read_bytes(),(base/'device/raw'/name).read_bytes())
                repeated=m.pull_queue('fixture','2G0YC5ZHBF00FJ',base/'out')
                self.assertEqual(repeated['delivered'][0]['status'],'already_verified')
                user=0;job['export_path']=job['export_path'].replace('/10/','/0/')
                primary=m.pull_queue('fixture','2G0YC5ZHBF00FJ',base/'out')
                self.assertEqual(primary['status'],'verified',primary)
                bundle0=Path(primary['delivered'][0]['bundle_path']);self.assertIn('user-0',bundle0.parts);self.assertNotEqual(bundle0,bundle10)
                job['participant_code']='DEV901'
                tampered=m.pull_queue('fixture','2G0YC5ZHBF00FJ',base/'out')
                self.assertEqual(tampered['status'],'partial',tampered);self.assertEqual(tampered['delivered'],[])
                print(json.dumps(dict(fixture='host reconnect and profile isolation',disconnected=disconnected['status'],reconnected=reconnected['status'],repeated=repeated['delivered'][0]['status'],tampered_identity=tampered['status'],profile_paths_distinct=bundle0!=bundle10)))

    def test_unverified_visibility_and_nonpass_outcomes_remain_explicit(self):
        m=module()
        for reason in ['time_limit','withdrawal','safety_stop','technical_failure','other']:
            rows=[dict(timestamp=0,seq=1,event='TASK_STARTED',task_id='A',phase='Experiment'),dict(timestamp=5,seq=2,event='TASK_TERMINATED',task_id='A',phase='Experiment',reason=reason)]
            d=m.duration(rows,rows);self.assertEqual(d['termination_status'],reason);self.assertEqual(d['elapsed_seconds'],5)
        rows=[dict(timestamp=0,seq=1,event='INFORMATION_OPEN',target='reference.manual',source='User',action_id='a'),dict(timestamp=5,seq=2,event='INFORMATION_CLOSE',target='reference.manual',source='System',action_id='a')]
        result=m.information(rows);self.assertIsNone(result['first_deliberate_format']);self.assertIsNone(result['verified_visible_seconds']);self.assertEqual(result['potential_open_seconds'],5)

    def test_study_and_private_questionnaire_fields_are_guarded(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source')
            links=dict(schema_version='research.questionnaire-links.1',participant_code='DEV900',session_id=r['session_id'],source_form_version='1.0 2026-06-18',nasa_tlx={},symptoms={},interviews=[],full_name='must not export')
            path=base/'links.json';path.write_text(json.dumps(links))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--questionnaires',str(path)).returncode,0)
            del links['full_name'];links['nasa_tlx']['A']=dict(status='missing',items={},full_name='must not export');path.write_text(json.dumps(links))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--questionnaires',str(path)).returncode,0)
            del links['nasa_tlx']['A']['full_name'];links['researcher']=dict(task_status=dict(full_name='must not export'));path.write_text(json.dumps(links))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--questionnaires',str(path)).returncode,0)

    def test_system_concealment_reopen_is_not_user_information_return(self):
        m=module()
        rows=[dict(timestamp=0,seq=1,event='INFORMATION_OPEN',target='reference.manual',source='User',action_id='a',after=dict(visibility_verified=True,selection_kind='automatic_default')),
              dict(timestamp=1,seq=2,event='INFORMATION_CLOSE',target='reference.manual',source='System',action_id='a',reason='system_concealment'),
              dict(timestamp=2,seq=3,event='INFORMATION_OPEN',target='reference.manual',source='System',action_id='b',after=dict(visibility_verified=True,selection_kind='system_resume')),
              dict(timestamp=3,seq=4,event='INFORMATION_CLOSE',target='reference.manual',source='User',action_id='b')]
        info=m.information(rows)
        self.assertEqual(info['returns'],0)
        self.assertEqual(info['opens'],1)
        self.assertEqual(info['visible_intervals'],2)
        self.assertEqual(info['system_concealments'],1)

    def test_original_and_reader_video_events_do_not_invent_deliberate_selection(self):
        m=module()
        rows=[dict(timestamp=i, seq=i+1, event=name, target='reference.video', source='System', action_id='video_'+str(i), after=dict(selection_kind='automatic_default'))
              for i,name in enumerate(['FORMAT_SELECTED','VIDEO_PLAYBACK_STARTED','REFERENCE_VIDEO_PREPARED','REFERENCE_VIDEO_SEEK_COMPLETED','REFERENCE_VIDEO_ENDED'])]
        info=m.information(rows)
        self.assertIsNone(info['first_deliberate_format'])
        self.assertEqual(len(info['video_playback_intervals']),4)
        self.assertEqual(info['intervals'],[])
        self.assertIsNone(info['potential_open_seconds'])
        self.assertIsNone(info['verified_visible_seconds'])
        self.assertEqual(info['visibility_status'],'unverified_or_incomplete')
        self.assertEqual(info['deliberate_selections'],[])

    def test_information_missing_and_complete_intervals_preserve_missingness_and_zero(self):
        m=module()
        opened=dict(timestamp=0,seq=1,event='INFORMATION_OPEN',target='reference.manual',source='User',action_id='a',after=dict(visibility_verified=True))
        closed=dict(timestamp=2,seq=2,event='INFORMATION_CLOSE',target='reference.manual',source='User',action_id='a')
        unmatched=dict(timestamp=3,seq=3,event='INFORMATION_OPEN',target='reference.images',source='User',action_id='b',after=dict(visibility_verified=True))
        for case,rows,seconds,status in [
            ('empty',[],None,'unverified_or_incomplete'),
            ('missing_close',[opened],None,'unverified_or_incomplete'),
            ('closed_and_missing_close',[opened,closed,unmatched],None,'unverified_or_incomplete'),
            ('observed_zero',[opened,dict(closed,timestamp=0)],0,'verified')]:
            with self.subTest(case=case):
                info=m.information(rows)
                self.assertEqual(info['visibility_status'],status)
                if seconds is None:
                    self.assertIsNone(info['potential_open_seconds'])
                    self.assertIsNone(info['verified_visible_seconds'])
                else:
                    self.assertEqual(info['potential_open_seconds'],seconds)
                    self.assertEqual(info['verified_visible_seconds'],seconds)

    def test_reader_ready_intervals_exclude_loading_seek_and_hidden(self):
        m=module()
        rows=[]
        def row(t,name,action,**after):
            rows.append(dict(timestamp=t,seq=len(rows)+1,event=name,target='reference.video',source='System',action_id=action,after=after))
        row(0,'REFERENCE_VIEWER_BOUND','bind',status='Loading')
        row(2,'REFERENCE_VIDEO_PREPARED','prepare',status='Loading')
        row(3,'REFERENCE_VIDEO_FIRST_FRAME_DECODED','frame',status='Ready')
        row(3,'INFORMATION_OPEN','read1',visibility_verified=True,selection_kind='automatic_default')
        row(5,'INFORMATION_CLOSE','read1',status='PlayRequested')
        row(5,'REFERENCE_VIDEO_PLAY_REQUESTED','play',status='PlayRequested')
        row(6,'REFERENCE_VIDEO_PLAYING','play',status='Playing')
        row(6,'INFORMATION_OPEN','read2',visibility_verified=True,selection_kind='system_resume')
        row(8,'INFORMATION_CLOSE','read2',status='Seeking')
        row(8,'REFERENCE_VIDEO_SEEK_REQUESTED','seek',status='Seeking')
        row(10,'REFERENCE_VIDEO_SEEK_COMPLETED','seek',status='Paused')
        row(10,'INFORMATION_OPEN','read3',visibility_verified=True,selection_kind='system_resume')
        row(11,'INFORMATION_CLOSE','read3',status='Hidden')
        info=m.information(rows)
        self.assertEqual(info['verified_visible_seconds'],5)
        self.assertEqual([(i['start'],i['end']) for i in info['intervals']],[(3,5),(6,8),(10,11)])
        self.assertIsNone(info['first_deliberate_format'])
        self.assertEqual(info['opens'],0)

    def test_video_link_import_rejects_private_document_fields(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source')
            links=dict(schema_version='research.video-links.1',participant_code='DEV900',session_id=r['session_id'],tasks=dict(A=dict(status='missing',consent='unknown',gaps=[])),full_name='must not export')
            path=base/'videos.json';path.write_text(json.dumps(links))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--video-links',str(path)).returncode,0)
            del links['full_name'];links['tasks']['A']['consent_document']='must not export';path.write_text(json.dumps(links))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--video-links',str(path)).returncode,0)

    def test_device_manifest_identity_and_movement_header_are_validated(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source');m=module();manifest=device_bundle(base/'source',r)
            manifest['identity']['run_id']='wrong_run'
            (base/'source/manifest.json').write_bytes(m.canonical(manifest))
            (base/'source/COMPLETE.json').write_bytes(m.canonical(dict(bundle_id='snapshot_fixture',manifest_sha256=hashlib.sha256((base/'source/manifest.json').read_bytes()).hexdigest())))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r).returncode,0)

    def test_video_measured_link_preserves_original_and_creates_verified_720p(self):
        import shutil
        ffmpeg,ffprobe=shutil.which('ffmpeg'),shutil.which('ffprobe')
        self.assertIsNotNone(ffmpeg,'Installed FFmpeg required for local derivative check')
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source');original=base/'synthetic_video.mp4'
            p=subprocess.run([ffmpeg,'-v','error','-f','lavfi','-i','color=c=blue:s=1920x1080:r=5','-t','1','-c:v','libx264','-y',str(original)],capture_output=True,text=True)
            self.assertEqual(p.returncode,0,p.stderr)
            links=dict(schema_version='research.video-links.1',participant_code='DEV900',session_id=r['session_id'],tasks=dict(A=dict(status='recorded',consent='granted',original_path=str(original),original_sha256=hashlib.sha256(original.read_bytes()).hexdigest(),required_720p=True,clock_mapping=dict(measured_pairs=[dict(video_seconds=0,logger_seconds=10),dict(video_seconds=1,logger_seconds=11.01)],uncertainty_seconds=.02),gaps=[]),B=dict(status='refused',consent='refused',clock_mapping=None,gaps=[])))
            path=base/'videos.json';path.write_text(json.dumps(links));p=self.run_cli(base/'source',base/'out',r,'--video-links',str(path))
            self.assertEqual(p.returncode,0,p.stderr)
            bundle=Path(json.loads(p.stdout)['bundle_path']);link=json.loads((bundle/'task_A/video-link.json').read_text())
            self.assertEqual((bundle/'raw/video_A.mp4').read_bytes(),original.read_bytes())
            derivative=bundle/'task_A/video_720p.mp4';self.assertTrue(derivative.is_file())
            dims=json.loads(subprocess.run([ffprobe,'-v','error','-select_streams','v:0','-show_entries','stream=width,height','-of','json',str(derivative)],capture_output=True,text=True,check=True).stdout)['streams'][0]
            self.assertEqual(dims,dict(width=1280,height=720))
            self.assertAlmostEqual(link['clock_mapping']['logger_seconds_per_video_second'],1.01)
            self.assertEqual(link['clock_mapping']['uncertainty_seconds'],.02)
            links['tasks']['A']['consent']='refused';path.write_text(json.dumps(links))
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--video-links',str(path)).returncode,0)
            print(json.dumps(dict(fixture='synthetic 1080p video',original_bytes=original.stat().st_size,derivative=dims,clock_mapping=link['clock_mapping'])))

    def test_host_watch_requires_expiring_bounded_lease(self):
        from datetime import datetime,timezone,timedelta
        m=module();now=datetime.now(timezone.utc)
        self.assertRaises(ValueError,m.lease_deadline,None,now)
        self.assertRaises(ValueError,m.lease_deadline,(now+timedelta(hours=19)).isoformat(),now)
        self.assertRaises(ValueError,m.lease_deadline,(now-timedelta(seconds=1)).isoformat(),now)
        self.assertEqual(m.lease_deadline((now+timedelta(hours=1)).isoformat(),now),now+timedelta(hours=1))

    def test_isolated_queue_roots_are_package_bounded(self):
        m=module();base=m.REMOTE_ROOT.removesuffix('/ResearchSessionRegistry')
        isolated=base+'/TechnicalScenarios/'+'a'*32+'/registry'
        self.assertEqual(m.registry_root(isolated),isolated)
        self.assertEqual(m.registry_root(isolated.replace('/sdcard','/storage/emulated/0')),isolated)
        for root in ['/sdcard/Downloads','/sdcard/Android/data/other/files/ResearchSessionRegistry',base+'/TechnicalScenarios/../registry',base+'/TechnicalScenarios/unsafe;command/registry']:
            self.assertRaises(ValueError,m.registry_root,root)

    def test_a_few_valid_rows_cannot_claim_complete_task_coverage(self):
        # Catches superficial nonzero-row passes despite an 11.6-second absent sampling tail.
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source');p=self.run_cli(base/'source',base/'out',r)
            self.assertEqual(p.returncode,0,p.stderr)
            bundle=Path(json.loads(p.stdout)['bundle_path']);qc=json.loads((bundle/'qc/report.json').read_text())
            self.assertFalse(qc['data_complete'])
            self.assertFalse(qc['movement_complete'])
            self.assertIn('insufficient_movement_coverage_A_handslab.head',qc['failures'])
            a=json.loads((bundle/'task_A/metrics.json').read_text())
            self.assertAlmostEqual(a['movement']['sources']['handslab.head']['tail_gap_seconds'],11.6)

    def test_ready_and_checkpoint_context_does_not_pollute_task_movement(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source');m=module();raw,events,rows,metadata,_,_=m.read_raw(base/'source')
            rows=list(rows)
            ready=dict(timestamp=.5,seq=0,run_id=r['run_id'],event='MOVEMENT_CONTEXT',task_id='A',phase='Experiment',after=dict(context_schema='research.movement-context.1',task_id='A',phase='Experiment',active_task=False,segment_id=99,paused=False,focused=True))
            pose=dict(rows[0]);pose.update(timestamp=.6,seq=0,px=999,frame_id='ready_frame')
            all_rows=sorted(events+rows+[ready,pose],key=lambda e:float(e['timestamp']))
            for seq,row in enumerate(all_rows,1):row['seq']=seq
            events=[row for row in all_rows if 'event' in row];rows=[row for row in all_rows if 'frame_id' in row]
            a=m.metrics('A',events,rows,metadata)
            self.assertEqual(a['movement']['sources']['handslab.head']['rows'],4)

    def test_study_import_requires_bound_operator_approval_identity(self):
        # Offline authorization contract fixture only; no real study registry record is created.
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);r=fixture(base/'source',header_only=True);r.update(participant_id='TH99',group='Thai',test_data=False)
            (base/'source/research-session.json').write_text(json.dumps(r))
            metadata=json.loads((base/'source/session_metadata.json').read_text());metadata.update(test_data=False,technical_test=False);(base/'source/session_metadata.json').write_text(json.dumps(metadata))
            p=self.run_cli(base/'source',base/'out',r);self.assertNotEqual(p.returncode,0)
            auth=dict(schema='research.operator-authorization.1',operator_id='fixture_operator',participant_id='TH99',session_id=r['session_id'],link_status='linked',consent_approved=True,content_approved=True,language_approved=True,consent_approval_ref='synthetic_contract_only',content_approval_ref='synthetic_contract_only',language_approval_ref='synthetic_contract_only')
            path=base/'authorization.json';path.write_text(json.dumps(auth));p=self.run_cli(base/'source',base/'out',r,'--authorization',str(path))
            self.assertEqual(p.returncode,0,p.stderr)
            auth['consent_approved']=False;path.write_text(json.dumps(auth));self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--authorization',str(path)).returncode,0)
            auth.update(consent_approved=True,link_status='pre_session',session_id='',record_revision=0)
            path.write_bytes(b'\xef\xbb\xbf'+json.dumps(auth).encode('utf-8'))
            link=dict(schema='research.operator-session-link.1',authorization_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),operator_id='fixture_operator',participant_id='TH99',session_id=r['session_id'],run_id=r['run_id'],linked_at='2026-10-02T00:00:00Z')
            link_path=base/'operator-link.json';link_path.write_text(json.dumps(link))
            p=self.run_cli(base/'source',base/'out',r,'--authorization',str(path),'--authorization-link',str(link_path))
            self.assertEqual(p.returncode,0,p.stderr)
            path.write_bytes(path.read_bytes()+b'\n')
            self.assertNotEqual(self.run_cli(base/'source',base/'out',r,'--authorization',str(path),'--authorization-link',str(link_path)).returncode,0)

    def test_dense_scheduled_ab_ba_have_coverage_and_measured_twenty_hz(self):
        for order in ['AB','BA']:
            with tempfile.TemporaryDirectory() as td:
                base=Path(td);r=fixture(base/'source',order,dense=True);p=self.run_cli(base/'source',base/'out',r)
                self.assertEqual(p.returncode,0,p.stderr)
                bundle=Path(json.loads(p.stdout)['bundle_path']);qc=json.loads((bundle/'qc/report.json').read_text())
                self.assertTrue(qc['data_complete'],qc)
                for task in 'AB':
                    d=json.loads((bundle/('task_'+task)/'metrics.json').read_text())
                    self.assertAlmostEqual(d['movement']['sources']['handslab.head']['effective_scheduled_hz'],20)
                    self.assertAlmostEqual(d['movement']['sources']['handslab.head']['distance_metres'],.2)
                    self.assertEqual(d['movement']['sources']['handslab.head']['scheduled_frames'],237)

    def test_near_target_repeat_needs_authored_location_and_observed_reentry(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);fixture(base/'source');m=module();_,events,rows,metadata,_,_=m.read_raw(base/'source')
            rows=list(rows)
            target=dict(timestamp=1.05,seq=0,event='TARGET_POSE',task_id='A',phase='Experiment',target='component.one',after=dict(position=[.1,0,0],near_radius_metres=.04))
            extra=dict(next(row for row in rows if row['target']=='handslab.head'));extra.update(timestamp=1.35,seq=0,sample_timestamp=1.35,px=.1,frame_id='reentry')
            all_rows=sorted(events+rows+[target,extra],key=lambda e:float(e['timestamp']))
            for seq,row in enumerate(all_rows,1):row['seq']=seq
            events=[r for r in all_rows if 'event' in r];rows=[r for r in all_rows if 'frame_id' in r]
            a=m.metrics('A',events,rows,metadata)
            self.assertEqual(a['movement']['sources']['handslab.head']['repeated_near_target_visits'],{'component.one':1})


    def test_m07_speed_disk_summary_ab_ba(self):
        for order in ('AB','BA'):
            with self.subTest(order=order), tempfile.TemporaryDirectory() as td:
                base=Path(td);record=fixture(base/'source',order)
                input_hashes={name:hashlib.sha256((base/'source'/name).read_bytes()).hexdigest() for name in module().RAW_FILES}
                print(json.dumps(dict(speed_fixture=order,input_hashes=input_hashes),sort_keys=True))
                p=self.run_cli(base/'source',base/'out',record)
                self.assertEqual(p.returncode,0,p.stderr)
                bundle=Path(json.loads(p.stdout)['bundle_path'])
                for task in 'AB':
                    m=json.loads((bundle/('task_'+task)/'metrics.json').read_text())
                    for source in module().SOURCES:
                        value=m['movement']['sources'][source]
                        self.assertAlmostEqual(value['speed_mean_metres_per_second'],2)
                        self.assertAlmostEqual(value['speed_max_metres_per_second'],2)
                        self.assertEqual(value['speed_interval_count'],2)
                        self.assertAlmostEqual(value['speed_interval_seconds'],.1)
                        self.assertAlmostEqual(value['distance_metres'],.2)
                    self.assertEqual(m['definition_version'],'researchflow.task-metrics.3')
                    self.assertEqual(m['movement']['definition']['speed']['units'],'metres_per_second')
                self.assertEqual((bundle/'raw/movement.csv').read_bytes(),(base/'source/movement.csv').read_bytes())
                self.assertEqual(module().verify(bundle)['definition_version'],'researchflow.task-metrics.3')
                print(json.dumps(dict(speed_fixture=order,mean_m_s=2,max_m_s=2,eligible_intervals=2,teleport_excluded=True)))

    def test_m07_speed_missing_pairs_and_stationary(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);fixture(base/'source');m=module()
            _,events,movement,metadata,_,_=m.read_raw(base/'source');original_rows=list(movement)
            for mode in ('invalid','stale','isolated','zero_dt','large_gap','stationary'):
                with self.subTest(mode=mode):
                    rows=[dict(r) for r in original_rows];head_index=0
                    for row in rows:
                        if row['target']!='handslab.head':continue
                        if mode=='invalid':row['tracking_valid']='False'
                        elif mode=='stale':row['sample_age_seconds']='1'
                        elif mode=='zero_dt':row['timestamp']='1.2';row['sample_timestamp']='1.2'
                        elif mode=='large_gap':row['timestamp']=str(1.2+head_index*.3);row['sample_timestamp']=row['timestamp']
                        elif mode=='stationary':row['px']='0'
                        head_index+=1
                    if mode=='isolated':rows=[r for r in rows if r['target']!='handslab.head' or float(r['timestamp']) in (1.2,1.4)]
                    timeline=sorted([dict(e) for e in events]+rows,key=lambda r:float(r['timestamp']))
                    for seq,row in enumerate(timeline,1):row['seq']=seq
                    scoped_events=[r for r in timeline if 'event' in r];rows=[r for r in timeline if 'frame_id' in r]
                    value=m.metrics('A',scoped_events,rows,metadata)['movement']['sources']['handslab.head']
                    if mode in ('isolated','zero_dt','large_gap'):self.assertGreaterEqual(value['valid_rows'],2)
                    if mode=='large_gap':self.assertGreater(value['max_gap_seconds'],.25)
                    if mode=='stationary':
                        self.assertEqual(value['speed_mean_metres_per_second'],0)
                        self.assertEqual(value['speed_max_metres_per_second'],0)
                        self.assertEqual(value['speed_interval_count'],2)
                    else:
                        self.assertIsNone(value['speed_mean_metres_per_second'])
                        self.assertIsNone(value['speed_max_metres_per_second'])
                        self.assertIsNone(value['speed_interval_seconds'])
                        self.assertEqual(value['speed_interval_count'],0)
                    print(json.dumps(dict(speed_case=mode,mean=value['speed_mean_metres_per_second'],maximum=value['speed_max_metres_per_second'],pairs=value['speed_interval_count'])))

    def test_m07_speed_unequal_intervals(self):
        with tempfile.TemporaryDirectory() as td:
            base=Path(td);fixture(base/'source');m=module()
            _,events,movement,metadata,_,_=m.read_raw(base/'source');rows=list(movement)
            for row in rows:
                if row['frame_id']=='frame_0_2':
                    row['timestamp']='1.35';row['sample_timestamp']='1.35'
            timeline=sorted([dict(e) for e in events]+rows,key=lambda r:float(r['timestamp']))
            for seq,row in enumerate(timeline,1):row['seq']=seq
            scoped_events=[r for r in timeline if 'event' in r];rows=[r for r in timeline if 'frame_id' in r]
            values=m.metrics('A',scoped_events,rows,metadata)['movement']['sources']
            for source in m.SOURCES:
                value=values[source]
                self.assertAlmostEqual(value['speed_mean_metres_per_second'],4/3)
                self.assertAlmostEqual(value['speed_max_metres_per_second'],2)
                self.assertEqual(value['speed_interval_count'],2)
                self.assertAlmostEqual(value['speed_interval_seconds'],.15)
                self.assertAlmostEqual(value['distance_metres'],.2)
                print(json.dumps(dict(speed_case='unequal_intervals',source=source,mean=value['speed_mean_metres_per_second'],maximum=value['speed_max_metres_per_second'],pairs=value['speed_interval_count'],seconds=value['speed_interval_seconds'])))

class PullResumeChecks(unittest.TestCase):
    def setUp(self):
        self.directory=tempfile.TemporaryDirectory();self.addCleanup(self.directory.cleanup)
        self.base=Path(self.directory.name);self.record=fixture(self.base/'device',dense=True)
        self.manifest=device_bundle(self.base/'device',self.record);self.m=module()
        self.job=dict(schema_version='researchflow.export_queue.1',bundle_id='snapshot_fixture',participant_code='DEV900',session_id=self.record['session_id'],run_id=self.record['run_id'])
        self.reads=[];self.denied=set();self.fail_name=None;self.fail_scope_at=None;self.scope_checks=0
        owner=self
        class Profile:
            def __init__(self):
                self.bound=dict(serial='fixture-serial',android_user=10,package='th.ac.tmu.vr.researchflow.quest3.technical',uid=1010158,pid=27306,process_start_ticks='fixture-birth',boot_id='fixture-boot',build_sha256='7'*64,apk_path='/data/app/fixture/base.apk',foreground_activity='fixture',instance_id='fixture',session_id=owner.record['session_id'],run_id=owner.record['run_id'])
                self.request_log=[]
            def scope_check(self):
                owner.scope_checks+=1
                if owner.scope_checks==owner.fail_scope_at:raise ConnectionError('fixture scope lost')
            def scoped(self,method,**params):
                owner.assertEqual((method,params),('queues',dict(registry='main')))
                self.request_log.append(dict(method=method,binding=dict(self.bound)))
                return [owner.job]
            def read(self,registry,bundle,name,target):
                owner.assertEqual((registry,bundle),('main','snapshot_fixture'))
                owner.reads.append(name)
                if name in owner.denied:raise ConnectionError('completed immutable cache unexpectedly re-read: '+name)
                if name==owner.fail_name:raise ConnectionError('fixture disconnected during '+name)
                self.request_log.append(dict(method='read_grant',name=name,binding=dict(self.bound)))
                target=Path(target);target.parent.mkdir(parents=True,exist_ok=True)
                stage=target.with_name(target.name+'.fixture-partial')
                copyfile(owner.base/'device'/name,stage);stage.replace(target)
        self.Profile=Profile

    def cache(self,destination,prime=False):
        source=destination/'quest/fixture-serial/user-10/.pull_main_snapshot_fixture/device-bundle'
        if prime:
            for name in ['manifest.json','COMPLETE.json',*self.manifest['files']]:
                target=source/name;target.parent.mkdir(parents=True,exist_ok=True);copyfile(self.base/'device'/name,target)
        return source

    def pull(self,destination):
        self.reads=[];self.scope_checks=0
        return self.m.pull_queue('fixture','fixture-serial',destination,client=self.Profile())

    def no_receipt(self,destination):
        self.assertEqual(list(destination.rglob('.delivery-receipts/main/*.json')),[])

    def test_completed_files_resume_after_disconnect(self):
        destination=self.base/'out';self.fail_name='raw/movement.csv'
        first=self.pull(destination)
        self.assertEqual(first['status'],'partial',first);self.assertEqual(first['delivered'],[]);self.no_receipt(destination)
        source=self.cache(destination)
        cached=['manifest.json',*[name for name in self.manifest['files'] if (source/name).is_file()]]
        self.fail_name=None;self.denied=set(cached)
        result=self.pull(destination)
        self.assertEqual(result['status'],'verified',result)
        missing=sorted(set(self.manifest['files'])-set(cached),key=lambda name:(self.manifest['files'][name]['bytes'],name))
        self.assertEqual(self.reads,['COMPLETE.json',*missing])
        bundle=Path(result['delivered'][0]['bundle_path']);manifest=self.m.verify(bundle)
        self.assertEqual(set(manifest['transport_provenance']['cached_files']),set(cached))
        for name in self.m.RAW_FILES:self.assertEqual((bundle/'raw'/name).read_bytes(),(self.base/'device/raw'/name).read_bytes())
        self.assertEqual(len(list(destination.rglob('.delivery-receipts/main/*.json'))),1)
        print(json.dumps(dict(check='completed-file resume',first=first['status'],resumed=result['status'],fresh_reads=self.reads,cached_files=sorted(cached),receipt=True,synthetic=True)))

    def test_small_files_survive_large_file_disconnect(self):
        destination=self.base/'out';self.fail_name='raw/movement.csv'
        result=self.pull(destination);source=self.cache(destination)
        self.assertEqual(result['status'],'partial',result)
        self.assertEqual(self.reads,['COMPLETE.json','manifest.json',*sorted(self.manifest['files'],key=lambda name:(self.manifest['files'][name]['bytes'],name))])
        self.assertEqual({name for name in self.manifest['files'] if (source/name).is_file()},set(self.manifest['files'])-{'raw/movement.csv'})
        self.no_receipt(destination)

    def test_cache_tampering_is_refetched_against_fresh_marker(self):
        raw='raw/research-session.json'
        for case in ['same_size','wrong_size','missing','manifest_corrupt','jointly_forged']:
            with self.subTest(case=case):
                destination=self.base/case;source=self.cache(destination,prime=True)
                if case=='same_size':
                    data=(source/raw).read_bytes();(source/raw).write_bytes(b'X'+data[1:])
                elif case=='wrong_size':(source/raw).write_bytes(b'short')
                elif case=='missing':(source/raw).unlink()
                else:
                    forged=dict(self.manifest,identity=dict(self.manifest['identity'],participant_code='DEV901'))
                    (source/'manifest.json').write_bytes(self.m.canonical(forged))
                    if case=='jointly_forged':(source/'COMPLETE.json').write_bytes(self.m.canonical(dict(schema_version='researchflow.bundle-complete.1',bundle_id='snapshot_fixture',manifest_sha256=self.m.file_digest(source/'manifest.json'))))
                result=self.pull(destination);self.assertEqual(result['status'],'verified',result)
                expected=raw if case in {'same_size','wrong_size','missing'} else 'manifest.json'
                self.assertEqual(self.reads,['COMPLETE.json',expected])
                bundle=Path(result['delivered'][0]['bundle_path']);self.m.verify(bundle)
                for name in self.m.RAW_FILES:self.assertEqual((bundle/'raw'/name).read_bytes(),(self.base/'device/raw'/name).read_bytes())

    def test_symlink_cache_is_refetched_without_writing_its_target(self):
        destination=self.base/'out';source=self.cache(destination,prime=True)
        target=self.base/'outside-cache';copyfile(self.base/'device/raw/research-session.json',target)
        raw=source/'raw/research-session.json';raw.unlink()
        try:raw.symlink_to(target)
        except OSError as ex:self.skipTest('File symlink unavailable on this host: '+str(ex))
        before=target.read_bytes();result=self.pull(destination)
        self.assertEqual(result['status'],'verified',result);self.assertEqual(self.reads,['COMPLETE.json','raw/research-session.json'])
        self.assertFalse(raw.is_symlink());self.assertEqual(target.read_bytes(),before)

    def test_unavailable_fresh_marker_never_authenticates_complete_cache(self):
        destination=self.base/'out';self.cache(destination,prime=True);self.fail_name='COMPLETE.json'
        result=self.pull(destination)
        self.assertEqual(result['status'],'partial',result);self.assertEqual(result['delivered'],[])
        self.assertEqual(self.reads,['COMPLETE.json']);self.no_receipt(destination)

    def test_invalid_fresh_marker_rejected_before_manifest_or_raw(self):
        original=self.m.load(self.base/'device/COMPLETE.json')
        for field,value in [('schema_version','wrong'),('bundle_id','another_snapshot'),('manifest_sha256',original['manifest_sha256'].upper()),('manifest_sha256',None)]:
            with self.subTest(field=field,value=value):
                destination=self.base/('marker_'+str(len(list(self.base.iterdir()))));self.cache(destination,prime=True)
                (self.base/'device/COMPLETE.json').write_bytes(self.m.canonical(dict(original,**{field:value})))
                result=self.pull(destination)
                self.assertEqual(result['status'],'partial',result);self.assertEqual(result['delivered'],[])
                self.assertEqual(self.reads,['COMPLETE.json']);self.no_receipt(destination)

    def test_authenticated_manifest_identity_and_metadata_rejected_before_raw(self):
        for case in ['schema','bundle','participant','session','run','file_shape','negative_size','bool_size','string_size','uppercase_hash']:
            with self.subTest(case=case):
                destination=self.base/case;manifest=json.loads(json.dumps(self.manifest))
                if case=='schema':manifest['schema_version']='wrong'
                elif case=='bundle':manifest['bundle_id']='another_snapshot'
                elif case in {'participant','session','run'}:manifest['identity'][{'participant':'participant_code','session':'session_id','run':'run_id'}[case]]='wrong_identity'
                elif case=='file_shape':manifest['files']=list(manifest['files'])
                else:
                    entry=manifest['files']['raw/research-session.json']
                    if case=='uppercase_hash':entry['sha256']=entry['sha256'].upper()
                    else:entry['bytes']={'negative_size':-1,'bool_size':True,'string_size':'12'}[case]
                (self.base/'device/manifest.json').write_bytes(self.m.canonical(manifest))
                (self.base/'device/COMPLETE.json').write_bytes(self.m.canonical(dict(schema_version='researchflow.bundle-complete.1',bundle_id='snapshot_fixture',manifest_sha256=self.m.file_digest(self.base/'device/manifest.json'))))
                self.cache(destination,prime=True);result=self.pull(destination)
                self.assertEqual(result['status'],'partial',result);self.assertEqual(result['delivered'],[])
                self.assertEqual(self.reads,['COMPLETE.json']);self.no_receipt(destination)

    def test_scope_loss_never_creates_receipt_even_with_all_files_cached(self):
        for at in [2,3]:
            with self.subTest(scope_check=at):
                destination=self.base/('scope_'+str(at));self.cache(destination,prime=True);self.fail_scope_at=at
                result=self.pull(destination)
                self.assertEqual(result['status'],'partial',result);self.assertEqual(result['delivered'],[])
                self.assertIn('fixture scope lost',result['queued_failures'][0]['reason'])
                self.assertEqual(self.reads,['COMPLETE.json']);self.no_receipt(destination)

if __name__=='__main__':unittest.main(verbosity=2)
