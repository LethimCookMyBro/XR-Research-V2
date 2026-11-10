"""Synthetic fixture for s1_session_report.py, never participant evidence."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("s1_session_report", Path(__file__).with_name("s1_session_report.py"))
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


def state(guard, clipped, issues):
    return {"task_id": "B", "task_state": {"frontGuardPlacement": guard, "frontGuardClipped": clipped, "capturedAtSeconds": 1.0, "user_created_issues": issues}}


EVENTS = [
    dict(event="TASK_STARTED", timestamp=10.0, task_id="B", phase="Experiment", target="task.B"),
    dict(event="INFORMATION_OPEN", timestamp=11.0, task_id="B", phase="Experiment", target="reference.video", after={"selection_kind": "deliberate"}),
    dict(event="INFORMATION_CLOSE", timestamp=15.5, task_id="B", phase="Experiment", target="reference.video", reason="tab_changed", after={"exposure_seconds": 4.5}),
    dict(event="GRAB", timestamp=20.0, task_id="B", phase="Experiment", target="B.front_guard", action_id="g1", task_state=state(2, True, [])),
    dict(event="PART_INTERACTION_CHANGED", timestamp=20.1, task_id="B", phase="Experiment", target="B.front_guard", action_id="g1", after={"block_reason": "ClipClosed"}),
    dict(event="RELEASE", timestamp=20.5, task_id="B", phase="Experiment", target="B.front_guard", action_id="g1", task_state=state(2, True, [])),
    dict(event="GRAB", timestamp=21.0, task_id="B", phase="Experiment", target="B.front_guard_clip", action_id="g2", task_state=state(2, True, [])),
    dict(event="RELEASE", timestamp=21.4, task_id="B", phase="Experiment", target="B.front_guard_clip", action_id="g2", task_state=state(2, False, ["front_clip_open"])),
    dict(event="INSPECT_PASS", timestamp=30.0, task_id="B", phase="Experiment", target="task.B"),
    dict(event="TASK_ENDED", timestamp=31.0, task_id="B", phase="Experiment", target="task.B", result="Completed",
         after={"completed": True, "end_reason": "passed", "active_elapsed_s": 21.0}),
]


class SessionReportTest(unittest.TestCase):
    def test_logged_actions_become_readable_summary(self):
        with tempfile.TemporaryDirectory() as root:
            session = Path(root)
            (session / "events.jsonl").write_text("\n".join(json.dumps(e) for e in EVENTS), encoding="utf-8")
            (session / "session_metadata.json").write_text(json.dumps({"run_id": "fixture"}), encoding="utf-8")
            summary = report.write(session, session / "out")["B"]
            text = (session / "out" / "report_th.md").read_text(encoding="utf-8")
        self.assertEqual(summary["active_seconds"], 21.0)
        self.assertEqual(summary["inspect_results"], ["Pass"])
        self.assertEqual(summary["information"]["video"], {"opens": 1, "exposure_s": 4.5, "pages_seen": []})
        self.assertEqual(summary["parts"]["B.front_guard"], {"grabs": 1, "grabs_that_changed_state": 0, "blocked": {"ClipClosed": 1}})
        self.assertEqual(summary["parts"]["B.front_guard_clip"]["grabs_that_changed_state"], 1)
        self.assertIn("ระบบกันไว้: คลิปยังปิด", text)
        self.assertIn("คลิปตะแกรงหน้า: ปิด → เปิด; จุดที่ยังไม่เรียบร้อย: +front_clip_open", text)


if __name__ == "__main__":
    unittest.main()
