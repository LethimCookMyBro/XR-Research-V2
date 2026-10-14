"""Explicit synthetic calculator fixtures, never participant or E2E evidence."""
import copy
import importlib.util
import os
import csv
import json
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(os.environ.get('S1_PROCESSOR', Path(__file__).with_name('research_export.py')))
spec = importlib.util.spec_from_file_location('s1_processor', SCRIPT)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


def state(task='B', **changes):
    if task == 'A':
        value = dict(systemIntegrityValid=True, capturedAtSeconds=0, workpieceYawDegrees=0,
                     baseState={}, ram=[], ssds=[], gpus=[], cables=[], assemblyObstructed=False)
    else:
        value = dict(systemIntegrityValid=True, capturedAtSeconds=0, workpieceYawDegrees=0,
                     baseId='B.base', frontGuardId='B.front', bladeId='B.blade', spinnerId='B.spinner',
                     rearGuardId='B.rear', rearLockId='B.lock', powerIsolated=True, motorRunning=False,
                     baseStable=True, assemblyObstructed=False, frontGuardPlacement=2, bladePlacement=2,
                     rearGuardPlacement=2, frontGuardAligned=True, frontGuardClipped=True, bladeCompatible=True,
                     bladeKeyAligned=True, rearGuardAligned=True, spinner=2, rearLock=1)
    value.update(changes)
    return value


def event(seq, time, name, task='B', source='User', target='B.lock', **kw):
    return dict(seq=seq, timestamp=time, event=name, task_id=task, phase='Experiment', run_id='TEST_METRICS',
                source=source, target=target, test_data=True,
                action_id=(kw.get('after') or {}).get('inspection_id') or 'event'+str(seq), **kw)


def start(task='B'):
    return event(1, 0, 'TASK_STARTED', task, after=dict(task_started_at=0, active_elapsed_s=0,
                 wall_elapsed_s=0, initial_state=state(task)), task_state=state(task))


def end(seq=99, time=20, active=None, task='B', reason='time_limit'):
    if active is None: active=time
    return event(seq, time+.01, 'TASK_ENDED', task, source='System', before=state(task),
                 after=dict(end_reason=reason, completed=reason=='passed', task_started_at=0,
                            task_ended_at=time, wall_elapsed_s=time, active_elapsed_s=active))


def attempt(seq, time, action, task='B', phase='started', target='B.lock', identity='hold1',
            before=None, after=None, source='User', **extra):
    common = dict(payload_version='research.task-attempt.1', attempt_id=identity, action_type=action,
                  phase=phase, input_source='SimulatedController', source=source, **extra)
    e = event(seq, time, 'REPAIR' if action=='REPAIR' else 'ACTION', task, source, target,
              before=dict(common, task_state=before or state(task)),
              after=dict(common, task_state=after or state(task)))
    e['action_id'] = identity
    return e


def inspect(seq, time, identity='i1', task='B'):
    e = event(seq, time, 'INSPECT', task, before=state(task), after=dict(inspection_id=identity))
    e['action_id'] = identity
    return e


def feedback(seq, time, identity='i1', passed=False):
    e = event(seq, time+.001, 'INSPECT_FEEDBACK_VISIBLE', source='System',
              result='Passed' if passed else 'NotPassed',
              after=dict(inspection_id=identity, feedback_timestamp=time, active_elapsed_s=time,
                         visibility_basis='rendered_readable_panel', human_seen=None))
    e['action_id'] = identity
    return e


def s1(events, task='B'):
    return m.metrics(task, events, [], {}).get('s1', {})


class S1MetricsTests(unittest.TestCase):
    def test_null_event_clocks_preserve_unknown_authoritative_duration(self):
        for clock in (0, None):
            with self.subTest(start_clock=clock):
                beginning=start();beginning['timestamp']=5;beginning['after']['task_started_at']=clock
                terminal=end(time=20,reason='technical_failure')
                terminal['after'].update(task_started_at=None,task_ended_at=None,wall_elapsed_s=None,active_elapsed_s=None)
                events=[beginning,terminal]
                result=m.duration(events,events)
                self.assertEqual(result['termination_status'],'technical_failure')
                self.assertEqual(result['observed_elapsed_seconds'],20.01-(5 if clock is None else clock))
                self.assertIsNone(result['elapsed_seconds']);self.assertIsNone(result['active_seconds'])
                self.assertEqual(result['end']['logger_timestamp'],20.01)
                analysis=s1(events)
                self.assertIsNone(analysis['active_elapsed_s']);self.assertIsNone(analysis['wall_elapsed_s'])
                self.assertIn('missing_authoritative_duration',analysis['qc_warnings'])

    def test_zero_and_invalid_clocks_are_not_treated_as_missing(self):
        terminal=end(time=0,active=0,reason='technical_failure')
        result=m.duration([start(),terminal],[start(),terminal])
        self.assertEqual(result['observed_elapsed_seconds'],0)
        self.assertEqual(result['elapsed_seconds'],0);self.assertEqual(result['active_seconds'],0)
        for value in (-1,'bad',float('nan')):
            for field in ('task_started_at','task_ended_at'):
                with self.subTest(value=value,field=field):
                    beginning=start();terminal=end()
                    (beginning if field=='task_started_at' else terminal)['after'][field]=value
                    with self.assertRaises(ValueError):m.duration([beginning,terminal],[beginning,terminal])

    def test_review_m2a_clock_bounds_reject_invalid_recovery_and_completion(self):
        for defect in ('backwards_from_failure','receipt_after_task_end','tuple_before_task_start','request_before_previous_receipt'):
            with self.subTest(defect=defect):
                events=[start(),inspect(2,2),event(3,2.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),
                        feedback(4,4),inspect(5,5,'i2'),
                        event(6,5.1,'INSPECT_PASS',after=dict(inspection_id='i2',evaluation_timestamp=5.1)),
                        feedback(7,8,'i2',passed=True),end(8,10,reason='passed')]
                events[-1]['after'].update(inspection_id='i2',feedback_visible=True)
                if defect in ('backwards_from_failure','tuple_before_task_start'):
                    events[4]['after']['request_timestamp']=1
                    events[5]['after']['evaluation_timestamp']=2
                    events[6]['after']['feedback_timestamp']=3
                if defect=='receipt_after_task_end':events[6]['after']['feedback_timestamp']=11
                if defect=='tuple_before_task_start':
                    events[0]['after']['task_started_at']=2
                    events[-1]['after'].update(task_started_at=2,wall_elapsed_s=8,active_elapsed_s=8)
                if defect=='request_before_previous_receipt':
                    events[4]['timestamp']=3;events[3],events[4]=events[4],events[3]
                    for seq,e in enumerate(events,1):e['seq']=seq
                result=s1(events);episode=result['failure_episodes'][0]
                self.assertIsNone(episode['recovery_wall_s'])
                self.assertIs(episode['recovery_censored'],True)
                self.assertIsNone(result['completion_active_s'])
                self.assertIn('feedback_clock_outside_task_or_order:7',result['qc_warnings'])

    def test_review_m2b_receipt_provenance_and_tuple_identity_contradictions(self):
        for defect in ('user_receipt','unknown_receipt_source','request_identity','evaluation_identity',
                       'receipt_identity','empty_receipt_identity'):
            with self.subTest(defect=defect):
                events=[start(),inspect(2,2),event(3,2.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),
                        feedback(4,4),inspect(5,5,'i2'),
                        event(6,5.1,'INSPECT_PASS',after=dict(inspection_id='i2')),feedback(7,8,'i2',passed=True),
                        end(8,10,reason='passed')]
                events[-1]['after'].update(inspection_id='i2',feedback_visible=True)
                if defect=='user_receipt':events[6]['source']='User'
                elif defect=='unknown_receipt_source':events[6]['source']=None
                else:
                    index={'request_identity':4,'evaluation_identity':5,'receipt_identity':6,'empty_receipt_identity':6}[defect]
                    events[index]['action_id']='' if defect=='empty_receipt_identity' else 'other_inspection'
                result=s1(events);episode=result['failure_episodes'][0]
                self.assertIsNone(episode['recovery_wall_s'])
                self.assertIs(episode['recovery_censored'],True)
                self.assertIsNone(result['completion_active_s'])
                self.assertIn('feedback_provenance_or_identity_conflict:7',result['qc_warnings'])

    def test_review_m2_valid_delayed_logger_receipt_uses_authoritative_clock(self):
        receipt=feedback(7,8,'i2',passed=True);receipt['timestamp']=10.005
        terminal=end(8,10,reason='passed');terminal['after'].update(inspection_id='i2',feedback_visible=True)
        events=[start(),inspect(2,2),event(3,2.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),
                feedback(4,4),inspect(5,5,'i2'),event(6,5.1,'INSPECT_PASS',after=dict(inspection_id='i2')),
                receipt,terminal]
        result=s1(events);episode=result['failure_episodes'][0]
        self.assertEqual(episode['recovery_wall_s'],4)
        self.assertIs(episode['recovery_censored'],False)
        self.assertEqual(result['completion_active_s'],10)
        self.assertFalse(any(w.startswith(('feedback_clock_','feedback_provenance_')) for w in result['qc_warnings']))

    def test_review_m1_neutral_action_keeps_first_response_without_inventing_repeat(self):
        for name in ('GRAB', 'WORKPIECE_ROTATE'):
            with self.subTest(name=name):
                events=[start(),attempt(2,1,'REPAIR',after=state(rearLock=0)),inspect(3,2),
                        event(4,2.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),feedback(5,4),
                        event(6,5,name),attempt(7,6,'REPAIR',identity='later'),end()]
                episode=s1(events)['failure_episodes'][0]
                self.assertIsNone(episode['repeat'])
                self.assertEqual(episode['repeat_reason'],'action_type_not_recorded')
                self.assertEqual(episode['next_event']['seq'],6)
                self.assertEqual(episode['latency_wall_s'],1)
                self.assertEqual(episode['response'],'ACTION')

    def test_review_m1_prior_missing_action_type_is_not_comparable(self):
        previous=attempt(2,1,'REPAIR',after=state(rearLock=0))
        previous['after']['action_type']=None
        events=[start(),previous,inspect(3,2),event(4,2.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),
                feedback(5,4),attempt(6,5,'REPAIR'),end()]
        episode=s1(events)['failure_episodes'][0]
        self.assertIsNone(episode['repeat'])
        self.assertEqual(episode['repeat_reason'],'action_type_not_recorded')

    def test_review_m2_invalid_pass_never_establishes_recovery_or_completion(self):
        for defect in ('orphan','missing_evaluation','mismatched_evaluation','duplicate_receipt',
                       'duplicate_request','duplicate_evaluation','evaluation_before_request',
                       'missing_clock','clock_before_evaluation','missing_rendered_basis'):
            with self.subTest(defect=defect):
                request=inspect(5,5,'i2')
                evaluation=event(6,5.1,'INSPECT_PASS',after=dict(inspection_id='i2',passed=True))
                receipt=feedback(7,8,'i2',passed=True)
                passing=[request,evaluation,receipt]
                if defect=='orphan': passing=[feedback(7,8,'orphan',passed=True)]
                if defect=='missing_evaluation': passing=[request,receipt]
                if defect=='mismatched_evaluation': evaluation['after']['inspection_id']='other'
                if defect=='duplicate_receipt': passing.append(feedback(8,8.1,'i2',passed=True))
                if defect=='duplicate_request': passing.insert(0,inspect(4.5,4.5,'i2'))
                if defect=='duplicate_evaluation': passing.insert(2,event(6.5,5.2,'INSPECT_PASS',after=dict(inspection_id='i2')))
                if defect=='evaluation_before_request':
                    evaluation.update(seq=5,timestamp=4.5);request['seq']=6;passing=[evaluation,request,receipt]
                if defect=='missing_clock': del receipt['after']['feedback_timestamp']
                if defect=='clock_before_evaluation': receipt['after']['feedback_timestamp']=5
                if defect=='missing_rendered_basis': del receipt['after']['visibility_basis']
                terminal=end(9,10,reason='passed');terminal['after'].update(inspection_id='i2',feedback_visible=True)
                events=[start(),inspect(2,1),event(3,1.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),
                        feedback(4,4),*passing,terminal]
                for seq,e in enumerate(events,1):e['seq']=seq
                result=s1(events);episode=result['failure_episodes'][0]
                self.assertIsNone(episode['recovery_wall_s'])
                self.assertIs(episode['recovery_censored'],True)
                self.assertIsNone(result['completion_active_s'])
                self.assertTrue(any(w.startswith('unmatched_pass_feedback:') for w in result['qc_warnings']))

    def test_review_m2_valid_identity_triple_and_both_feedback_clock_keys(self):
        for clock in ('feedback_timestamp','feedback_visible_at'):
            with self.subTest(clock=clock):
                receipt=feedback(7,8,'i2',passed=True)
                receipt['after'][clock]=receipt['after'].pop('feedback_timestamp')
                terminal=end(9,10,reason='passed');terminal['after'].update(inspection_id='i2',feedback_visible=True)
                events=[start(),inspect(2,1),event(3,1.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),
                        feedback(4,4),inspect(5,5,'i2'),
                        event(6,5.1,'INSPECT_PASS',after=dict(inspection_id='i2',passed=True)),receipt,terminal]
                result=s1(events);episode=result['failure_episodes'][0]
                self.assertEqual(episode['recovery_wall_s'],4)
                self.assertIs(episode['recovery_censored'],False)
                self.assertEqual(result['completion_active_s'],10)

    def test_review_m3_cross_start_pause_clips_overlapping_reference_union(self):
        beginning=start();beginning.update(seq=2,timestamp=10);beginning['after']['task_started_at']=10
        terminal=end(9,20,8);terminal['after'].update(task_started_at=10,wall_elapsed_s=10)
        events=[event(1,8,'SESSION_PAUSED',source='System'),beginning,
                event(3,10.5,'INFORMATION_OPEN',target='reference.manual',after=dict(visibility_verified=True)),
                event(4,11,'INFORMATION_OPEN',target='reference.images',after=dict(visibility_verified=True)),
                event(5,12,'SESSION_RESUMED',source='System'),
                event(6,14,'INFORMATION_CLOSE',target='reference.manual'),
                event(7,15,'INFORMATION_CLOSE',target='reference.images'),event(8,16,'GRAB'),terminal]
        result=m.metrics('B',events,[],{})
        self.assertEqual(result['duration']['pause_intervals'],[[10,12]])
        self.assertEqual(result['duration']['paused_seconds'],2)
        self.assertEqual(result['s1']['h1']['visible_seconds_before_first_physical'],3)

    def test_review_m3_cross_end_open_and_outside_pauses(self):
        beginning=start();beginning.update(seq=2,timestamp=10);beginning['after']['task_started_at']=10
        terminal=end(5,20,8);terminal['after'].update(task_started_at=10,wall_elapsed_s=10)
        for interval,expected in [((18,22),[[18,20]]),((18,None),[[18,20]]),
                                  ((2,8),[]),((22,24),[])]:
            with self.subTest(interval=interval):
                pauses=[event(3,interval[0],'SESSION_PAUSED',source='System')]
                if interval[1] is not None:pauses.append(event(4,interval[1],'SESSION_RESUMED',source='System'))
                result=m.duration([beginning,terminal],sorted([beginning,terminal,*pauses],key=lambda e:e['timestamp']))
                self.assertEqual(result['pause_intervals'],expected)
                self.assertEqual(result['paused_seconds'],sum(b-a for a,b in expected))
                if interval[1] is None:self.assertIn('pause_without_resume:3',result.get('pause_warnings',[]))

    def test_review_m3_payload_pause_clock_not_logger_filter(self):
        beginning=start();beginning['after']['task_started_at']=10;beginning['timestamp']=10
        terminal=end(3,20,8);terminal['after'].update(task_started_at=10,wall_elapsed_s=10)
        pauses=[event(2,12.5,'SESSION_PAUSED',source='System',after=dict(pause_started_at=8)),
                event(4,22,'SESSION_RESUMED',source='System',after=dict(pause_resumed_at=12))]
        result=m.duration([beginning,terminal],[beginning,pauses[0],terminal,pauses[1]])
        self.assertEqual(result['pause_intervals'],[[10,12]])
        self.assertEqual(result['paused_seconds'],2)

    def test_review_m3_unmatched_resume_is_explicit_not_fabricated(self):
        events=[start(),event(2,2,'SESSION_RESUMED',source='System'),end()]
        result=m.duration(events,events)
        self.assertEqual(result['pause_intervals'],[])
        self.assertIn('resume_without_pause:2',result.get('pause_warnings',[]))

    def test_authoritative_end_clock_not_earlier_completed_or_logger_write(self):
        events = [start(), event(2, 10.01, 'TASK_COMPLETED'), end(3, 10, 7, reason='passed')]
        result = m.duration(events, events)
        self.assertEqual(result['active_seconds'], 7)
        self.assertEqual(result['elapsed_seconds'], 10)
        self.assertEqual(result['end']['event'], 'TASK_ENDED')

    def test_modern_missing_duration_is_not_legacy_fallback(self):
        terminal = end(time=10)
        del terminal['after']['active_elapsed_s']
        result = m.duration([start(), terminal], [start(), terminal])
        self.assertIsNone(result['active_seconds'])
        self.assertEqual(result['termination_status'], 'time_limit')

    def test_h1_grab_boundary_clips_union_and_formal_pause(self):
        events = [start(), event(2, 1, 'INFORMATION_OPEN', target='reference.manual',
                  after=dict(visibility_verified=True, selection_kind='deliberate')),
                  event(3, 2, 'INFORMATION_OPEN', target='reference.images', after=dict(visibility_verified=True)),
                  event(4, 3, 'SESSION_PAUSED', source='System'), event(5, 5, 'SESSION_RESUMED', source='System'),
                  event(6, 6, 'GRAB'), event(7, 8, 'INFORMATION_CLOSE', target='reference.manual'),
                  event(8, 9, 'INFORMATION_CLOSE', target='reference.images'), end()]
        h = s1(events).get('h1', {})
        self.assertEqual(h.get('visible_seconds_before_first_physical'), 3)
        self.assertEqual(h.get('first_physical_seq'), 6)
        self.assertEqual(h.get('ordering'), 'reference_first')

    def test_h1_unclosed_exposure_not_fabricated_and_no_action_na(self):
        events = [start(), event(2, 1, 'INFORMATION_OPEN', target='reference.manual',
                  after=dict(visibility_verified=True)), event(3, 5, 'GRAB'), end()]
        self.assertIsNone(s1(events).get('h1', {}).get('visible_seconds_before_first_physical'))
        self.assertEqual(s1([start(), end()]).get('h1', {}).get('na_reason'), 'no_action_observed')

    def test_h2_a_rejected_assembly_counts_but_grab_does_not(self):
        events = [start('A'), event(2, 1, 'GRAB', 'A'), event(3, 2, 'EXAMINE', 'A', target='A.one'),
                  attempt(4, 3, 'INSTALL', 'A', phase='rejected', blocked=True),
                  event(5, 4, 'EXAMINE', 'A', target='A.two'), end(task='A')]
        h = s1(events, 'A').get('h2', {})
        self.assertEqual(h.get('boundary_seq'), 4)
        self.assertEqual(h.get('distinct_targets'), 1)

    def test_h2_b_access_not_repair_and_missing_repair_is_na(self):
        events = [start(), attempt(2, 1, 'ACCESS'), event(3, 2, 'EXAMINE', target='B.one'),
                  event(4, 3, 'EXAMINE', target='B.two'), event(5, 4, 'EXAMINE', target='B.one'), end()]
        h = s1(events).get('h2', {})
        self.assertEqual(h.get('na_reason'), 'no_repair_observed')
        self.assertIsNone(h.get('distinct_targets'))
        self.assertEqual(h.get('observed_until_end', {}).get('switches'), 2)
        events.insert(-1, attempt(6, 5, 'REPAIR'))
        self.assertEqual(s1(events).get('h2', {}).get('boundary_seq'), 6)

    def test_h3_adjacency_cannot_jump_over_information_or_examine(self):
        events = [start(), attempt(2, 1, 'REPAIR'), event(3, 2, 'EXAMINE'), inspect(4, 3),
                  attempt(5, 4, 'ACCESS', identity='h2'), event(6, 5, 'INFORMATION_OPEN', target='reference.manual'),
                  inspect(7, 6, 'i2'), attempt(8, 7, 'REPAIR', identity='h3'), inspect(9, 8, 'i3'), end()]
        h = s1(events).get('h3', {})
        self.assertEqual(h.get('action_to_inspect'), 1)
        self.assertEqual(h.get('action_to_action'), 0)
        self.assertEqual(h.get('first_inspect_state'), state())

    def test_attempt_lifecycle_rows_count_one_effort_and_preserve_phase_reversal(self):
        events = [start(), inspect(2, .5), attempt(3, 1, 'ACCESS'), attempt(4, 2, 'ACCESS', phase='ended'),
                  attempt(5, 2, 'REPAIR'), attempt(6, 3, 'REPAIR', phase='ended', after=state(rearLock=2)),
                  inspect(7, 4, 'i2'), end()]
        h = s1(events).get('h3', {})
        self.assertEqual(h.get('physical_attempt_count'), 1)
        self.assertEqual(h.get('action_to_action'), 1)
        self.assertEqual(len(h.get('between_inspections', [{}])[0].get('meaningful_actions', [])), 2)
        self.assertEqual(len(h.get('between_inspections', [{}])[0].get('state_changes', [])), 1)

    def test_h4_starts_visible_and_ignores_continue_and_system_closure(self):
        events = [start(), attempt(2, 1, 'REPAIR', before=state(), after=state(rearLock=2)),
                  inspect(3, 2), event(4, 2.1, 'INSPECT_FAIL', after=dict(inspection_id='i1')),
                  event(5, 3, 'EXAMINE'), feedback(6, 5), event(7, 5.1, 'RETRY'),
                  event(8, 5.2, 'INFORMATION_CLOSE', source='System'), attempt(9, 7, 'REPAIR', identity='hold2'), end()]
        episodes = s1(events).get('failure_episodes', [])
        self.assertEqual(len(episodes), 1)
        if not episodes: return
        self.assertEqual(episodes[0]['next_event']['seq'], 9)
        self.assertEqual(episodes[0]['response'], 'REPAIR')
        self.assertEqual(episodes[0]['latency_wall_s'], 2)
        self.assertEqual(episodes[0]['repeat'], 'same_action_same_target')

    def test_h4_missing_visible_and_no_fail_are_different_na(self):
        events = [start(), inspect(2, 1), event(3, 1.1, 'INSPECT_FAIL', after=dict(inspection_id='i1')), end()]
        h = s1(events)
        self.assertEqual(h.get('h4_na_reason'), 'no_visible_failure_exposure')
        self.assertEqual(h.get('failure_episodes'), [])
        self.assertEqual(s1([start(), end()]).get('h4_na_reason'), 'no_failure_opportunity')

    def test_h4_no_prior_action_and_no_response_before_end(self):
        events = [start(), inspect(2, 1), event(3, 1.1, 'INSPECT_FAIL', after=dict(inspection_id='i1')),
                  feedback(4, 4), end(time=6)]
        episodes = s1(events).get('failure_episodes', [])
        self.assertEqual(len(episodes), 1)
        if not episodes: return
        self.assertEqual(episodes[0]['response'], 'NONE_BEFORE_END')
        self.assertEqual(episodes[0]['repeat'], 'no_prior_action')
        self.assertIsNone(episodes[0]['latency_wall_s'])

    def test_timeout_is_censored_not_completion_420(self):
        h = s1([start(), end(time=420)])
        self.assertIsNone(h.get('completion_active_s'))
        self.assertEqual(h.get('completion_censored'), True)

    def test_actual_full_snapshot_not_claimed_from_component_fragment(self):
        e = inspect(2, 1)
        e['before'] = dict(component_state=dict(rearLock=1))
        h = s1([start(), e, end()]).get('h3', {})
        self.assertEqual(h.get('first_inspect_state_status'), 'missing_or_partial')
        self.assertEqual(h.get('first_inspect_state'), e['before'])
        self.assertEqual(m.s1_state_status('A',state('A')),'missing_or_partial')
        self.assertEqual(m.s1_state_status('B',state(rearLock=None)),'missing_or_partial')

    def test_summary_does_not_mutate_raw(self):
        events = [start(), inspect(2, 1), end()]
        original = copy.deepcopy(events)
        s1(events)
        self.assertEqual(events, original)

    def test_not_started_counts_are_unknown_not_zero(self):
        h = s1([], 'A')
        self.assertIsNone(h.get('h3', {}).get('physical_attempt_count'))
        self.assertIsNone(h.get('h2', {}).get('observed_until_end'))

    def test_duplicate_feedback_is_one_episode_and_missing_evaluation_is_qc(self):
        h = s1([start(), inspect(2, 1), feedback(3, 4), feedback(4, 4), end()])
        self.assertEqual(len(h['failure_episodes']), 1)
        self.assertIn('feedback_without_request_or_failure:3', h['qc_warnings'])
        self.assertIn('missing_or_duplicate_feedback_identity:4', h['qc_warnings'])

    def test_no_motion_grab_is_a_physical_attempt(self):
        e = event(2, 1, 'GRAB', after=dict(attempt_id='hold1'))
        self.assertEqual(s1([start(), e, end()])['h3']['physical_attempt_count'], 1)

    def test_pass_without_render_receipt_is_not_valid_completion(self):
        h = s1([start(), inspect(2, 1), end(reason='passed')])
        self.assertIsNone(h['completion_active_s'])
        self.assertIsNone(h['completion_censored'])

    def test_disk_bundle_summary_episodes_timeline_and_raw_hashes(self):
        with tempfile.TemporaryDirectory() as td:
            root=Path(td);raw=root/'input';raw.mkdir()
            events=[start(), inspect(2, 1), event(3, 1.1, 'INSPECT_FAIL', after=dict(inspection_id='i1')),
                    feedback(4, 4), end(5, 6)]
            (raw/'events.jsonl').write_text(''.join(json.dumps(e)+'\n' for e in events),encoding='utf-8')
            (raw/'movement.csv').write_text(','.join(m.MOVEMENT_HEADER)+'\n',encoding='utf-8')
            (raw/'session_metadata.json').write_text(json.dumps(dict(run_id='TEST_METRICS',test_data=True,hardware_verified=False)),encoding='utf-8')
            (raw/'research-session.json').write_text(json.dumps(dict(participant_id='DEV900',session_id='TEST_METRICS_SESSION',run_id='TEST_METRICS',group='Dev',test_data=True)),encoding='utf-8')
            before={name:m.file_digest(raw/name) for name in m.RAW_FILES}
            result=m.ingest(raw,root/'out','DEV900','TEST_METRICS_SESSION')
            bundle=Path(result['bundle_path']);manifest=m.verify(bundle)
            self.assertEqual({name:m.file_digest(raw/name) for name in m.RAW_FILES},before)
            self.assertEqual(manifest['input_hashes'],before)
            summaries=m.load(bundle/'task_summary.json');episodes=m.load(bundle/'failure_episodes.json')
            self.assertEqual(len(summaries),2)
            self.assertEqual(summaries[0]['task_status'],'not_started')
            self.assertEqual(summaries[1]['task_status'],'time_limit')
            self.assertEqual(episodes[0]['response'],'NONE_BEFORE_END')
            with (bundle/'event_timeline.csv').open(encoding='utf-8',newline='') as f:
                timeline=list(csv.DictReader(f))
            self.assertEqual(len(timeline),5)
            self.assertEqual(json.loads(timeline[1]['state_before']),state())
            repeated=m.ingest(raw,root/'out','DEV900','TEST_METRICS_SESSION')
            self.assertTrue(repeated['reused'])

    def test_operation_end_does_not_duplicate_already_observed_state_change(self):
        events=[start(), inspect(2, .5), attempt(3, 1, 'REPAIR', after=state(rearLock=2)),
                attempt(4, 2, 'REPAIR', phase='ended', after=state(rearLock=2)), inspect(5, 3, 'i2'), end()]
        self.assertEqual(len(s1(events)['h3']['between_inspections'][0]['state_changes']),1)

    def test_completion_requires_matching_inspection_receipt_strictly_before_420(self):
        terminal=end(5, 10, reason='passed')
        terminal['after'].update(inspection_id='i2',feedback_visible=True)
        h=s1([start(),inspect(2,1),event(3,1.1,'INSPECT_PASS',after=dict(inspection_id='i1')),
              feedback(4,10,'i1',passed=True),terminal])
        self.assertIsNone(h['completion_active_s'])
        terminal['after']['inspection_id']='i1'
        self.assertEqual(s1([start(),inspect(2,1),event(3,1.1,'INSPECT_PASS',after=dict(inspection_id='i1')),
                             feedback(4,10,passed=True),terminal])['completion_active_s'],10)
        terminal=end(5,420,reason='passed');terminal['after'].update(inspection_id='i1',feedback_visible=True)
        self.assertIsNone(s1([start(),inspect(2,417),event(3,417.1,'INSPECT_PASS',after=dict(inspection_id='i1')),
                             feedback(4,420,passed=True),terminal])['completion_active_s'])

    def test_partial_observation_has_no_response_or_censoring_claim(self):
        h=s1([start(),inspect(2,1),event(3,1.1,'INSPECT_FAIL',after=dict(inspection_id='i1')),feedback(4,4)])
        episode=h['failure_episodes'][0]
        self.assertIsNone(episode['response'])
        self.assertIsNone(episode['recovery_censored'])
        self.assertEqual(episode['na_reason'],'observation_incomplete')

    def test_modern_completed_row_cannot_replace_missing_task_ended(self):
        events=[start(),event(2,10,'TASK_COMPLETED')]
        d=m.duration(events,events)
        self.assertIsNone(d['active_seconds'])
        self.assertIsNone(d['end'])
        self.assertEqual(d['termination_status'],'in_progress')


if __name__ == '__main__': unittest.main(verbosity=2)
