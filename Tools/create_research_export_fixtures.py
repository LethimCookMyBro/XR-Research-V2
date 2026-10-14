"""Create durable, explicitly synthetic technical exports for operator review.

This never contacts Quest or creates a participant registry record. All input is
generated test data. Existing evidence directories and immutable bundles stay intact.
"""
import argparse
import csv
from datetime import datetime, timezone
import json
from pathlib import Path

from test_research_export import HEADER, fixture
import research_export as export


def create(evidence_root, destination, run_id):
    export.token(run_id)
    root = Path(evidence_root) / 'export-fixtures' / run_id
    root.mkdir(parents=True, exist_ok=False)
    cases = [
        ('AB_dense', 'AB', True, False, False, False),
        ('BA_dense', 'BA', True, False, False, False),
        ('interrupted_B', 'AB', True, True, False, False),
        ('header_only', 'AB', False, False, True, False),
        ('missing_right', 'AB', True, False, False, True),
        ('sparse_tail', 'AB', False, False, False, False),
    ]
    results = []
    for name, order, dense, interrupted, header_only, missing_right in cases:
        source = root / name
        record = fixture(source, order, header_only, interrupted, dense)
        record.update(session_id='fixture_session_' + name + '_' + run_id,
                      run_id='fixture_run_' + name + '_' + run_id)
        (source / 'research-session.json').write_bytes(export.canonical(record))
        metadata = export.load(source / 'session_metadata.json')
        metadata.update(run_id=record['run_id'], synthetic_fixture=True,
                        physical_wearer_evidence=False, fixture_case=name)
        (source / 'session_metadata.json').write_bytes(export.canonical(metadata))
        events = [json.loads(line) for line in (source / 'events.jsonl').read_text().splitlines()]
        for event in events:
            event['run_id'] = record['run_id']
        (source / 'events.jsonl').write_text(''.join(json.dumps(e) + '\n' for e in events), encoding='utf-8')
        with (source / 'movement.csv').open(newline='', encoding='utf-8') as handle:
            rows = list(csv.DictReader(handle))
        for row in rows:
            row['run_id'] = record['run_id']
            if missing_right and row['target'] == 'handslab.right.provider':
                row.update(tracking_valid='False', reason='synthetic_missing_provider')
                for field in ('sample_timestamp', 'sample_age_seconds', 'px', 'py', 'pz', 'qx', 'qy', 'qz', 'qw'):
                    row[field] = ''
        with (source / 'movement.csv').open('w', newline='', encoding='utf-8') as handle:
            writer = csv.DictWriter(handle, fieldnames=HEADER)
            writer.writeheader()
            writer.writerows(rows)

        raw = source / 'raw'
        raw.mkdir()
        for filename in export.RAW_FILES:
            (source / filename).rename(raw / filename)
        files = {p.relative_to(source).as_posix(): dict(bytes=p.stat().st_size, sha256=export.file_digest(p)) for p in raw.iterdir()}
        source_manifest = dict(
            schema_version='researchflow.device-bundle.1', bundle_id='snapshot_fixture_' + name + '_' + run_id,
            identity=dict(participant_code=record['participant_id'], session_id=record['session_id'], run_id=record['run_id']),
            lifecycle='partial' if interrupted else 'finalized', files=files,
            provenance=dict(kind='offline_synthetic_fixture', synthetic_commands=True,
                            simulated_presence=False, native_device=False, hardware_verified=False,
                            device='no_device_offline_fixture', apk_sha256=None, render='unmeasured'))
        (source / 'manifest.json').write_bytes(export.canonical(source_manifest))
        (source / 'COMPLETE.json').write_bytes(export.canonical(dict(
            bundle_id=source_manifest['bundle_id'], manifest_sha256=export.file_digest(source / 'manifest.json'))))
        export.verify(source)
        delivered = export.ingest(source, destination, record['participant_id'], record['session_id'])
        bundle = Path(delivered['bundle_path'])
        manifest = export.verify(bundle)
        qc = export.load(bundle / 'qc/report.json')
        expected_complete = dense and not missing_right
        if qc['data_complete'] != expected_complete or qc['movement_complete'] != expected_complete:
            raise AssertionError('Fixture completeness mismatch: ' + name)
        if manifest['hardware_verified'] is not False or manifest['actual_task_order'] != list(order):
            raise AssertionError('Fixture provenance/order mismatch: ' + name)
        task_checks = []
        for task in 'AB':
            metrics = export.load(bundle / ('task_' + task) / 'metrics.json')
            selected = [json.loads(line) for line in (bundle / ('task_' + task) / 'events.jsonl').read_text().splitlines()]
            if any(row.get('task_id') != task or row.get('phase') != 'Experiment' for row in selected):
                raise AssertionError('Tutorial/task contamination')
            status = 'technical_failure' if interrupted and task == 'B' else 'pass'
            expected = dict(termination_status=status, elapsed_seconds=12, active_seconds=11,
                            wrong_actions=1, inspect_attempts=2, additional_inspects_after_first_fail=1,
                            user_information_opens=2, first_deliberate_format='step_by_step', verified_visible_seconds=2,
                            pre_repair_examine_count=3, distinct_pre_repair_targets=2,
                            first_inspect_component_two='original', post_failure_classification='switch_target',
                            head_distance_metres=None if header_only else .2,
                            scheduled_head_frames=0 if header_only else 237 if dense else 4,
                            right_valid_rows=0 if header_only or missing_right else 216 if dense else 4,
                            assessment_status='missing', video_status='missing')
            actual = dict(termination_status=metrics['duration']['termination_status'],
                          elapsed_seconds=metrics['duration']['elapsed_seconds'], active_seconds=metrics['duration']['active_seconds'],
                          wrong_actions=metrics['errors']['wrong_actions'], inspect_attempts=metrics['errors']['inspect_attempts'],
                          additional_inspects_after_first_fail=metrics['errors']['additional_inspects_after_first_fail'],
                          user_information_opens=metrics['information']['opens'], first_deliberate_format=metrics['information']['first_deliberate_format'],
                          verified_visible_seconds=metrics['information']['verified_visible_seconds'],
                          pre_repair_examine_count=len(metrics['exploration']['ordered_targets']), distinct_pre_repair_targets=metrics['exploration']['distinct_targets'],
                          first_inspect_component_two=metrics['progress']['first_inspect_state']['components']['two'],
                          post_failure_classification=metrics['post_failure'][0]['classification'],
                          head_distance_metres=metrics['movement']['sources']['handslab.head']['distance_metres'],
                          scheduled_head_frames=metrics['movement']['sources']['handslab.head']['scheduled_frames'],
                          right_valid_rows=metrics['movement']['sources']['handslab.right.provider']['valid_rows'],
                          assessment_status=export.load(bundle / 'questionnaires/linked.json')['nasa_tlx'][task]['status'],
                          video_status=export.load(bundle / ('task_' + task) / 'video-link.json')['status'])
            for key, value in expected.items():
                got = actual[key]
                if isinstance(value, float):
                    if got is None or abs(value - got) > 1e-8:
                        raise AssertionError(f'{name}/{task}/{key}: expected {value}, actual {got}')
                elif got != value:
                    raise AssertionError(f'{name}/{task}/{key}: expected {value}, actual {got}')
            task_checks.append(dict(task_id=task, expected=expected, actual=actual, result='PASS'))
        again = export.ingest(source, destination, record['participant_id'], record['session_id'])
        if not again['reused'] or again['bundle_path'] != delivered['bundle_path']:
            raise AssertionError('Repeated delivery produced duplicate')
        results.append(dict(case=name, source=str(source.resolve()), bundle_path=str(bundle.resolve()),
                            manifest_sha256=export.file_digest(bundle / 'manifest.json'), identity=manifest['identity'],
                            expected_data_complete=expected_complete, actual_data_complete=qc['data_complete'],
                            lifecycle=qc['lifecycle'], raw_movement_rows=qc['raw_movement_rows'],
                            failures=qc['failures'], warnings=qc['warnings'], checks=task_checks,
                            repeated_delivery_reused=True, verified_files=len(manifest['files'])))
    report = dict(schema_version='researchflow.durable-fixture-verification.1', run_id=run_id,
                  synthetic_only=True, physical_wearer_evidence=False, native_device_evidence=False,
                  processor_sha256=export.file_digest(Path(export.__file__)), cases=results)
    report_path = root / 'expected-actual.json'
    report_path.write_bytes(export.canonical(report))
    lines = ['# Synthetic fixture verification', '',
             'ข้อมูลสังเคราะห์ offline เท่านั้น ไม่มี Quest หรือผู้สวม ไม่มีคำตอบแบบสอบถามที่สร้างขึ้น', '',
             '| Case | Task | Status | Duration / active s | Inspect / extra | Head frames / distance m | QC data complete |',
             '|---|---|---|---:|---:|---:|---|']
    for case in results:
        for check in case['checks']:
            a = check['actual']
            lines.append(f"| {case['case']} | {check['task_id']} | {a['termination_status']} | {a['elapsed_seconds']} / {a['active_seconds']} | {a['inspect_attempts']} / {a['additional_inspects_after_first_fail']} | {a['scheduled_head_frames']} / {a['head_distance_metres']} | {case['actual_data_complete']} |")
    lines.extend(['', 'ทุกกรณี expected = actual ตาม expected-actual.json; raw/hash/task separation/การส่งซ้ำเปิดตรวจจริงแล้ว', ''])
    (root / 'EXPECTED_ACTUAL.md').write_text('\n'.join(lines), encoding='utf-8')
    return dict(report=str(report_path.resolve()), synthetic_only=True,
                cases=[{key:case[key] for key in ('case','bundle_path','manifest_sha256','actual_data_complete','raw_movement_rows')} for case in results])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--evidence-root', required=True)
    parser.add_argument('--destination', default=r'D:\VR_TMU\ResearchData')
    parser.add_argument('--run-id', default=datetime.now(timezone.utc).strftime('%Y%m%d_%H%M%S'))
    args = parser.parse_args()
    print(json.dumps(create(args.evidence_root, args.destination, args.run_id), ensure_ascii=False))
