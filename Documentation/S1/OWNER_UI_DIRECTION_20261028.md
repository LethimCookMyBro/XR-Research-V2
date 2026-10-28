# Owner UI direction — 28 October 2026

The owner's latest instruction restores participant panel dragging. The owner reports UI flicker, an unusable Reference button, and hidden drag controls in RC7. This design direction supersedes the 14 October prohibition on free panel movement for the new implementation. Do not ask the owner again whether dragging is wanted and do not re-hide the grip merely to match the old fixed-layout mockup.

This is a design instruction, NOT an assertion of advisor/ethics/content approval and NOT a claim of hardware validation.

## Required behavior

- Expose a single restrained drag grip with an adequate hit target on appropriate Task, Reader and practice panels. Do not bring back redundant toolbars/buttons.
- Drag from the grip, scroll from document content, and click from controls; keep input ownership separate.
- Panels stay in the user-chosen safe pose after release. Do not chase the headset or relocate while a grip/scroll/click is held.
- Preserve static-geometry collision checks, Stop/Pause access, task evaluators, time limits, and raw events.
- Do not convert old recorded fixed sessions into movable sessions in place. New/default UI-policy selection and any migration must be versioned; old evidence keeps its recorded policy.
- Continue using genuine approval gates for Study. Enabling panel dragging does not approve experimental media or authorize study data collection.

## Reproduced and corrected in this source change

`ResearchFlowView.Refresh` disabled the entire Main toolbar whenever `compactTask` was true, even under `S1_DEV_MOVABLE_UI_20261017` or the legacy movable policy. Enabling its child grip could not restore the inactive parent.

The focused correction keeps the Task grip available when the session's policy already allows dragging, without adding Minimize/Recenter buttons to the compact Task card. Existing fixed historical policy behavior is unchanged.

`ResearchMovableTaskToolbarChecks` exercises the actual View Refresh/layout in stopped-Editor preview scenes, TH/JA, Task A/B and all three existing policies. It does not simulate a physical Quest controller or establish through-lens readability.

## Still pending

- Native reproduction of the reported flicker and Reference click failure: Quest is absent from ADB during this audit.
- Versioned policy/default behavior for the complete new draggable UI, including pre-session and result/checkpoint screens where appropriate; do not rewrite old sessions.
- Inspect Canvas/GraphicRaycaster enable transitions, placement reasons, tracking/focus, media loading and actual exposure independently. A viewport-edge test is not a substitute for actual surface lifecycle; do not falsify exposure or keep blocked controls clickable.
- Validate drag/scroll/button ray ownership, safe placement and Reference reopen with real Quest input before claiming the installed application fixed.

Frozen RC7 delivery and its installed APK are not modified by this source-only patch. Build and install a separately verified candidate only after the affected regression and native tests are ready.

## Second change, 28 October 2026 (after 828c971)

- New policy `S1_MOVABLE_UI_20261028` (`ResearchUIPolicy.OwnerMovable`) is the default for every new session, Study and DEV alike. It behaves like `S1_DEV_MOVABLE_UI_20261017`: station-anchored first pose, single-grip drag, participant recenter, no compulsory drag lesson. Its protocol id is `S1_LOCKED_TH_BASELINE+OWNER_MOVABLE_UI_20261028`. Recorded sessions keep their recorded policy; `fixed`, `dev_movable` and `legacy_movable` remain selectable for regression. Study approval gates are unchanged.
- Flicker source fixed in `ResearchPanelMotion`: the per-frame idle clearance test also failed whenever the other panel or a held/movable part crossed the line of sight. That disabled the Canvas and GraphicRaycaster, and Main and Reader could conceal each other in turn. Idle re-checks of an accepted pose now ignore the peer panel and movable parts. Static geometry, facing and new placements (recenter, drag release) are checked as before. A blocked panel is re-shown only after its pose stays clear for 0.25 s, with input off while it waits.
- Reader input fixed in `ResearchReferenceViewer`: "surface shown" and "fully inside both eye viewports" were one flag. A corner of the 1.04 m Reader leaving either eye made every Reader command return false and cancelled the video decoder, which reloaded on the next frame back in view. `SetPresentation(shown, inView)` keeps controls and media alive while the surface is shown. Only `active_reading` and exposure still require the full in-view test, so out-of-view time is never counted as reading.
- Not yet proven on a Quest: whether these were the root causes the owner saw. Native repro and after-check are still pending.
