# Pre-UI research logging contract

The existing `HandsLabLogger` JSONL writer is reusable; do not introduce another
event writer for Task A/B. The default constructor still creates a HandsLab
technical run. `TrainingSession` remains the existing orientation prototype,
not the Task A/B state machine.

For another phase, supply `phase`, stable `taskId`, `contentVersion`,
`technicalTest` and `testData` explicitly. Every simulator/fixture run must keep
`testData=true` and `hardwareVerified=false`. Real participant configuration and
task content belong to the next implementation phase. A constructor creates one
unique run directory; group events by `run_id`, never by filename alone.

`Record(eventName, target, result, taskState, handedness, before, after, source,
reason, actionId, inputSourceOverride, trackingValid)` is the common hook.
No forwarding API per event is necessary. `taskState` accepts a serializable
snapshot (for example a DTO or `JObject`), a legacy state label, or null when
unknown. Schema `handslab.events.2` adds structured `task_state` and `task_id`;
readers must not assume `task_state` is always a string.

| Behavior | Event and caller responsibility |
| --- | --- |
| Information exposure | `INFORMATION_OPEN` / `INFORMATION_CLOSE`, stable document/page target. Emit on actual visibility transitions, including dismissal, task exit and interruption. Reuse an `action_id` for one open/close interval if several intervals can overlap. |
| Examination | `EXAMINE`, examined target; do not infer examination from a generic grab or ray hover. |
| Task action / repair | `ACTION` or `REPAIR`, meaningful action in `reason`, acted-on target, result and full task state before/after. Emit each attempt, including repeated identical actions. |
| Inspection | `INSPECT`, `PASS` / `FAIL` / `ERROR`, full current task snapshot. A related `FAIL` can share its inspection `action_id`; the existing Training prototype uses `FAILURE` with the same semantics. |
| Component changes | Include stable component IDs and state in `before`, `after` and current `task_state`. Future schemas can distinguish installed, removed, connected and incorrectly placed without fixing a component list here. |
| Retry / completion | `RETRY` when there is an explicit retry interaction; ordinary repeated `ACTION` records remain separate. `TASK_COMPLETED` records the final snapshot, then call `Complete`. Run completion alone is not evidence of task success. |
| Technical errors | `SYSTEM_ERROR`, `source="System"`, reason and available state. Writer failure is separate: check boolean return, `State`, `LastError` and `WriteFailed`; never continue claiming complete data after failure. |

Snapshots are serialized synchronously at the call, including before/after;
later mutation cannot rewrite saved history. Capture **full task state** at each
inspection and at both sides of a task-changing action. Missing optional fields
remain null; missing event/target or invalid actor/input identifiers are rejected.
Non-serializable cyclic snapshots mark the writer `WriteError` and do not append
a partial JSON record. Callers provide authored stable IDs, not Unity instance
IDs, translated labels or generated hierarchy names.

Order by `seq` within a run; `timestamp` is monotonic elapsed seconds and may tie.
The same sequence spans event JSONL and movement CSV, so event-only sequences
can have gaps. Metadata supplies UTC start and clock semantics. This supports
information-before-action intervals, examined-target order before repair,
actions between inspections and behavior following failure using saved data
alone. Open duration measures information **exposure**, not eye gaze or proven
attention. No culture, nationality or strategy inference is performed at runtime.

Every event is flushed. `Complete` flushes/closes both writers; `Dispose` without
explicit completion records `RUN_CLOSED_INCOMPLETE`. A new logger creates a new
directory and sequence. Callers must close the previous run before replacing it.
The existing HandsLab `NewRun` integration verifies this through simulator input.

Run the existing disk checks:

```powershell
powershell -NoProfile -File Assets/VRMaintenanceResearch/HandsLabQuest3/Tests/Run-LoggerChecks.ps1 -EvidenceDirectory D:/VR_TMU/XR-Research-V2/Logs/PreUIReadiness/logger
```

The `LoggingFixture` records are synthetic technical data, not Task A/B content
or participant observations. Tests reopen closed files to reconstruct H1–H4,
component snapshots, repeated attempts, run isolation and failure behavior.
