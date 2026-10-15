"""Researcher-side harness for one S1 DEV run on the standalone Quest technical build.

It installs and verifies a candidate, checks the live render configuration, and drives only the researcher side
(create/start/resume/abort/export) through researcher-adb.sh. Participant input comes from the physical controllers;
this tool never injects input. While it watches the run it samples app frame timing in each phase, keeps the
VrApi log, and at the end pulls the export bundle and runs the PC ingest.

  python quest_s1_device_run.py install <apk> <run_dir>
  python quest_s1_device_run.py render <run_dir> [expected_msaa=4]
  python quest_s1_device_run.py start <run_dir> <Thai|Japanese> <AB_P|AB_Q|BA_P|BA_Q>
  python quest_s1_device_run.py watch <run_dir>        (Ctrl+C to stop; resume/abort are separate commands)
  python quest_s1_device_run.py resume <run_dir>
  python quest_s1_device_run.py abort <run_dir> <withdrawal|safety_stop|technical_failure|other>
  python quest_s1_device_run.py export <run_dir>       (pull the newest bundle of this run + ingest)
"""
import hashlib
import json
import os
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
PACKAGE = 'th.ac.tmu.vr.researchflow.quest3.technical'
SERIAL = os.environ.get('QUEST_SERIAL', '2G0YC5ZHBF00FJ')
FILES = '/sdcard/Android/data/' + PACKAGE + '/files'
BASH = os.environ.get('GIT_BASH', 'bash')


def now():
    return datetime.now(timezone.utc).isoformat()


def adb(*args, check=True, text=True):
    env = dict(os.environ, MSYS2_ARG_CONV_EXCL='*')
    p = subprocess.run(['adb', '-s', SERIAL, *args], capture_output=True, text=text, env=env, timeout=900)
    if check and p.returncode:
        raise SystemExit('adb ' + ' '.join(args) + ' failed: ' + (p.stderr or p.stdout))
    return p.stdout


def researcher(*args):
    p = subprocess.run([BASH, str(HERE / 'researcher-adb.sh'), *args], capture_output=True, text=True, encoding='utf-8', cwd=HERE, timeout=120)
    try:
        return json.loads(p.stdout)
    except json.JSONDecodeError:
        return {'ok': False, 'error': (p.stderr or p.stdout).strip()[:500]}


def log(run, event, **values):
    run.mkdir(parents=True, exist_ok=True)
    row = dict(utc=now(), event=event, **values)
    with (run / 'run_log.jsonl').open('a', encoding='utf-8') as f:
        f.write(json.dumps(row, ensure_ascii=False) + '\n')
    print(json.dumps(row, ensure_ascii=False)[:400])
    return row


def registry_hashes():
    out = adb('shell', 'cd ' + FILES + ' && find ResearchSessionRegistry -type f -not -path "*/Logs/*" -not -path "*/Exports/*" -not -path "*/ExportQueue/*" -exec sha256sum {} +', check=False)
    return sorted(out.strip().splitlines())


def install(apk, run):
    apk = Path(apk)
    expected = hashlib.sha256(apk.read_bytes()).hexdigest()
    before = registry_hashes()
    log(run, 'install_begin', apk=str(apk), sha256=expected, registry_files=len(before))
    out = adb('install', '-r', str(apk))
    path = adb('shell', 'pm path --user 0 ' + PACKAGE).strip().replace('package:', '')
    installed = adb('shell', 'sha256sum ' + path).split()[0]
    after = registry_hashes()
    ok = installed == expected and before == after
    log(run, 'install_readback', adb=out.strip()[-40:], installed_sha256=installed, matches=installed == expected, registry_unchanged=before == after)
    if not ok:
        raise SystemExit('Install readback failed; stop here.')
    adb('shell', 'am start -W -n ' + PACKAGE + '/com.unity3d.player.UnityPlayerGameActivity')
    for _ in range(40):
        state = researcher('state')
        if state.get('ok'):
            log(run, 'launched', identity=state.get('transport_identity'), flow=state['state'].get('flow_state'))
            return
        time.sleep(3)
    raise SystemExit('App did not answer state after launch: ' + str(state.get('error')))


def render(run, expected_msaa=4):
    s = researcher('state')
    if not s.get('ok'):
        raise SystemExit(s.get('error'))
    r = s['state']['render_settings']
    passes = r.get('pass_targets') or []
    ok = r.get('urp_msaa') == expected_msaa and len(passes) >= 3 and all(passes[i + 2] == expected_msaa for i in range(0, len(passes), 3))
    log(run, 'render_readback', ok=ok, expected_msaa=expected_msaa, render=r)
    return ok


def start(run, language, cell):
    created = researcher('create', 'Dev')
    if not created.get('ok'):
        raise SystemExit(created.get('error'))
    participant = created['data']['created']
    result = researcher('start', participant, language, 'Controller', cell)
    log(run, 'start', participant=participant, ok=result.get('ok'), error=result.get('error'), data=result.get('data'))
    (run / 'participant.txt').write_text(participant, encoding='utf-8')


def watch(run):
    logcat = subprocess.Popen(['adb', '-s', SERIAL, 'logcat', '-v', 'threadtime'], stdout=(run / 'logcat.txt').open('ab'), stderr=subprocess.STDOUT)
    seen, timed, last = None, set(), None
    try:
        while True:
            s = researcher('state')
            if not s.get('ok'):
                if last != s.get('error'):
                    last = s.get('error'); log(run, 'state_unavailable', error=last)
                time.sleep(3); continue
            st = s['state']
            key = (st.get('flow_state'), st.get('paused'), st.get('current_task'), st.get('device_ready'))
            if key != seen:
                seen = key
                log(run, 'flow', flow=key[0], paused=key[1], task=key[2], device_ready=key[3], participant=st.get('participant_id'),
                    run_id=st.get('run_id'), error=st.get('error'))
            phase = st.get('flow_state') or ''
            if not st.get('paused') and phase not in timed and ('Active' in phase or phase.startswith('Tutorial')):
                started = researcher('frame_timing', '30')
                if started.get('ok'):
                    timed.add(phase); log(run, 'frame_timing_started', phase=phase, directory=started['data'].get('directory'))
            if phase in ('ParticipantComplete', 'ResearcherSummary'):
                log(run, 'flow_finished', flow=phase)
                break
            time.sleep(2)
    except KeyboardInterrupt:
        pass
    finally:
        logcat.terminate()


def resume(run):
    r = researcher('resume')
    log(run, 'resume', ok=r.get('ok'), error=r.get('error'), data=r.get('data'))


def abort(run, reason):
    r = researcher('abort', reason)
    log(run, 'abort', reason=reason, ok=r.get('ok'), error=r.get('error'), data=r.get('data'))


def export(run):
    participant = (run / 'participant.txt').read_text(encoding='utf-8').strip()
    bundles = adb('shell', 'ls -t ' + FILES + '/ResearchSessionRegistry/Exports').split()
    pulled = None
    for bundle in bundles:
        manifest = adb('shell', 'cat ' + FILES + '/ResearchSessionRegistry/Exports/' + bundle + '/manifest.json', check=False)
        if participant in manifest:
            pulled = run / 'export' / bundle
            pulled.parent.mkdir(parents=True, exist_ok=True)
            adb('pull', FILES + '/ResearchSessionRegistry/Exports/' + bundle, str(pulled.parent))
            break
    if not pulled:
        raise SystemExit('No export bundle names ' + participant)
    record = json.loads((pulled / 'raw' / 'research-session.json').read_text(encoding='utf-8'))
    destination = run / 'ingest'
    p = subprocess.run([sys.executable, str(HERE / 'research_export.py'), 'ingest', '--source', str(pulled), '--destination', str(destination),
                        '--participant', participant, '--session', record['session_id']], capture_output=True, text=True, encoding='utf-8')
    log(run, 'export_ingest', bundle=pulled.name, session=record['session_id'], state=record.get('session_state'),
        termination_reason=record.get('termination_reason'), exit=p.returncode, stdout=p.stdout[-800:], stderr=p.stderr[-800:])


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    command, rest = sys.argv[1], sys.argv[2:]
    if command == 'install':
        install(rest[0], Path(rest[1]))
    else:
        run = Path(rest[0])
        if command == 'render':
            sys.exit(0 if render(run, int(rest[1]) if len(rest) > 1 else 4) else 1)
        elif command == 'start':
            start(run, rest[1], rest[2])
        elif command == 'watch':
            watch(run)
        elif command == 'resume':
            resume(run)
        elif command == 'abort':
            abort(run, rest[1])
        elif command == 'export':
            export(run)
        else:
            sys.exit(__doc__)


if __name__ == '__main__':
    main()
