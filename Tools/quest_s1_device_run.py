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
import re
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from quest_profile import Client, ProfileError

HERE = Path(__file__).resolve().parent
PACKAGE = 'th.ac.tmu.vr.researchflow.quest3.technical'
SERIAL = os.environ.get('QUEST_SERIAL', '2G0YC5ZHBF00FJ')
FILES = '/sdcard/Android/data/' + PACKAGE + '/files'
AAPT = r'C:\Program Files\Unity\Hub\Editor\6000.3.20f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools\36.0.0\aapt.exe'
# Plain 'bash' on Windows resolves to WSL (System32), which cannot see D:/ paths; use Git for Windows' bash.
BASH = os.environ.get('GIT_BASH') or next((str(d / 'bin' / 'bash.exe') for d in Path(shutil.which('git') or '.').resolve().parents if (d / 'bin' / 'bash.exe').exists()), 'bash')


def now():
    return datetime.now(timezone.utc).isoformat()


def adb(*args, check=True, text=True):
    env = dict(os.environ, MSYS2_ARG_CONV_EXCL='*')
    p = subprocess.run(['adb', '-s', SERIAL, *args], capture_output=True, text=text, env=env, timeout=900)
    if check and p.returncode:
        raise SystemExit('adb ' + ' '.join(args) + ' failed: ' + (p.stderr or p.stdout))
    return p.stdout


def researcher(*args):
    p = subprocess.run([BASH, (HERE / 'researcher-adb.sh').as_posix(), *args], capture_output=True, text=True, encoding='utf-8', cwd=HERE, timeout=120)
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


def registry_hashes(client):
    out = client.run('shell', 'sh', '-c', 'cd ' + FILES + ' && find ResearchSessionRegistry -type f -not -path "*/Logs/*" -not -path "*/Exports/*" -not -path "*/ExportQueue/*" -exec sha256sum {} +')
    rows = sorted(out.splitlines())
    if not any(row.endswith('  ResearchSessionRegistry/registry.json') for row in rows) or any(not re.fullmatch(r'[a-f0-9]{64}  ResearchSessionRegistry/.+', row) for row in rows):
        raise ProfileError('Valid registry hash snapshot including registry.json required')
    return rows


def apk_metadata(apk):
    info = apk.stat()
    return info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns


def install(apk, run):
    apk = Path(apk)
    expected = hashlib.sha256(apk.read_bytes()).hexdigest()
    source_stat = apk_metadata(apk)
    try:
        manifest = subprocess.run([AAPT, 'dump', 'badging', str(apk)], capture_output=True, text=True, timeout=60)
        if manifest.returncode or re.findall(r"^package: name='([^']+)'", manifest.stdout, re.M) != [PACKAGE]:
            raise ProfileError('Candidate APK must declare only the exact technical package')
        client = Client(os.environ.get('ADB', 'adb'), SERIAL, runner=subprocess.run)
        if client.bound['android_user'] != 0:
            raise ProfileError('Legacy registry snapshot is User 0 only; no other-profile fallback')
        users = client.run('shell', 'pm', 'list', 'users').splitlines()
        user_ids = []
        for row in users:
            if row.strip() == 'Users:':
                continue
            match = re.fullmatch(r'\s*UserInfo\{(\d+):[^{}\r\n]*\}(?: running)?\s*', row)
            if not match:
                raise ProfileError('Android profile inventory unavailable or ambiguous')
            user_ids.append(match[1])
        if '0' not in user_ids or len(user_ids) != len(set(user_ids)):
            raise ProfileError('Android profile inventory unavailable or ambiguous')
        for user in user_ids:
            if user != '0' and client.run('shell', 'sh', '-c', 'pm list packages --user ' + user + ' ' + PACKAGE + ' 2>&1'):
                raise ProfileError('Technical package exists on another profile; its session is unknown, so shared APK replacement is refused')
        for row in client.run('shell', 'ps', '-A', '-o', 'UID,PID,NAME').splitlines():
            fields = row.split()
            if fields and (fields[-1] == PACKAGE or fields[-1].startswith(PACKAGE + ':')) and (len(fields) != 3 or not fields[0].isdigit() or int(fields[0]) // 100000 != 0):
                raise ProfileError('Technical process on another or unknown profile; shared APK replacement is refused')
        before = registry_hashes(client)
        log(run, 'install_begin', apk=str(apk), sha256=expected, registry_files=len(before), identity=client.bound)
        if hashlib.sha256(apk.read_bytes()).hexdigest() != expected:
            raise ProfileError('Candidate APK changed after source hash capture')
        if apk_metadata(apk) != source_stat:
            raise ProfileError('Candidate APK metadata changed after capture')
        client.scope_check()
        state = client.request(dict(command='state'))
        st = state.get('state') or {}
        if state.get('ok') is not True or (st.get('headset') or {}).get('user_present') is not False or st.get('flow_state') not in ('ResearcherSetup', 'ParticipantComplete', 'ResearcherSummary'):
            raise ProfileError('Install refused while wearer presence/session is active or unknown')
        if st.get('flow_state') == 'ResearcherSetup' and (st.get('session_id') or st.get('run_id')):
            raise ProfileError('Install refused while a session remains bound at setup')
        if apk_metadata(apk) != source_stat:
            raise ProfileError('Candidate APK changed during final live checks')
        installed_process = subprocess.run([client.adb, '-s', client.serial, 'install', '--user', '0', '-r', str(apk)],
                                           capture_output=True, text=True, timeout=900)
        if installed_process.returncode:
            raise ProfileError('Install failed; no launch sent: ' + (installed_process.stderr or installed_process.stdout))
        out = installed_process.stdout
        if client.run('shell', 'am', 'get-current-user') != '0' or client.run('get-serialno') != client.bound['serial'] or client.run('get-state') != 'device' or client.run('shell', 'cat', '/proc/sys/kernel/random/boot_id') != client.bound['boot_id']:
            raise ProfileError('Device/profile changed after install; no launch sent')
        paths = client.run('shell', 'pm', 'path', '--user', '0', PACKAGE).splitlines()
        if len(paths) != 1 or not re.fullmatch(r'package:/data/app/[A-Za-z0-9_~+=/.-]+\.apk', paths[0]):
            raise ProfileError('Unambiguous installed base APK required; no launch sent')
        installed = client.run('shell', 'sha256sum', paths[0][8:]).split()[0]
        after = registry_hashes(client)
        log(run, 'install_readback', adb=out.strip()[-40:], installed_sha256=installed, matches=installed == expected, registry_unchanged=before == after)
        if installed != expected or before != after:
            raise ProfileError('Install readback failed; no launch sent')
        if client.run('shell', 'am', 'get-current-user') != '0':
            raise ProfileError('User changed before launch; no launch sent')
        client.run('shell', 'am', 'start', '--user', '0', '-W', '-n', PACKAGE + '/com.unity3d.player.UnityPlayerGameActivity')
        last_error = 'No state response'
        for _ in range(40):
            try:
                refreshed = Client(client.adb, SERIAL, runner=subprocess.run)
                if any(refreshed.bound[key] != client.bound[key] for key in ('serial', 'android_user', 'package', 'uid', 'boot_id')) or refreshed.bound['build_sha256'] != expected:
                    raise ProfileError('Launched device/profile/build differs from install binding')
                state = refreshed.request(dict(command='state'))
                if state.get('ok'):
                    log(run, 'launched', identity=refreshed.bound, flow=state['state'].get('flow_state'))
                    return
            except ProfileError as error:
                last_error = str(error)
            time.sleep(3)
        raise ProfileError('App did not answer bound state after launch: ' + last_error)
    except (ProfileError, OSError, subprocess.TimeoutExpired) as error:
        raise SystemExit('QUEST_PROFILE_GUARD: ' + str(error)) from error


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
