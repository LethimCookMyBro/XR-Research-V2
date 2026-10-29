"""Synthetic elapsed-clock/lease races; fixture-adb never contacts a device."""
import base64
import json
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from quest_profile import Client, ProfileError
from test_quest_profile import Device, SERIAL


class LeaseDevice(Device):
    def __init__(self):
        super().__init__()
        self.elapsed = 1000
        self.grant = None
        self.grant_elapsed = self.open_elapsed = None
        self.open_delay = 0
        self.after_grant = self.after_read = None
        self.fail_read = False
        self.bytes = b"immutable synthetic export\n"

    def identity(self):
        return dict(super().identity(), elapsed_ms=self.elapsed)

    def change(self, kind):
        if kind == "session": self.session = "changed-session"
        elif kind == "run": self.run_id = "changed-run"
        elif kind == "switch": self.user = 0
        elif kind == "restart": self.birth = "9999999"
        elif kind == "focus": self.focused = False

    @staticmethod
    def result(args, data):
        text = "Result: Bundle[{result=" + base64.b64encode(json.dumps(dict(ok=True, data=data)).encode()).decode() + "}]"
        return subprocess.CompletedProcess(args, 0, text.encode(), b"")

    def validate(self, request):
        remaining = request["deadline_elapsed_ms"] - self.elapsed
        if not 0 < remaining <= 15000:
            raise ProfileError("Synthetic native request deadline expired or extended")
        actual = self.identity()
        if not actual["app_focused"] or not actual["foreground_user"]:
            raise ProfileError("Synthetic native grant focus changed")
        if any(actual.get(key) != value for key, value in request["binding"].items()
               if key not in ("serial", "foreground_activity")):
            raise ProfileError("Synthetic native grant binding changed")

    def run(self, args, **kwargs):
        command = args[3:]
        if command[0] in ("shell", "exec-out"):
            command = [command[0], *shlex.split(command[1])]
        if command[:3] == ["shell", "content", "call"]:
            method = command[command.index("--method") + 1]
            if method in ("probe", "read_grant"):
                request = json.loads(command[command.index("--arg") + 1])
                self.validate(request)
                if method == "read_grant":
                    self.grant = request
                    self.grant_elapsed = self.elapsed
                    self.change(self.after_grant)
                    return self.result(args, {"grant_id": "a5631bd8-264d-4827-b833-eb2cc23402b3"})
        if command[:3] == ["exec-out", "content", "read"]:
            self.elapsed += self.open_delay
            self.open_elapsed = self.elapsed
            grant, self.grant = self.grant, None
            if grant is None: raise ProfileError("Synthetic native grant already consumed")
            self.validate(grant)
            kwargs["stdout"].write(self.bytes)
            self.elapsed += 60000  # A valid open may stream longer than the grant's open deadline.
            self.change(self.after_read)
            if self.fail_read:
                return subprocess.CompletedProcess(args, 1, b"", b"synthetic stream failure")
            return subprocess.CompletedProcess(args, 0, b"", b"")
        return super().run(args, **kwargs)


class LeaseChecks(unittest.TestCase):
    @staticmethod
    def client(device):
        return Client("fixture-adb", SERIAL, runner=device.run)

    def test_scoped_dispatch_uses_fresh_identity_after_slow_host_checks(self):
        device = LeaseDevice()
        client = self.client(device)
        check = client.check
        def slow_check():
            check()
            device.elapsed += 16000
        client.check = slow_check
        self.assertTrue(client.scoped("probe", action="status")["enabled"])

    def test_grant_is_consumed_before_post_checks_and_long_stream_is_allowed(self):
        device = LeaseDevice()
        client = self.client(device)
        check = client.check
        def slow_after_grant():
            check()
            if device.grant_elapsed is not None: device.elapsed += 16000
        client.check = slow_after_grant
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "result"
            client.read("main", "snapshot_fixture", "manifest.json", target)
            self.assertEqual(target.read_bytes(), device.bytes)
            self.assertEqual(list(Path(directory).glob("*.partial")), [])
        self.assertEqual(device.open_elapsed, device.grant_elapsed)
        self.assertGreater(device.elapsed - device.open_elapsed, 15000)

    def test_output_directory_preparation_does_not_age_a_grant(self):
        device = LeaseDevice()
        client = self.client(device)
        mkdir = Path.mkdir
        def slow_mkdir(path, *args, **kwargs):
            result = mkdir(path, *args, **kwargs)
            device.elapsed += 16000
            return result
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "nested" / "result"
            with patch.object(Path, "mkdir", slow_mkdir):
                client.read("main", "snapshot_fixture", "manifest.json", target)
            self.assertEqual(target.read_bytes(), device.bytes)
        self.assertEqual(device.open_elapsed, device.grant_elapsed)

    def test_native_deadline_still_rejects_a_late_open(self):
        device = LeaseDevice()
        client = self.client(device)
        device.open_delay = 16000
        self.assert_preserved_failure(client, device, "deadline")

    def test_native_open_still_checks_grant_binding_and_focus(self):
        for change in ("session", "run", "switch", "restart", "focus"):
            with self.subTest(change=change):
                device = LeaseDevice()
                client = self.client(device)
                device.after_grant = change
                self.assert_preserved_failure(client, device)

    def test_post_stream_checks_reject_profile_process_session_and_focus_races(self):
        for change in ("session", "run", "switch", "restart", "focus"):
            with self.subTest(change=change):
                device = LeaseDevice()
                client = self.client(device)
                device.after_read = change
                self.assert_preserved_failure(client, device)
                self.assertIsNotNone(device.open_elapsed)

    def test_large_immutable_stream_uses_separate_bounded_io_timeout(self):
        device = LeaseDevice()
        run = device.run
        timeouts = []
        def two_minute_stream(args, **kwargs):
            command = args[3:]
            if command[0] in ("shell", "exec-out"):
                command = [command[0], *shlex.split(command[1])]
            is_read = command[:3] == ["exec-out", "content", "read"]
            timeout = kwargs["timeout"]
            timeouts.append((is_read, timeout))
            self.assertGreaterEqual(timeout, 120 if is_read else 60)
            self.assertEqual(timeout, 1800 if is_read else 60)
            result = run(args, **kwargs)
            if is_read: device.elapsed += 60000  # Combined with LeaseDevice's minute:120s, without wall-clock sleep.
            return result
        client = Client("fixture-adb", SERIAL, runner=two_minute_stream)
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "movement.csv"
            client.read("main", "snapshot_fixture", "raw/movement.csv", target)
            self.assertEqual(target.read_bytes(), device.bytes)
        self.assertEqual(device.open_elapsed, device.grant_elapsed)
        self.assertEqual(device.elapsed - device.open_elapsed, 120000)
        self.assertEqual(sum(is_read for is_read, _ in timeouts), 1)

    def test_stream_error_preserves_destination_and_removes_partial(self):
        device = LeaseDevice()
        client = self.client(device)
        device.fail_read = True
        self.assert_preserved_failure(client, device, "stream failure")

    def assert_preserved_failure(self, client, device, message=None):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "result"
            target.write_bytes(b"existing destination")
            with self.assertRaisesRegex(ProfileError, message or ".*"):
                client.read("main", "snapshot_fixture", "manifest.json", target)
            self.assertEqual(target.read_bytes(), b"existing destination")
            self.assertEqual(list(Path(directory).glob("*.partial")), [])


if __name__ == "__main__":
    unittest.main(verbosity=2)
