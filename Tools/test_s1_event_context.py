"""Calculator fixtures only. Does not claim runtime interaction or rendered exposure."""
import os
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(os.environ.get('S1_PROCESSOR', str(Path(__file__).with_name('research_export.py')))).parent))
from test_s1_metrics import state, start, end, inspect, event, feedback, s1


class ContextExportChecks(unittest.TestCase):
    def episode(self, snapshot, evaluation=None):
        request = inspect(2, 1)
        request['before'] = {'task_state': snapshot, 'technical_test': True, 'test_data': True}
        return s1([start(), request, event(3, 1.1, 'INSPECT_FAIL', after=dict(inspection_id='i1', **(evaluation or {}))),
                   feedback(4, 4), end(5, 10)])['failure_episodes'][0]['context']

    def test_request_snapshot_context_is_preserved(self):
        snapshot = state(original_fault_active=True, user_created_issues=['front_clip_open'],
                         observation_accessible_targets=['B.front_guard'], original_fault_id='B04.rear_guard_lock_engaged_loose',
                         context_version='research.s1-b04-context.1', context_basis='authored_B04_readiness_delta_not_error_or_intent')
        context = self.episode(snapshot)
        for key in ('original_fault_active', 'user_created_issues', 'observation_accessible_targets', 'original_fault_id', 'context_version', 'context_basis'):
            self.assertEqual(context.get(key), snapshot[key], key)

    def test_false_and_empty_are_not_missing(self):
        context = self.episode(state(original_fault_active=False, user_created_issues=[]))
        self.assertIs(context['original_fault_active'], False)
        self.assertEqual(context['user_created_issues'], [])

    def test_evaluation_context_remains_authoritative(self):
        context = self.episode(state(original_fault_active=True, user_created_issues=['front_clip_open']),
                               dict(original_fault_active=False, user_created_issues=[]))
        self.assertIs(context['original_fault_active'], False)
        self.assertEqual(context['user_created_issues'], [])

    def test_legacy_unknown_is_not_zero_or_false(self):
        context = self.episode(state())
        self.assertIsNone(context['original_fault_active'])
        self.assertIsNone(context['user_created_issues'])
        self.assertEqual(context['status'], 'not_recorded')

    def test_null_context_still_warns(self):
        beginning = start()
        beginning['after']['initial_state'].update(original_fault_active=None, user_created_issues=None,
                                                  observation_accessible_targets=None)
        self.assertIn('missing_s1_state_fields_original_fault_user_issues_observation_access',
                      s1([beginning, end()])['qc_warnings'])

    def test_false_and_empty_context_do_not_warn(self):
        beginning = start()
        beginning['after']['initial_state'].update(original_fault_active=False, user_created_issues=[],
                                                  observation_accessible_targets=[])
        self.assertNotIn('missing_s1_state_fields_original_fault_user_issues_observation_access',
                         s1([beginning, end()])['qc_warnings'])


if __name__ == '__main__':
    unittest.main(verbosity=2)
