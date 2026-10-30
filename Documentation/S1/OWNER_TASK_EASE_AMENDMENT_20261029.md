# S1 amendment: easier task mechanics and clearer information (owner, 29 Oct 2026)

Owner decision on 29 Oct 2026: each task should take about 5–6 minutes, and must not take more than 6–7. Information in all four formats must say clearly what to do. The original `VR_TaskSpec_S1_LOCKED_TH.md` stays unchanged. This file records what changed for new sessions. Each session logs these changes in `SESSION_PROTOCOL_VERSIONS`.

## Mechanics

| Item | Before | Now | Logged version |
|---|---|---|---|
| Task A screws (SSD tray thumbscrew, GPU bracket screw) | real 0.79 mm pitch: 1134°–2313° (3–6.4 turns) | 270° | `task_a_thread_version = S1_TASKA_SHORT_THREADS_20261029` |
| Task B blade cap (left-hand) | 720° | 270° | `task_b_thread_version = S1_TASKB_SHORT_THREADS_20261029` |
| Task B rear ring (right-hand) | 540°, seeded fault 19.2° short of the stop | 180°, seeded fault 20° short of the stop | same |
| "Tight" rule (all threads, training screw included) | must reach the stop exactly | the stop or up to 3° short of it (`ResearchS1FanKinematics.TightToleranceDegrees`) | — |

A thread still stops at its end, and the 8° detent still holds a thread that reached the stop against release twist. Turning "all the way" therefore always counts as tight. The seeded fault is far outside the 3° tolerance. The one real fault, the rear ring short of tight, and the pass criteria are unchanged.

## Sound

- Reader video narration (TH/JA, already in every A/B/T3 video) now plays at full AudioSource volume instead of half.
- During Task B's 3 s Inspect, the fan plays a smooth spin if it passes, or a spin with a knock once per blade pass if the symptom remains. No sound plays when the guards are not ready. The sounds are synthesized in code (`ResearchS1TaskBMechanics.SpinSound`), and the same knock is played in the first scene of the Task B video.

## Information (Thai/Japanese, all four formats)

- Task B is framed as a repair ticket ("ใบแจ้งซ่อม: ผู้ใช้แจ้งว่า…" / "修理依頼"), because the participant does not hear the fault before testing.
- The Examine button is renamed "ส่องดู" / "よく見る" so it does not read like "ตรวจผล" / "検査" (UI, training T3 media, Task B media).
- Task B Steps went from 22 to 14. The front-guard opening is one step. The conditional rear re-seat (5 steps) is one conditional step, "if the rear guard moves or the ring is loose", and keeps the same wording rule as the front and blade fixes, so it does not point to the answer.
- Illustrated: the Task B normal/abnormal thumbnails use 1:1 source pixels around each part (they looked identical at 2× crop). The fix picture has labels. The Task A part-comparison text is larger.
- Video: the Back/Next buttons now jump between scenes using the package chapters. Packages without chapters keep the 10 s steps.

## Short text pass (owner, 29 Oct evening)

The owner asked for short, easy text everywhere, with readable layout and icons where they help.

- Steps (A 17, B 13) are rewritten as one action, an optional hint and the result. The renderer puts the action in large bold type, the hint in grey (amber when it starts with ⚠) and the result after a green ✓. The old "Look:" and "Correct when:" labels are gone. Units are unchanged, so every format still carries the same essentials (`build.py check`).
- Video narration is shorter, with one or two sentences per scene and the same shots, callouts, units and chapters. Motion windows are regrouped to the new sentence count.
- In-game tutorial lines (33 keys, EN/TH/JA) are one action each and about 40 Thai characters at most.
- The whole-fan Examine chip lists only the visible areas, in short lines that fit its tested 340 × 220 size.
- The recovery/feedback bar's height candidates moved from −0.25/−0.4/+0.2 m to −0.15/+0.05/−0.25 m relative to the eye. In the owner video it sat at bench height and looked sunk into the table.
- The training screw's fasten/loosen label needs 8° of net travel before it flips. In the owner log it flickered about 25 times in 5 s.
- The PDF manual stays the detailed reference.

## Study start opened (owner, 29 Oct night)

The owner instructed, repeatedly, that the delivered app must start Thai/Japanese Study sessions directly. `ResearchUIPolicy.StudyOpenWithoutDeviceApprovals` (version `S1_OWNER_OPEN_STUDY_20261029`) lets a Study session start when no `OperatorControl/research-operator-authorization.json` exists. It also no longer requires the task ports' `contentApproved` or the controller's `translationsReviewed` flag. A present authorization file is still validated as before. Operator commands without a file log the scope `OwnerOpenStudyNoDeviceAuthorization`, and every session logs `study_gate_version`. Consent and ethics approvals are the researcher's responsibility outside the app.

Also fixed the same night: the headset group menu is selectable again, the Reader no longer waits behind "Placement unavailable" while the wearer's hand is busy (a hidden, unplaced panel is placed at once), an edge-on Reader shows a direction hint instead, and the Examine chip never sits lower than the bottom of the part it labels.

## Reader shows in Task B (owner, 30 Oct)

On the headset the Reader opened in Task B showed nothing (session 3d0d01f0, APK 20261029.2112). The Editor reproduced it at every gaze: in Task B the pedestal fan's head sits about 0.6 m in front of the eyes at eye height, so every authored anchor and head-relative Reader pose was either inside the fan, behind it in the sight line, or outside the view. The Reader also opened at its Tutorial pose (18 m away, facing away), was face-hidden there, and the face-hidden state suppressed both the retry and the `Blocked` log.

The owner's photos of the next build (APK 20261030.0610, session acd11c74) showed the Reader on top of the Main panel, and every drag sample was rejected (31/31), again because the fan sat in the sight line of each candidate pose.

`ResearchPanelMotion` now lets equipment in the sight line cover part of a Reader during a task (`EquipmentMayCover`); a Reader pose still may not intersect equipment or the Main panel, and the Main panel must not sit in its sight line. A Reader asked for while looking down is still placed facing the wearer. A panel never placed since it opened is retried rather than left face-hidden, and every non-retry placement failure is logged. Editor regression: `ResearchS1FlowDriver.ReaderOpenSweep` (Play Mode, active task) reopens the Reader from the stale far pose under five gazes and once with Main moved to the Reader's spot, and measures on-screen Reader/Main overlap.

## English Reader for Tasks A and B (owner, 30 Oct)

DEV sessions offer English, but only Training had English Reader packages, so every task format showed "Unavailable". `Tools/s1_reader_v2` now builds A_en and B_en (manual, steps, illustrated, narrated video) from English text added next to `ja` in `content_A/B.json`; result quotes use the app's English outcome strings. English is DEV-only and not language-reviewed.

## Not verified

No participant has completed a timed run. The 5–6 minute target is an estimate from action counts, not a measurement. Check `TASK_STARTED` to `TASK_COMPLETED` with 2–3 people outside the project.
