"""Portable QC entrypoint safety tests; real-data pipeline checks are recorded separately."""
import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import research_host_qc as qc


class PortableHostQcChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / 'Recipient Project With Spaces'
        (self.project / 'Tools').mkdir(parents=True)
        (self.project / 'Tools' / 'research_export.py').write_text('# location fixture only\n', encoding='utf-8')
        self.bundle = self.root / 'input'
        self.bundle.mkdir()

    def call(self, *extra):
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            exit_code = qc.main(['--project-root', str(self.project), '--bundle', str(self.bundle), *extra])
        return exit_code, json.loads(stdout.getvalue())

    def test_explicit_recipient_project_not_author_path(self):
        self.assertEqual(qc.resolve_tool(self.project), self.project / 'Tools' / 'research_export.py')

    def test_s1_project_environment(self):
        with patch.dict(os.environ, {'S1_PROJECT': str(self.project)}):
            self.assertEqual(qc.resolve_tool(None), self.project / 'Tools' / 'research_export.py')

    def test_explicit_project_wins_over_environment(self):
        with patch.dict(os.environ, {'S1_PROJECT': str(self.root / 'not_the_selected_project')}):
            self.assertEqual(qc.resolve_tool(self.project), self.project / 'Tools' / 'research_export.py')

    def test_missing_tools_does_not_fall_back_to_owner_pc(self):
        with self.assertRaisesRegex(ValueError, 'project-root'):
            qc.resolve_tool(self.root / 'missing')

    def test_reject_output_inside_raw_snapshot_before_writing(self):
        out = self.bundle / 'do_not_create'
        code, report = self.call('--output', str(out))
        self.assertEqual(code, 1)
        self.assertIn('separate', report['error'])
        self.assertFalse(out.exists())

    def test_reject_output_ancestor_of_snapshot(self):
        code, report = self.call('--output', str(self.root))
        self.assertEqual(code, 1)
        self.assertIn('separate', report['error'])

    def test_existing_results_never_overwritten(self):
        out = self.root / 'existing_output'
        out.mkdir()
        sentinel = out / 'RESULT.json'
        sentinel.write_text('preserve', encoding='utf-8')
        code, report = self.call('--output', str(out))
        self.assertEqual(code, 1)
        self.assertIn('already exists', report['error'])
        self.assertEqual(sentinel.read_text(), 'preserve')

    def test_missing_complete_marker_refuses_processing(self):
        code, report = self.call('--output', str(self.root / 'new_output'))
        self.assertEqual(code, 1)
        self.assertIn('COMPLETE.json', report['error'])
        self.assertFalse((self.root / 'new_output').exists())

    def test_invalid_timeout_creates_nothing(self):
        out = self.root / 'timeout_output'
        code, report = self.call('--output', str(out), '--timeout', '0')
        self.assertEqual(code, 1)
        self.assertIn('positive', report['error'])
        self.assertFalse(out.exists())

    def test_streaming_digest(self):
        path = self.root / 'test.bin'
        path.write_bytes(b'abc')
        self.assertEqual(qc.file_sha256(path), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad')


if __name__ == '__main__':
    unittest.main()
