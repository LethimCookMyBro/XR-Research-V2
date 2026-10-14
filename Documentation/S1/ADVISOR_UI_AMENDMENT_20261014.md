# S1 demo revision: controlled UI, shorter training, offline-first data

Date: 2026-10-14. Authority: user's latest request and supplied advisor meeting summary. Status: engineering requirements for the next technical demo; NOT human language/ethics approval and NOT evidence that implementation passes.

## Sources and discrepancies

- Meeting automatic transcript reviewed locally at 12:58–13:40 (storage), 14:11–14:53 (demo), and 15:14–16:14 (four formats and controlled UI). The transcript contains recognition errors; it has NOT been certified against the original audio. Raw meeting audio/transcript are private and excluded from this repository.
- The user reports a tentative demo on 22 or 23 October afternoon, before the 29 October meeting. This is a planning target, not a confirmed calendar appointment.
- The supplied meeting summary says Manual is implemented and lists PDF, Video and Steps as the other three formats. This does NOT match the September 24 slides/S1 taxonomy. Preserve four existing IDs: **Manual (PDF), Step-by-step, Illustrated, Video**. Record the discrepancy for clarification; do not silently duplicate Manual/PDF or remove Illustrated. The statement about implementation in the meeting is not a current code audit.
- `VR_TaskSpec_S1_LOCKED_TH.md` is copied unchanged as the baseline. Do not rewrite its historical text. This addendum supersedes free panel dragging for the revised technical demo only; task answers, fault, time limit, measurement definitions and researcher gates remain unchanged.

## 1. Controlled participant UI (new priority)

Implement a versioned policy such as `S1_FIXED_UI_DEMO_20261014` in the existing settings/session metadata, not a new experiment framework.

- Four format buttons remain in the same positions, in the existing Reader sidebar. One content viewport changes when a format is selected. Participant chooses WHEN to open information and WHICH format; no enforced order or automatic corrective hint.
- Disable participant free translation/rotation/resizing of Task, Tutorial and Reader panels in this policy. Remove visible grip, invisible drag hit-target and floating drag instructions from this policy. A merely hidden grip with an active collider is not sufficient.
- Also remove the *required panel-drag exercise* from revised training so completion remains reachable. Retain relevant select/open/switch/scroll/video/close/reopen practice and all required maintenance motor skills. Do not bypass training by filling credits.
- Keep baseline movable-panel behavior available only where necessary for legacy technical regression, not as a participant menu choice. Do not globally delete old logging event types or historical data.
- Anchor UI to an authored station pose, not the user's head every frame. Fix pose within a task. Researcher may calibrate height/reach before task timing; any later safety recovery must follow a documented rule, pause if required, and be logged as System/Researcher, not an information-selection action.
- Fixed does NOT mean inaccessible. Preserve Pause, Close/Hide and a predictable route to reopen the Reader. Open/close/return and media position must be measurable. No compulsory reading, no automatic media opening at the correct repair step.
- Preserve pause single-action design when self-resume is allowed. Researcher-owned checkpoints retain a real operator path and text saying who acts next.
- A fixed UI policy does NOT prohibit manipulating task parts, turntable, screws or cables as required by S1.

## 2. Shorter, understandable training

Reduce redundant screens and repeated demands, not required competence or safety. First map every S1 §4 skill to one observable practice opportunity.

Suggested presentation grouping (a design proposal, not a new experimental condition):
1. Look/move/select and Pause.
2. Pick up, orient and place one neutral object in a continuous sequence.
3. Open the fixed Reader, select format, scroll/play and close/reopen.
4. Neutral trainer mechanisms: Examine, fitting/locking, rotation/access, screw/cable and Inspect Fail/Pass.

- The same valid event must not be demanded again just because a panel changed visibility. Credits come once from real events. Combine compatible actions into one continuous activity, with a clear next action after each success.
- Show progress by meaningful section rather than a discouraging wall of 24 micro-quests. Underlying evidence may still track individual skills. Do not declare 24 credits intrinsically wrong or delete all of them.
- One current instruction + one relevant demonstration + immediate, brief success feedback. Do not show three nearly identical controller drawings for unrelated actions. For Examine show the actual neutral module and the matching visible control, not an unrelated generic menu.
- Feedback from the previous step must not remain dominant while the current instruction asks a different action. Keep feedback out of media transport controls and paging footer.
- Do not add an arbitrary short time limit or automatically pass a confused participant. Optional repetition must be deliberate and recorded. Both languages use equivalent exposure and rules.
- Animations demonstrate direction/control/state using the actual model or controller. No ornamental motion, head-following panels, or screenshots mislabeled as motion demonstrations.

## 3. Table and model layout

- Keep functional assembly/collision work already verified. No blanket collider removal or replacement of the interaction stack.
- Inspect the actual authored setup from the participant's standing/seated calibrated pose: reachable objects, no overlap or concealment, label legibility, cable slack, safe working clearance and free access to required screws/slots.
- Provide neutral trays/areas for loose parts; do not group by correct/incorrect answer or by the intended repair sequence. All alternatives retain comparable visibility and reach.
- Respect P/Q counterbalance, update both consistently and record `layout_version` and calibrated origin/height. Do not move a hard-to-reach object closer for one participant without logging a protocol deviation.
- Do not infer that objects are missing from the scene merely because a capture frame shows an empty portion of the table. Inspect scene hierarchy and runtime visibility before fixing.

## 4. Data: local first, not cloud-dependent collection

Recommended engineering choice: write to app-owned local storage during the task; finalize and export over the existing controlled route to the research PC; verify hashes/identity; make an access-controlled backup. Network availability must not determine event capture.

- Cloud coding credits and cloud storage of participant datasets are DIFFERENT decisions. This repo and cloud coding sessions must not receive raw participant video/audio/registry/consent or credentials.
- Keep pseudonymous IDs, DEV/test_data separation, timestamps, raw validity and analysis_version. Do not fill missing samples, questionnaires or interview answers.
- The provided proposal §8.7 specifies retention for **one year after publication, or one year after project completion when unpublished**, then disposal. Confirm the approved version before execution. Do NOT delete immediately after export, after a demo, or after analysis merely to satisfy the phrase “destroy after use.”
- Implement a documented retention manifest/process with preview and authorized confirmation; no automatic bulk deletion of existing data in this task. Cover backups and exports as well as original files. This is not legal/ethics approval.

## 5. Acceptance and versioning

- New policy/addendum is reviewed separately from baseline S1. Record protocol/training/UI/layout/content versions so old freely movable-panel sessions are not pooled silently with fixed-panel sessions.
- Test pointer acquisition, no accidental panel movement, scrolling/seek ownership, Hide/Reopen, pause/recovery, wrong action, timeout/withdrawal, logging failures and full A/B flow in the revised policy.
- Test all required languages/orders/layouts; retain legacy evidence with its exact code and policy version.
- Updated training/UI in Editor is not Quest or human-comprehension acceptance. Q5 image settings remain a measured candidate, not a promise that through-lens blur is gone.
- The next demo may be a technical demonstration with pending human approvals stated. Do not enable real-study access or label the system participant-ready by changing flags.
