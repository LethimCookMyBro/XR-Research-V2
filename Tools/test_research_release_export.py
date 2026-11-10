"""Synthetic event/export regressions. No participant or device evidence."""
import csv
import gc
import json
from pathlib import Path
import tempfile
import unittest
import warnings

import test_s1_metrics as f
import test_research_export as disk
import s1_session_report as report

m = f.m
DEV = 'S1_DEV_INTENTIONAL_EXAMINE_20261105'
V7 = 'S1_INTENTIONAL_EXAMINE_20261106'


def protocol(version=DEV, tasks=None, training='S1_TRAINING_TRAINER_V6_20261031', **fields):
    data = dict(training_version=training, examine_version=version, build_flavor='dev_reader_prototype', **fields)
    if tasks is not None:
        data['examine_tasks'] = tasks
    return f.event(0, 0, 'SESSION_PROTOCOL_VERSIONS', task='session', source='System', after=data)


def failure(*response):
    return [f.start(), f.attempt(2, 1, 'REPAIR'), f.inspect(3, 2),
            f.event(4, 2.1, 'INSPECT_FAIL', after=dict(inspection_id='i1')), f.feedback(5, 3), *response, f.end()]


class ReleaseExportTests(unittest.TestCase):
    def test_reader_navigation_is_information_for_h3_and_h4(self):
        names = ('REFERENCE_VIEWER_NAVIGATE', 'REFERENCE_VIEWER_SECTION_SELECTED', 'REFERENCE_PDF_VIEW_CHANGED',
                 'REFERENCE_VIEWER_CONTENTS_OPENED', 'REFERENCE_IMAGE_VIEW_CHANGED',
                 'REFERENCE_STEPS_VIEW_CHANGED', 'REFERENCE_STEP_SELECTED', 'REFERENCE_CONTENTS_SCROLLED')
        for name in names:
            with self.subTest(event=name):
                item = f.event(6, 4, name, target='reference.step_by_step', after={'viewer': {'index': 2}})
                value = f.s1(failure(item, f.attempt(7, 5, 'REPAIR', identity='second')))
                episode = value['failure_episodes'][0]
                self.assertEqual(episode['response'], 'INFORMATION')
                self.assertEqual(episode['next_event']['seq'], 6)
                self.assertEqual(episode['latency_wall_s'], 1)
                adjacent = f.s1([f.start(), f.attempt(2, 1, 'REPAIR'), dict(item, seq=3, timestamp=2),
                                 f.attempt(4, 3, 'REPAIR', identity='second'), f.end()])
                self.assertEqual(adjacent['h3']['action_to_action'], 0)

    def test_v7_reader_events_credit_only_effective_user_commands(self):
        for name in ('REFERENCE_STEPS_VIEW_CHANGED', 'REFERENCE_STEP_SELECTED', 'REFERENCE_CONTENTS_SCROLLED'):
            for source, result, after in [('System', 'Success', {}), ('User', 'NoChange', {}),
                    ('User', 'Rejected', {}), ('User', 'Unavailable', {}),
                    ('User', 'Success', {'selection_kind': 'automatic_default'})]:
                with self.subTest(event=name, source=source, result=result, after=after):
                    item = f.event(6, 4, name, source=source, result=result, after=after)
                    self.assertIsNone(m.s1_meaningful(item))

    def test_rejected_boundary_automatic_and_system_media_are_not_information(self):
        cases = [('REFERENCE_VIEWER_INPUT_REJECTED', {}), ('REFERENCE_VIEWER_PAGE_BOUNDARY', {}),
                 ('REFERENCE_VIDEO_LOAD_PHASE', {}), ('REFERENCE_VIDEO_SEEK_COMPLETED', {}),
                 ('REFERENCE_VIDEO_PLAY_REQUESTED', {'selection_kind': 'automatic_default'}),
                 ('REFERENCE_VIEWER_NAVIGATE', {'selection_kind': 'automatic_default'}),
                 ('REFERENCE_VIEWER_NAVIGATE', {'result': 'Rejected'})]
        for name, data in cases:
            with self.subTest(event=name, data=data):
                event = f.event(6, 4, name, after=data)
                if 'result' in data: event['result'] = data.pop('result')
                self.assertIsNone(m.s1_meaningful(event))
        for name in ('REFERENCE_VIEWER_NAVIGATE', 'REFERENCE_VIDEO_PLAY_REQUESTED', 'EXAMINE_REJECTED'):
            self.assertIsNone(m.s1_meaningful(f.event(6, 4, name, source='System')))

    def test_delayed_video_pause_ack_is_not_a_new_failure_response(self):
        request = f.event(4, 2.05, 'REFERENCE_VIDEO_PAUSE_REQUESTED', target='reference.video')
        acknowledgement = f.event(7, 4, 'REFERENCE_VIDEO_PAUSED', target='reference.video')
        self.assertEqual(m.s1_meaningful(request), 'INFORMATION')
        self.assertIsNone(m.s1_meaningful(acknowledgement))
        events = [f.start(), f.attempt(2, 1, 'REPAIR'), f.inspect(3, 2), request,
                  f.event(5, 2.1, 'INSPECT_FAIL', after=dict(inspection_id='i1')), f.feedback(6, 3),
                  acknowledgement, f.attempt(8, 5, 'REPAIR', identity='second'), f.end()]
        episode = f.s1(events)['failure_episodes'][0]
        self.assertEqual(episode['response'], 'REPAIR')
        self.assertEqual(episode['next_event']['seq'], 8)
        self.assertEqual(episode['latency_wall_s'], 2)

    def test_h2_unavailable_is_not_zero_and_legacy_observation_stays_observed(self):
        for task in ('A', 'B'):
            events = [protocol(None), f.start(task), f.attempt(2, 1, 'INSTALL' if task == 'A' else 'REPAIR', task), f.end(task=task)]
            h2 = f.s1(events, task)['h2']
            self.assertIsNone(h2['distinct_targets'])
            self.assertIsNone(h2['switches'])
            self.assertIsNone(h2['observed_until_end'])
            self.assertEqual(h2['na_reason'], 'not_measurable_examine_unavailable_in_this_protocol')
        legacy = f.s1([f.start(), f.event(2, 1, 'EXAMINE', target='B.one'), f.attempt(3, 2, 'REPAIR'), f.end()])
        self.assertEqual(legacy['h2']['distinct_targets'], 1)
        self.assertIsNone(legacy['h2']['instrument_available'])

    def test_h2_dev_a_unavailable_new_capability_available_and_rejected_excluded(self):
        old_a = f.s1([protocol(), f.start('A'), f.attempt(2, 1, 'INSTALL', 'A'), f.end(task='A')], 'A')
        self.assertIsNone(old_a['h2']['distinct_targets'])
        for task in ('A', 'B'):
            events = [protocol(V7, ['A', 'B'], 'S1_TRAINING_TRAINER_V7_20261106'), f.start(task),
                      f.event(2, 1, 'EXAMINE_REJECTED', task, after={'rejected_reason': 'unavailable'}),
                      f.event(3, 2, 'EXAMINE', task, target='part.one', after={'examine': {'access_state': 'accessible'}}),
                      f.event(4, 3, 'EXAMINE', task, target='part.hidden', after={'examine': {'access_state': 'blocked_by_front_guard'}}),
                      f.attempt(5, 4, 'INSTALL' if task == 'A' else 'REPAIR', task), f.end(task=task)]
            h2 = f.s1(events, task)['h2']
            self.assertEqual(h2['distinct_targets'], 1)
            self.assertIs(h2['instrument_available'], True)
            self.assertEqual([e['seq'] for e in h2['rejected_attempts_not_counted']], [2])

    def test_h4_extension_does_not_replace_primary_and_uses_pause_clock(self):
        for version, tasks in ((DEV, ['B']), (V7, ['A', 'B'])):
            events = [protocol(version, tasks), *failure(
                f.event(6, 3.2, 'SESSION_PAUSED', source='System'), f.event(7, 4.2, 'SESSION_RESUMED', source='System'),
                f.event(8, 5, 'EXAMINE_REJECTED', target='B.blade_mount', after={'rejected_reason': 'blocked_by_front_guard'}),
                f.event(9, 6, 'EXAMINE', target='B.front_guard', after={'examine': {'access_state': 'accessible'}}))]
            episode = f.s1(events)['failure_episodes'][0]
            self.assertEqual(episode['response'], 'EXAMINE')
            self.assertEqual(episode['next_event']['seq'], 9)
            self.assertEqual(episode['latency_wall_s'], 3)
            self.assertEqual(episode['latency_active_s'], 2)
            extension = episode['dev_examine_extension']
            self.assertEqual(extension['response'], 'EXAMINE_REJECTED')
            self.assertEqual(extension['next_event']['seq'], 8)
            self.assertEqual(extension['latency_wall_s'], 2)
            self.assertEqual(extension['latency_active_s'], 1)
            self.assertEqual(extension['rejected_reason'], 'blocked_by_front_guard')
            self.assertEqual(extension['status'], 'draft_not_primary_analysis')
            self.assertEqual(extension['examine_version'], version)
            self.assertEqual(extension['s1_s11_first_meaningful_event'], 'EXAMINE')
            self.assertEqual(extension['s1_s11_latency_s'], 3)

    def test_h4_extension_requires_recorded_supported_version(self):
        responses = [f.event(6, 4, 'EXAMINE_REJECTED', after={'rejected_reason': 'unavailable'}), f.event(7, 5, 'EXAMINE')]
        for versions in ([], [protocol(None)], [protocol('unknown_version')]):
            episode = f.s1([*versions, *failure(*responses)])['failure_episodes'][0]
            self.assertEqual(episode['response'], 'EXAMINE')
            self.assertIsNone(episode['dev_examine_extension'])

    def test_h4_extension_requires_available_instrument_for_this_task(self):
        for task, capability in [('A', None), ('B', []), ('B', ['A'])]:
            with self.subTest(task=task, capability=capability):
                rows = [dict(e, task_id=task) for e in failure(
                    f.event(6, 4, 'EXAMINE_REJECTED', after={'rejected_reason': 'unavailable'}),
                    f.attempt(7, 5, 'REPAIR', identity='second'))]
                value = f.s1([protocol(tasks=capability), *rows], task)
                self.assertIs(value['h2']['instrument_available'], False)
                episode = value['failure_episodes'][0]
                self.assertEqual(episode['response'], 'REPAIR')
                self.assertIsNone(episode['dev_examine_extension'])

    def test_h4_extension_rejects_invalid_capability_and_conflicts(self):
        for versions in ([protocol(tasks='B')], [protocol(tasks=['invalid'])],
                         [protocol(tasks=['B']), dict(protocol(tasks=[]), seq=20, timestamp=10)]):
            with self.subTest(versions=versions):
                value = f.s1([*versions, *failure(f.event(6, 4, 'EXAMINE_REJECTED'))])
                self.assertIsNone(value['h2']['instrument_available'])
                self.assertIsNone(value['failure_episodes'][0]['dev_examine_extension'])

    def test_unknown_instrument_without_observation_is_not_measurable_zero(self):
        for recorded in ([], [protocol('unknown_future_version')]):
            value = f.s1([*recorded, f.start(), f.attempt(2, 1, 'REPAIR'), f.end()])
            self.assertIsNone(value['h2']['instrument_available'])
            self.assertIsNone(value['h2']['distinct_targets'])
            self.assertIsNone(value['h2']['observed_until_end'])
            self.assertEqual(value['h2']['na_reason'], 'unknown_protocol')

    def test_record_capability_and_record_event_conflict_reach_task_metrics(self):
        for conflict in (False, True):
            with self.subTest(conflict=conflict), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); source = root / 'source'; record = disk.fixture(source)
                record.update(training_version='S1_TRAINING_TRAINER_V6_20261031', examine_version=None)
                if conflict: record.update(examine_version=V7, examine_tasks=['A', 'B'])
                (source / 'research-session.json').write_text(json.dumps(record), encoding='utf-8')
                if conflict:
                    events = m.read_events(source / 'events.jsonl')
                    events[0].update(event='SESSION_PROTOCOL_VERSIONS', source='System',
                                     after={'examine_version': DEV, 'examine_tasks': ['B']})
                    (source / 'events.jsonl').write_text(''.join(json.dumps(e) + '\n' for e in events), encoding='utf-8')
                result = m.ingest(source, root / 'out', record['participant_id'], record['session_id'])
                rows = m.load(Path(result['bundle_path']) / 'task_summary.json')
                for row in rows:
                    self.assertIsNone(row['h2']['distinct_targets'])
                    self.assertIsNone(row['h2']['observed_until_end'])
                    if conflict:
                        self.assertEqual(row['h2']['instrument_basis'], 'conflicting_protocol')
                        self.assertIn('examine_version', row['protocol_versions']['conflicts'])
                    else:
                        self.assertIs(row['h2']['instrument_available'], False)
                        self.assertIn('examine_events_conflict_with_unavailable_instrument', row['qc_warnings'])
                timeline = (Path(result['bundle_path']) / 'event_timeline.csv').read_text(encoding='utf-8')
                self.assertIn('EXAMINE', timeline)

    def test_ui_policy_conflict_is_not_overwritten_by_attribution(self):
        events = [protocol(ui_policy='policy_event')]
        value = m.session_protocol_versions(events, {'ui_policy': 'policy_record'})
        self.assertIsNone(value['ui_policy'])
        self.assertIn('ui_policy', value['conflicts'])

    def test_protocol_conflict_stays_explicit_in_analysis(self):
        first = protocol(DEV, ['B'])
        second = dict(protocol(V7, ['A', 'B']), seq=6, timestamp=4)
        value = f.s1([first, *failure(second, f.event(7, 5, 'EXAMINE_REJECTED'))])
        self.assertIsNone(value['protocol_versions']['examine_version'])
        self.assertIn('examine_version', value['protocol_versions']['conflicts'])
        self.assertEqual(len(value['protocol_versions']['recorded_events']), 2)
        self.assertIsNone(value['failure_episodes'][0]['dev_examine_extension'])
        self.assertIsNone(value['h2']['distinct_targets'])

    def test_export_preserves_recorded_protocol_fields_and_raw_hashes(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); source = root / 'source'; record = disk.fixture(source)
            events = m.read_events(source / 'events.jsonl')
            value = dict(examine_version=V7, examine_tasks=['A', 'B'], build_flavor='standard',
                         training_version='S1_TRAINING_TRAINER_V7_20261106', task_a_thread_version='thread-A',
                         task_b_thread_version='thread-B', release_physics_version='physics', future_version='preserved')
            events.insert(0, dict(events[0], event='SESSION_PROTOCOL_VERSIONS', source='System', seq=0, timestamp=0, after=value))
            movement = list(m.MovementRows(source / 'movement.csv'))
            for seq, row in enumerate(sorted(events + movement, key=lambda r: (float(r['timestamp']), int(r['seq']))), 1): row['seq'] = seq
            (source / 'events.jsonl').write_text(''.join(json.dumps(e) + '\n' for e in events), encoding='utf-8')
            with (source / 'movement.csv').open('w', newline='', encoding='utf-8') as stream:
                writer = csv.DictWriter(stream, fieldnames=m.MOVEMENT_HEADER); writer.writeheader(); writer.writerows(movement)
            before = {name: m.file_digest(source / name) for name in m.RAW_FILES}
            result = m.ingest(source, root / 'out', record['participant_id'], record['session_id'])
            bundle = Path(result['bundle_path']); manifest = m.verify(bundle)
            versions = manifest['protocol_versions']
            for key, expected in value.items(): self.assertEqual(versions[key], expected)
            self.assertIsNone(versions['task_mechanics_version'])
            for row in m.load(bundle / 'task_summary.json'):
                self.assertEqual(row['protocol_versions']['examine_version'], V7)
            self.assertEqual(before, {name: m.file_digest(source / name) for name in m.RAW_FILES})
            self.assertEqual(before, {name: m.file_digest(bundle / 'raw' / name) for name in m.RAW_FILES})

    def test_timeline_report_closes_event_stream_on_success_and_parse_failure(self):
        for malformed in (False, True):
            with self.subTest(malformed=malformed), tempfile.TemporaryDirectory() as folder:
                root = Path(folder)
                content = '{invalid json' if malformed else ''.join(json.dumps(e) + '\n' for e in (f.start(), f.end()))
                (root / 'events.jsonl').write_text(content, encoding='utf-8')
                (root / 'session_metadata.json').write_text('{}', encoding='utf-8')
                with warnings.catch_warnings(record=True) as caught:
                    warnings.simplefilter('always', ResourceWarning)
                    if malformed:
                        with self.assertRaises(json.JSONDecodeError):
                            report.build(root)
                    else:
                        _, timeline, _, _ = report.build(root)
                        self.assertEqual([row['seq'] for row in timeline], [1, 99])
                    gc.collect()
                self.assertEqual([str(w.message) for w in caught if issubclass(w.category, ResourceWarning)], [])

    def test_timeline_keeps_examine_rejection_feedback_zoom_and_chapter(self):
        items = [f.start(), f.event(2, 1, 'EXAMINE', target='B.front_guard', after={'examine': {'shown_text': 'observed'}}),
                 f.event(3, 2, 'EXAMINE_REJECTED', after={'rejected_reason': 'blocked_by_front_guard'}),
                 f.event(4, 3, 'PART_CONDITION_SHOWN', source='System', after={'shown_text': 'held condition'}),
                 f.event(5, 4, 'PART_CONDITION_HIDDEN', source='System'),
                 f.event(6, 5, 'REFERENCE_PDF_VIEW_CHANGED', after={'viewer': {'zoom': 2}}),
                 f.event(7, 6, 'REFERENCE_IMAGE_VIEW_CHANGED', after={'viewer': {'zoom': 3}}),
                 f.event(8, 7, 'REFERENCE_VIEWER_SECTION_SELECTED', after={'to_section': 'chapter-2'}), f.end()]
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'events.jsonl').write_text(''.join(json.dumps(e) + '\n' for e in items), encoding='utf-8')
            (root / 'session_metadata.json').write_text('{}', encoding='utf-8')
            _, timeline, _, _ = report.build(root)
        self.assertEqual([row['seq'] for row in timeline], [1, 2, 3, 4, 5, 6, 7, 8, 99])
        text = '\n'.join(row['text'] for row in timeline)
        for expected in ('EXAMINE', 'EXAMINE_REJECTED', 'blocked_by_front_guard', 'held condition', 'chapter-2'):
            self.assertIn(expected, text)


class PointedExamineVersionTests(unittest.TestCase):
    def test_pointed_examine_is_a_recorded_instrument_for_both_tasks(self):
        import research_export as rx
        for task in 'AB':
            self.assertEqual(rx.examine_instrument({'conflicts': {}, 'examine_version': 'S1_POINTED_EXAMINE_20261107'}, task), (True, 'recorded_protocol'))
        self.assertEqual(rx.examine_instrument({'conflicts': {}, 'examine_version': None, 'training_version': 'S1_TRAINING_FIXED_UI_20261014'}, 'A')[0] is True, False)


class VideoAutoResumeTests(unittest.TestCase):
    def test_system_resume_is_not_a_participant_information_action(self):
        resumed = f.event(8, 5, 'REFERENCE_VIDEO_AUTO_RESUMED', target='reference.video', source='System',
                          after=dict(paused_at_s=9.67, hidden_seconds=3.2, hide_cause='edge_on', window_seconds=10))
        self.assertIsNone(m.s1_meaningful(resumed))
        self.assertEqual(m.s1_meaningful(f.event(9, 6, 'REFERENCE_VIDEO_PLAY_REQUESTED', target='reference.video')), 'INFORMATION')


if __name__ == '__main__':
    unittest.main()
