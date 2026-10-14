# Start here: S1 Q5 current-source handoff

Updated 2026-10-14. **Use `main` as the single current working and delivery branch**, as explicitly requested by the user. It includes the complete committed S1 Q5 source checkpoint `4a3f14bfa257b4cecf70674fecab98aec4a7bad4`. This remains a **PARTIAL technical source checkpoint**, not a participant-ready release. Use repository-relative paths and check actual Git status; the earlier uncommitted-source warning no longer describes this checkpoint.

Do not create extra branches/worktrees by default. Use one writer per checkout, commit reviewed batches, and deliver them to `main` without force-push. If a host requires a temporary branch, integrate reviewed changes into `main` before reporting delivery complete. Never overwrite newer local/cloud edits.

## Latest cloud session (14 Oct 2026 night)

Read `Documentation/S1/HANDOFF_20261015_CLOUD_TH.md` first. It added the versioned fixed-position UI policy
(`ResearchFlow/Core/ResearchUIPolicy.cs`), shorter Training presentation, Reader feedback/video/caption fixes and export
protocol versions. Unity and Quest were NOT reachable from that session: nothing was built, installed or played.

## Read order

1. `AGENTS.md`, `CLAUDE.md`, root `design_rule.md` v1.1.
2. `Documentation/S1/ADVISOR_UI_AMENDMENT_20261014.md` (latest user/advisor direction).
3. `Documentation/S1/CAPTURE_REVIEW_20261014.md` (actual user-capture findings and uncertainties).
4. `Documentation/S1/VR_TaskSpec_S1_LOCKED_TH.md`, `content_contract.json`, `verification_design.json` (unchanged baseline copies).
5. `Documentation/S1/CLOUD_CONTINUATION_PROMPT_TH.txt` (execution brief).
6. Relevant current source/tests only. Do not recursively scan historical Logs or ask the user to reattach the whole conversation.

## Source and artifact identities

- Canonical working/delivery branch: `main`. Verify local and remote commit/status before each batch.
- Source checkpoint: `4a3f14bfa257b4cecf70674fecab98aec4a7bad4`; its ancestors include the earlier research checkpoint `3c2655ed05c7655021a64f98c480ee9b88960ada` and old main `3c0ee3d74d2db5b0c332c41fca92806b48062ff5`. The handoff and research branches are historical only; do not select them for new work.
- Unity: `6000.3.20f1` with URP 17.3, Meta Interaction SDK 205 and OpenXR from Packages lock.
- Canonical scene: `Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity`.
- Current source contains Q5 changes: render scale 1.10, MSAA 4x, incremental GC enabled, narrowed panel-clearance candidate checks, cached cable validation, operator AB/BA/P/Q and abort reason tools.
- Q4 APK: `ResearchFlowQuest3_20261014_091322.apk`, SHA256 `b4cce39d8c484fcf231aee05e1992cc7cfc81912bc1bfcb762b452ccffde17e4`.
- Q5 APK: `ResearchFlowQuest3_20261014_093556.apk`, SHA256 `f8749d8981cccccc309d2dbe9377fb0bd6a4b88bb96ba27206fff17281890ec9`.
- Q5 was built, but NOT installed in the last verified install report. Q4 was the last confirmed installed version. A subsequent recording cannot be assigned a build from its filename alone. APK binaries are local artifacts, not included here.

## What exists and what its evidence means

- Task A/B mechanics and neutral Trainer_FX1 exist. Editor driver uses SDK ray/trigger/grip inputs; a small Create-next-ID menu operation remains programmatic. Do not replace the working state machine or interaction stack.
- Editor matrix attempted eight cells. Six have A and B Pass, TH AB_Q includes a deliberate A timeout, and JA AB_P includes withdrawal after A. Keep negative cases but add missing normal completion cases.
- S1 packages exist for Training and A/B in TH/JA; A/B EN is not supplied. Reader video is currently a slideshow, not complete motion instruction. Do not call all four formats content-accepted.
- Event export can produce S1 analysis, but movement coverage is incomplete in earlier Editor runs. `s1_analysis_complete=true` is NOT `data_complete=true`.
- The on-device simulated-input driver is NOT implemented. Existing `ResearchS1FlowDriver` is Editor-only. ADB operator commands do not themselves constitute full participant interaction.
- On-device idle performance improved. The reported 278->1 statistic uses a threshold of about 20.8 ms (1.5 frame budgets), NOT all intervals exceeding 13.9 ms. p99 remained about 16 ms. Multiple optimizations changed together; don't attribute total allocation reduction solely to incremental GC.
- Offscreen on-device image probing favored 1.10/4x; a custom shimmer index fell about 16% in one matched comparison. This does not establish human clarity, timewarp/compositor stability or comfort. Retain candidate settings until meaningful workload comparison justifies changing them.
- Latest user footage shows ongoing UI/training issues. Human usability and lens acceptance remain open.

## Immediate implementation order

1. Keep working Task mechanics. Implement the fixed-position four-format policy and remove its drag lesson/hit areas together, as a versioned demo revision. Fix current reader layout overlap/loading recovery and checkpoint clarity.
2. Condense Training presentation while retaining required skill evidence; use action-specific illustrations/actual motion rather than the same generic controller diagram.
3. Inspect and author table/part/UI anchors with matched P/Q reach and visibility. Do not expose correct parts or force a solution sequence.
4. Finish actual motion videos and neutral illustrative close-ups; test Reader A/B x TH/JA. Preserve PDF/Steps that are correct.
5. Finish interaction/error/restore/export coverage and test local-first data loss/retry/QC; no fabricated movement/questionnaires.
6. On a correctly authorized local bridge, test/install an exact technical candidate and run native workload/physical-input acceptance. Otherwise create a precise local-validation checklist and continue source/tests offline.
7. Minimal cleanup, final source/content identity and demo guide. Do not merge a cloud patch over dirty local edits without review.

## Cloud versus local capabilities

A cloud VM does not automatically have `D:\VR_TMU`, Unity licensing, Windows desktop or the USB Quest. Inspect available tools first. If a configured authenticated local MCP is actually available, read its status and take exclusive ownership before using Unity/ADB. Do not expose a new public file server, embed tokens in git, or claim remote hardware tests without a real bridge.

If Unity/device are unavailable, implement and test what is supported in the checkout, use pure-code/media/static checks appropriately, and mark Editor/device/wearer checks unrun. Do not install a second uncontrolled environment or replace Unity tests with unrelated web tests.

## Publication exclusions / local-only dependencies

This public-source branch must exclude user/participant recordings, meeting audio/transcript, registries, consent, raw research logs, credentials/keystores, `Library`, `Temp`, `.claude` machine settings, profiler dumps and recovery scenes. Historical legacy ReaderDemo manual/images/video are local-only because redistribution rights are not confirmed. Raw local font source is not a downloadable deliverable; existing Unity SDF assets and font GUID references may require the owner's licensed local source for regeneration. No material is relicensed by this handoff.

Absent local legacy fixtures may require explicit test skips with a reason; do not delete tests, make fallback media participant content, or silently mark them passed. Never fetch the missing legacy manual just to make CI green.

The S1 baseline copies match their external originals:
- specification: `10ff5299d0b2d2951e32e6f099c8be0125ee14173a3d1218adc39d0e80834d39`
- content contract: `f31508232c8b43c8d3808aabcaecf6f62ce1530770a0e6d7e78c7ded00878dd7`
- verification design: `46b1801c3f71b2a727e664a7ebce140fe03b81fd775ccac040fad21bdcff430b`

## Data and approvals

Use app-local recording followed by verified export to the authorized PC, then controlled backup. Cloud coding is not consent to upload participant datasets. Follow the approved retention policy; the provided June proposal says one year after publication or, if unpublished, one year after project end. Do not delete data now. Human language/content/ethics approval and wearer acceptance cannot be created by changing a flag.
