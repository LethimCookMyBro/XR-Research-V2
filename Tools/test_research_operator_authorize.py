"""Study authorization tool against a synthetic adb. No real device is contacted."""
import hashlib
import json
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

import research_operator_authorize as tool

SERIAL = '2G0YC5ZHBF00FJ'
APPROVALS = {'operator_id': 'researcher-01', 'content_approved': True, 'content_approval_ref': 'CONTENT-2026-014',
             'language_approved': True, 'language_approval_ref': 'LANG-2026-007'}


def record(**changes):
    base = {'participant_id': 'TH02', 'group': 'Thai', 'test_data': False, 'session_state': 'Available', 'session_id': '', 'revision': 3}
    base.update(changes)
    return base


class FakeAdb:
    def __init__(self, records, bump_revision_on_second_read=False):
        self.files = {tool.REGISTRY: json.dumps({'schema': 'x', 'participants': records}).encode()}
        self.reads, self.bump = 0, bump_revision_on_second_read
        self.android_user = 0
        self.hardware = SERIAL
        self.package_uid = 10234
        self.switch_user_after_push = False
        self.pushes = 0

    def __call__(self, argv, capture_output, timeout):
        args = argv[3:]
        out = b''
        if args == ['get-state']:
            out = b'device'
        elif args[:2] == ['shell', 'getprop']:
            out = self.hardware.encode()
        elif args == ['shell', 'am', 'get-current-user']:
            out = str(self.android_user).encode()
        elif args[:4] == ['shell', 'pm', 'list', 'packages']:
            out = ('package:' + tool.PACKAGE + ' uid:' + str(self.package_uid)).encode()
        elif args[0] == 'pull':
            self.reads += 1
            data = json.loads(self.files[args[1]])
            if self.bump and self.reads > 1:
                data['participants'][0]['revision'] += 1
            Path(args[2]).write_bytes(json.dumps(data).encode())
        elif args[0] == 'push':
            self.pushes += 1
            self.files[args[2]] = Path(args[1]).read_bytes()
            if self.switch_user_after_push:
                self.android_user = 10
        elif args[:2] == ['shell', 'sha256sum']:
            out = (hashlib.sha256(self.files[args[2]]).hexdigest() + '  ' + args[2]).encode()
        elif args[:2] == ['shell', 'mv']:
            self.files[args[4]] = self.files.pop(args[3])
        elif args[:2] == ['shell', 'rm']:
            self.files.pop(args[3], None)
        return subprocess.CompletedProcess(argv, 0, out, b'')


class Tests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp())
        (self.tmp / 'approvals.json').write_text(json.dumps(APPROVALS), encoding='utf-8')

    def tearDown(self):
        shutil.rmtree(self.tmp)

    def args(self, **changes):
        values = dict(adb='adb', serial=SERIAL, approvals=str(self.tmp / 'approvals.json'), participant='TH02',
                      consent_ref='CONSENT-TH02-0001', consent_confirmed=True, outbox=str(self.tmp / 'outbox'))
        values.update(changes)
        return type('Args', (), values)

    def test_fields_match_the_app_contract(self):
        source = Path(__file__).resolve().parents[1] / 'Assets/VRMaintenanceResearch/ResearchFlow/Core/ResearchOperatorAuthorization.cs'
        block = re.search(r'class ResearchOperatorAuthorization\s*\{(.*?)\}', source.read_text(encoding='utf-8'), re.S).group(1)
        members = re.findall(r'\b([a-z_]+)\b(?=\s*[,;])', block)
        self.assertEqual(set(members), set(tool.FIELDS))

    def test_installs_bound_document_and_verifies_hash(self):
        adb = FakeAdb([record()])
        receipt = tool.authorize(self.args(), adb)
        installed = json.loads(adb.files[tool.TARGET])
        self.assertEqual(set(installed), set(tool.FIELDS))
        self.assertEqual((installed['participant_id'], installed['record_revision'], installed['link_status'], installed['session_id']),
                         ('TH02', 3, 'pre_session', None))
        self.assertEqual(receipt['installed_sha256'], hashlib.sha256(adb.files[tool.TARGET]).hexdigest())
        self.assertNotIn(tool.TARGET + '.partial', adb.files)
        self.assertTrue((self.tmp / 'outbox' / 'TH02_rev3.authorization-receipt.json').exists())

    def test_missing_items_are_named_and_nothing_is_installed(self):
        (self.tmp / 'approvals.json').write_text(json.dumps(dict(APPROVALS, language_approval_ref='TBD', content_approved=None)))
        adb = FakeAdb([record()])
        with self.assertRaises(tool.AuthorizationError) as error:
            tool.authorize(self.args(consent_confirmed=False), adb)
        text = str(error.exception)
        for field in ('content_approved', 'language_approval_ref', '--consent-confirmed'):
            self.assertIn(field, text)
        self.assertNotIn('operator_id', text)
        self.assertNotIn(tool.TARGET, adb.files)

    def test_refuses_test_data_and_started_records(self):
        for bad in (record(group='Dev', test_data=True), record(session_state='InProgress', session_id='s1'), record(test_data=True)):
            with self.assertRaises(tool.AuthorizationError):
                tool.authorize(self.args(), FakeAdb([bad]))

    def test_record_change_during_authorization_aborts(self):
        adb = FakeAdb([record()], bump_revision_on_second_read=True)
        with self.assertRaises(tool.AuthorizationError):
            tool.authorize(self.args(), adb)
        self.assertNotIn(tool.TARGET, adb.files)

    def test_wrong_android_profile_refused_before_any_push(self):
        adb = FakeAdb([record()])
        adb.android_user = 10
        with self.assertRaises(tool.AuthorizationError):
            tool.authorize(self.args(), adb)
        self.assertEqual(adb.pushes, 0)

    def test_usb_hardware_identity_mismatch_refused(self):
        adb = FakeAdb([record()])
        adb.hardware = 'OTHER_HEADSET'
        with self.assertRaises(tool.AuthorizationError):
            tool.authorize(self.args(), adb)
        self.assertEqual(adb.pushes, 0)

    def test_profile_switch_after_staging_preserves_existing_authorization(self):
        adb = FakeAdb([record()])
        old = b'previous operator authorization'
        adb.files[tool.TARGET] = old
        adb.switch_user_after_push = True
        with self.assertRaises(tool.AuthorizationError):
            tool.authorize(self.args(), adb)
        self.assertEqual(adb.files[tool.TARGET], old)

    def test_another_active_session_prevents_authorization_replacement(self):
        adb = FakeAdb([record(), record(participant_id='TH01', session_state='InProgress', session_id='active')])
        with self.assertRaises(tool.AuthorizationError):
            tool.authorize(self.args(), adb)
        self.assertEqual(adb.pushes, 0)

    def test_stale_technical_in_progress_rows_do_not_lock_authorization(self):
        adb = FakeAdb([record(), record(participant_id='DEV001', group='Dev', test_data=True, session_state='InProgress', session_id='old')])
        receipt = tool.authorize(self.args(), adb)
        self.assertEqual(receipt['participant_id'], 'TH02')

    def test_in_progress_study_is_named_in_the_refusal(self):
        adb = FakeAdb([record(), record(participant_id='JA03', group='Japanese', session_state='InProgress', session_id='live')])
        with self.assertRaisesRegex(tool.AuthorizationError, 'JA03'):
            tool.authorize(self.args(), adb)
        self.assertEqual(adb.pushes, 0)

    def wizard_args(self):
        import argparse
        return argparse.Namespace(adb='adb', serial=SERIAL, android_user=0, hardware_serial=None,
                                  approvals=str(self.tmp / 'wizard-approvals.json'), outbox=str(self.tmp / 'outbox'))

    def test_wizard_asks_every_value_and_installs(self):
        answers = iter(['researcher-01', 'y', 'CONTENT-2026-014', 'y', 'LANG-2026-007', 'TH02', 'CONSENT-TH02-0001', 'y', 'y'])
        adb = FakeAdb([record()])
        receipt = tool.wizard(self.wizard_args(), lambda prompt: next(answers), adb)
        self.assertEqual((receipt['participant_id'], receipt['consent_approval_ref']), ('TH02', 'CONSENT-TH02-0001'))
        self.assertTrue((self.tmp / 'wizard-approvals.json').exists())

    def test_wizard_without_consent_confirmation_installs_nothing(self):
        (self.tmp / 'wizard-approvals.json').write_text(json.dumps(APPROVALS), encoding='utf-8')
        answers = iter(['TH02', 'CONSENT-TH02-0001', 'n', 'y'])
        adb = FakeAdb([record()])
        with self.assertRaisesRegex(tool.AuthorizationError, 'consent-confirmed'):
            tool.wizard(self.wizard_args(), lambda prompt: next(answers), adb)
        self.assertEqual(adb.pushes, 0)

    def test_wizard_does_not_save_unapproved_project_values(self):
        answers = iter(['researcher-01', 'n', '', 'n', ''])
        with self.assertRaisesRegex(tool.AuthorizationError, 'content_approved'):
            tool.wizard(self.wizard_args(), lambda prompt: next(answers), FakeAdb([record()]))
        self.assertFalse((self.tmp / 'wizard-approvals.json').exists())

    def test_single_usb_serial_ignores_wifi_transport(self):
        out = b'List of devices attached\n2G0YC5ZHBF00FJ\tdevice\n10.0.0.5:5555\tdevice\n'
        runner = lambda argv, capture_output, timeout: subprocess.CompletedProcess(argv, 0, out, b'')
        self.assertEqual(tool.single_usb_serial('adb', runner), SERIAL)

    def test_rejects_boolean_record_revision(self):
        with self.assertRaises(tool.AuthorizationError):
            tool.build(record(revision=True), APPROVALS, 'CONSENT-TH02-0001', True)

    def test_explicit_secondary_profile_uses_its_own_storage(self):
        adb = FakeAdb([record()])
        adb.android_user, adb.package_uid = 10, 1010234
        adb.files = {k.replace('/0/', '/10/'): v for k, v in adb.files.items()}
        receipt = tool.authorize(self.args(android_user=10), adb)
        self.assertEqual(receipt['android_user'], 10)
        self.assertIn('/storage/emulated/10/', receipt['installed_path'])
        self.assertNotIn(tool.TARGET, adb.files)

    def test_wifi_requires_and_checks_hardware_pin(self):
        with self.assertRaises(tool.AuthorizationError):
            tool.authorize(self.args(serial='10.80.83.2:5555'), FakeAdb([record()]))
        adb = FakeAdb([record()])
        receipt = tool.authorize(self.args(serial='10.80.83.2:5555', hardware_serial=SERIAL), adb)
        self.assertEqual(receipt['device_serial'], SERIAL)

    def test_package_uid_must_belong_to_expected_profile(self):
        adb = FakeAdb([record()])
        adb.package_uid = 1010234
        with self.assertRaises(tool.AuthorizationError):
            tool.authorize(self.args(), adb)
        self.assertEqual(adb.pushes, 0)

    def test_truthy_string_is_not_researcher_consent(self):
        with self.assertRaises(tool.AuthorizationError):
            tool.build(record(), APPROVALS, 'CONSENT-TH02-0001', 'false')

    def test_template_holds_no_approval_values(self):
        out = self.tmp / 't.json'
        self.assertEqual(tool.main(['approvals-template', str(out)]), 0)
        template = json.loads(out.read_text())
        self.assertTrue(all(template[k] is None for k in template if not k.startswith('_')))
        self.assertEqual(tool.main(['approvals-template', str(out)]), 2)


if __name__ == '__main__':
    unittest.main()
