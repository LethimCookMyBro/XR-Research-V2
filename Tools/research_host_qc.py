"""Portable front-end for the existing, unchanged research_export.py pipeline.

Example:
  python -X utf8 Tools/research_host_qc.py --bundle X:/exports/snapshot_ID \
    --output X:/QA/new_run

Run from any working directory. --project-root or S1_PROJECT can locate an extracted
source tree; otherwise use the Tools directory beside this script. No cloud upload,
raw-data editing, approval fabrication, or QC-threshold overrides are performed.
Exit 0: pipeline + data QC pass; 2: QC incomplete; 1: invocation/pipeline failure.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from typing import Any


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def resolve_tool(project_root: Path | None) -> Path:
    configured = project_root or (Path(os.environ['S1_PROJECT']) if os.environ.get('S1_PROJECT') else None)
    tool = (configured.resolve() / 'Tools' / 'research_export.py') if configured else Path(__file__).resolve().with_name('research_export.py')
    if not tool.is_file():
        raise ValueError('research_export.py not found. Use --project-root for the extracted Unity source root.')
    return tool


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(value, dict):
        raise ValueError(f'Expected a JSON object: {path.name}')
    return value


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--bundle', type=Path, required=True, help='Existing device export snapshot containing manifest.json and raw/.')
    parser.add_argument('--output', type=Path, required=True, help='New results directory outside the input snapshot.')
    parser.add_argument('--project-root', type=Path, help='Optional extracted source root, not an author-specific PC path.')
    parser.add_argument('--authorization', type=Path)
    parser.add_argument('--authorization-link', type=Path)
    parser.add_argument('--questionnaires', type=Path)
    parser.add_argument('--video-links', type=Path)
    parser.add_argument('--timeout', type=int, default=180, help='Maximum seconds for each existing pipeline command.')
    args = parser.parse_args(argv)
    result: dict[str, Any] = {'scope': 'Host processing of an existing export, not new Quest acquisition or study approval.', 'commands': []}
    output: Path | None = None
    raw_before: dict[str, str] = {}
    raw_paths: dict[str, Path] = {}
    try:
        if args.timeout <= 0:
            raise ValueError('--timeout must be positive.')
        tool = resolve_tool(args.project_root)
        bundle = args.bundle.resolve(strict=True)
        destination = args.output.resolve()
        if destination == bundle or bundle in destination.parents or destination in bundle.parents:
            raise ValueError('Output and input snapshot must be separate; neither may contain the other.')
        if destination.exists():
            raise ValueError('Output directory already exists; use a fresh directory. Existing results are never overwritten.')
        if not (bundle / 'manifest.json').is_file() or not (bundle / 'COMPLETE.json').is_file():
            raise ValueError('Bundle needs manifest.json and COMPLETE.json; do not select raw/ directly.')
        for name in ('events.jsonl', 'movement.csv', 'session_metadata.json', 'research-session.json'):
            path = bundle / 'raw' / name
            if not path.is_file():
                raise ValueError('Required raw file is missing: ' + name)
            raw_paths[name] = path
            raw_before[name] = file_sha256(path)
        destination.mkdir(parents=True, exist_ok=False)
        output = destination
        result.update(source_bundle=str(bundle), tool=str(tool), raw_before=raw_before)

        def run(label: str, arguments: list[str]) -> str:
            command = [sys.executable, '-X', 'utf8', '-B', str(tool), *arguments]
            completed = subprocess.run(command, capture_output=True, text=True, encoding='utf-8', errors='replace',
                                       timeout=args.timeout, env={**os.environ, 'PYTHONUTF8': '1', 'PYTHONIOENCODING': 'utf-8'})
            (output / (label + '.stdout.txt')).write_text(completed.stdout, encoding='utf-8')
            (output / (label + '.stderr.txt')).write_text(completed.stderr, encoding='utf-8')
            result['commands'].append({'label': label, 'command': command, 'exit_code': completed.returncode})
            if completed.returncode:
                raise ValueError(f'{label} failed (exit {completed.returncode}); see its stderr/stdout files. No later stage was run.')
            return completed.stdout

        run('verify_device', ['verify', '--bundle', str(bundle)])
        manifest = load_json(bundle / 'manifest.json')
        identity = manifest.get('identity') or {}
        participant, session = identity.get('participant_code'), identity.get('session_id')
        if not isinstance(participant, str) or not isinstance(session, str):
            raise ValueError('Verified device bundle lacks participant_code/session_id.')
        result['identity'] = identity
        run('audit', ['audit', '--source', str(bundle / 'raw'), '--output', str(output / 'HOST_AUDIT.json')])
        host_root = output / 'host-bundles'
        ingest = ['ingest', '--source', str(bundle / 'raw'), '--destination', str(host_root), '--participant', participant, '--session', session]
        for name in ('authorization', 'authorization_link', 'questionnaires', 'video_links'):
            value = getattr(args, name)
            if value is not None:
                ingest.extend(['--' + name.replace('_', '-'), str(value.resolve(strict=True))])
        ingested = json.loads(run('ingest', ingest))
        host_bundle = Path(ingested['bundle_path']).resolve(strict=True)
        if host_root.resolve() not in host_bundle.parents:
            raise ValueError('Ingest returned a bundle outside this run output directory.')
        run('verify_host', ['verify', '--bundle', str(host_bundle)])
        qc = load_json(host_bundle / 'qc' / 'report.json')
        result.update(host_bundle=str(host_bundle), pipeline_success=True, data_complete=qc.get('data_complete'),
                      movement_complete=qc.get('movement_complete'), s1_analysis_complete=qc.get('s1_analysis_complete'),
                      qc_failures=qc.get('failures', []), qc_warnings=qc.get('warnings', []))
        code = 0 if qc.get('data_complete') is True and qc.get('s1_analysis_complete') is True else 2
    except (OSError, ValueError, KeyError, subprocess.TimeoutExpired) as error:
        result.update(pipeline_success=False, error=str(error))
        code = 1
    finally:
        if raw_before:
            try:
                result['raw_after'] = {name: file_sha256(path) for name, path in raw_paths.items()}
                result['raw_hashes_unchanged'] = result['raw_after'] == raw_before
                if not result['raw_hashes_unchanged']:
                    result.update(pipeline_success=False, error='Raw data changed during processing; discard this QC result and investigate.')
                    code = 1
            except OSError as error:
                result.update(pipeline_success=False, error='Raw data verification failed: ' + str(error))
                code = 1
    result['exit_code'] = code
    if output is not None:
        (output / 'RESULT.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({key: result.get(key) for key in ('pipeline_success', 'data_complete', 'movement_complete',
        's1_analysis_complete', 'raw_hashes_unchanged', 'host_bundle', 'error', 'exit_code')}, ensure_ascii=False))
    return code


if __name__ == '__main__':
    sys.exit(main())
