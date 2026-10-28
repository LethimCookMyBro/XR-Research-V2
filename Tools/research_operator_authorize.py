"""Install the Study start authorization for one participant on the headset, from the researcher's own approval records.

Project-level approvals (content, language) are written once by the researcher into a private JSON on this PC.
Consent is confirmed separately for each participant. This tool never supplies an approval value or reference; it binds
what the researcher entered to the exact Available participant record revision the app checks
(ResearchOperatorGuard.Demand) and verifies the bytes on the headset.

usage:
  research_operator_authorize.py wizard            (interactive: asks each value, USB headset auto-selected)
  research_operator_authorize.py approvals-template OUT.json
  research_operator_authorize.py authorize --adb ADB --serial SERIAL --approvals APPROVALS.json \
      --participant TH02 --consent-ref REF --consent-confirmed [--outbox DIR]
"""
import argparse
import hashlib
import json
import re
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path

from quest_profile import PACKAGE, TCP_TARGET, token

FILES = '/storage/emulated/0/Android/data/' + PACKAGE + '/files'
REGISTRY = FILES + '/ResearchSessionRegistry/registry.json'
TARGET = FILES + '/OperatorControl/research-operator-authorization.json'
SCHEMA = 'research.operator-authorization.1'
# Same members as ResearchOperatorAuthorization (the app rejects any other member).
FIELDS = ('schema', 'operator_id', 'participant_id', 'session_id', 'link_status', 'record_revision',
          'consent_approved', 'content_approved', 'language_approved',
          'consent_approval_ref', 'content_approval_ref', 'language_approval_ref')
PLACEHOLDERS = {'', '-', 'x', 'xx', 'xxx', 'todo', 'tbd', 'none', 'n/a', 'na', 'null', 'pending', 'ref', 'example'}


class AuthorizationError(Exception):
    pass


def real_reference(value):
    return isinstance(value, str) and value.strip().lower() not in PLACEHOLDERS and len(value.strip()) >= 3


def approvals_problems(approvals):
    problems = []
    if not real_reference(approvals.get('operator_id')):
        problems.append('operator_id')
    for kind in ('content', 'language'):
        if approvals.get(kind + '_approved') is not True:
            problems.append(kind + '_approved (must be the JSON value true, set by the researcher)')
        if not real_reference(approvals.get(kind + '_approval_ref')):
            problems.append(kind + '_approval_ref')
    return problems


def build(record, approvals, consent_ref, consent_confirmed):
    problems = approvals_problems(approvals)
    if consent_confirmed is not True:
        problems.append('--consent-confirmed (the researcher confirms consent was obtained from this participant)')
    if not real_reference(consent_ref):
        problems.append('--consent-ref')
    if problems:
        raise AuthorizationError('Missing or placeholder: ' + ', '.join(problems))
    if record.get('group') not in ('Thai', 'Japanese') or record.get('test_data') is not False:
        raise AuthorizationError(record.get('participant_id', '?') + ' is not a Study record (test data never needs authorization)')
    if record.get('session_state') != 'Available' or record.get('session_id'):
        raise AuthorizationError(record['participant_id'] + ' is ' + str(record.get('session_state')) + '; only an Available record can be authorized')
    if type(record.get('revision')) is not int or record['revision'] < 0:
        raise AuthorizationError('Record revision must be a nonnegative integer')
    return {
        'schema': SCHEMA, 'operator_id': approvals['operator_id'].strip(), 'participant_id': record['participant_id'],
        'session_id': None, 'link_status': 'pre_session', 'record_revision': record['revision'],
        'consent_approved': True, 'content_approved': True, 'language_approved': True,
        'consent_approval_ref': consent_ref.strip(), 'content_approval_ref': approvals['content_approval_ref'].strip(),
        'language_approval_ref': approvals['language_approval_ref'].strip(),
    }


def find_record(registry, participant):
    records = [p for p in registry.get('participants', []) if p.get('participant_id') == participant]
    if len(records) != 1:
        raise AuthorizationError(participant + ' not found exactly once in the headset registry')
    return records[0]


class Device:
    def __init__(self, adb, serial, runner=subprocess.run, android_user=0, hardware_serial=None):
        self.adb, self.runner = str(adb), runner
        self.serial = serial if TCP_TARGET.fullmatch(serial or '') else token(serial)
        if type(android_user) is not int or android_user < 0:
            raise AuthorizationError('Expected Android profile must be a nonnegative integer')
        self.android_user = android_user
        self.hardware = hardware_serial or (self.serial if not TCP_TARGET.fullmatch(self.serial) else None)
        if not self.hardware:
            raise AuthorizationError('Wi-Fi authorization requires --hardware-serial; an IP address is not device identity')
        self.hardware = token(self.hardware)
        # Never let /sdcard silently choose a different profile. All writes remain under the explicitly selected owner.
        self.files = '/storage/emulated/' + str(android_user) + '/Android/data/' + PACKAGE + '/files'
        self.registry = self.files + '/ResearchSessionRegistry/registry.json'
        self.target = self.files + '/OperatorControl/research-operator-authorization.json'
        self.check_identity()

    def check_identity(self):
        if self.run('get-state') != 'device' or self.run('shell', 'getprop', 'ro.serialno') != self.hardware:
            raise AuthorizationError('Connected headset identity differs from the selected hardware')
        if self.run('shell', 'am', 'get-current-user') != str(self.android_user):
            raise AuthorizationError('Current Android profile differs from --android-user; no profile switch is performed')
        packages = self.run('shell', 'pm', 'list', 'packages', '--user', self.android_user, '-U', PACKAGE)
        ids = re.findall(r'^package:' + re.escape(PACKAGE) + r' uid:(\d+)\s*$', packages, re.M)
        if len(ids) != 1 or int(ids[0]) // 100000 != self.android_user:
            raise AuthorizationError('Package ownership does not match the selected Android profile')

    def run(self, *args):
        p = self.runner([self.adb, '-s', self.serial, *map(str, args)], capture_output=True, timeout=60)
        if p.returncode:
            raise AuthorizationError((p.stderr or p.stdout or b'adb failed').decode(errors='replace').strip())
        return p.stdout.decode(errors='strict').strip()

    def read_registry(self):
        self.check_identity()
        with tempfile.TemporaryDirectory() as tmp:
            local = Path(tmp) / 'registry.json'
            self.run('pull', self.registry, local)
            registry = json.loads(local.read_text(encoding='utf-8-sig'))
        self.check_identity()
        # Only a Study session reads the authorization file. Old DEV/TEST/PILOT InProgress rows never lock this tool.
        active = [p.get('participant_id', '?') for p in registry.get('participants', [])
                  if p.get('session_state') == 'InProgress' and p.get('test_data') is False]
        if active:
            raise AuthorizationError('Study session ' + ', '.join(active) + ' is still InProgress on this profile; '
                                     'end or abort it in the app before replacing authorization')
        return registry

    def install(self, payload):
        import uuid
        self.check_identity()
        staged = self.target + '.' + uuid.uuid4().hex + '.partial'
        self.run('shell', 'mkdir', '-p', self.files + '/OperatorControl')
        with tempfile.TemporaryDirectory() as tmp:
            local = Path(tmp) / 'authorization.json'
            local.write_bytes(payload)
            self.run('push', local, staged)
        self.check_identity()
        if self.run('shell', 'sha256sum', staged).split()[0] != hashlib.sha256(payload).hexdigest():
            self.run('shell', 'rm', '-f', staged)
            raise AuthorizationError('Staged file hash differs; nothing installed')
        # Re-read after transfer; never replace authorization for an active or revised participant record.
        document = json.loads(payload)
        current = find_record(self.read_registry(), document['participant_id'])
        if current.get('revision') != document['record_revision'] or current.get('session_state') != 'Available' or current.get('session_id'):
            raise AuthorizationError('Participant changed while staging; authorization was not replaced')
        self.check_identity()
        self.run('shell', 'mv', '-f', staged, self.target)
        self.check_identity()
        return self.run('shell', 'sha256sum', self.target).split()[0]


def authorize(args, runner=subprocess.run):
    approvals = json.loads(Path(args.approvals).read_text(encoding='utf-8'))
    token(args.participant)
    device = Device(args.adb, args.serial, runner, getattr(args, 'android_user', 0), getattr(args, 'hardware_serial', None))
    record = find_record(device.read_registry(), args.participant)
    document = build(record, approvals, args.consent_ref, args.consent_confirmed)
    payload = json.dumps(document, ensure_ascii=False, indent=2).encode('utf-8')
    # The app also re-checks the revision at Start; this catches a record that changed while the researcher was typing.
    if find_record(device.read_registry(), args.participant).get('revision') != document['record_revision']:
        raise AuthorizationError('Participant record changed during authorization; run again')
    installed = device.install(payload)
    if installed != hashlib.sha256(payload).hexdigest():
        raise AuthorizationError('Installed hash differs from the written document')
    receipt = dict(document, installed_sha256=installed, device_serial=device.hardware,
                   android_user=device.android_user, installed_path=device.target,
                   installed_utc=datetime.now(timezone.utc).isoformat())
    outbox = Path(args.outbox)
    outbox.mkdir(parents=True, exist_ok=True)
    (outbox / (args.participant + '_rev' + str(document['record_revision']) + '.authorization-receipt.json')).write_text(
        json.dumps(receipt, ensure_ascii=False, indent=2), encoding='utf-8')
    return receipt


def single_usb_serial(adb, runner=subprocess.run):
    out = runner([str(adb), 'devices'], capture_output=True, timeout=30).stdout.decode(errors='replace')
    usb = [line.split()[0] for line in out.splitlines()[1:] if line.endswith('\tdevice') and not TCP_TARGET.fullmatch(line.split()[0])]
    if len(usb) != 1:
        raise AuthorizationError('Connect exactly one headset by USB (found ' + str(len(usb)) + '), or pass --serial')
    return usb[0]


def wizard(args, ask=input, runner=subprocess.run):
    """Interactive Study setup: project approvals once per PC, then consent per participant. Nothing is pre-filled."""
    def yes(prompt):
        return ask(prompt + ' [y/N]: ').strip().lower() in ('y', 'yes')
    approvals_path = Path(args.approvals)
    if not approvals_path.exists():
        print('Project approvals / การอนุมัติระดับโครงการ (saved only on this PC: ' + str(approvals_path) + ')')
        approvals = {'operator_id': ask('Researcher ID / รหัสผู้วิจัย: ').strip(),
                     'content_approved': yes('S1 content approved by its reviewer? / เนื้อหาได้รับอนุมัติแล้ว?') or None,
                     'content_approval_ref': ask('Content approval document reference / เลขอ้างอิงเอกสารอนุมัติเนื้อหา: ').strip(),
                     'language_approved': yes('Thai/Japanese wording approved? / ภาษาได้รับอนุมัติแล้ว?') or None,
                     'language_approval_ref': ask('Language approval document reference / เลขอ้างอิงเอกสารอนุมัติภาษา: ').strip()}
        problems = approvals_problems(approvals)
        if problems:
            raise AuthorizationError('Not saved. Missing: ' + ', '.join(problems))
        approvals_path.parent.mkdir(parents=True, exist_ok=True)
        approvals_path.write_text(json.dumps(approvals, ensure_ascii=False, indent=2), encoding='utf-8')
    serial = args.serial or single_usb_serial(args.adb, runner)
    participant = ask('Participant ID / รหัสผู้เข้าร่วม (TH.. / JA..): ').strip()
    consent_ref = ask('Consent form reference for ' + participant + ' / เลขอ้างอิงใบยินยอม: ').strip()
    confirmed = yes('Did you obtain ' + participant + "'s consent yourself? / ท่านได้รับความยินยอมจากผู้เข้าร่วมคนนี้ด้วยตนเอง?")
    if not yes('Install the authorization for ' + participant + ' on headset ' + serial + '? / ติดตั้ง?'):
        raise AuthorizationError('Cancelled by the researcher; nothing installed')
    run = argparse.Namespace(adb=args.adb, serial=serial, android_user=args.android_user, hardware_serial=args.hardware_serial,
                             approvals=str(approvals_path), participant=participant, consent_ref=consent_ref,
                             consent_confirmed=confirmed, outbox=args.outbox)
    return authorize(run, runner)


TEMPLATE = {
    '_note': 'Fill from your approval documents. Leave a value null if it does not exist yet; the tool will name it.',
    'operator_id': None,
    'content_approved': None, 'content_approval_ref': None,
    'language_approved': None, 'language_approval_ref': None,
}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    sub = parser.add_subparsers(dest='command', required=True)
    t = sub.add_parser('approvals-template')
    t.add_argument('out')
    a = sub.add_parser('authorize')
    a.add_argument('--adb', required=True)
    a.add_argument('--serial', required=True)
    a.add_argument('--android-user', type=int, default=0, help='Expected current Android profile; no profile is switched automatically')
    a.add_argument('--hardware-serial', help='Required for Wi-Fi IP:port; pins the physical headset identity')
    a.add_argument('--approvals', required=True)
    a.add_argument('--participant', required=True)
    a.add_argument('--consent-ref', required=True)
    a.add_argument('--consent-confirmed', action='store_true')
    a.add_argument('--outbox', default=str(Path.home() / 'S1_OperatorAuthorizations'))
    w = sub.add_parser('wizard', help='Interactive setup; asks for every value, writes nothing it was not given')
    w.add_argument('--adb', default='adb')
    w.add_argument('--serial', help='Default: the single USB-connected headset')
    w.add_argument('--android-user', type=int, default=0)
    w.add_argument('--hardware-serial')
    w.add_argument('--approvals', default=str(Path.home() / 'S1_OperatorAuthorizations' / 'project-approvals.json'))
    w.add_argument('--outbox', default=str(Path.home() / 'S1_OperatorAuthorizations'))
    args = parser.parse_args(argv)
    try:
        if args.command == 'wizard':
            receipt = wizard(args)
            print('AUTHORIZED ' + receipt['participant_id'] + ' rev ' + str(receipt['record_revision'])
                  + ' sha256 ' + receipt['installed_sha256'])
        elif args.command == 'approvals-template':
            out = Path(args.out)
            if out.exists():
                raise AuthorizationError(str(out) + ' exists; not overwritten')
            out.write_text(json.dumps(TEMPLATE, indent=2), encoding='utf-8')
            print('Template written: ' + str(out))
        else:
            receipt = authorize(args)
            print('AUTHORIZED ' + receipt['participant_id'] + ' rev ' + str(receipt['record_revision'])
                  + ' sha256 ' + receipt['installed_sha256'])
    except AuthorizationError as error:
        print('NOT AUTHORIZED: ' + str(error), file=sys.stderr)
        return 2
    return 0


if __name__ == '__main__':
    sys.exit(main())
