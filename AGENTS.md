# AI working rules — XR Research V2

This is the TMU/TMU Meta Quest 3 research project, not a generic XRI sample project.

## Git workflow — user decision, 14 October 2026
- `main` is the single canonical working and delivery branch. Work on `main` by default; do not create extra feature/handoff branches or worktrees unless the user requests them.
- Keep one writer per checkout. Before editing/pushing, check local status and remote `main`; preserve other work and reconcile divergence without force-push, reset or clean.
- Commit reviewed changes in small batches and push to `main` when authorized. If a host requires a temporary branch, integrate its reviewed changes back into `main` before calling delivery complete; do not leave the user to choose between versions.
- Existing older branches are historical checkpoints, not active alternatives. This branch policy does not imply runtime, wearer or study approval.

## Read first
- Cloud/current S1 handoff: read `Documentation/S1/CLOUD_START_HERE.md` first. It uses portable paths and distinguishes current work from historical reports.
- Latest owner UI revision: `Documentation/S1/OWNER_UI_DIRECTION_20261028.md` restores panel dragging for the new implementation and supersedes the no-drag direction of `Documentation/S1/ADVISOR_UI_AMENDMENT_20261014.md`. Preserve baseline task rules, historical recorded sessions, approval gates and protocol/UI versions; do not assume this design instruction is proof of hardware readiness.
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
- Check the actual working-tree state; earlier reports of a dirty tree are historical. Preserve any current uncommitted work. Never broad reset/clean/stash/checkout/pull/revert.
- Do not say fixed/pass/ready without direct evidence.
- Editor/synthetic pass != Quest hardware pass.
- Owner decision 29 Oct 2026 (`S1_OWNER_OPEN_STUDY_20261029`, `ResearchUIPolicy.StudyOpenWithoutDeviceApprovals`): TH/JP Study sessions start without the on-device authorization file, content-approval or bilingual-review flags; consent and approvals are handled by the researcher outside the app, and every session logs `study_gate_version`. Do not re-close the gate without the owner. Do not invent task answers/criteria.
- Tutorial may teach. Experimental tasks must not provide answer hints/strategy labels.
- Before saving a scene, check dirty/unsaved state.

## Token/context discipline
Search narrowly. Prefer current source + the two current status/runbook files. Avoid loading historical galleries, screenshots, build logs, old reports, or all of `Documentation/` unless the task specifically requires them.
