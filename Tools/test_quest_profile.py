"""Synthetic ADB profile races. No real device is contacted."""
import base64
import json
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest
from quest_profile import Client, ProfileError, PACKAGE, KEYS

SERIAL = '2G0YC5ZHBF00FJ'
APK = '/data/app/~~fixture/' + PACKAGE + '-fixture/base.apk'
BOOT = 'b1d2c367-adf9-44ee-bee4-273bad80340a'


class Device:
    def __init__(self, user=10):
        self.user, self.calls, self.request = user, [], None
        self.uid_wrong = self.foreground_wrong = self.native_wrong = False
        self.activity_override = None
        self.focused = True
        self.after_dispatch = self.bad_response = self.after_scoped = None
        self.session, self.run_id = 'fixture-session', 'fixture-run'
        self.pid, self.birth = 27306 if user == 10 else 9372, '8925587' if user == 10 else '8618498'

    def identity(self):
        return dict(package=PACKAGE, android_user=self.user, uid=self.user * 100000 + 10158,
                    pid=self.pid, process_start_ticks=self.birth, boot_id=BOOT, build_sha256='7' * 64,
                    apk_path=APK, instance_id='fixture-instance', session_id=self.session, run_id=self.run_id,
                    elapsed_ms=1000, app_focused=self.focused, foreground_user=True)

    def run(self, args, **kwargs):
        self.calls.append(args)
        assert args[:3] == ['fixture-adb', '-s', SERIAL], args
        command = args[3:]
        if command[0] in ('shell', 'exec-out'):
            command = [command[0], *shlex.split(command[1])]
        if command == ['shell', 'am', 'get-current-user']:
            value = str(self.user) if self.user is not None else ''
        elif command == ['get-serialno']: value = SERIAL
        elif command == ['get-state']: value = 'device'
        elif command[:5] == ['shell', 'pm', 'list', 'packages', '--user']:
            uid = self.user * 100000 + 10158
            value = 'package:' + PACKAGE + ' uid:' + str(uid)
        elif command == ['shell', 'ps', '-A', '-o', 'UID,PID,NAME']:
            selected = f'{self.user * 100000 + 10158} {self.pid} {PACKAGE}'
            other = f'{10158 if self.user == 10 else 1010158} {9372 if self.user == 10 else 27306} {PACKAGE}'
            value = 'UID PID NAME\n' + other + '\n' + selected
        elif command[-1].endswith('/status'):
            uid = self.user * 100000 + 10158 + int(self.uid_wrong)
            value = 'Uid:\t' + '\t'.join([str(uid)] * 4)
        elif command[-1].endswith('/stat'):
            value = f'{self.pid} (uest3.technical) ' + ' '.join(['S'] + ['0'] * 18 + [self.birth] + ['0'] * 30)
        elif command == ['shell', 'dumpsys', 'activity', 'activities']:
            user = 0 if self.foreground_wrong else self.user
            component = PACKAGE + '/com.unity3d.player.UnityPlayerGameActivity'
            record = f'ActivityRecord{{fixture u{user} {component} t12}}'
            value = (f'Display #0 (activities from top to bottom):\n'
                     f'  * Task{{root #11 type=standard}}\n'
                     f'    * Task{{leaf #12 type=standard}}\n'
                     f'      topResumedActivity={record}\n'
                     f'ActivityTaskSupervisor state:\n'
                     f'  topDisplayFocusedRootTask=Task{{root #11 type=standard}}\n'
                     f'  Display: mDisplayId=0\n'
                     f'  mCurrentFocus=Window{{window u{user} {component}}}\n'
                     f'  mFocusedApp={record}\n')
            if self.activity_override is not None: value = self.activity_override
        elif command[:4] == ['shell', 'pm', 'path', '--user']: value = 'package:' + APK
        elif command[:2] == ['shell', 'sha256sum']: value = '7' * 64 + '  ' + APK
        elif command == ['shell', 'cat', '/proc/sys/kernel/random/boot_id']: value = BOOT
        elif command[:3] == ['shell', 'content', 'call']:
            assert command[command.index('--user') + 1] == str(self.user), command
            method = command[command.index('--method') + 1]
            if method == 'identity':
                data = self.identity()
                if self.native_wrong: data['uid'] = 10158
            elif method == 'dispatch':
                self.request = json.loads(command[command.index('--arg') + 1])
                data = dict(queued=True)
                if self.after_dispatch == 'switch': self.user = 0
                if self.after_dispatch == 'restart': self.birth = '9999999'
            elif method == 'response':
                data = dict(ok=True, request_id=self.request['request_id'], request_binding=self.request['binding'],
                            transport_identity=self.identity(), state=dict(session_id='fixture-session', run_id='fixture-run'))
                if self.bad_response == 'uuid': data['request_id'] = 'stale-request'
                if self.bad_response == 'session': data['request_binding'] = dict(self.request['binding'], session_id='stale-session')
                if self.bad_response == 'instance': data['transport_identity']['instance_id'] = 'restarted-instance'
            elif method == 'probe':
                data = dict(enabled=True)
                if self.after_scoped: self.session = 'changed-after-validation'
            else: raise AssertionError(command)
            value = 'Result: Bundle[{result=' + base64.b64encode(json.dumps(dict(ok=True, data=data)).encode()).decode() + '}]'
        else: raise AssertionError(command)
        return subprocess.CompletedProcess(args, 0, value.encode(), b'')


class ProfileChecks(unittest.TestCase):
    def client(self, device): return Client('fixture-adb', SERIAL, runner=device.run)

    def test_user_zero_and_ten_select_owned_pid_not_first_pid(self):
        for user in (0, 10):
            with self.subTest(user=user):
                device = Device(user); client = self.client(device)
                result = client.request(dict(command='state'))
                self.assertTrue(result['ok'])
                self.assertEqual(client.bound['android_user'], user)
                self.assertEqual(client.bound['pid'], 27306 if user == 10 else 9372)
                request = device.request
                self.assertEqual(request['binding']['session_id'], 'fixture-session')
                self.assertEqual(request['binding']['run_id'], 'fixture-run')
                self.assertRegex(request['request_id'], r'^[a-f0-9-]{36}$')
                self.assertEqual(request['deadline_elapsed_ms'], 16000)
                self.assertFalse(any('/sdcard' in ' '.join(call) or 'push' in call or 'pull' in call for call in device.calls))

    def test_unknown_user_foreground_mismatch_uid_ownership_and_provider_mismatch_refuse(self):
        for flag in ('unknown', 'foreground_wrong', 'uid_wrong', 'native_wrong'):
            with self.subTest(flag=flag):
                device = Device()
                if flag == 'unknown': device.user = None
                else: setattr(device, flag, True)
                with self.assertRaises(ProfileError): self.client(device)
                self.assertIsNone(device.request)

    def test_user_switch_and_pid_lifetime_restart_during_request_refuse_response(self):
        for change in ('switch', 'restart'):
            with self.subTest(change=change):
                device = Device(); client = self.client(device); device.after_dispatch = change
                with self.assertRaises(ProfileError): client.request(dict(command='state'))

    def test_stale_uuid_session_and_instance_are_not_accepted(self):
        for bad in ('uuid', 'session', 'instance'):
            with self.subTest(bad=bad):
                device = Device(); client = self.client(device); device.bad_response = bad
                with self.assertRaises(ProfileError): client.request(dict(command='state'))

    def test_wrong_bound_identity_stops_before_command_and_no_generic_export_path(self):
        device = Device(); first = self.client(device)
        with self.assertRaises(ProfileError): Client('fixture-adb', SERIAL, runner=device.run, expected=dict(first.bound, android_user=0))
        with tempfile.TemporaryDirectory() as td:
            for name in ('../events.jsonl', '/sdcard/file', 'raw/other.json', 'researcher-command.json'):
                with self.assertRaises(ProfileError): first.read('main', 'snapshot_fixture', name, Path(td)/'out')
            self.assertEqual(list(Path(td).iterdir()), [])
        self.assertIsNone(device.request)

    def test_frozen_constructor_and_scope_change_during_native_operation_refuse(self):
        device = Device(); client = self.client(device); frozen = dict(client.bound)
        device.session = 'next-session'
        with self.assertRaises(ProfileError): Client('fixture-adb', SERIAL, runner=device.run, expected=frozen)
        device.session = frozen['session_id']; device.after_scoped = True
        with self.assertRaises(ProfileError): client.scoped('probe', action='on')

    def test_all_required_os_fields_are_bound(self):
        client = self.client(Device())
        self.assertTrue(all(key in client.bound for key in KEYS))

    def test_same_pid_session_or_run_change_before_dispatch_cannot_retarget_operator(self):
        for key in ('session', 'run_id'):
            for operation in ('request', 'scoped'):
                with self.subTest(key=key, operation=operation):
                    device = Device(); client = self.client(device)
                    setattr(device, key, 'another-session-or-run')
                    with self.assertRaisesRegex(ProfileError, 'session/run changed'):
                        if operation == 'request': client.request(dict(command='abort'))
                        else: client.scoped('probe', action='on')
                    self.assertIsNone(device.request)

    def test_authoritative_focus_not_unity_presence_or_device_wide_resumed_count(self):
        device = Device()
        dump = device.run(['fixture-adb', '-s', SERIAL, 'shell', 'dumpsys activity activities']).stdout.decode()
        other = ('Display #5 (activities from top to bottom):\n'
                 '  * Task{other #21 type=standard}\n'
                 '    topResumedActivity=ActivityRecord{panel u10 com.oculus.store/.StoreActivity t21}\n')
        extra = ('  Display: mDisplayId=5\n'
                 '  mCurrentFocus=Window{store u10 com.oculus.store/.StoreActivity}\n'
                 '  mFocusedApp=ActivityRecord{panel u10 com.oculus.store/.StoreActivity t21}\n')
        multi = dump.replace('ActivityTaskSupervisor state:', other + 'ActivityTaskSupervisor state:') + extra
        for valid in (multi, multi.replace('Display #0 ', 'Display #7 ').replace('mDisplayId=0', 'mDisplayId=7')):
            device.activity_override = valid
            self.assertEqual(self.client(device).bound['android_user'], 10)
        for invalid in (
            multi.replace('topDisplayFocusedRootTask=Task{root #11', 'topDisplayFocusedRootTask=Task{other #21'),
            multi.replace('mFocusedApp=ActivityRecord{fixture u10', 'mFocusedApp=ActivityRecord{fixture u0'),
            multi.replace('mFocusedApp=ActivityRecord{fixture ', 'mFocusedApp=ActivityRecord{different '),
            multi.replace('mCurrentFocus=Window{window u10 ' + PACKAGE, 'mCurrentFocus=Window{window u10 com.oculus.store'),
            multi.replace('topResumedActivity=ActivityRecord{fixture u10', 'topResumedActivity=ActivityRecord{fixture u0'),
            multi.replace('topDisplayFocusedRootTask=Task{root #11 type=standard}', 'topDisplayFocusedRootTask=null'),
            multi.replace('  topDisplayFocusedRootTask=', '  topDisplayFocusedRootTask=Task{root #11 type=standard}\n  topDisplayFocusedRootTask='),
            multi.replace('ActivityTaskSupervisor state:', 'No authoritative supervisor:'),
        ):
            with self.subTest(dump=invalid):
                device.activity_override = invalid
                with self.assertRaises(ProfileError): self.client(device)
                self.assertIsNone(device.request)

    def test_native_focus_loss_before_command_still_refuses_dispatch(self):
        device = Device(); client = self.client(device); device.focused = False
        with self.assertRaisesRegex(ProfileError, 'foreground and focused'):
            client.request(dict(command='state'))
        self.assertIsNone(device.request)

    def test_slow_host_snapshot_does_not_expire_command_before_dispatch(self):
        device = Device(); client = self.client(device)
        clock = [1000]
        identity = device.identity
        run = device.run
        check = client.check
        device.identity = lambda: dict(identity(), elapsed_ms=clock[0])
        def slow_check():
            check()
            clock[0] += 16000
        def timed_run(args, **kwargs):
            command = shlex.split(args[-1])
            if '--method' in command and command[command.index('--method') + 1] == 'dispatch':
                payload = json.loads(command[command.index('--arg') + 1])
                self.assertGreater(payload['deadline_elapsed_ms'], clock[0])
            return run(args, **kwargs)
        client.check = slow_check
        client.runner = timed_run
        self.assertTrue(client.request(dict(command='state'))['ok'])

    def test_arg_json_keeps_colons_quotes_backslashes_and_unicode_and_rejects_dual_payload(self):
        device = Device(); client = self.client(device)
        body = dict(command='state', note="a:b 'quote' \\ path\nภาษาไทย")
        client.request(body)
        self.assertEqual(device.request['note'], body['note'])
        before = len(device.calls)
        with self.assertRaisesRegex(ProfileError, 'cannot both'):
            client.call('dispatch', payload=body, arg='another')
        self.assertEqual(len(device.calls), before)
        self.assertFalse(any('--extra' in shlex.split(call[-1]) for call in device.calls if call[3] == 'shell'))


if __name__ == '__main__':
    unittest.main(verbosity=2)
