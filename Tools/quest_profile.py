"""Restricted app-owned Quest transport. Every request belongs to one foreground profile."""
import argparse
import base64
import json
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import time
import uuid

PACKAGE = 'th.ac.tmu.vr.researchflow.quest3.technical'
AUTHORITY = PACKAGE + '.quest-profile'
KEYS = ('serial', 'android_user', 'package', 'uid', 'pid', 'process_start_ticks', 'boot_id', 'build_sha256', 'apk_path', 'foreground_activity')
NATIVE_KEYS = tuple(k for k in KEYS if k not in ('serial', 'foreground_activity'))
EXPORT_FILES = ('manifest.json', 'COMPLETE.json', 'raw/events.jsonl', 'raw/movement.csv', 'raw/session_metadata.json', 'raw/research-session.json')
RESPONSE_WAIT_SECONDS = 120


class ProfileError(ConnectionError):
    pass


def token(value):
    if not isinstance(value, str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,159}', value):
        raise ProfileError('Unsafe transport identity')
    return value


def foreground_activity(dump, user, package):
    # Android may resume one activity per display/root task. Only the authoritative
    # top-focused root and its display's focused app/window determine our foreground.
    sections = dump.split('ActivityTaskSupervisor state:')
    if len(sections) != 2:
        raise ProfileError('Authoritative foreground task/display unavailable')
    roots = re.findall(r'(?m)^[ \t]*topDisplayFocusedRootTask=Task\{(\S+) #(\d+)\b[^\r\n]*\}[ \t]*\r?$', sections[1])
    if len(roots) != 1:
        raise ProfileError('Authoritative foreground root task unavailable or ambiguous')
    lines = sections[0].splitlines()
    display, selected = None, []
    for index, line in enumerate(lines):
        header = re.fullmatch(r'Display #(\d+) \(activities from top to bottom\):', line)
        if header:
            display = header[1]
        task = re.match(r'^([ \t]*)\* Task\{(\S+) #(\d+)\b', line)
        if task and task.group(2, 3) == roots[0]:
            selected.append((index, len(task[1]), display))
    if len(selected) != 1 or selected[0][2] is None:
        raise ProfileError('Focused root task not uniquely present on a display')
    begin, indent, display = selected[0]
    end = len(lines)
    for index in range(begin + 1, len(lines)):
        task = re.match(r'^([ \t]*)\* Task\{', lines[index])
        if lines[index].startswith('Display #') or task and len(task[1]) <= indent:
            end = index
            break
    record = r'ActivityRecord\{(\S+) u(\d+) (\S+) t(\d+)(?:[ \t][^}]*)?\}'
    resumed = re.findall(r'(?m)^[ \t]*topResumedActivity=' + record + r'[ \t]*$', '\n'.join(lines[begin:end]))
    displays = re.split(r'(?m)^[ \t]*Display: mDisplayId=(\d+)[^\r\n]*\r?$', sections[1])
    blocks = [displays[i + 1] for i in range(1, len(displays), 2) if displays[i] == display]
    if len(blocks) != 1:
        raise ProfileError('Focused display unavailable or ambiguous')
    focused = re.findall(r'(?m)^[ \t]*mFocusedApp=' + record + r'[ \t]*\r?$', blocks[0])
    windows = re.findall(r'(?m)^[ \t]*mCurrentFocus=Window\{\S+ u(\d+) ([^}\r\n]+)\}[ \t]*\r?$', blocks[0])
    component = package + '/com.unity3d.player.UnityPlayerGameActivity'
    if len(focused) != 1 or resumed.count(focused[0]) != 1 or focused[0][1:3] != (str(user), component) or windows != [(str(user), component)]:
        raise ProfileError('Foreground activity/profile mismatch')
    return component


class Client:
    def __init__(self, adb, serial, package=PACKAGE, runner=subprocess.run, expected=None, native=True):
        if package != PACKAGE:
            raise ProfileError('Only the exact technical package is permitted')
        self.adb, self.serial, self.package, self.runner = str(adb), token(serial), package, runner
        self.request_log = []
        self._apk_cache = {expected['apk_path']: expected['build_sha256']} if expected and expected.get('apk_path') and re.fullmatch(r'[a-f0-9]{64}', expected.get('build_sha256', '')) else {}
        self.bound = self.snapshot()
        if expected:
            self.equal(expected, self.bound)
        identity = self.call('identity', binding=False) if native else None
        if identity is not None:
            self.native_equal(identity, self.bound)
        if identity is not None and (identity.get('foreground_user') is not True or identity.get('app_focused') is not True):
            raise ProfileError('App/profile is not foreground and focused')
        if identity is not None:
            self.bound.update({k: identity.get(k) for k in ('instance_id', 'session_id', 'run_id')})
            if expected and all(k in expected for k in ('instance_id', 'session_id', 'run_id')):
                if any(identity.get(k) != expected[k] for k in ('instance_id', 'session_id', 'run_id')):
                    raise ProfileError('Native instance/session/run differs from the frozen operation binding')
        self.check()

    def run(self, *args, output=None):
        args = list(map(str, args))
        if args and args[0] in ('shell', 'exec-out'):
            args = [args[0], shlex.join(args[1:])]
        p = self.runner([self.adb, '-s', self.serial, *args], capture_output=output is None,
                        stdout=output, stderr=subprocess.PIPE if output else None, timeout=60)
        if p.returncode:
            err = (p.stderr or p.stdout or b'ADB disconnected')
            raise ProfileError(err.decode(errors='replace').strip() if isinstance(err, bytes) else str(err))
        result = p.stdout or b''
        return result.decode(errors='strict').strip() if isinstance(result, bytes) else result.strip()

    def snapshot(self):
        raw = self.run('shell', 'am', 'get-current-user')
        if not re.fullmatch(r'[0-9]+', raw):
            raise ProfileError('Current Android user unavailable or malformed')
        user = int(raw)
        if self.run('get-serialno') != self.serial or self.run('get-state') != 'device':
            raise ProfileError('Device serial/state mismatch')
        packages = self.run('shell', 'pm', 'list', 'packages', '--user', user, '-U', self.package)
        owners = re.findall(r'^package:' + re.escape(self.package) + r' uid:(\d+)\s*$', packages, re.M)
        if len(owners) != 1 or int(owners[0]) // 100000 != user:
            raise ProfileError('Package UID/profile ownership mismatch')
        uid = int(owners[0])
        procs = self.run('shell', 'ps', '-A', '-o', 'UID,PID,NAME')
        pids = re.findall(r'^\s*' + str(uid) + r'\s+(\d+)\s+' + re.escape(self.package) + r'\s*$', procs, re.M)
        if len(pids) != 1:
            raise ProfileError('Expected exactly one main process owned by selected package/user')
        pid = int(pids[0])
        status = self.run('shell', 'cat', f'/proc/{pid}/status')
        uids = re.findall(r'^Uid:\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*$', status, re.M)
        if uids != [(str(uid),) * 4]:
            raise ProfileError('Process UID ownership changed')
        stat = self.run('shell', 'cat', f'/proc/{pid}/stat').rsplit(') ', 1)
        if len(stat) != 2 or len(stat[1].split()) < 20:
            raise ProfileError('Process lifetime unavailable')
        birth = stat[1].split()[19]
        if not birth.isdigit():
            raise ProfileError('Malformed process birth token')
        activity = self.run('shell', 'dumpsys', 'activity', 'activities')
        foreground = foreground_activity(activity, user, self.package)
        paths = self.run('shell', 'pm', 'path', '--user', user, self.package).splitlines()
        if len(paths) != 1 or not re.fullmatch(r'package:/data/app/[A-Za-z0-9_~+=/.-]+\.apk', paths[0]):
            raise ProfileError('Unambiguous installed base APK required')
        apk = paths[0][8:]
        sha = self._apk_cache.get(apk)
        if sha is None:
            sha = self.run('shell', 'sha256sum', apk).split()[0]
        if not re.fullmatch(r'[a-f0-9]{64}', sha):
            raise ProfileError('Installed APK hash unavailable')
        self._apk_cache[apk] = sha
        boot = self.run('shell', 'cat', '/proc/sys/kernel/random/boot_id')
        if not re.fullmatch(r'[a-f0-9-]{36}', boot):
            raise ProfileError('Device boot identity unavailable')
        if self.run('shell', 'am', 'get-current-user') != str(user):
            raise ProfileError('Android user changed during identity capture')
        return dict(serial=self.serial, android_user=user, package=self.package, uid=uid, pid=pid,
                    process_start_ticks=birth, boot_id=boot, build_sha256=sha, apk_path=apk, foreground_activity=foreground)

    @staticmethod
    def equal(expected, actual):
        if any(expected.get(k) != actual.get(k) for k in KEYS):
            raise ProfileError('Bound device/profile/process lifetime/build/foreground changed')

    @staticmethod
    def native_equal(native, expected):
        if any(native.get(k) != expected.get(k) for k in NATIVE_KEYS):
            raise ProfileError('Native provider owner/process/build mismatch')

    def check(self):
        self.equal(self.bound, self.snapshot())

    def scope_equal(self, identity):
        self.native_equal(identity, self.bound)
        if any(identity.get(k) != self.bound.get(k) for k in ('instance_id', 'session_id', 'run_id')):
            raise ProfileError('Bound native instance/session/run changed')
        if identity.get('foreground_user') is not True or identity.get('app_focused') is not True:
            raise ProfileError('App/profile is no longer foreground and focused')

    def scope_check(self):
        self.scope_equal(self.call('identity'))

    def call(self, method, payload=None, arg=None, binding=True):
        if payload is not None:
            if arg is not None:
                raise ProfileError('Transport arg and payload cannot both be supplied')
            arg = json.dumps(payload, separators=(',', ':'), allow_nan=False)
        if binding:
            self.check()
        args = ['shell', 'content', 'call', '--user', self.bound['android_user'], '--uri', 'content://' + AUTHORITY, '--method', method]
        if arg is not None:
            args += ['--arg', str(arg)]
        text = self.run(*args)
        match = re.fullmatch(r'Result: Bundle\[\{result=([A-Za-z0-9+/=]+)\}\]', text)
        if not match:
            if text:
                raise ProfileError('Content call returned no native result: ' + text[:512])
            raise ProfileError('Restricted native provider unavailable; no legacy /sdcard fallback')
        try:
            result = json.loads(base64.b64decode(match[1], validate=True))
        except (ValueError, json.JSONDecodeError) as ex:
            raise ProfileError('Malformed native provider response') from ex
        if result.get('ok') is not True:
            raise ProfileError(result.get('error') or 'Native provider refused request')
        if binding:
            self.check()
        return result.get('data')

    def request(self, body):
        identity = self.call('identity')
        self.scope_equal(identity)
        binding = dict(self.bound)
        request = dict(body, request_id=str(uuid.uuid4()), binding=binding,
                       deadline_elapsed_ms=identity['elapsed_ms'] + 15000)
        self.request_log.append(dict(method=body.get('command'), request_id=request['request_id'], binding=binding))
        self.call('dispatch', request)
        # The device accepts a request only within 15 s; the reply can take longer while a large export snapshot is written.
        until = time.monotonic() + RESPONSE_WAIT_SECONDS
        while time.monotonic() < until:
            response = self.call('response', arg=request['request_id'])
            if response:
                if response.get('request_id') != request['request_id'] or response.get('request_binding') != binding:
                    raise ProfileError('Response request/session binding mismatch')
                self.native_equal(response.get('transport_identity') or {}, self.bound)
                if response.get('transport_identity', {}).get('instance_id') != self.bound['instance_id']:
                    raise ProfileError('Response belongs to another native instance')
                return response
            time.sleep(.25)
        raise ProfileError('No response within the bound request lifetime')

    def scoped(self, method, **values):
        identity = self.call('identity')
        self.scope_equal(identity)
        payload = dict(binding=dict(self.bound),
                       request_id=str(uuid.uuid4()), deadline_elapsed_ms=identity['elapsed_ms'] + 15000, **values)
        self.request_log.append(dict(method=method, request_id=payload['request_id'], binding=payload['binding']))
        result = self.call(method, payload)
        self.scope_check()
        return result

    def read(self, registry, bundle, name, destination):
        # Only immutable queued export files are addressable. The provider revalidates the lease.
        if name not in EXPORT_FILES:
            raise ProfileError('File not in immutable export allowlist')
        grant = self.scoped('read_grant', registry=token(registry), bundle=token(bundle), name=name)
        uri = 'content://' + AUTHORITY + '/export/' + grant['grant_id']
        target = Path(destination)
        target.parent.mkdir(parents=True, exist_ok=True)
        temp = target.with_name(target.name + '.' + str(uuid.uuid4()) + '.partial')
        try:
            self.check()
            with temp.open('xb') as out:
                self.run('exec-out', 'content', 'read', '--user', self.bound['android_user'], '--uri', uri, output=out)
            self.check()
            self.scope_check()
            temp.replace(target)
        finally:
            temp.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--adb', required=True)
    parser.add_argument('--serial', default=os.environ.get('QUEST_SERIAL', '2G0YC5ZHBF00FJ'))
    parser.add_argument('--binding', default=os.environ.get('QUEST_PROFILE_BINDING'))
    parser.add_argument('action', choices=('identity', 'check', 'request', 'probe', 'relaunch'))
    parser.add_argument('value', nargs='?')
    args = parser.parse_args()
    try:
        expected = json.loads(Path(args.binding).read_text(encoding='utf-8')) if args.binding else None
        native = args.action not in ('identity', 'check') or args.action == 'check' and expected is not None and all(k in expected for k in ('instance_id', 'session_id', 'run_id'))
        client = Client(args.adb, args.serial, expected=expected, native=native)
        if args.action in ('identity', 'check'):
            result = client.bound
        elif args.action == 'request':
            result = client.request(json.loads(args.value))
        elif args.action == 'probe':
            result = client.scoped('probe', action=args.value or 'status')
        else:
            state = client.request(dict(command='state'))['state']
            if state.get('headset', {}).get('user_present') is not False or state.get('flow_state') not in ('ResearcherSetup', 'ParticipantComplete', 'ResearcherSummary'):
                raise ProfileError('Relaunch refused while wearer presence/session is active or unknown')
            client.scope_check()
            client.run('shell', 'am', 'force-stop', '--user', client.bound['android_user'], client.package)
            if client.run('shell', 'am', 'get-current-user') != str(client.bound['android_user']):
                raise ProfileError('User changed after stop; no relaunch sent')
            client.run('shell', 'am', 'start', '--user', client.bound['android_user'], '-n', client.package + '/com.unity3d.player.UnityPlayerGameActivity')
            result = dict(relaunched_user=client.bound['android_user'], previous_binding=client.bound)
        print(json.dumps(result, allow_nan=False))
        return 0 if not isinstance(result, dict) or result.get('ok', True) is True else 1
    except (ProfileError, OSError, ValueError, KeyError, subprocess.TimeoutExpired) as ex:
        print('QUEST_PROFILE_GUARD: ' + str(ex), file=sys.stderr)
        return 3


if __name__ == '__main__':
    sys.exit(main())
