# Usability recovery, 31 October 2026

New sessions use V6 Reader content and `S1_LAYOUT_REACH_LOWER_FAN_20261031`. Recorded V4/V5 sessions retain their Reader routes. Recorded `S1_LAYOUT_REACH_20261029` retains the original fan height.

## Changes

- Fan head and work parts are 0.30 m lower. The stand and head pipes are shortened; the base, table, part sizes, socket mates and task criteria remain unchanged.
- Held collision checks accept ordinary carry distances up to a combined translation/rotation arc of 2.048 m. The previous limit was 0.128 m. Sampling remains 0.25 mm, with swept collision checks. Existing shallow contact may slide without increasing penetration.
- The front guard's filled collision proxy permits the seated blade cap inside its cavity only during the aligned, unlocked bayonet entry/exit path. Other obstacles, sideways motion and closed-clip conditions remain checked.
- V6 PDF manuals separate part sections and use larger model images. Task steps include a target image. Fan steps separate actions into 21 cards, and illustrated guidance separates its focus across 12 images. Every format remains optional; viewing a page does not complete a repair.
- TH/JA/EN media were regenerated. Video uses captured Unity model motion with narration and chapters. It is not footage of a person using native Quest controllers or repairing physical equipment.
- Attempt payload v2 records operation and block reason. Separate observations preserve meaningful-action counts. Part motion records selection, release/cancel, pose, velocity, thread degrees, first contact and settling. Moving samples are capped at 10 Hz and identify their observation epoch and actual attempt when known.

## Evidence

Local evidence is in `Logs/UsabilityRecovery20261030/`. The original actual-donor carry test failed 17 cases. The modified carry/constraint checks passed. A further incremental refit test exposed four blocked positions; after correction all 33 checks passed. The reach profile passed 25 checks and old/new/repeat/restore routing. Startup passed 101 checks; free-body regression passed 127; fan regression passed 225; PC fastener regression passed 51. Reader verification passed 536 checks across two tasks, three languages and four formats. Nine source suites and nine media packages passed.

Actual part-observer callbacks through the controller logger passed 30 stopped-Editor checks. Separate writer/export fixtures preserved observation records. The runtime observer fixture itself lacks a research-session sidecar and is not a full exported research session. These tests do not establish complete participant data.

Technical APK `3.4.1.20261030.1802`, code 435962, built with 0 errors and 674 warnings. SHA256 is `43bdab5d08a8c7cb8f1a6582c6e46cb2dc29a071286a2bc381d1b1e751f9cd6a`. This candidate was not installed during this change.

## Open acceptance checks

Native controller acquisition, floor retrieval, sustained collision workload, visual blur and wearer reach need Quest testing. No timed participant run establishes the 5 to 7 minute goal. Sampled motion does not capture every intermediate pose, gaze, comprehension or intent. No new participant recovery command was added. Translation equivalence needs human review. Do not mark study readiness or data completeness from the engineering checks.

## Owner headset session, 31 October afternoon (run 48aef287)

Three problems from the owner's session and log.

- Panel drags froze. During Task B, 60 to 90 % of drag samples were rejected. In the Editor at the fan, 0 of 450 candidate poses for the Main panel passed the old drag check. Most failed because one corner left an eye's viewport (349), and the rest because equipment sat in the sight line. A held panel now follows the ray wherever it faces the wearer, clears geometry and keeps off the other panel (`ResearchPanelMotion.DragPoseClear`). The same census passes 233 of 450; the remainder intersect the fan, bench or tray. `PANEL_DRAG_METRICS.rejection_reasons` now counts rejections by reason.
- The front guard would not twist or come off. The log shows 28 grabs; the guard toggled between seated, partial and `Contact` and never came off. A part on its guide had to stay within 4 to 7 mm and 8 deg of the axis. Any wobble dropped it off the guide into the blade cap. A part now stays on its guide until pulled out along the axis. The guide captures within 15 mm and 15 deg, and keys snap within 20 deg. The front bayonet is two-position: past two thirds of its 12 deg turn it snaps unlocked, and below one third it snaps locked. `ResearchFanReassemblyChecks` adds wobbling-hand cases. The old code fails 24 of them and the new code passes all 59.
- The four formats looked alike. V6 Steps cards carried the same model screenshots as Illustrated and the PDF. Steps is now text only. PDF pages sit on a grey desk, and video plays on a black player. `reader_ui_version` is `S1_DISTINCT_FORMATS_READER_20261031_R5`. Package content is unchanged; the Steps pictures stay in the package but are not shown.

`Tools/s1_session_report.py SESSION_DIR` writes `report_th.md`, `timeline.csv` and `summary.json` from the raw log. It covers each task's result and time, Inspect results, which formats were opened in what order and for how long, pages visited, and every grab with what changed in the equipment and what the system blocked. It reports logged facts only. Panel exposure is not gaze. `research_export.py` keeps the study measures.

Task runners after the V5 free-body change. Released parts now fall, roll and settle, so the SDK runners' parking moves were out of date. The Task B runner now lays round parts flat on the bench, lifts each part clear before lining it up again, and has a 240 s budget. With those changes, the full Task B repair and reassembly passes through simulated controllers in the real scene (`BOUNDED_B_REPAIR_REASSEMBLY_OBSERVED`, also inside full sessions). Two Task A runner steps were updated. The tracking-jump check now jumps 3 m, beyond the documented 2.048 m carry limit. The SSD drop-back accepts a loose settled pose instead of the exact rest pose. The GPU bracket screw is lifted before it is turned. `ResearchS1RemainingMechanicsChecks.RunPhysics` uses the same 3 m jump.

Open: the Task A runner still stops while carrying the SATA data plug through `S1_TrayRoutingSlot1`. In a static probe, the plug passes the slot in its rest orientation with clear collision sweeps and an allowed cable. The cable refuses the plug turned 90 deg. A carry-orientation change did not fix the runner, so the cause is not known. No headset session has reached Task A cable routing yet. Check SATA routing on the Quest before collecting data.

Suites that fail with current source: `ResearchSpatialReadabilityChecks.Run` fails on Examine chip placement. It fails the same way with the panel files at HEAD; it passed on 30 Oct before the V5 commit. `ResearchFocusedReaderInputChecks.RunProduction` pins Reader package hashes from before V5/V6. The 8 suites that already failed on HEAD 9b14689 still fail.

## Shorter Reader steps (owner, 31 October evening)

The owner asked to cut unneeded content so the tasks go faster and the text is unambiguous. V6 A/B content was condensed in place. Recorded V6 sessions carry the previous package hash in their logs.

- Steps now give one card per job: Task B went from 21 to 11 cards, Task A from 17 to 11. Each card names the job, the controls, and what done looks like. For example, "Remove the front guard: open the clip, turn to the mark, pull off" replaces three separate cards. Steps are text-only (`step_layout: text`), and the per-step pictures are gone from the packages.
- Task B Illustrated went from 12 to 10 pictures. The separate "fix the front guard" and "fix the blade" pictures repeated the reassembly picture. The reassembly picture now names the front-guard lock turn.
- The PDF and video are unchanged (`--keep-manual`; narration from the cached TTS). All four formats still cover every essential unit (`build.py check`: A 17/17, B 19/19).
- Unity checks: content clarity 428/428, optional picture 270/270, tutorial Reader 143/143.

## Owner headset session, 2 November (run 535d20bb)

The owner reported that the Reader was still confusing and that parts got stuck near the case and fan, sank through the table and floor, or went missing. They also said the cable layout was poor.

- Held parts stuck near equipment. In Task A the log shows 29 moves rejected as `Contact`, most of them G01 against the case rear wall. When a move was blocked, the part froze while the hand kept moving. `ResearchS1HeldCollision.Slide` now keeps the rotation and takes the largest clear axis-aligned part of the move, so a blocked part slides along the surface instead of freezing. Task A free moves (`ResearchS1TaskAMechanics.FreeMove`, used by RAM/GPU via `QAPlug.SlideFree`, sleds and loose screws) and Task B free moves (`ResearchS1TaskBMechanics.FreePose`) use it. Seated, guided and threaded motion is unchanged. `ResearchS1FanPart.FreePose` reports `Contact` only when the accepted pose differs from the requested one.
- Parts looked missing. RAM, one GPU, the screws and the SSD sleds sat 0.85 to 1.13 m from the case, past the ends of the participant's view. Two loose case side panels stood upright on the bench and hid the GPUs and SSDs. `ResearchS1TaskABuilder.PlaceOptionsWithinReach` now puts RAM and screws on the near edge to either side of the case, the GPUs at the near corners, and the SSDs on the right. Every part is at least 0.5 m from the service pivot, because the case sweeps about 0.44 m when turned. The side panels were removed. P/Q mirroring is unchanged.
- The unplugged AC cord lay across the participant's table edge and hung to the floor. It now lies behind the case, inside the turn circle.
- Parts sank into mats. The ESD mats, parts trays and spare board had no colliders, so parts settled 9 to 27 mm inside them. Box colliders were added.
- Thrown tutorial props fell through the training table. Measured in the Editor, a slow drop of the stone or the mug onto the training table settles on it, but a fast drop (8 m/s) of either ends on the floor under the table. The training table collider is a concave mesh with a thin top. A 6 cm box slab (`Training_Practice_A_TopSlab`) now sits under the top surface. With it, 16 of 18 drops at 1, 8 and 12 m/s settle on the table and 2 bounce off the edge onto the floor; none end under it. Released inside the table or the floor, a prop also had nothing to push it out. `HandsLabObject.LiftOutOfStatic` places a released prop that overlaps static geometry on the surface below its centre. In the same probe, slow and fast drops of all 8 Task A/B part types onto the computer bench and the fan floor stayed on the surface or bounced off it. None ended below a surface.
- "Power is isolated / no OS / do not open the motor" confused the owner. Those lines were removed from the task panel and every Reader format. Task A keeps "CPU, cooler, PSU and cables are already connected". The Task B scope unit was deleted, which removed one Illustrated picture (B: 9 pictures). Coverage: A 17/17 and B 18/18 in all four formats.

Checks: Task A runner reached the same 16 phases as before the change, including both full service-yaw turns, and stopped at the known SATA routing step. Task B runner `BOUNDED_B_REPAIR_REASSEMBLY_OBSERVED`. Fan reassembly 59/59, carry constraints, part motion 30/30, free body 127/127 and startup 101/101, content clarity 422/422, optional picture 270/270, compact UI, PDF zoom, video chapters and Reader stuck all pass. `ResearchHeldCollisionParityChecks` now reports mismatches because it compares `Allows` with a frozen baseline copy, and the fan parts it samples now have colliders under them that were not there before. Its 125 queries ran with 103 accepted, against 88 for the baseline.

Not done: video narration is still long (TH about 1:55, JA about 2:50). The Reader formats were made shorter but were not redesigned. SATA data routing in the Task A runner is still open. None of this was tested on the headset.
