"""Local ResearchFlow export, seven task measures and explicit missing-data QC.

Python stdlib only. Raw files remain immutable; Tutorial/diagnostics are excluded.
No research answers, task outcomes, valid poses or video clock alignment are inferred.
"""
import argparse
import bisect
import csv
from datetime import datetime,timezone
import hashlib
import heapq
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import uuid
from quest_profile import Client, ProfileError

VERSION = 'researchflow.task-metrics.3'
S1_VERSION = 'researchflow.s1-metrics.1'
RAW_FILES = ('events.jsonl','movement.csv','session_metadata.json','research-session.json')
MOVEMENT_HEADER = 'timestamp,run_id,phase,seq,frame_id,task_state,handedness,input_source,runtime,execution_environment,source,target,pose_kind,provider,tracking_valid,reason,sample_timestamp,sample_age_seconds,px,py,pz,qx,qy,qz,qw'.split(',')
SOURCES = ('handslab.head','handslab.left.provider','handslab.right.provider')
NASA = ('mental_demand','physical_demand','temporal_demand','performance','effort','frustration')
SYMPTOMS = ('dizziness','nausea','headache','eye_discomfort','loss_of_balance','fatigue')
BOUNDARIES = {'TELEPORT','SNAP_TURN','RECENTER','LOCOMOTION_COMMITTED','SESSION_PAUSED','SESSION_RESUMED','RUN_RESUMED','TRACKING_LOST','TRACKING_RECOVERED','FOCUS_LOST','FOCUS_GAINED'}
REMOTE_ROOT = '/sdcard/Android/data/th.ac.tmu.vr.researchflow.quest3.technical/files/ResearchSessionRegistry'

def canonical(value):
    return json.dumps(value,ensure_ascii=False,sort_keys=True,indent=2,allow_nan=False).encode('utf-8')

def digest(data):return hashlib.sha256(data).hexdigest()

def file_digest(path):
    with Path(path).open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()

def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'),parse_constant=lambda v: (_ for _ in ()).throw(ValueError('Non-finite JSON: '+v)))

def token(value):
    if not isinstance(value,str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,159}',value) or value.upper() in {'CON','PRN','AUX','NUL',*(f'COM{i}' for i in range(10)),*(f'LPT{i}' for i in range(10))}:
        raise ValueError('Unsafe identity/path token')
    return value

def number(value):
    n=float(value)
    if not math.isfinite(n) or n<0:raise ValueError('Invalid timestamp/numeric value')
    return n

def raw_paths(source):
    root=Path(source)
    if (root/'raw').is_dir():root=root/'raw'
    return {name:root/name for name in RAW_FILES}

def read_events(path):
    with Path(path).open(encoding='utf-8-sig') as f:return [json.loads(line) for line in f]

def check_log_tails(raw):
    for name in ('events.jsonl','movement.csv'):
        with raw[name].open('rb') as f:
            if f.seek(0,os.SEEK_END)==0:raise ValueError('Partial or empty log tail: '+name)
            f.seek(-1,os.SEEK_END)
            if f.read(1)!=b'\n':raise ValueError('Partial or empty log tail: '+name)

class MovementRows:
    # ponytail: re-read CSV per metric pass; events and reported gaps remain output-sized.
    def __init__(self,path):self.path=Path(path);self.count=None
    def __iter__(self):
        with self.path.open(encoding='utf-8-sig',newline='') as f:
            reader=csv.DictReader(f,strict=True)
            if reader.fieldnames!=MOVEMENT_HEADER:raise ValueError('Unsupported movement schema/header; use versioned reader')
            count=0
            for row in reader:
                if None in row or any(v is None for v in row.values()):raise ValueError('Malformed movement CSV column count')
                count+=1
                yield row
            self.count=count
    def __len__(self):
        return self.count if self.count is not None else sum(1 for _ in self)

def read_raw(source):
    raw=raw_paths(source)
    check_log_tails(raw)
    events=read_events(raw['events.jsonl']);movement=MovementRows(raw['movement.csv'])
    metadata=load(raw['session_metadata.json']);record=load(raw['research-session.json'])
    run=token(record['run_id']);token(record['session_id']);token(record['participant_id'])
    if metadata.get('run_id')!=run:raise ValueError('Metadata run identity mismatch')
    if type(record.get('test_data')) is not bool or metadata.get('test_data')!=record['test_data']:raise ValueError('Test/study provenance mismatch')
    gaps=chronology_gaps(events,movement,run)
    return raw,events,movement,metadata,record,gaps

def chronology_gaps(events,movement,run):
    for rows in (events,movement):
        previous_seq=0;previous_time=-1
        for row in rows:
            if row.get('run_id')!=run:raise ValueError('Raw row run identity mismatch')
            seq=int(row['seq']);t=number(row['timestamp'])
            if seq<=previous_seq or t<previous_time:raise ValueError('Non-monotonic saved chronology')
            previous_seq=seq;previous_time=t
    # Legacy files may have discarded sidecars/rows: flag sequence gaps, never fill them.
    previous_seq=None;previous_time=None;gaps=[]
    for row in heapq.merge(events,movement,key=lambda row:int(row['seq'])):
        seq=int(row['seq']);t=number(row['timestamp'])
        if previous_seq is not None:
            if seq==previous_seq:raise ValueError('Duplicate global sequence')
            if t<previous_time:raise ValueError('Merged chronology goes backwards')
            if seq!=previous_seq+1:gaps.append((previous_seq,seq))
        previous_seq=seq;previous_time=t
    return gaps

def task_events(events,task):
    return [e for e in events if e.get('task_id')==task and e.get('phase')=='Experiment']

def brief(event):
    return {key:event.get(key) for key in ('seq','timestamp','event','target','result','action_id','reason','before','after','task_state','source')}

def payload(event,key='after'):
    value=event.get(key) if isinstance(event,dict) else None
    return value if isinstance(value,dict) else {}

def duration(rows,all_events,observed_until=None):
    start=next((e for e in rows if e['event']=='TASK_STARTED'),None)
    modern=next((e for e in rows if e['event']=='TASK_ENDED'),None)
    timed=modern is not None or 'task_started_at' in payload(start)
    terminal=modern if timed else next((e for e in rows if e['event'] in {'TASK_COMPLETED','TASK_TERMINATED','TASK_ABORTED','TASK_TIME_LIMIT','TASK_WITHDRAWAL','TASK_SAFETY_STOP','TASK_TECHNICAL_FAILURE'}),None)
    if not start:return dict(termination_status='not_started',elapsed_seconds=None,active_seconds=None,observed_elapsed_seconds=None,start=None,end=None,paused_seconds=None,pause_intervals=[])
    bound=terminal or (rows[-1] if rows else start)
    lo=number(payload(start).get('task_started_at') if payload(start).get('task_started_at') is not None else start['timestamp'])
    hi=number(payload(modern).get('task_ended_at') if payload(modern).get('task_ended_at') is not None else bound['timestamp'])
    if hi<lo:raise ValueError('Task end precedes task start')
    if terminal is None and observed_until is not None:hi=max(hi,number(observed_until))
    pause_start=None;pause_event=None;pause_intervals=[];pause_warnings=[]
    for e in all_events:
        t=number(e['timestamp'])
        if e['event']=='SESSION_PAUSED' and pause_start is None:
            pause_start=number(payload(e).get('pause_started_at') if payload(e).get('pause_started_at') is not None else t);pause_event=e
        if e['event']=='SESSION_RESUMED':
            resumed=number(payload(e).get('pause_resumed_at') if payload(e).get('pause_resumed_at') is not None else t)
            if pause_start is None:
                if lo<=resumed<=hi:pause_warnings.append('resume_without_pause:'+str(e['seq']))
                continue
            if resumed<pause_start:raise ValueError('Pause end precedes pause start')
            if min(hi,resumed)>max(lo,pause_start):pause_intervals.append([max(lo,pause_start),min(hi,resumed)])
            pause_start=None
    if pause_start is not None and pause_start<hi:
        pause_intervals.append([max(lo,pause_start),hi]);pause_warnings.append('pause_without_resume:'+str(pause_event['seq']))
    paused=sum(b-a for a,b in pause_intervals)
    status='in_progress'
    if terminal:
        status={'TASK_COMPLETED':'pass','TASK_TIME_LIMIT':'time_limit','TASK_WITHDRAWAL':'withdrawal','TASK_SAFETY_STOP':'safety_stop','TASK_TECHNICAL_FAILURE':'technical_failure'}.get(terminal['event'],terminal.get('reason') or terminal.get('result') or 'other')
    wall=hi-lo if terminal else None;active=max(0,hi-lo-paused) if terminal else None
    if modern:
        data=payload(modern);reason=data.get('end_reason')
        status='pass' if reason=='passed' else reason or 'unknown'
        wall=number(data['wall_elapsed_s']) if data.get('wall_elapsed_s') is not None else None
        active=number(data['active_elapsed_s']) if data.get('active_elapsed_s') is not None else None
        if wall is not None and active is not None and active>wall+.000001:raise ValueError('Active task duration exceeds wall duration')
    beginning=brief(start);beginning['logger_timestamp']=beginning['timestamp'];beginning['timestamp']=lo
    ending=brief(terminal) if terminal else None
    if ending:ending['logger_timestamp']=ending['timestamp'];ending['timestamp']=hi
    return dict(termination_status=status,elapsed_seconds=wall,active_seconds=active,observed_elapsed_seconds=hi-lo,start=beginning,end=ending,paused_seconds=paused,pause_intervals=pause_intervals,pause_warnings=pause_warnings,timing_basis='TASK_ENDED_payload' if modern else 'TASK_ENDED_missing' if timed else 'legacy_event_interval')

def s1_state(value):
    while isinstance(value,dict) and isinstance(value.get('task_state'),dict):value=value['task_state']
    return value

def s1_state_status(task,value):
    fields={'systemIntegrityValid','capturedAtSeconds','workpieceYawDegrees','assemblyObstructed'}
    fields.update({'baseState','ram','ssds','gpus','cables'} if task=='A' else
                  {'baseId','frontGuardId','bladeId','spinnerId','rearGuardId','rearLockId','powerIsolated',
                   'motorRunning','baseStable','frontGuardPlacement','bladePlacement','rearGuardPlacement',
                   'frontGuardAligned','frontGuardClipped','bladeCompatible','bladeKeyAligned','rearGuardAligned','spinner','rearLock'})
    state=s1_state(value)
    if not isinstance(state,dict) or not fields<=state.keys() or any(state[key] is None for key in fields):return 'missing_or_partial'
    if task=='A':
        nested={'baseState':{'caseId','motherboardId','cpuId','coolerId','psuId','caseIntact','motherboardSecured','cpuSeated','coolerSecured','psuSecured','atx24Connected','epsConnected','cpuFanConnected','frontPanelConnected','powerIsolated'},
                'ram':{'id','specification','memoryType','capacityGb','placement','slot','retained'},
                'ssds':{'id','storageInterface','capacityGb','ratedEnduranceTbw','placement','mount','trayFastener'},
                'gpus':{'id','vramGb','declaredBoardPowerW','placement','slot','retained','bracketFastener','auxiliaryPower'},
                'cables':{'id','sourcePartId','targetPartId','sourceConnector','targetConnector','sourceConnected','targetConnected'}}
        for key,required in nested.items():
            values=[state[key]] if key=='baseState' else state[key]
            if not isinstance(values,list) or any(not isinstance(item,dict) or not required<=item.keys() for item in values):return 'missing_or_partial'
    return 'runtime_snapshot_fields_present'

def s1_event(event):
    after=payload(event);before=payload(event,'before')
    return dict(brief(event),action_type=event.get('action_type') or after.get('action_type'),
                attempt_id=event.get('attempt_id') or after.get('attempt_id'),operation_phase=after.get('phase'),
                state_before=s1_state(before.get('task_state',event.get('before'))),
                state_after=s1_state(after.get('task_state',event.get('after'))))

def s1_meaningful(event):
    if event.get('source')!='User':return None
    name=event['event'];after=payload(event)
    if name in {'ACTION','REPAIR'} and after.get('phase') in {'ended','cancelled'}:return None
    if name in {'GRAB','WORKPIECE_ROTATE','ACTION'}:return 'ACTION'
    if name=='REPAIR':return 'REPAIR'
    if name=='EXAMINE':return 'EXAMINE'
    if name=='INSPECT':return 'INSPECT_AGAIN'
    if name.startswith(('INFORMATION_','MEDIA_','VIDEO_','REFERENCE_VIDEO_')) or name in {'FORMAT_SELECTED','REFERENCE_SECTION_SELECTED'}:
        if after.get('selection_kind')!='automatic_default':return 'INFORMATION'
    return None

MOVABLE_EVIDENCE={'PANEL_REPOSITION','PANEL_DRAG_METRICS'}
def protocol_attribution(record,events):
    """UI policy with its evidence: recorded in the session record, stated in the session's own log, or proven by panel-drag
    events (only possible in the movable-panel UI). Anything else stays 'unknown' - a missing field is not proof of a build."""
    if record.get('ui_policy'):return dict(ui_policy=record['ui_policy'],ui_policy_source='recorded')
    for e in events:
        if e.get('event')=='SESSION_PROTOCOL_VERSIONS':
            policy=payload(e).get('recorded_ui_policy')
            if policy:return dict(ui_policy=policy,ui_policy_source='session_event_log')
    drag=[e for e in events if e.get('event') in MOVABLE_EVIDENCE or (e.get('event')=='TUTORIAL_SKILL_CREDIT' and e.get('target')=='training.reposition')]
    if drag:return dict(ui_policy='S1_BASELINE_MOVABLE_PANELS',ui_policy_source='observed_drag_events',evidence_seq=[e.get('seq') for e in drag[:5]])
    return dict(ui_policy='unknown',ui_policy_source='unknown')

def interval_seconds(intervals,lo,hi,pauses):
    clipped=sorted((max(lo,a),min(hi,b)) for a,b in intervals if min(hi,b)>max(lo,a))
    merged=[]
    for a,b in clipped:
        if merged and a<=merged[-1][1]:merged[-1][1]=max(b,merged[-1][1])
        else:merged.append([a,b])
    return sum(b-a-sum(max(0,min(b,y)-max(a,x)) for x,y in pauses) for a,b in merged)

def s1_metrics(task,events,d):
    all_rows=task_events(events,task);start=d['start'];end=d['end'];warnings=list(d.get('pause_warnings',[]))
    rows=[e for e in all_rows if start and int(e['seq'])>=int(start['seq']) and (not end or int(e['seq'])<=int(end['seq']))]
    inspections=[e for e in rows if e['event']=='INSPECT' and e.get('source')=='User']
    actions=[e for e in rows if e['event'] in {'ACTION','REPAIR'} and s1_meaningful(e)]
    physical=next((e for e in rows if e.get('source')=='User' and (e['event'] in {'GRAB','WORKPIECE_ROTATE'} or e in actions)),None)
    reference=next((e for e in rows if e['event'] in {'INFORMATION_OPEN','INFORMATION_VISIBLE'} and s1_meaningful(e)=='INFORMATION'),None)
    examined=[e for e in rows if s1_meaningful(e)=='EXAMINE']
    first_examine=examined[0] if examined else None
    first_channel=min([(int(e['seq']),kind) for e,kind in [(reference,'reference'),(first_examine,'examine')] if e],default=(None,None))[1]
    info=information(rows);exposure=None;exposure_reason=None
    if not physical:exposure_reason='no_action_observed'
    else:
        prior=[i for i in info['intervals'] if int(i['open_seq'])<int(physical['seq'])]
        if any(not i['visibility_verified'] or i['end'] is None for i in prior):exposure_reason='unverified_or_unclosed_exposure'
        elif info['warnings']:exposure_reason='ambiguous_exposure_lifecycle'
        else:exposure=interval_seconds([(number(i['start']),number(i['end'])) for i in prior],number(start['timestamp']),number(physical['timestamp']),d['pause_intervals'])
    boundary=next((e for e in actions if (task=='A' and s1_event(e)['action_type'] in {'INSTALL','CONNECT','FASTEN'}) or (task=='B' and e['event']=='REPAIR')),None)
    def exploration(values):
        targets=[e.get('target') for e in values]
        return dict(ordered_targets=targets,distinct_targets=len(set(targets)),switches=sum(a!=b for a,b in zip(targets,targets[1:])),seq=[e['seq'] for e in values])
    before_boundary=exploration([e for e in examined if boundary and int(e['seq'])<int(boundary['seq'])]) if boundary else dict(ordered_targets=None,distinct_targets=None,switches=None,seq=None)
    meaningful=[(e,s1_meaningful(e)) for e in rows if s1_meaningful(e)]
    adjacency=[dict(from_seq=a['seq'],to_seq=b['seq'],from_kind=x,to_kind=y) for (a,x),(b,y) in zip(meaningful,meaningful[1:])]
    initial_state=s1_state(payload(start).get('initial_state',start.get('task_state'))) if start else None
    previous_state=initial_state;changes=[]
    for e in rows:
        if e['event'] not in {'ACTION','REPAIR','WORKPIECE_ROTATE'}:continue
        record=s1_event(e);before=record['state_before'];after=record['state_after']
        if s1_state_status(task,before)!='runtime_snapshot_fields_present' or s1_state_status(task,after)!='runtime_snapshot_fields_present':
            warnings.append('partial_action_snapshot:'+str(e['seq']));continue
        prior=previous_state if s1_state_status(task,previous_state)=='runtime_snapshot_fields_present' else before
        if {k:v for k,v in prior.items() if k!='capturedAtSeconds'}!={k:v for k,v in after.items() if k!='capturedAtSeconds'}:
            changes.append(dict(record,previous_observed_state=prior))
        previous_state=after
    first=inspections[0] if inspections else None
    first_state=s1_state(first.get('before') if first.get('before') is not None else first.get('task_state')) if first else None
    between=[dict(from_inspect_seq=a['seq'],to_inspect_seq=b['seq'],
                  meaningful_actions=[s1_event(e) for e in actions if int(a['seq'])<int(e['seq'])<int(b['seq'])],
                  state_changes=[e for e in changes if int(a['seq'])<int(e['seq'])<int(b['seq'])]) for a,b in zip(inspections,inspections[1:])]
    def inspection_id(e):return payload(e).get('inspection_id') or e.get('action_id')
    failed=[e for e in rows if e['event']=='INSPECT_FAIL']
    visible=[e for e in rows if e['event']=='INSPECT_FEEDBACK_VISIBLE']
    def feedback_clock(e):
        clock=payload(e).get('feedback_visible_at')
        if clock is None:clock=payload(e).get('feedback_timestamp')
        return number(clock) if clock is not None else None
    def matched_feedback(e,evaluation_name):
        identity=inspection_id(e)
        requests=[r for r in inspections if inspection_id(r)==identity]
        evaluations=[r for r in rows if r['event'] in {'INSPECT_FAIL','INSPECT_PASS'} and inspection_id(r)==identity]
        receipts=[r for r in visible if inspection_id(r)==identity]
        request=requests[0] if len(requests)==1 else None
        evaluation=evaluations[0] if len(evaluations)==1 else None
        visible_at=feedback_clock(e)
        valid=bool(identity and request and evaluation and len(receipts)==1 and evaluation['event']==evaluation_name and
                   int(request['seq'])<int(evaluation['seq'])<int(e['seq']) and visible_at is not None and
                   payload(e).get('visibility_basis')=='rendered_readable_panel')
        if valid:
            valid=e.get('source')=='System' and all(
                ('inspection_id' not in payload(r) or payload(r)['inspection_id']==identity) and
                ('action_id' not in r or r['action_id']==identity) for r in (request,evaluation,e))
            if not valid:warnings.append('feedback_provenance_or_identity_conflict:'+str(e['seq']))
        if valid:
            requested=number(payload(request).get('request_timestamp',request['timestamp']))
            evaluated=number(payload(evaluation).get('evaluation_timestamp',evaluation['timestamp']))
            lo=number(start['timestamp']);hi=number(end['timestamp']) if end else lo+number(d['observed_elapsed_seconds'])
            previous=[r for r in visible if int(r['seq'])<int(e['seq'])]
            valid=lo<=requested<=evaluated<=visible_at<=hi and all(
                int(r['seq'])<int(request['seq']) and (feedback_clock(r) is None or feedback_clock(r)<=requested) for r in previous)
            if not valid:warnings.append('feedback_clock_outside_task_or_order:'+str(e['seq']))
        return request,evaluation,visible_at,valid
    valid_passes=[]
    for e in visible:
        if str(e.get('result')).lower() not in {'pass','passed'}:continue
        request,evaluation,visible_at,valid=matched_feedback(e,'INSPECT_PASS')
        if valid:valid_passes.append((e,visible_at))
        else:warnings.append('unmatched_pass_feedback:'+str(e['seq']))
    episodes=[];seen=set()
    for visible_event in visible:
        result=str(visible_event.get('result')).lower()
        if result not in {'notpassed','fail','failed'}:continue
        identity=inspection_id(visible_event)
        if not identity or identity in seen:warnings.append('missing_or_duplicate_feedback_identity:'+str(visible_event['seq']));continue
        seen.add(identity)
        request,evaluation,visible_at,valid=matched_feedback(visible_event,'INSPECT_FAIL')
        if not valid:warnings.append('feedback_without_request_or_failure:'+str(visible_event['seq']))
        if visible_at is None:warnings.append('missing_feedback_clock:'+str(visible_event['seq']))
        next_event=next((e for e,kind in meaningful if int(e['seq'])>int(visible_event['seq'])),None)
        previous=next((e for e in reversed(changes) if request and e['source']=='User' and int(e['seq'])<int(request['seq'])),None)
        repeat='no_prior_action' if not previous else None;repeat_reason=None
        if previous and next_event and s1_meaningful(next_event) in {'ACTION','REPAIR'}:
            next_action=s1_event(next_event)['action_type']
            if previous['action_type'] is None or next_action is None:repeat_reason='action_type_not_recorded'
            else:repeat='changed_target' if previous['target']!=next_event.get('target') else 'same_action_same_target' if previous['action_type']==next_action else 'changed_action_same_target'
        elif previous:repeat='next_event_not_task_action' if next_event else 'no_response_before_end' if end else 'observation_incomplete'
        next_time=number(next_event['timestamp']) if next_event else None
        if visible_at is not None and next_time is not None and next_time<visible_at:raise ValueError('Response precedes visible feedback clock')
        snapshot=s1_state(request.get('before')) if request else None
        context={key:payload(evaluation).get(key) for key in ('inspection_category','reason_code','original_fault_active','user_created_issues')}
        if isinstance(snapshot,dict):
            for key in ('original_fault_active','user_created_issues','original_fault_id','context_version','context_basis','observation_accessible_targets'):
                if context.get(key) is None:context[key]=snapshot.get(key)
        context['status']='recorded' if any(value is not None for value in context.values()) else 'not_recorded'
        recovered,recovery_at=next(((e,t) for e,t in valid_passes if int(e['seq'])>int(visible_event['seq']) and visible_at is not None and t>=visible_at),(None,None))
        episodes.append(dict(inspection_id=identity,inspect_seq=request['seq'] if request else None,feedback_seq=visible_event['seq'],
                             feedback_visible_at=visible_at,feedback=brief(visible_event),inspection_state=snapshot,context=context,
                             next_event=s1_event(next_event) if next_event else None,response=s1_meaningful(next_event) if next_event else 'NONE_BEFORE_END' if end else None,
                             na_reason=None if next_event or end else 'observation_incomplete',previous_task_change=previous,repeat=repeat,repeat_reason=repeat_reason,
                             latency_wall_s=next_time-visible_at if next_time is not None and visible_at is not None else None,
                             latency_active_s=interval_seconds([(visible_at,next_time)],visible_at,next_time,d['pause_intervals']) if next_time is not None and visible_at is not None else None,
                             recovery_wall_s=number(recovery_at)-visible_at if recovery_at is not None and visible_at is not None else None,
                             recovery_censored=not bool(recovered) if end else None))
    if first and s1_state_status(task,first_state)=='missing_or_partial':warnings.append('first_inspect_snapshot_missing_or_partial')
    if start and s1_state_status(task,initial_state)=='missing_or_partial':warnings.append('initial_snapshot_missing_or_partial')
    if start and not end:warnings.append('task_not_finalized')
    if start and d['timing_basis']!='TASK_ENDED_payload':warnings.append('missing_authoritative_task_end' if d['timing_basis']=='TASK_ENDED_missing' else 'legacy_timing_not_s1_authoritative')
    if end and (d['active_seconds'] is None or d['elapsed_seconds'] is None):warnings.append('missing_authoritative_duration')
    if start and task=='B' and (not isinstance(initial_state,dict) or any(initial_state.get(key) is None for key in ('original_fault_active','user_created_issues','observation_accessible_targets'))):
        warnings.append('missing_s1_state_fields_original_fault_user_issues_observation_access')
    if start and task=='A' and (not isinstance(initial_state,dict) or 'pending_attempts' not in initial_state):warnings.append('missing_s1_state_field_pending_attempts')
    if any(s1_event(e)['action_type'] is None for e in actions):warnings.append('missing_action_type')
    passed=d['termination_status']=='pass'
    end_id=payload(end).get('inspection_id')
    if passed and not (payload(end).get('completed') is True and payload(end).get('feedback_visible') is True and end_id and
                       any(inspection_id(e)==end_id and payload(e).get('active_elapsed_s') is not None and number(payload(e)['active_elapsed_s'])<420 for e,t in valid_passes)):
        warnings.append('completed_without_visible_pass_before_420')
    return dict(analysis_version=S1_VERSION,task_id=task,task_status=d['termination_status'],wall_elapsed_s=d['elapsed_seconds'],active_elapsed_s=d['active_seconds'],
                completion_active_s=d['active_seconds'] if passed and 'completed_without_visible_pass_before_420' not in warnings else None,
                completion_censored=not passed if end and 'completed_without_visible_pass_before_420' not in warnings else None,initial_state=initial_state,final_state=s1_state(end.get('before') or end.get('task_state')) if end else None,
                h1=dict(first_physical_seq=physical['seq'] if physical else None,first_reference_seq=reference['seq'] if reference else None,
                        first_examine_seq=first_examine['seq'] if first_examine else None,first_information_channel=first_channel,
                        ordering='reference_first' if reference and physical and int(reference['seq'])<int(physical['seq']) else 'action_first' if physical else None,
                        visible_seconds_before_first_physical=exposure,na_reason=exposure_reason,interpretation='visible exposure, not gaze or comprehension'),
                h2=dict(before_boundary,boundary='first_assembly_attempt' if task=='A' else 'first_corrective_operation',boundary_seq=boundary['seq'] if boundary else None,
                        na_reason=None if boundary else 'task_not_started' if not start else 'no_assembly_attempt_observed' if task=='A' else 'no_repair_observed',observed_until_end=exploration(examined) if start else None),
                h3=dict(n_inspect=len(inspections) if start else None,first_inspect_state=first_state,first_inspect_state_status=s1_state_status(task,first_state),
                        first_inspect_latency_s=number(first['timestamp'])-number(start['timestamp']) if first and start else None,
                        physical_attempt_count=len({s1_event(e)['attempt_id'] or e.get('action_id') or e['seq'] for e in rows if e in actions or (e['event']=='GRAB' and e.get('source')=='User')}) if start else None,
                        between_inspections=between,adjacency=adjacency,action_to_inspect=sum(a['from_kind'] in {'ACTION','REPAIR'} and a['to_kind']=='INSPECT_AGAIN' for a in adjacency),
                        action_to_action=sum(a['from_kind'] in {'ACTION','REPAIR'} and a['to_kind'] in {'ACTION','REPAIR'} for a in adjacency)),
                failure_episodes=episodes,h4_na_reason=None if episodes else 'no_visible_failure_exposure' if failed else 'no_failure_opportunity' if start else 'task_not_started',
                timeline=[s1_event(e) for e in all_rows],qc_warnings=sorted(set(warnings)),
                provenance=dict(test_data=any(e.get('test_data') is True for e in all_rows),execution_environments=sorted({e['execution_environment'] for e in all_rows if e.get('execution_environment')}),
                                arranged_state_not_e2e=any(e['event']=='TEST_FIXTURE_ARRANGED' for e in all_rows),hardware_verified=None,
                                hardware_verification_basis='not_established_by_event_replay'))

def information(rows):
    intervals=[];opened={};first=None;selected=[];warnings=[]
    for e in rows:
        name=e['event'];after=payload(e)
        if name in {'FORMAT_SELECTED','INFORMATION_FORMAT_SELECTED'} and e.get('source')=='User' and after.get('selection_kind')!='automatic_default':
            fmt=e['target'].removeprefix('reference.');selected.append(brief(e))
            if first is None:first=fmt
        if name in {'INFORMATION_OPEN','INFORMATION_VISIBLE'}:
            key=e.get('action_id') or e['target']
            if key in opened:warnings.append('duplicate_open:'+str(key))
            opened[key]=e
            if after.get('selection_kind')=='deliberate' and e.get('source')=='User' and first is None:first=e['target'].removeprefix('reference.')
        if name in {'INFORMATION_CLOSE','INFORMATION_HIDDEN'}:
            key=e.get('action_id') or e['target']
            o=opened.pop(key,None)
            # Legacy hooks sometimes used a fresh action ID. Only the matching target can close.
            if o is None:
                match=next((k for k,v in opened.items() if v['target']==e['target']),None)
                o=opened.pop(match,None) if match is not None else None
            if o:
                verified=payload(o).get('visibility_verified') is True or o['event']=='INFORMATION_VISIBLE'
                intervals.append(dict(format=o['target'].removeprefix('reference.'),open_seq=o['seq'],close_seq=e['seq'],start=o['timestamp'],end=e['timestamp'],seconds=number(e['timestamp'])-number(o['timestamp']),visibility_verified=verified,open_source=o.get('source'),closure_reason=e.get('reason') or e.get('result'),closure_source=e.get('source'),selection_kind=payload(o).get('selection_kind','unknown')))
            else:warnings.append('close_without_open:'+str(key))
    for o in opened.values():
        intervals.append(dict(format=o['target'].removeprefix('reference.'),open_seq=o['seq'],close_seq=None,start=o['timestamp'],end=None,seconds=None,visibility_verified=False,open_source=o.get('source'),closure_reason='missing_close',closure_source=None,selection_kind=payload(o).get('selection_kind','unknown')))
    formats=[i['format'] for i in intervals if i['open_source']=='User'];seen=set();returns=0
    for fmt in formats:
        if fmt in seen:returns+=1
        seen.add(fmt)
    verified=[i for i in intervals if i['visibility_verified'] and i['seconds'] is not None]
    visibility_complete=bool(intervals) and len(verified)==len(intervals)
    return dict(first_deliberate_format=first,first_presented_format=intervals[0]['format'] if intervals else None,opens=len(formats),visible_intervals=len(intervals),system_concealments=sum(i['closure_source']=='System' for i in intervals),system_resumptions=sum(i['open_source']=='System' for i in intervals),deliberate_selections=selected,switches=sum(a!=b for a,b in zip(formats,formats[1:])),returns=returns,verified_visible_seconds=sum(i['seconds'] for i in verified) if visibility_complete else None,potential_open_seconds=sum(i['seconds'] for i in intervals) if intervals and all(i['seconds'] is not None for i in intervals) else None,intervals=intervals,visibility_status='verified' if visibility_complete else 'unverified_or_incomplete',video_playback_intervals=[brief(e) for e in rows if e['event'].startswith(('VIDEO_PLAYBACK_', 'REFERENCE_VIDEO_'))],warnings=warnings)

def movement_metrics(task,events,movement,metadata,task_duration):
    contexts=[e for e in events if e['event']=='MOVEMENT_CONTEXT']
    context_seq=[int(e['seq']) for e in contexts]
    boundary_seq=[int(e['seq']) for e in events if e['event'] in BOUNDARIES or (e['event']=='LOCOMOTION' and e.get('source')=='System')]
    attribution=set()
    start=task_duration['start'];end=task_duration['end']
    target_updates={}
    for e in events:
        if e.get('task_id')==task and e['event']=='TARGET_POSE':target_updates.setdefault(e['target'],[]).append(e)
    target_sequences={target:[int(e['seq']) for e in rows] for target,rows in target_updates.items()}
    def samples(source):
        for row in movement:
            if row.get('pose_kind')!='ProviderReported' or row.get('target')!=source:continue
            seq=int(row['seq']);idx=bisect.bisect_left(context_seq,seq)-1
            context=payload(contexts[idx]) if idx>=0 else {}
            if context:
                belongs=context.get('task_id')==task and context.get('phase')=='Experiment' and context.get('active_task',True);method='movement_context.1'
            else:
                t=number(row['timestamp']);belongs=start is not None and t>=number(start['timestamp']) and (end is None or t<=number(end['timestamp']));method='legacy_event_interval_unverified'
            t=number(row['timestamp'])
            if not belongs or start is None or t<number(start['timestamp']) or (end is not None and t>number(end['timestamp'])):continue
            attribution.add(method)
            segment=(context.get('segment_id'),bisect.bisect_left(boundary_seq,seq))
            valid=row.get('tracking_valid','').lower()=='true' and not context.get('paused',False) and context.get('focused',True)
            position=None
            try:
                p=tuple(float(row[k]) for k in ('px','py','pz'));observation=number(row['sample_timestamp']);age=number(row['sample_age_seconds'])
                valid=valid and all(math.isfinite(n) for n in p) and age<=float(metadata.get('maximum_pose_age_seconds',.25)) and observation<=number(row['timestamp'])+.001
                if valid:position=p
            except (ValueError,KeyError,TypeError):valid=False
            yield row,segment,position
    def active_gap(a,b):return max(0,b-a-sum(max(0,min(b,hi)-max(a,lo)) for lo,hi in task_duration.get('pause_intervals',[])))
    results={};missing=[]
    for source in SOURCES:
        count=0;valid_count=0;distance=0;direction_changes=0;stationary_seconds=0;max_gap=None;previous=None;previous_vector=None;near_visits={};current_near='__unknown__'
        first_time=None;last_time=None;scheduled_frames=0;sampling_gaps=[]
        speed_count=0;speed_seconds=0;max_speed=None
        def add_gap(a,b):
            if active_gap(a,b)>float(metadata.get('maximum_pose_age_seconds',.25)):sampling_gaps.append(dict(start=a,end=b,raw_seconds=b-a,active_seconds=active_gap(a,b)))
        for row,segment,p in samples(source):
            count+=1
            t=number(row['timestamp'])
            if last_time is None or t!=last_time:
                if last_time is not None:add_gap(last_time,t)
                elif start:add_gap(number(start['timestamp']),t)
                if first_time is None:first_time=t
                last_time=t;scheduled_frames+=1
            if p is None:previous=None;previous_vector=None;current_near='__unknown__';continue
            valid_count+=1
            if previous and previous[1]==segment:
                prev_row,_,prev_p=previous;dt=t-number(prev_row['timestamp']);max_gap=dt if max_gap is None else max(max_gap,dt)
                # Never bridge an unavailable interval or a large sampling gap.
                if 0<dt<=max(.25,3/float(metadata.get('movement_configured_max_hz',20))):
                    v=tuple(a-b for a,b in zip(p,prev_p));step=math.sqrt(sum(a*a for a in v));distance+=step
                    speed=step/dt;speed_count+=1;speed_seconds+=dt;max_speed=speed if max_speed is None else max(max_speed,speed)
                    if speed<=.01:stationary_seconds+=dt
                    if step>=.001 and previous_vector:
                        norm=math.sqrt(sum(a*a for a in previous_vector))*step
                        if norm and sum(a*b for a,b in zip(v,previous_vector))/norm<=math.cos(math.radians(45)):direction_changes+=1
                    if step>=.001:previous_vector=v
                else:previous_vector=None;current_near='__unknown__'
            else:previous_vector=None;current_near='__unknown__'
            previous=(row,segment,p)
            # Near-target movement needs an authored location at the observation, never gaze/grab inference.
            nearest=None
            for target,updates in target_updates.items():
                idx=bisect.bisect_left(target_sequences[target],int(row['seq']))-1
                if idx<0:continue
                e=updates[idx]
                a=payload(e);tp=a.get('position');radius=a.get('near_radius_metres')
                if isinstance(tp,list) and len(tp)==3 and isinstance(radius,(int,float)) and radius>0 and math.dist(p,tp)<=radius:nearest=target;break
            if nearest!=current_near and nearest and (current_near!='__unknown__' or nearest not in near_visits):near_visits[nearest]=near_visits.get(nearest,0)+1
            current_near=nearest
        if end:add_gap(last_time if last_time is not None else number(start['timestamp']),number(end['timestamp']))
        rate=(scheduled_frames-1)/(last_time-first_time) if scheduled_frames>1 and last_time>first_time else None
        if count==0 or valid_count==0:missing.append(source)
        first_gap=first_time-number(start['timestamp']) if scheduled_frames and start else None
        tail_gap=number(end['timestamp'])-last_time if scheduled_frames and end else None
        results[source]=dict(rows=count,valid_rows=valid_count,invalid_rows=count-valid_count,missing_fraction=(count-valid_count)/count if count else None,distance_metres=distance if valid_count else None,direction_changes=direction_changes if valid_count else None,stationary_seconds=stationary_seconds if valid_count else None,speed_mean_metres_per_second=distance/speed_seconds if speed_count else None,speed_max_metres_per_second=max_speed,speed_interval_count=speed_count,speed_interval_seconds=speed_seconds if speed_count else None,effective_scheduled_hz=rate,max_gap_seconds=max_gap,last_sample_timestamp=last_time,first_sample_latency_seconds=first_gap,tail_gap_seconds=tail_gap,sampling_coverage_complete=bool(scheduled_frames) and not sampling_gaps,sampling_gaps=sampling_gaps,scheduled_frames=scheduled_frames,repeated_near_target_visits={k:max(0,v-1) for k,v in near_visits.items()},near_target_status='measured' if target_updates else 'unavailable_no_authored_target_locations')
    return dict(sources=results,required_movement_complete=not missing,missing_sources=missing,attribution=sorted(attribution),definition=dict(speed=dict(units='metres_per_second',interval='accepted adjacent distance / elapsed saved timestamp seconds',mean='accepted distance sum / accepted interval seconds sum',maximum='maximum accepted interval speed',clock='saved observation timestamp, not native sensor sample time',missing='null without an eligible adjacent pair; valid stationary pairs are measured zero',status='exploratory_descriptive_summary_not_research_approval'),distance='adjacent valid ProviderReported world positions only, same context segment, no gaps > max(0.25s,3/configuredHz)',direction_change_degrees=45,stationary_speed_metres_per_second=.01,near_target='authored TARGET_POSE.position and near_radius_metres, entry counts minus first visit',scope='provider observations, not raw sensor timestamps; no wearer inference'))

def metrics(task,events,movement,metadata):
    rows=task_events(events,task);d=duration(rows,events)
    inspections=[e for e in rows if e['event']=='INSPECT' and e.get('source')=='User']
    failed=[e for e in rows if e.get('source')=='User' and (e['event']=='INSPECT_FAIL' or (e['event']=='INSPECT' and str(e.get('result')).lower() in {'fail','failed','notpassed'}))]
    first_fail=failed[0] if failed else None
    actions=[e for e in rows if e['event'] in {'ACTION','REPAIR'} and e.get('source')=='User']
    wrong=[e for e in actions if payload(e).get('wrong_action') is True or str(e.get('result')).lower() in {'wrong','wrongaction','incorrect'}]
    repair=next((e for e in rows if e['event']=='REPAIR' and e.get('source')=='User'),None)
    examined=[e for e in rows if e['event']=='EXAMINE' and e.get('source')=='User' and (repair is None or int(e['seq'])<int(repair['seq']))]
    response=[]
    for fail in failed:
        preceding=[e for e in actions if int(e['seq'])<int(fail['seq'])]
        previous_target=preceding[-1]['target'] if preceding else None
        next_event=next((e for e in rows if int(e['seq'])>int(fail['seq']) and e['event'] in {'ACTION','REPAIR','EXAMINE','INFORMATION_OPEN','INSPECT'} and e.get('source')=='User'),None)
        kind='none_observed'
        if next_event:
            if next_event['event']=='INFORMATION_OPEN':kind='information_return'
            elif next_event['event']=='INSPECT':kind='repeat_inspect'
            elif previous_target:kind='repeat_target' if previous_target==next_event['target'] else 'switch_target'
            else:kind='action_target_without_prior_target'
        response.append(dict(fail=brief(fail),next_event=brief(next_event) if next_event else None,previous_action_target=previous_target,classification=kind,interpretation='observable chronology, not inferred mental strategy'))
    between=[]
    for left,right in zip(inspections,inspections[1:]):between.append(dict(from_inspect_seq=left['seq'],to_inspect_seq=right['seq'],actions=[brief(e) for e in actions if int(left['seq'])<int(e['seq'])<int(right['seq'])]))
    first=inspections[0] if inspections else None
    motion=movement_metrics(task,events,movement,metadata,d)
    if d['start'] is not None and d['end'] is None:
        observed=[value['last_sample_timestamp'] for value in motion['sources'].values() if value['last_sample_timestamp'] is not None]
        if observed:
            measured=duration(rows,events,max(observed))
            # No terminal or task policy is inferred. Re-evaluate gaps only if the measured partial bound extends a pause.
            if measured['pause_intervals']!=d['pause_intervals']:motion=movement_metrics(task,events,movement,metadata,measured)
            d=measured
    return dict(definition_version=VERSION,legacy_metrics_scope="not_the_S1_H1_H4_contract",s1=s1_metrics(task,events,d),task_id=task,event_selection=dict(phase='Experiment',task_id=task,seq=[e['seq'] for e in rows]),duration=d,errors=dict(wrong_actions=len(wrong),wrong_action_records=[brief(e) for e in wrong],inspect_attempts=len(inspections),additional_inspects_after_first_fail=sum(int(e['seq'])>int(first_fail['seq']) for e in inspections) if first_fail else 0,return_interactions=sum(e['event']=='RETRY' and e.get('source')=='User' for e in rows),ui_misclicks=sum(e['event']=='UI_CLICK_REJECTED' and e.get('source')=='User' for e in rows)),information=information(rows),exploration=dict(ordered_targets=[e['target'] for e in examined],distinct_targets=len({e['target'] for e in examined}),before_first_repair_seq=repair['seq'] if repair else None,repair_absence_is_not_zero_exploration=True),progress=dict(first_inspect_state=(first.get('before') or first.get('task_state') or first.get('after')) if first else None,first_inspect=brief(first) if first else None,actions_between_inspections=between),post_failure=response,movement=motion)

def questionnaires(path,record):
    result=load(path) if path else dict(schema_version='research.questionnaire-links.1',participant_code=record['participant_id'],session_id=record['session_id'],source_form_version='1.0 2026-06-18',nasa_tlx={},symptoms={},interviews=[])
    if result.get('schema_version')!='research.questionnaire-links.1' or result.get('participant_code')!=record['participant_id'] or result.get('session_id')!=record['session_id']:raise ValueError('Questionnaire identity/schema mismatch')
    if set(result)-{'schema_version','participant_code','session_id','source_form_version','source_references','nasa_tlx','symptoms','interviews','researcher'}:raise ValueError('Unknown/private questionnaire fields rejected')
    if set(result.get('nasa_tlx',{}))-{'A','B'}:raise ValueError('Unknown questionnaire task')
    for ref in result.get('source_references',[]):
        if set(ref)-{'reference_id','sha256','form_version'}:raise ValueError('Unknown/private questionnaire source fields')
    for ref in result.get('interviews',[]):
        if set(ref)-{'reference_id','status','question_id','sha256'} or ref.get('status') not in {'complete','incomplete','missing','skipped','refused'}:raise ValueError('Invalid/private interview link')
    if set(result.get('researcher',{}))-{'reference_id','task_order','task_status','consent_status','screening_status','training_status'}:raise ValueError('Unknown/private researcher form fields')
    researcher=result.get('researcher',{})
    if set(researcher.get('task_status',{}))-{'A','B'} or any(value not in {'pass','time_limit','withdrawal','safety_stop','technical_failure','other','unknown','not_started','in_progress'} for value in researcher.get('task_status',{}).values()):raise ValueError('Invalid/private researcher task status')
    for field,values in [('task_order',{'AB','BA',None}),('consent_status',{'granted','refused','unknown'}),('screening_status',{'passed','not_passed','reassessed','unknown'}),('training_status',{'passed','not_passed','discontinued','unknown'})]:
        if field in researcher and researcher[field] not in values:raise ValueError('Invalid researcher form '+field)
    for task in ('A','B'):
        entry=result.setdefault('nasa_tlx',{}).setdefault(task,dict(status='missing',items={}))
        if set(entry)-{'status','items','source_reference'}:raise ValueError('Unknown/private assessment fields')
        if entry.get('status') not in {'complete','incomplete','missing','skipped','refused'}:raise ValueError('Invalid assessment status')
        values=entry.setdefault('items',{})
        if set(values)-set(NASA):raise ValueError('Unknown NASA item; preserve six original items')
        for item in NASA:
            value=values.setdefault(item,None)
            if value is not None and (type(value) is not int or not 0<=value<=20):raise ValueError('NASA response outside original integer 0-20 scale')
        if entry['status']=='complete' and any(v is None for v in values.values()):raise ValueError('Complete assessment has missing original item')
        if any(k in entry for k in ('score','imputed','normalized_score')):raise ValueError('This link importer does not calculate/impute scores')
    for stage,values in result.setdefault('symptoms',{}).items():
        if stage not in {'before','between','after'} or set(values)-set(SYMPTOMS):raise ValueError('Unknown symptom stage/item')
        for value in values.values():
            if value is not None and (type(value) is not int or not 0<=value<=3):raise ValueError('Symptoms outside original 0-3 scale')
    result['scale_definition']=dict(nasa_original_range=[0,20],performance_direction='0 good/very successful -> 20 poor/not successful',other_item_direction='0 very low -> 20 very high',symptom_range=[0,3],stages=['before','between','after'],scores='not calculated',checkpoint_is_not_questionnaire=True)
    return result

def videos(path,record):
    result=load(path) if path else dict(schema_version='research.video-links.1',participant_code=record['participant_id'],session_id=record['session_id'],tasks={t:dict(status='missing',consent='unknown',clock_mapping=None,gaps=[]) for t in 'AB'})
    if result.get('schema_version')!='research.video-links.1' or result.get('participant_code')!=record['participant_id'] or result.get('session_id')!=record['session_id']:raise ValueError('Video identity/schema mismatch')
    if set(result)-{'schema_version','participant_code','session_id','tasks','source_references'}:raise ValueError('Unknown/private video link fields rejected')
    for ref in result.get('source_references',[]):
        if set(ref)-{'reference_id','sha256'}:raise ValueError('Unknown/private video source fields')
    for task,item in result.get('tasks',{}).items():
        if set(item)-{'status','consent','consent_reference_id','original_path','original_sha256','required_720p','clock_mapping','gaps','reference_id'}:raise ValueError('Unknown/private video task fields rejected')
        if task not in ('A','B') or item.get('status') not in {'missing','refused','recorded','interrupted','not_required'}:raise ValueError('Invalid video task/status')
        if 'required_720p' in item and type(item['required_720p']) is not bool:raise ValueError('Video derivative requirement must be explicit boolean')
        for gap in item.get('gaps',[]):
            if set(gap)-{'start_seconds','end_seconds','clock','reason','reference_id'}:raise ValueError('Unknown/private video gap fields')
            if gap.get('clock') not in {'video_seconds','logger_seconds'} or number(gap['end_seconds'])<number(gap['start_seconds']):raise ValueError('Video gap needs ordered timestamps and explicit clock')
        if item.get('status') in {'recorded','interrupted'}:
            if item.get('consent')!='granted':raise ValueError('Recorded video requires explicit consent linkage')
            original=Path(item['original_path'])
            if not original.is_file() or file_digest(original)!=item.get('original_sha256'):raise ValueError('Video original hash mismatch')
        mapping=item.get('clock_mapping')
        if mapping is not None:
            if set(mapping)-{'measured_pairs','uncertainty_seconds'}:raise ValueError('Only measured video clock inputs accepted')
            pairs=mapping.get('measured_pairs',[])
            if any(set(pair)-{'video_seconds','logger_seconds'} for pair in pairs):raise ValueError('Unknown/private video anchor fields')
            if len(pairs)<2 or not isinstance(mapping.get('uncertainty_seconds'),(int,float)) or mapping['uncertainty_seconds']<0:raise ValueError('Video clock mapping needs measured anchors and uncertainty')
            a,b=pairs[0],pairs[-1];dt=number(b['video_seconds'])-number(a['video_seconds'])
            if dt<=0:raise ValueError('Video mapping anchors not increasing')
            slope=(number(b['logger_seconds'])-number(a['logger_seconds']))/dt
            if slope<=0:raise ValueError('Invalid video clock direction')
            mapping['logger_seconds_per_video_second']=slope;mapping['logger_offset_seconds']=number(a['logger_seconds'])-slope*number(a['video_seconds']);mapping['method']='measured_anchor_affine_mapping; filename not synchronization'
    return result

def verify(bundle):
    root=Path(bundle);manifest=load(root/'manifest.json');marker=load(root/'COMPLETE.json')
    if digest((root/'manifest.json').read_bytes())!=marker.get('manifest_sha256'):raise ValueError('Completion marker/manifest hash mismatch')
    if marker.get('bundle_id')!=manifest.get('bundle_id'):raise ValueError('Completion marker bundle identity mismatch')
    actual={p.relative_to(root).as_posix() for p in root.rglob('*') if p.is_file()}
    expected=set(manifest['files'])|{'manifest.json','COMPLETE.json'}
    if actual!=expected:raise ValueError('Bundle missing/unexpected files')
    for name,meta in manifest['files'].items():
        relative=Path(name)
        if relative.is_absolute() or '..' in relative.parts:raise ValueError('Unsafe manifest path')
        p=root/relative
        if p.is_symlink() or p.stat().st_size!=meta['bytes'] or file_digest(p)!=meta['sha256']:raise ValueError('Bundle file hash mismatch: '+name)
    return manifest

def copy_video(stage,task,item):
    if item.get('status') not in {'recorded','interrupted'}:return
    original=Path(item['original_path'])
    if original.suffix.lower() not in {'.mp4','.mkv','.mov','.webm'}:raise ValueError('Unsupported original video container')
    raw_path=stage/'raw'/('video_'+task+original.suffix.lower());shutil.copyfile(original,raw_path)
    if file_digest(raw_path)!=item['original_sha256']:raise ValueError('Video changed during immutable copy')
    item['bundle_original_path']=raw_path.relative_to(stage).as_posix()
    if not item.get('required_720p',False):return
    ffmpeg,ffprobe=shutil.which('ffmpeg'),shutil.which('ffprobe')
    if not ffmpeg or not ffprobe:raise ValueError('720p requested but installed FFmpeg/FFprobe unavailable; original preserved in staging')
    derivative=stage/('task_'+task)/'video_720p.mp4'
    p=subprocess.run([ffmpeg,'-v','error','-i',str(raw_path),'-map','0:v:0','-map','0:a?','-vf','scale=-2:720','-c:v','libx264','-preset','fast','-crf','20','-c:a','aac','-n',str(derivative)],capture_output=True,text=True,timeout=300)
    if p.returncode:raise ValueError('720p derivative failed: '+p.stderr[:500])
    probe=subprocess.run([ffprobe,'-v','error','-select_streams','v:0','-show_entries','stream=width,height','-of','json',str(derivative)],capture_output=True,text=True,timeout=30,check=True)
    dims=json.loads(probe.stdout)['streams'][0]
    if dims['height']!=720:raise ValueError('Derivative readback is not 720p')
    item['derivative']=dict(path=derivative.relative_to(stage).as_posix(),sha256=file_digest(derivative),**dims,method='FFmpeg libx264 scale=-2:720; original retained')

def ingest(source,destination,participant,session,questionnaire_path=None,video_path=None,authorization=None,authorization_link=None,transport_binding=None):
    source=Path(source);hashes={name:file_digest(path) for name,path in raw_paths(source).items()}
    raw,events,movement,metadata,record,gaps=read_raw(source)
    if record['participant_id']!=participant or record['session_id']!=session:raise ValueError('Requested participant/session identity mismatch')
    if (source/'manifest.json').exists():
        source_identity=verify(source).get('identity',{})
        if any(source_identity.get(key)!=expected for key,expected in [('participant_code',participant),('session_id',session),('run_id',record['run_id'])]):raise ValueError('Device manifest/raw identity mismatch')
    group=record.get('group');group=['Thai','Japanese','Dev','Test','Pilot'][group] if type(group) is int and 0<=group<=4 else group
    if group not in {'Thai','Japanese','Dev','Test','Pilot'}:raise ValueError('Unknown participant group')
    technical=group in {'Dev','Test','Pilot'}
    if technical!=record['test_data']:raise ValueError('Group/test-data mismatch')
    if not technical:
        a=load(authorization) if authorization else {}
        if a.get('schema')!='research.operator-authorization.1' or a.get('participant_id')!=participant or not a.get('operator_id') or not all(a.get(k) is True for k in ('consent_approved','content_approved','language_approved')) or not all(a.get(k) for k in ('consent_approval_ref','content_approval_ref','language_approval_ref')):raise ValueError('Study export requires matching operator identity, consent and content/language approvals')
        linked=a.get('link_status')=='linked' and a.get('session_id')==session
        if not linked and a.get('link_status')=='pre_session' and authorization_link:
            link=load(authorization_link);original_hash=digest(Path(authorization).read_bytes())
            linked=link.get('schema')=='research.operator-session-link.1' and link.get('authorization_sha256')==original_hash and link.get('operator_id')==a.get('operator_id') and link.get('participant_id')==participant and link.get('session_id')==session and link.get('run_id')==record['run_id']
        if not linked:raise ValueError('Study operator approval is not bound to exact session/run')
    linked=questionnaires(questionnaire_path,record);video=videos(video_path,record)
    task={t:metrics(t,events,movement,metadata) for t in 'AB'}
    if any(file_digest(path)!=hashes[name] for name,path in raw.items()):raise ValueError('Raw source changed during ingest')
    order=[e['task_id'] for e in events if e['event']=='TASK_STARTED' and e.get('phase')=='Experiment' and e.get('task_id') in ('A','B')]
    failures=[];warnings=[]
    for t in 'AB':
        if task[t]['duration']['start'] and not task[t]['movement']['required_movement_complete']:failures.append('missing_required_movement_'+t)
        if task[t]['duration']['start']:
            failures.extend('insufficient_movement_coverage_'+t+'_'+source for source,value in task[t]['movement']['sources'].items() if not value['sampling_coverage_complete'])
        if linked['nasa_tlx'][t]['status']!='complete':warnings.append('missing_or_incomplete_assessment_'+t)
        if video.get('tasks',{}).get(t,{}).get('status') not in {'recorded','interrupted'}:warnings.append('missing_video_'+t)
        if task[t]['information']['visibility_status']!='verified':warnings.append('unverified_information_visibility_'+t)
        if task[t]['duration']['termination_status']=='in_progress':warnings.append('task_not_finalized_'+t)
    if gaps:failures.append('global_sequence_gaps')
    qc=dict(schema_version='researchflow.qc.1',movement_complete=not any(f.startswith(('missing_required_movement','insufficient_movement_coverage')) for f in failures),data_complete=not failures,data_completeness_scope='raw event chronology and required scheduled movement coverage; lifecycle, assessment and video are reported separately',delivery_status='verified_local_bundle',lifecycle='finalized' if any(e['event']=='RUN_COMPLETED' for e in events) else 'partial_or_task_finalized',failures=failures,warnings=warnings,global_sequence_gaps=gaps,raw_event_rows=len(events),raw_movement_rows=len(movement),actual_task_order=order,scheduled_sample_rate_is_not_measured_rate=True,legacy_missingness_preserved=True)
    qc['s1_analysis_version']=S1_VERSION
    qc['s1_warnings']={t:task[t]['s1']['qc_warnings'] for t in 'AB'}
    qc['s1_analysis_complete']=not gaps and all(not task[t]['s1']['qc_warnings'] for t in order)
    identity=dict(run_id=record['run_id'],session_id=session,participant_code=participant,group=group,test_data=technical)
    # ADVISOR_UI_AMENDMENT_20261014: never pool fixed-panel and movable-panel sessions silently, and never guess.
    protocol_versions=dict(protocol_attribution(record,events),ui_policy_recorded=record.get('ui_policy') is not None,
        protocol_version=record.get('protocol_version'),training_version=record.get('training_version'),
        layout_version=record.get('layout_version'),task_a_layout=record.get('task_a_layout'))
    qc['protocol_versions']=protocol_versions
    source_manifest=load(source/'manifest.json') if (source/'manifest.json').exists() else {}
    processor_sha=digest(Path(__file__).read_bytes())
    external_inputs={name:digest(Path(path).read_bytes()) for name,path in [('questionnaire_import',questionnaire_path),('video_links_import',video_path),('operator_authorization',authorization),('operator_session_link',authorization_link)] if path}
    transport_identity=None if transport_binding is None else dict(profile={key:transport_binding['binding'][key] for key in ('serial','android_user','package')},registry=transport_binding['registry'],export_identity=transport_binding['export_identity'])
    identity_bytes=canonical(dict(transport_binding=transport_identity,raw=hashes,external_inputs=external_inputs,questionnaires=linked,video=video,definition_version=VERSION,processor_sha256=processor_sha,source_manifest_sha256=digest((source/'manifest.json').read_bytes()) if source_manifest else None))
    bundle_id='v1_'+digest(identity_bytes)[:24]
    parent=Path(destination)/('technical' if technical else 'study')
    if not technical:parent=parent/('TH' if group=='Thai' else 'JP')
    parent=parent/token(participant)/token(session);parent.mkdir(parents=True,exist_ok=True)
    final=parent/bundle_id
    if final.exists():
        existing=verify(final)
        if existing['input_hashes']!=hashes:raise ValueError('Immutable bundle identity collision')
        return dict(bundle_path=str(final.resolve()),bundle_id=bundle_id,actual_task_order=order,qc=load(final/'qc/report.json'),reused=True)
    stage=parent/('.staging_'+bundle_id+'_'+uuid.uuid4().hex);stage.mkdir()
    # A failed stage is retained for diagnosis; only a verified COMPLETE bundle is promoted.
    for sub in ('raw','task_A','task_B','questionnaires','qc'): (stage/sub).mkdir()
    for name,path in raw.items():
        shutil.copyfile(path,stage/'raw'/name)
        if file_digest(stage/'raw'/name)!=hashes[name]:raise ValueError('Raw source changed during immutable copy: '+name)
    movement=MovementRows(stage/'raw/movement.csv')
    if questionnaire_path:(stage/'raw/questionnaires-import.json').write_bytes(Path(questionnaire_path).read_bytes())
    if video_path:(stage/'raw/video-links-import.json').write_bytes(Path(video_path).read_bytes())
    if os.environ.get('RESEARCH_EXPORT_TEST_DISK_FAIL')=='1':raise OSError('Injected disk-full failure before completion marker')
    for t in 'AB':
        task[t]['input_hashes']=hashes
        (stage/('task_'+t)/'metrics.json').write_bytes(canonical(task[t]))
        with (stage/('task_'+t)/'events.jsonl').open('w',encoding='utf-8') as f:
            for e in task_events(events,t):f.write(json.dumps(e,ensure_ascii=False,allow_nan=False)+'\n')
        # Preserve full CSV rows in per-task extracts using the same sidecar/legacy attribution.
        context=None
        with (stage/('task_'+t)/'movement.csv').open('w',newline='',encoding='utf-8') as f:
            w=csv.DictWriter(f,fieldnames=MOVEMENT_HEADER);w.writeheader()
            for row in heapq.merge(events,movement,key=lambda r:int(r['seq'])):
                if row.get('event')=='MOVEMENT_CONTEXT':context=payload(row)
                if 'frame_id' not in row:continue
                tstart=task[t]['duration']['start'];tend=task[t]['duration']['end']
                belongs=(context.get('task_id')==t and context.get('phase')=='Experiment' and context.get('active_task',True)) if context else True
                belongs=belongs and tstart is not None and number(row['timestamp'])>=number(tstart['timestamp']) and (tend is None or number(row['timestamp'])<=number(tend['timestamp']))
                if belongs:w.writerow(row)
        video_item=video.get('tasks',{}).get(t,dict(status='missing'))
        copy_video(stage,t,video_item)
        (stage/('task_'+t)/'video-link.json').write_bytes(canonical(video_item))
    summaries=[];episodes=[]
    for t in 'AB':
        s1=task[t]['s1']
        summaries.append(dict(identity,ui_policy=protocol_versions['ui_policy'],layout_version=protocol_versions['layout_version'],
            **{key:value for key,value in s1.items() if key not in {'timeline','failure_episodes'}}))
        episodes.extend(dict(identity,task_id=t,analysis_version=S1_VERSION,**episode) for episode in s1['failure_episodes'])
    (stage/'task_summary.json').write_bytes(canonical(summaries))
    (stage/'failure_episodes.json').write_bytes(canonical(episodes))
    with (stage/'event_timeline.csv').open('w',encoding='utf-8',newline='') as f:
        columns=['seq','timestamp','task_id','source','event','action_type','target','action_id','attempt_id','operation_phase','result','reason','state_before','state_after','task_state']
        writer=csv.DictWriter(f,fieldnames=columns);writer.writeheader()
        for event in events:
            if event.get('task_id') not in ('A','B') or event.get('phase')!='Experiment':continue
            row=dict(s1_event(event),task_id=event['task_id'])
            writer.writerow({key:json.dumps(row.get(key),ensure_ascii=False,allow_nan=False) if isinstance(row.get(key),(dict,list)) else row.get(key) for key in columns})
    (stage/'questionnaires/linked.json').write_bytes(canonical(linked))
    (stage/'qc/report.json').write_bytes(canonical(qc))
    if source_manifest:(stage/'raw/device-export-manifest.json').write_bytes((source/'manifest.json').read_bytes());(stage/'raw/device-export-COMPLETE.json').write_bytes((source/'COMPLETE.json').read_bytes())
    if transport_binding:(stage/'raw/quest-profile-transport.json').write_bytes(canonical(transport_binding))
    files={p.relative_to(stage).as_posix():dict(bytes=p.stat().st_size,sha256=file_digest(p)) for p in stage.rglob('*') if p.is_file()}
    manifest=dict(schema_version='researchflow.pc-bundle.1',bundle_id=bundle_id,definition_version=VERSION,processor_sha256=processor_sha,identity=identity,input_hashes=hashes,external_input_hashes=external_inputs,actual_task_order=order,configured_task_order=record.get('task_order'),protocol_versions=protocol_versions,software=dict(content_version=metadata.get('content_version'),event_schema=metadata.get('schema_version'),movement_schema='handslab.movement.legacy25',apk_sha256=source_manifest.get('provenance',{}).get('apk_sha256'),source_code_version=source_manifest.get('provenance',{}).get('source_code_version')),device_render_provenance=source_manifest.get('provenance',dict(device='unknown',render='unknown',simulated_presence='unknown')),transport_provenance=transport_binding,hardware_verified=metadata.get('hardware_verified',False),lifecycle=qc['lifecycle'],data_complete=qc['data_complete'],delivery_status='verified_local_bundle',files=files,unknown_policy='unknown values stay null/unknown; no imputation',source_authority='24 September 2026 slide7/8; forms v1.0 June18 2026; definition thresholds are exploratory processing choices, not newly approved hypotheses')
    (stage/'manifest.json').write_bytes(canonical(manifest))
    (stage/'COMPLETE.json').write_bytes(canonical(dict(schema_version='researchflow.bundle-complete.1',bundle_id=bundle_id,manifest_sha256=digest((stage/'manifest.json').read_bytes()))))
    verify(stage)
    try:stage.rename(final)
    except FileExistsError:
        verify(final)
        if load(final/'manifest.json')['input_hashes']!=hashes:raise ValueError('Concurrent immutable bundle differs')
    verify(final)
    return dict(bundle_path=str(final.resolve()),bundle_id=bundle_id,actual_task_order=order,qc=qc,reused=False)

def adb_command(adb,serial,*args):
    p=subprocess.run([str(adb),'-s',serial,*args],capture_output=True,text=True,timeout=60)
    if p.returncode:raise ConnectionError(p.stderr.strip() or p.stdout.strip() or 'ADB disconnected')
    return p.stdout.strip()

def registry_root(value):
    if isinstance(value,str) and value.startswith('/storage/emulated/0/'):value='/sdcard/'+value[len('/storage/emulated/0/'):]
    base=REMOTE_ROOT.removesuffix('/ResearchSessionRegistry')
    valid=value==REMOTE_ROOT or isinstance(value,str) and re.fullmatch(re.escape(base)+r'/TechnicalScenarios/[A-Za-z0-9_-]{1,160}/registry',value)
    if not valid:raise ValueError('Registry root outside guarded technical package')
    return value

def profile_client(adb,serial):
    expected=load(os.environ['QUEST_PROFILE_BINDING']) if os.environ.get('QUEST_PROFILE_BINDING') else None
    return Client(adb,serial,expected=expected)

def registry_name(value,user):
    if value==REMOTE_ROOT:return 'main'
    base='/storage/emulated/'+str(user)+'/Android/data/'+'th.ac.tmu.vr.researchflow.quest3.technical'+'/files'
    if value==base+'/ResearchSessionRegistry':return 'main'
    legacy=REMOTE_ROOT.removesuffix('/ResearchSessionRegistry')
    match=re.fullmatch('(?:'+re.escape(base)+'|'+re.escape(legacy)+r')/TechnicalScenarios/([A-Za-z0-9][A-Za-z0-9_-]{0,159})/registry',value)
    if not match:raise ValueError('Registry selector outside exact technical package/profile')
    return match[1]

def pc_delivery_receipt(path,job,queue_bytes,profile,result):
    bundle=Path(result['bundle_path']);manifest=verify(bundle);qc=load(bundle/'qc/report.json')
    fields=dict(schema_version='researchflow.pc-delivery-receipt.1',job_id=job['bundle_id'],queue_sha256=digest(queue_bytes),profile={k:profile[k] for k in ('serial','android_user','package')},identity={k:job[k] for k in ('participant_code','session_id','run_id')},source_manifest_sha256=file_digest(bundle/'raw/device-export-manifest.json'),pc_bundle_id=manifest['bundle_id'],pc_manifest_sha256=file_digest(bundle/'manifest.json'),qc_sha256=file_digest(bundle/'qc/report.json'),qc_data_complete=qc['data_complete'],qc_failure_count=len(qc['failures']),status='verified_on_pc')
    if path.exists():
        previous=load(path)
        if previous.get('schema_version')==fields['schema_version']:
            if any(previous.get(k)!=v for k,v in fields.items()):raise ValueError('Immutable PC receipt evidence changed')
            if datetime.fromisoformat(previous['received_utc'].replace('Z','+00:00')).utcoffset()!=timezone.utc.utcoffset(None):raise ValueError('Invalid PC receipt UTC timestamp')
            return previous
        if previous.get('schema_version') is not None:raise ValueError('Unsupported PC receipt schema')
    value=dict(result,**fields,received_utc=datetime.now(timezone.utc).isoformat(timespec='microseconds').replace('+00:00','Z'))
    temporary=path.with_name(path.name+'.'+uuid.uuid4().hex+'.partial')
    try:
        temporary.write_bytes(canonical(value))
        if load(temporary)!=value:raise ValueError('PC receipt readback mismatch')
        temporary.replace(path)
    finally:temporary.unlink(missing_ok=True)
    if load(path)!=value:raise ValueError('Published PC receipt readback mismatch')
    return value

def acknowledge_delivery(client,registry,job,path,receipt):
    ack=dict(schema_version='researchflow.pc-receipt.1',bundle_id=job['bundle_id'],**receipt['identity'],source_manifest_sha256=receipt['source_manifest_sha256'],pc_bundle_id=receipt['pc_bundle_id'],pc_manifest_sha256=receipt['pc_manifest_sha256'],receipt_sha256=file_digest(path),received_utc=receipt['received_utc'],qc_data_complete=receipt['qc_data_complete'],qc_failure_count=receipt['qc_failure_count'])
    try:
        response=client.scoped('delivery_ack',registry=registry,bundle_id=job['bundle_id'],receipt=ack)
        expected=dict(status='received_on_device',bundle_id=job['bundle_id'],receipt_sha256=ack['receipt_sha256'])
        if not isinstance(response,dict) or any(response.get(k)!=v for k,v in expected.items()):raise ValueError('Quest delivery acknowledgement readback mismatch')
        return dict(ack_status='received_on_device',receipt_sha256=ack['receipt_sha256'])
    except (OSError,ValueError,KeyError,TypeError,ConnectionError,subprocess.TimeoutExpired) as ex:
        return dict(ack_status='ack_pending',ack_reason=str(ex),receipt_sha256=ack['receipt_sha256'])


def pull_queue(adb,serial,destination,remote_registry=REMOTE_ROOT,client=None):
    try:
        client=client or profile_client(adb,serial)
        registry=registry_name(remote_registry,client.bound['android_user'])
        jobs=client.scoped('queues',registry=registry)
    except (ConnectionError,OSError,subprocess.TimeoutExpired) as ex:return dict(status='queued_profile_unavailable',reason=str(ex),delivered=[])
    profile=dict(client.bound)
    destination=Path(destination)/'quest'/token(serial)/('user-'+str(profile['android_user']))
    destination.mkdir(parents=True,exist_ok=True)
    receipt_dir=destination/'.delivery-receipts'/token(registry);receipt_dir.mkdir(parents=True,exist_ok=True)
    delivered=[];failed=[]
    for job in jobs:
        job_id=token(job['bundle_id']);temp=destination/('.pull_'+token(registry)+'_'+job_id);temp.mkdir(exist_ok=True)
        request_start=len(client.request_log)
        try:
            client.scope_check()
            queue_bytes=canonical(job);(temp/'queue.json').write_bytes(queue_bytes)
            if job.get('schema_version')!='researchflow.export_queue.1':raise ValueError('Queue schema mismatch')
            participant=token(job['participant_code']);session=token(job['session_id']);token(job['run_id'])
            if not re.fullmatch(r'(DEV|TEST|PILOT)[0-9]+',participant):raise ValueError('Automatic technical queue cannot pull TH/JP study data')
            receipt=receipt_dir/(job_id+'.json')
            if receipt.exists():
                r=load(receipt);manifest=verify(r['bundle_path'])
                expected_profile={k:profile[k] for k in ('serial','android_user','package')}
                if r.get('queue_sha256')!=digest(queue_bytes) or r.get('profile')!=expected_profile or any(manifest['identity'].get(k)!=job.get(k) for k in ('participant_code','session_id','run_id')):raise ValueError('Immutable profile/queue/receipt identity changed')
                r=pc_delivery_receipt(receipt,job,queue_bytes,profile,r)
                delivered.append(dict(job_id=job_id,status='already_verified',bundle_path=r['bundle_path'],**acknowledge_delivery(client,registry,job,receipt,r)));continue
            source=temp/'device-bundle';source.mkdir(exist_ok=True)
            client.read(registry,job_id,'COMPLETE.json',source/'COMPLETE.json')
            marker=load(source/'COMPLETE.json')
            if not isinstance(marker,dict) or marker.get('schema_version')!='researchflow.bundle-complete.1' or marker.get('bundle_id')!=job_id or not isinstance(marker.get('manifest_sha256'),str) or not re.fullmatch(r'[0-9a-f]{64}',marker['manifest_sha256']):raise ValueError('Invalid fresh device completion marker')
            manifest_path=source/'manifest.json';cached_files=[]
            if not manifest_path.is_symlink() and manifest_path.is_file() and file_digest(manifest_path)==marker['manifest_sha256']:cached_files.append('manifest.json')
            else:client.read(registry,job_id,'manifest.json',manifest_path)
            if manifest_path.is_symlink() or not manifest_path.is_file() or file_digest(manifest_path)!=marker['manifest_sha256']:raise ValueError('Fresh completion marker/manifest hash mismatch')
            device_manifest=load(manifest_path)
            if not isinstance(device_manifest,dict) or device_manifest.get('schema_version')!='researchflow.device-bundle.1' or device_manifest.get('bundle_id')!=job_id or not isinstance(device_manifest.get('identity'),dict) or any(device_manifest['identity'].get(k)!=job.get(k) for k in ('participant_code','session_id','run_id')):raise ValueError('Queue/device bundle identity mismatch')
            names=device_manifest.get('files')
            if not isinstance(names,dict) or set(names)!={'raw/'+name for name in RAW_FILES}:raise ValueError('Unexpected device export file allowlist')
            for meta in names.values():
                if not isinstance(meta,dict) or type(meta.get('bytes')) is not int or meta['bytes']<0 or not isinstance(meta.get('sha256'),str) or not re.fullmatch(r'[0-9a-f]{64}',meta['sha256']):raise ValueError('Invalid device export file metadata')
            for name in sorted(names,key=lambda name:(names[name]['bytes'],name)):
                path=source/name;meta=names[name]
                if not path.is_symlink() and path.is_file() and path.stat().st_size==meta['bytes'] and file_digest(path)==meta['sha256']:cached_files.append(name)
                else:client.read(registry,job_id,name,path)
            source_manifest=verify(source)
            if source_manifest.get('schema_version')!='researchflow.device-bundle.1' or source_manifest.get('bundle_id')!=job_id or any(source_manifest.get('identity',{}).get(k)!=job.get(k) for k in ('participant_code','session_id','run_id')):raise ValueError('Queue/device bundle identity mismatch')
            client.scope_check()
            provenance=dict(schema_version='researchflow.profile-transport.1',binding=profile,requests=client.request_log[request_start:],cached_files=cached_files,registry=registry,export_identity={k:job[k] for k in ('participant_code','session_id','run_id')},transport_build_is_not_historical_session_build=True)
            result=ingest(source,destination,participant,session,transport_binding=provenance)
            client.scope_check()
            r=pc_delivery_receipt(receipt,job,queue_bytes,profile,result)
            delivered.append(dict(job_id=job_id,status='verified_on_pc',**result,**acknowledge_delivery(client,registry,job,receipt,r)))
        except (OSError,ValueError,KeyError,ConnectionError,subprocess.TimeoutExpired) as ex:failed.append(dict(job_id=job_id,status='queued_retryable',reason=str(ex)))
    return dict(status='partial' if failed else 'verified',profile=profile,delivered=delivered,queued_failures=failed)

def pull_all_queues(adb,serial,destination,remote_registry,include_isolated):
    if not include_isolated:return pull_queue(adb,serial,destination,remote_registry)
    try:
        client=profile_client(adb,serial)
        names=client.scoped('roots')
    except (ConnectionError,OSError,subprocess.TimeoutExpired) as ex:return dict(status='queued_profile_unavailable',reason=str(ex),delivered=[])
    base='/storage/emulated/'+str(client.bound['android_user'])+'/Android/data/th.ac.tmu.vr.researchflow.quest3.technical/files'
    roots=[remote_registry]+[base+'/TechnicalScenarios/'+token(name)+'/registry' for name in names if name!='main']
    results=[dict(registry_root=root,**pull_queue(adb,serial,destination,root,client)) for root in dict.fromkeys(roots)]
    return dict(status='partial' if any(r['status']!='verified' for r in results) else 'verified',roots=results,isolated_roots_scanned=len(names)-1)

def lease_deadline(value,now=None):
    if not value:raise ValueError('Host watch requires explicit expiring lease')
    deadline=datetime.fromisoformat(value.replace('Z','+00:00'));now=now or datetime.now(timezone.utc)
    if deadline.tzinfo is None or not 0<(deadline-now).total_seconds()<=18*3600:raise ValueError('Lease must be future UTC/offset timestamp within 18 hours')
    return deadline

def main():
    parser=argparse.ArgumentParser(description=__doc__);sub=parser.add_subparsers(dest='command',required=True)
    i=sub.add_parser('ingest');i.add_argument('--source',required=True);i.add_argument('--destination',default=r'D:\VR_TMU\ResearchData');i.add_argument('--participant',required=True);i.add_argument('--session',required=True);i.add_argument('--questionnaires');i.add_argument('--video-links');i.add_argument('--authorization');i.add_argument('--authorization-link')
    v=sub.add_parser('verify');v.add_argument('--bundle',required=True)
    a=sub.add_parser('audit');a.add_argument('--source',required=True);a.add_argument('--output',required=True)
    p=sub.add_parser('pull-queue');p.add_argument('--adb',required=True);p.add_argument('--serial',required=True);p.add_argument('--destination',default=r'D:\VR_TMU\ResearchData');p.add_argument('--registry-root',default=REMOTE_ROOT);p.add_argument('--include-isolated',action='store_true');p.add_argument('--watch',action='store_true');p.add_argument('--lease-expires-utc')
    args=parser.parse_args()
    try:
        if args.command=='pull-queue' and args.watch:
            deadline=lease_deadline(args.lease_expires_utc);previous=None
            while datetime.now(timezone.utc)<deadline:
                result=pull_all_queues(args.adb,args.serial,args.destination,args.registry_root,args.include_isolated);state=json.dumps(result,ensure_ascii=False,allow_nan=False)
                if state!=previous:print(state,flush=True);previous=state
                time.sleep(min(5,max(0,(deadline-datetime.now(timezone.utc)).total_seconds())))
            print(json.dumps(dict(status='host_watch_lease_expired',lease_expires_utc=deadline.isoformat())),flush=True);return 0
        if args.command=='ingest':result=ingest(args.source,args.destination,args.participant,args.session,args.questionnaires,args.video_links,args.authorization,args.authorization_link)
        elif args.command=='verify':result=dict(status='verified',manifest=verify(args.bundle))
        elif args.command=='audit':
            # Read-only audit may receive historical logger files without a participant record.
            source=Path(args.source);hashes={n:file_digest(source/n) for n in RAW_FILES if (source/n).exists()}
            check_log_tails({name:source/name for name in ('events.jsonl','movement.csv')})
            events=read_events(source/'events.jsonl');movement=MovementRows(source/'movement.csv');metadata=load(source/'session_metadata.json')
            chronology_gaps(events,movement,token(metadata['run_id']))
            result=dict(source=str(source.resolve()),input_hashes=hashes,tasks={t:metrics(t,events,movement,metadata) for t in 'AB'},historical_raw_modified=False)
            if any(file_digest(source/n)!=sha for n,sha in hashes.items()):raise ValueError('Raw source changed during audit')
            Path(args.output).write_bytes(canonical(result))
        else:result=pull_all_queues(args.adb,args.serial,args.destination,args.registry_root,args.include_isolated)
        print(json.dumps(result,ensure_ascii=False,allow_nan=False));return 0
    except (OSError,ValueError,KeyError,TypeError,csv.Error,ConnectionError,subprocess.TimeoutExpired) as ex:
        print(json.dumps(dict(status='failed',reason=str(ex)),ensure_ascii=False),file=sys.stderr);return 1

if __name__=='__main__':sys.exit(main())
