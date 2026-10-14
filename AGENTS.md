# AI working rules — XR Research V2

This is the TMU/TMU Meta Quest 3 research project, not a generic XRI sample project.

## Read first
- Cloud/current S1 handoff: read `Documentation/S1/CLOUD_START_HERE.md` first. It uses portable paths and distinguishes current work from historical reports.
- Latest user/advisor UI revision: `Documentation/S1/ADVISOR_UI_AMENDMENT_20261014.md` supersedes free participant panel dragging for the revised technical demo. Preserve baseline task rules and record protocol/UI versions.
- For UI/workflow changes, read `design_rule.md` before editing and again before completion or Build. Inspect the affected runtime screens and interactions; fix reproducible violations and report untested cases without weakening S1 or researcher gates.
- Current status only when needed: `Logs/QuestFinalIntegration/FINAL_INTEGRATION_STATUS.md`
- Next physical test only when needed: `Logs/QuestFinalIntegration/NEXT_WEAR_RUNBOOK.md`
- Do **not** recursively scan `Logs/`; it is historical evidence and is gitignored.
- Do **not** treat old evidence/report files as current state when the current status disagrees.

## Canonical implementation
- Main scene: `Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity`
- Research flow: `Assets/VRMaintenanceResearch/ResearchFlow/`
- Existing input/grab/logger runtime reused by ResearchFlow: `Assets/VRMaintenanceResearch/HandsLabQuest3/`
- `TrainingV2/` is still a dependency (UI graphics/fonts); do not delete it.
- `Assets/XRI_Examples/` still supplies project/runtime dependencies; do not broadly prune it.

Historical Presentation prototype, crash recovery scenes, obsolete UX handoff docs, and unused local Roslyn DLLs were archived outside the project at:
`D:\VR_TMU\XR-Research-V2-Archive\cleanup-20260929`
Do not restore them unless explicitly requested.

## Safety / verification
- Working tree is intentionally dirty. Never broad reset/clean/stash/checkout/pull/revert.
- Do not say fixed/pass/ready without direct evidence.
- Editor/synthetic pass != Quest hardware pass.
- Preserve TH/JP study gate. Production Task A/B content remains blocked until approved; do not invent task answers/criteria.
- Tutorial may teach. Experimental tasks must not provide answer hints/strategy labels.
- Before saving a scene, check dirty/unsaved state.

## Token/context discipline
Search narrowly. Prefer current source + the two current status/runbook files. Avoid loading historical galleries, screenshots, build logs, old reports, or all of `Documentation/` unless the task specifically requires them.
