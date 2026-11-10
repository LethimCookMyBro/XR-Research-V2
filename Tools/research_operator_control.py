"""Existing STANDARD local mailbox transport. Preview is offline; execute is explicit."""
import argparse
import hashlib
import json
import re
import shlex
import sys
import tempfile
import subprocess
from datetime import datetime, timezone
import uuid
from pathlib import Path

from research_operator_authorize import AuthorizationError, Device, find_record
from quest_profile import PACKAGE, ProfileError

COMMANDS = ('start', 'resume', 'abort', 'invalidate', 'export')
BINDING = ('participant_id', 'session_id', 'run_id', 'revision', 'session_state', 'flow_state', 'paused')
REASONS = ('time_limit', 'withdrawal', 'safety_stop', 'technical_failure', 'other')


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def save_new(path, document):
    with Path(path).open('x', encoding='utf-8') as stream:
        json.dump(document, stream, ensure_ascii=False, indent=2, allow_nan=False)
        stream.flush()
        import os
        os.fsync(stream.fileno())


def binding(record):
    if type(record.get('revision')) is not int or record['revision'] < 0:
        raise AuthorizationError('Invalid record revision')
    if any(k not in record for k in BINDING) or type(record['paused']) is not bool:
        raise AuthorizationError('Incomplete record binding')
    return {k: record[k] for k in BINDING}


def preview(snapshot, participant, command, reason=None):
    if command not in COMMANDS or reason is not None and (command not in ('abort', 'invalidate') or reason not in REASONS):
        raise AuthorizationError('Unsupported command or termination reason')
    if command in ('abort', 'invalidate') and reason is None:
        raise AuthorizationError('Explicit termination reason required')
    record = find_record(snapshot['registry'], participant)
    expected = binding(record)
    request = dict(schema='research.operator-command.2', request_id=uuid.uuid4().hex,
                   command=command, participant_id=participant, session_id=record['session_id'],
                   run_id=record['run_id'], record_revision=record['revision'], expected_state=record['flow_state'],
                   expected_paused=record['paused'], issued_utc=datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z'))
    if reason is not None:
        request['termination_reason'] = reason
    return dict(schema='research.operator-preview.1', identity=snapshot['identity'], expected=expected, request=request)


def validate(plan, check_time=True):
    request = plan['request']
    if plan.get('schema') != 'research.operator-preview.1' or request.get('schema') != 'research.operator-command.2':
        raise AuthorizationError('Unsupported preview or command schema')
    if not re.fullmatch('[0-9a-f]{32}', request.get('request_id', '')):
        raise AuthorizationError('Request UUID must be lowercase N format')
    allowed = {'schema', 'request_id', 'command', 'participant_id', 'session_id', 'termination_reason', 'run_id', 'record_revision', 'expected_state', 'expected_paused', 'issued_utc'}
    if set(request) - allowed or request.get('command') not in COMMANDS:
        raise AuthorizationError('Unsupported command fields')
    if request.get('participant_id') != plan['expected']['participant_id'] or request.get('session_id') != plan['expected']['session_id']:
        raise AuthorizationError('Request and preview identities differ')
    if plan['identity'].get('package') != PACKAGE:
        raise AuthorizationError('Wrong package binding')
    reason = request.get('termination_reason')
    if request['command'] in ('abort', 'invalidate') and reason not in REASONS:
        raise AuthorizationError('Explicit non-pass termination reason required')
    if request['command'] not in ('abort', 'invalidate') and reason is not None:
        raise AuthorizationError('Termination reason does not apply')
    binding(plan['expected'])
    for wire, record in [('run_id', 'run_id'), ('record_revision', 'revision'), ('expected_state', 'flow_state'), ('expected_paused', 'paused')]:
        if request.get(wire) != plan['expected'][record] or type(request.get(wire)) is not type(plan['expected'][record]):
            raise AuthorizationError('Request and preview state bindings differ')
    issued = datetime.fromisoformat(request['issued_utc'].replace('Z', '+00:00'))
    if check_time and (not request['issued_utc'].endswith('Z') or not -5 <= (datetime.now(timezone.utc) - issued).total_seconds() <= 300):
        raise AuthorizationError('Preview issue time expired or invalid; do not replay')


class Mailbox(Device):
    def shell(self, script):
        return self.run('shell', script)

    def document(self, path):
        with tempfile.TemporaryDirectory() as directory:
            local = Path(directory) / 'readback.json'
            self.run('pull', path, local)
            return load(local)

    def exists(self, path):
        result = self.shell('if [ -e ' + shlex.quote(path) + ' ]; then echo present; else echo absent; fi')
        if result not in ('present', 'absent'):
            raise AuthorizationError('Ambiguous remote existence check')
        return result == 'present'

    def snapshot(self):
        self.check_identity()
        registry = self.document(self.registry)
        self.check_identity()
        if registry.get('schema') != 1 or not isinstance(registry.get('participants'), list):
            raise AuthorizationError('Unsupported registry schema')
        return dict(identity=dict(hardware_serial=self.hardware, android_user=self.android_user, package=PACKAGE), registry=registry)

    def check_plan(self, plan):
        current = self.snapshot()
        if current['identity'] != plan['identity']:
            raise AuthorizationError('Device/profile differs from preview')
        if binding(find_record(current['registry'], plan['expected']['participant_id'])) != plan['expected']:
            raise AuthorizationError('Participant/session/revision/state changed since preview')

    def result(self, plan):
        validate(plan, check_time=False)
        self.check_identity()
        if plan['identity'] != dict(hardware_serial=self.hardware, android_user=self.android_user, package=PACKAGE):
            raise AuthorizationError('Device/profile differs from preview')
        request = plan['request']
        path = self.files + '/OperatorControl/results/' + request['request_id'] + '.response.json'
        if not self.exists(path):
            return dict(status='OutcomeUnknown', request_id=request['request_id'], replay_permitted=False)
        response = self.document(path)
        self.check_identity()
        if response.get('schema') != 'research.operator-response.1' or response.get('request_id') != request['request_id']:
            raise AuthorizationError('Response schema/UUID mismatch; do not replay')
        if response.get('status') not in ('Executed', 'Blocked', 'ClaimedOutcomePending'):
            raise AuthorizationError('Invalid response status; do not replay')
        if response['status'] == 'Executed':
            if response.get('command') != request['command'] or response.get('participant_id') != request['participant_id']:
                raise AuthorizationError('Response command/participant mismatch; do not replay')
            if request['session_id'] and response.get('session_id') != request['session_id']:
                raise AuthorizationError('Response session mismatch; do not replay')
            if request['run_id'] and response.get('run_id') != request['run_id']:
                raise AuthorizationError('Response run mismatch; do not replay')
            if not response.get('session_id') or not response.get('run_id'):
                raise AuthorizationError('Executed response has no session/run; do not replay')
        return response

    def execute(self, plan, journal):
        validate(plan, check_time=False)
        receipt = Path(journal) / (plan['request']['request_id'] + '.attempt.json')
        if receipt.exists():
            return self.result(plan)
        validate(plan)
        self.check_plan(plan)
        directory = self.files + '/OperatorControl'
        target = directory + '/research-operator-command.json'
        result = self.result(plan)
        if result['status'] != 'OutcomeUnknown':
            return result
        if self.exists(target):
            raise AuthorizationError('Pending mailbox exists; nothing overwritten')
        payload = json.dumps(plan['request'], ensure_ascii=False, allow_nan=False).encode('utf-8')
        staged = directory + '/' + plan['request']['request_id'] + '.partial'
        self.run('shell', 'mkdir', '-p', directory)
        if self.exists(staged):
            raise AuthorizationError('Prior staged request exists; inspect outcome, do not replay')
        with tempfile.TemporaryDirectory() as tmp:
            local = Path(tmp) / 'command.json'
            local.write_bytes(payload)
            self.run('push', local, staged)
        if self.run('shell', 'sha256sum', staged).split()[0] != hashlib.sha256(payload).hexdigest():
            raise AuthorizationError('Staged command hash mismatch; nothing published')
        lock = directory + '/host-publish.lock'
        self.check_identity()
        self.run('shell', 'mkdir', lock)
        try:
            self.check_plan(plan)
            validate(plan)
            save_new(receipt, dict(status='PublishAttemptOutcomeUnknown', preview=plan))
            self.check_identity()
            self.run('shell', 'mv', '-n', staged, target)
            if self.exists(staged):
                raise AuthorizationError('Mailbox was not empty; staged request retained, do not replay')
            return self.result(plan)
        finally:
            self.check_identity()
            self.run('shell', 'rmdir', lock)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    p = sub.add_parser('preview', help='Offline only; writes a new local preview')
    p.add_argument('--snapshot', required=True)
    p.add_argument('--participant', required=True)
    p.add_argument('--command', choices=COMMANDS, required=True)
    p.add_argument('--reason', choices=REASONS)
    p.add_argument('--out', required=True)
    for action in ('state', 'execute', 'result'):
        p = sub.add_parser(action)
        p.add_argument('--adb', required=True)
        p.add_argument('--serial', required=True)
        p.add_argument('--hardware-serial', required=True)
        p.add_argument('--android-user', required=True, type=int)
        if action == 'state':
            p.add_argument('--out', required=True)
        else:
            p.add_argument('--preview', required=True)
        if action == 'execute':
            p.add_argument('--execute', required=True, action='store_true')
    args = parser.parse_args(argv)
    try:
        if args.action == 'preview':
            output = preview(load(args.snapshot), args.participant, args.command, args.reason)
            save_new(args.out, output)
        else:
            device = Mailbox(args.adb, args.serial, android_user=args.android_user, hardware_serial=args.hardware_serial)
            if args.action == 'state':
                output = device.snapshot()
                save_new(args.out, output)
            elif args.action == 'execute':
                output = device.execute(load(args.preview), Path(args.preview).resolve().parent)
            else:
                output = device.result(load(args.preview))
        print(json.dumps(output, ensure_ascii=False, allow_nan=False))
        return 0 if output.get('status') in (None, 'Executed') else 3
    except (AuthorizationError, ProfileError, OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print(json.dumps(dict(status='Error', error=str(error), replay_permitted=False)), file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
