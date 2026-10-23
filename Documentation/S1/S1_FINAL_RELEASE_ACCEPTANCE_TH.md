# Current engineering checkpoint ? 23 October 2026

?????: **?????? Technical Ready / ?????? Study Ready**. ?????????? DEV engineering candidate ????????.
?????????????: `D:/VR_TMU/transaction-verification-external/s1-continue-20261023`.
???????????????????? Historical ??????????????????? ???????????????????????????.

| Gate | Status | Evidence / ?????? |
|---|---|---|
| Legacy / Examine | PASS_EDITOR | scene_contract.py: A.module ? ServicePivot (31 nontrigger); B.module ? ResearchS1TaskB (11); Legacy inactive; stable IDs preserved |
| TH/JA P/Q eye 1.0/1.65 | PASS_EDITOR | delivery-seated-P/Q, delivery-standing-P/Q: 112/112 main clearance/readability; mono fixture ?????? wearer/stereo acceptance |
| Compact single-focus / Details | PASS_EDITOR | neutral-actions-compiled/session-stages.json: 72 checks; Reference/Inspect Secondary ???????; body paragraphs ?????; ??????????????????? |
| Localization | PASS_EDITOR | localization-delivery.txt: 392 keys ? 3; ?????? human language approval |
| Core / approval | PASS_EDITOR | core-delivery.txt 163/163; approval-delivery-corrected.txt 38 cases/114 checks; study gate ?????? |
| FX1 dropped recovery | PASS_EDITOR | training-recovery-results.json: falling fast recovers; flying far stays; settled far requires dwell; cloned stopped-Editor fixture ?????? grip/physics acceptance |
| Simulator OpenXR | PASS_EDITOR | simulator-unity.json, simulator-runtime.json, simulator-game.png: active display, Head + two Touch Plus; no complete controller/task sequence |
| Fixed-position transient occlusion | FAIL | inherited clearance_retry may select another anchor after obstruction; policy acceptance remains open; clearance guard retained |
| Full A/B / training controller workflow | NOT_TESTED | ????? full native or wearer acceptance ???????? |
| Reader four formats / H1?H4 / movement / host QC | NOT_TESTED | ???????? end-to-end current-build dataset acceptance |
| APK build/install | NOT_TESTED | ?? build/install evidence; ???????????????????????????????????? Technical Ready |
| Human language / protocol / ethics / wearer | NOT_TESTED | ??????????????????????; simulator ???????????? |

Root causes fixed: duplicate legacy visuals with stale Examine anchors; insufficient elevated seated computer fallback; split-width summary burden and redundant Details row; unequal Inspect emphasis; training fall recovery incorrectly gated by settled velocity.
????????????? Task evaluator, stable IDs, Inspect timing, research criteria, furniture colliders ???? XR Origin.

Glossary for human review: Reference = ????????????? / ????; Inspect = ?????? / ??; Examine = ????????? / ?????; Pause = ??? / ????; Continue = ????? / ???. Examine ?????????? ???? Inspect ??????????? ?????????? event semantics.
Summary copy ???? goal/action/safety; OS exclusion ???????????????????????? inline Details. Thai/Japanese semantic approval ???????????.

## Historical evidence below

# S1 final release acceptance

วันที่ 23 ตุลาคม 2026. ผลรอบนี้เป็นการแก้ P0 source และ regression แบบจำกัด. ไม่ใช่ final release acceptance.

## A. Executive result

**NOT READY. Technical Ready=false. Study Ready=false. full_goal_complete=false.**

ผู้วิจัยยังเริ่มเก็บข้อมูลผู้เข้าร่วมจากผลรอบนี้ไม่ได้. ผล E2E เดิม 4,257 assertions และ 71 captures เป็น EditorOffline technical fixture. Raw เดิมมี `completed_without_visible_pass_before_420` และ movement coverage ไม่ครบ. ผลนั้นไม่รับรอง mechanics หรือ native Quest.

- Checkout อยู่ที่ `D:/VR_TMU/XR-Research-V2` บน `main`.
- HEAD คือ `cb410154fef1b4e2cb71b9662929f1eb4cec9c1a`. ยังมี dirty source ที่มีอยู่ก่อนรอบนี้และ changes รอบนี้. ยังไม่สร้าง release commit.
- Unity จริงคือ `6000.3.20f1`. สำรอง dirty Scene ด้วย SaveAsCopy ภายนอกแล้วพบว่าตรง disk ทุก byte. แก้ hierarchy ที่ review แล้วและ Save canonical หลังตรวจ preimage/candidate hashes. Scene กลับ clean.
- ไม่มี APK ใหม่จาก source หลังแก้. ไม่ได้ install, launch, force-stop, switch user, clear data หรือแก้ profile guards.
- Quest 3 serial `2G0YC5ZHBF00FJ` เชื่อมต่อจริง. Current Android user=0. Controller ซ้ายและขวาเป็น CONNECTED_INACTIVE. ผู้ใช้ยืนยันว่ายังไม่มีผู้สวม.
- Static profile identity ปฏิเสธด้วย `QUEST_PROFILE_GUARD: Foreground activity/profile mismatch`, exit 3. ไม่บังคับ focus หรือเปลี่ยน user.

หลักฐานรอบนี้อยู่ที่ `D:/VR_TMU/transaction-verification-external/s1-final-20261023`. รายการ source hashes และ rollback จะอยู่ใน `transaction-manifest.json` และ `VERIFICATION.txt`.

## B. Complete issue matrix ของสิ่งที่ตรวจในรอบนี้

| ID | Severity | Component | Root cause | Fix | Editor test | Quest test | Evidence | Status |
|---|---|---|---|---|---|---|---|---|
| P0-01 | P0 | Bound task evaluation | demoPassed=true ข้าม evaluator จริง | คืนการประเมิน bound DTO และ feedback จาก evaluator | Component 7 checks PASS. Snapshot 3 cases PASS | NOT TESTED | baseline/component; modified/component | FIXED scoped component |
| P0-02 | P0 | Inspect timing | Editor shortcut ข้าม 3 seconds และ AdvanceTaskTiming จบก่อน render receipt | ลบ FastForwardFixtureVisible. คืน completion ไป InspectionFeedbackRendered | Timing 73 PASS. Enhanced snapshot 3 cases ยังคง Ready/active จนมี receipt และปฏิเสธ no-view receipt | NOT TESTED | baseline/snapshot; modified/snapshot | FIXED source. Actual rendered component PASS; native pending |
| P0-03 | P0 | Task start | ถอด HasBoundMechanics guard และยอม initial_state=null | คืน guard และ mandatory CaptureState | Actual inactive controller/view ผ่าน bound Start, initial snapshot และ missing/gateless binding negatives. ไม่ใช่ native-input proof | NOT TESTED | continuation-clean-scene-retest/accepted | FIXED scoped command/component |
| P0-04 | P0 | ScrollRect buttons/toggles | Absorber รับทุก drag จน list ไม่เลื่อนและ release กลายเป็น click | Redirect pointerDrag ไป active parent ScrollRect. คง threshold และ outside-list absorber | Current compiled SDK component test 13/13 PASS | NOT TESTED | modified-recompiled/drag | FIXED scoped component |
| P1-05 | P1 | .NET test outputs | 3 csproj override shared Library output กลับเข้า Assets. Shim DLL ทำ Unity CS0433 | ลบ 6 XML overrides. ใช้ Directory.Build.props เดิม | Plain rerun 22/80/27 PASS. Resolved outputs อยู่ Library | N/A | regression/*.paths.json; *.corrected.stdout.txt | FIXED. Quarantine preserved |
| P0-06 | P0 | Accepted UI regression | Inactive fixture ไม่มี bound port และคาด immediate Inspect/checkpoint | จัด bound synthetic DTO/gate. ตรวจ Checking จริง. Seed Visible เฉพาะ command-contract subcase แยกชัด | 39 PASS. Duplicate/early Continue rejected. Abort/export isolated | NOT TESTED | continuation-clean-scene-retest/accepted | FIXED scoped fixture. Actual rendered component PASS; native pending |
| P0-07 | P0 | Full A/B mechanics and FX1 | Full physical acceptance ยังไม่ครอบคลุม. Tolerances dirty เดิมยังไม่มี wearer proof | ไม่ปรับ tolerance, fault, timer หรือ hints ในรอบนี้ | Pure mechanics tests ผ่านตามรายการข้างล่างเท่านั้น | NOT TESTED | mechanics-readonly.md | OPEN |
| P0-08 | P0 | Research data | Raw E2E เดิมขาด visible-pass evidence และ coverage head/left/right ทั้ง A/B | ไม่แก้ raw, เติม zero หรือผ่อน QC | Read-only existing processor ให้ data_complete=false | NOT TESTED | release-readonly.md | OPEN |
| P0-09 | P0 | New APK/install | ยังไม่มี reviewed source-bound candidate หลังแก้. Current profile guard refused และ package shared profiles | รักษา guard และข้อมูลเดิม | SOURCE_NOT_BOUND สำหรับ APK เดิม | BLOCKED | device-identity.stderr.txt; release-readonly.md | OPEN |

ไม่ใช่ exhaustive defect inventory. ประเด็นที่ยังไม่ reproduce จะไม่ถูกเรียกว่า root-cause verified.

## C. Full workflow matrix

ตารางนี้เป็น coverage ledger ของรุ่นหลังแก้. PASS ของ component ไม่เท่ากับทั้งหน้าหรือ transition.

| Screen/state | Normal transition | Disabled/error/pause/recovery/resume | Current evidence |
|---|---|---|---|
| App launch/ResearcherSetup | Launch ไป setup | Device/controller missing, invalid ID, completed ID, interrupted session | NOT TESTED native |
| TutorialWelcome | Welcome ไป Training | Pause, focus lost, researcher stop | NOT TESTED full workflow |
| TutorialInteractions | Teleport/grab/rotate/release/FX1 | Wrong orientation, drop, tracking lost, recovery | Tutorial callback 9/9 PASS. Physical NOT TESTED |
| TutorialReferenceInspect | Four-format skills และ Inspect | Early close, reopen, media missing/error, pause | Current full render/media NOT TESTED |
| TutorialComplete | Continue ไป Task1Ready | Researcher intervention, invalid readiness | NOT TESTED current E2E |
| Task1Ready | Confirmation แล้วเริ่ม Task1Active | Missing bound adapter ถูก guard ปฏิเสธ. Valid bound start ยังต้องรัน | Partial source/negative evidence |
| Task1Active | Examine/reference/repair/inspect | Fail/retry, repeated inspect, pause/resume, timeout/stop | Snapshot 3 cases และ pure timing 73 PASS. Physical/render pending |
| PostTask1Checkpoint | NASA-TLX และ researcher resume | Missing questionnaire, unauthorized resume, export failure | NOT TESTED current E2E |
| Task2Ready | Confirmation แล้วเริ่ม clock ใหม่ | Bound adapter/readiness failures | Pure clock restart PASS. Workflow NOT TESTED |
| Task2Active | Task อีกงานและ Inspect | Fail/retry/pause/timeout/recovery | NOT TESTED physical/current E2E |
| PostTask2Checkpoint | Final assessment | Stop/interrupted assessment/data error | NOT TESTED current E2E |
| ParticipantComplete | Participant completion | App restart/focus lost | NOT TESTED current E2E |
| ResearcherSummary | Export/receipt/host import | Disk full/logger/export/network/crash recovery | Current real native export/QC NOT TESTED |

Condition cells หลังแก้ยังไม่มี full native pass.

| Language | Order | Layout | Current full Editor E2E | Native |
|---|---|---|---|---|
| Thai | AB | P | NOT TESTED | NOT TESTED |
| Thai | AB | Q | NOT TESTED | NOT TESTED |
| Thai | BA | P | NOT TESTED | NOT TESTED |
| Thai | BA | Q | NOT TESTED | NOT TESTED |
| Japanese | AB | P | NOT TESTED | NOT TESTED |
| Japanese | AB | Q | NOT TESTED | NOT TESTED |
| Japanese | BA | P | NOT TESTED | NOT TESTED |
| Japanese | BA | Q | NOT TESTED | NOT TESTED |

Standing/seated, controller left/right, cold launch/restart, timeout, early stop, device/media/storage failure และ recovery ยังต้อง native acceptance. ไม่ลด matrix เพื่อให้ PASS.

## D. UI/UX gallery

ไม่มี before/after screenshots ของ rendered TH/JP screens จากรุ่นหลังแก้ในรอบนี้. ไม่มีภาพผ่านเลนส์. Existing 71 captures เป็น historical fixture evidence ไม่ใช่ gallery acceptance ของรุ่นปัจจุบัน. ไม่ใช้ภาพสร้างใหม่แทน runtime.

## E. Controller and spatial report

Current drag test ใช้ synthetic PointerEventData ผ่าน installed SDK UpdatePointerEventData และ ProcessDrag. ไม่เรียก onClick.Invoke. Ordinary click และ jitter5px เกิด click ครั้งเดียว. Deliberate80px list drag เลื่อน content90px และ clicks=0 ทั้ง button/toggle. Outside-list และ disabled-scroll ไม่ย้าย parent panel.

Baseline behavior run มี 2 failures จาก 13 cases ใน fixture revision ก่อนแก้ EventSystem Bind/Unbind. Final 13/13 ใช้ fixture revision หลังแก้ lifecycle และ compile production ใหม่. จึงไม่เรียกคู่นี้ว่า strict same-fixture B/M/R. เก็บ fixture compile failures, lifecycle failures และ stale-assembly run ไว้ ไม่ใช้เป็น product verdict.

Native Raycast hit, hover, down, up, click, command acceptance และ UI state changed ยังไม่มี controller trace รอบใหม่. Dropdown จริง, left/right, teleport, boundary, reachability, standing/seated และ recovery เป็น NOT TESTED. ไม่เปลี่ยน spatial coordinates หรือ Guardian.

## F. Research data verification

Existing DEV433 artifact ที่อ่านใหม่มี 647 event rows และ 14,664 movement rows. Chronology gaps ว่าง แต่ existing QC พบ insufficient movement coverage ทั้ง 6 ช่อง A/B × head/left/right. มี completed_without_visible_pass_before_420, missing_action_type และ partial snapshot warnings. data_complete=false, movement_complete=false, s1_analysis_complete=false. รายละเอียดและ literal outputs อยู่ release-readonly.md.

รอบแก้ใช้ TEST registry/loggers ภายนอกแยกจาก participant data. Enhanced snapshot ยืนยัน evaluator/capture ครั้งเดียว, รอผลที่ Ready, ไม่มี TASK_COMPLETED/TASK_ENDED/INSPECT_FEEDBACK_VISIBLE ก่อน receipt และปฏิเสธ no-view acknowledgement. ไม่ได้สร้าง rendered success ปลอม. ยังไม่มี full native raw timeline, movement QC, export receipt และ host import/QC หลังแก้.

## G. APK delivery

ไม่มี APK ใหม่จากรุ่นหลังแก้และไม่มี install ในรอบนี้.

| Field | Actual result |
|---|---|
| Historical latest APK | D:/VR_TMU/XR-Research-V2/Logs/QuestFinalQA/build/ResearchFlowQuest3_20261022_182040.apk |
| Historical SHA256 | fd8cac32405d3e43c9d94d3ef23ba8c42ab64e807776dd5c21d52ce31c558f88 |
| Historical provenance | SOURCE_NOT_BOUND. ไม่ใช่ source หลังแก้ |
| Current installed SHA256 ที่อ่าน | 715a0046b3fbce84a557508f6890fffa79bc7a36b71bb1ebc0915985d94878f6 |
| Package | th.ac.tmu.vr.researchflow.quest3.technical |
| Activity ที่ตรวจจาก APK เดิม | com.unity3d.player.UnityPlayerGameActivity |
| Requested launcher name/icon | Build-only label VR Maintenance Lab. New headset+maintenance icon source/import verified. APK resources ยัง NOT TESTED |
| New build/signing/installed hash | NOT TESTED |

Current Android user=0 ไม่ใช่สิทธิให้อ่าน/เขียนข้อมูล User10 แทน. Shared package replacement ต้องมี data-owner/profile binding และ fresh backups/hash ของ profiles ที่เกี่ยวข้องก่อนติดตั้ง. ไม่ clear หรือ uninstall เพื่อแก้ signing.

## H. Remaining human gates and next execution

1. ทดสอบ actual rendered feedback, no duplicate completion และ explicit Continue ไป checkpoint. Accepted command fixture ผ่าน 39 checks แล้ว แต่ seeded Visible ไม่ใช่ rendered proof.
2. ใช้ real mechanics driver ตรวจ FX1/A/B, wrong-part paths, P/Q และ standing/seated. ห้ามคืน demoPassed override เพื่อให้ E2E เขียว.
3. อ่าน readiness สดและเตรียม profile-aware backups. Build จาก reviewed source ที่ freeze/hash แล้วเท่านั้น. ตรวจชื่อ/icon/package/activity/signature และ installed APK hash.
4. เมื่อมีผู้สวม ให้ทดสอบ Touch Plus ซ้าย/ขวา, native ray chain, 3-second feedback, full workflow, lens clarity/comfort และ workload performance. ผู้ใช้แจ้งว่ายังไม่มีผู้สวมในรอบนี้.
5. ผู้ตรวจ TH/JP, ผู้วิจัย และ ethics/consent/content approval ยังต้องให้หลักฐานจริงก่อน Study Ready. Proposal/Ethics PDF ไม่พบใน targeted spec inventory จึงไม่อ้างว่าอนุมัติหรืออ่านแล้ว.

## Checks ที่รันจริง

- Unity component 7 PASS. Enhanced snapshot 3 cases PASS. Tutorial callback 9 PASS.
- Current SDK synthetic drag 13 PASS. ไม่มี native-ray claim.
- Pure timing 73 PASS. Pure mechanics/evaluator 280, catalog80, input22, Task A kinematics27, fan kinematics24, observations10 PASS. ชุดนี้ไม่ใช่ physical mechanics acceptance.
- Python unittest รัน 91 tests, exit0, skipped1 ตาม platform guard. ไม่ยก skip เป็น pass.
- Accepted UI fixture ปัจจุบัน 39 PASS, revision=accepted-command-20261023-r2, scene_preserved=true. Actual rendered Inspect component ผ่าน. Full real-mechanics regression และ new release build ยังไม่ผ่านครบ.

Prove It Works ทำให้แยก current compiled results ออกจาก source, fixtures และ native. Fix Root Causes คืน completion ที่ render receipt และแก้ output overrides แทนปิด error. Model the Domain ใช้ stage และ pointerDrag เดิม ไม่เพิ่ม state relay. Laziness Protocol ใช้ evaluator/build props เดิม. Sequence Work into Verifiable Units เก็บ baseline, แก้ทีละจุด แล้วรัน checks ก่อนจุดถัดไป.

ข้ามการปรับ tolerance, spatial rules, timer, experiment conditions และ global plugins ที่ไม่มีหลักฐาน. ความเสี่ยงหลักยังเป็น positive rendered workflow, real mechanics/native matrix, missing data และ human approvals.

## Continuation วันที่ 23 ตุลาคม 2026

### Accepted command fixture

Baseline continuation-baseline/accepted ล้มที่ Start. Candidate พบ file sharing และ assumption captures==1 ผิด เพราะ PANEL_PLACEMENT บันทึก snapshot เพิ่มจริง. แก้ reader เป็น FileShare.ReadWrite โดยไม่เปลี่ยน logger. ตรวจหนึ่ง TASK_STARTED และ payload equality แทนห้าม legitimate layout logging. ระหว่าง Inspect ตรวจ evaluator/request ครั้งเดียว และ rejected duplicates ไม่เพิ่ม captures/evaluation/events. Compile CS7036 และ stale-assembly attempts เก็บไว้ ไม่ใช้เป็น product verdict.

Current continuation-clean-scene-retest/accepted/evidence/accepted-commands.json ผ่าน 39 checks. RegistryRoot, logger run_id และ abort/export อยู่ external fixture เดียวกัน. WaitForExports(15000) จบและไม่มี snapshot/export error. COMPLETE.json อยู่ external แต่ queue ยัง queued_for_host ไม่ใช่ host delivery. Seeded Visible Fail/Pass ทดสอบ command contract เท่านั้น. ไม่มี INSPECT_FEEDBACK_VISIBLE/TASK_COMPLETED ปลอม. TASK_ENDED หนึ่งแถวเป็น intentional missing-adapter technical_failure. Component7, snapshot3, tutorial9 และ drag13 rerun ผ่านใน continuation-regression.

### Scene hierarchy

Researcher Display2 หลุดเป็น root ทั้ง actual loaded Scene และ disk. คืน parent ใต้ ResearchFlow_Session โดยคง pose. ลบ generated Dropdown List ที่ค้าง 98 serialized documents. Diff ของ existing documents มีเพียง 4 hierarchy documents. ไม่เปลี่ยน geometry, coordinates, fonts, thresholds หรือ content. Candidate, precommit memory copy และ canonical หลัง Save ตรงทุก byte, SHA256 2d752a4aec90130646eccf95ba2faec9912f3eb10efd4379ca23c2515d2cd693. หลักฐานอยู่ continuation/scene-preservation.

ใช้ [Unity 6000.3 SaveScene saveAsCopy](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SceneManagement.EditorSceneManager.SaveScene.html) รักษา unsaved marker ระหว่างสำรอง. ตรวจจริงว่าดิสก์ไม่เปลี่ยนก่อน review. ผู้ใช้ยืนยันไม่มีงาน Scene ค้าง.

ผู้ใช้เปิด Game View แล้ว. Actual Play Mode focus=True. Production OpenResearcherSetup ยังคืน false เพราะ placement guard ระบุ occluded:Training_Practice_A. Head อยู่ (9.7200,-0.0025,4.9500), panel อยู่ (9.7200,1.5500,4.0500), FOV60. ภาพ actual Game View continuation/scene-preservation/setup-rejection-gameview.png แสดงมุมใกล้พื้น. ไม่ถือว่า hierarchy fix ปิด rendered/native Setup acceptance และไม่ผ่อน placement guard. ออกจาก owned Play Mode โดยไม่เริ่ม Record.

### Branding และ review

เปลี่ยนเฉพาะ build override productName เป็น VR Maintenance Lab. Current product setting และ package เดิมคงอยู่. ชื่อภายใน VR Maintenance Research เดิมถูกต้อง. Built-in image_gen สร้าง icon headset+maintenance gear/tool ขาวฟ้า ไม่มีข้อความ/โลโก้มหาวิทยาลัย. Source1254x1254, Unity import1024x1024. Prompt/provenance/hash อยู่ continuation/branding-provenance.json. ยังไม่มี APK resource หรือ launcher validation.

แก้ readiness ของ saved study ให้ใช้ bounded hash-bound session link ผ่าน shared loader เดียวกับ execution. คง approval checks และ directory override เดิม. Training tolerances dirty เดิม, TEST/PILOT chooser visibility และ station-adjust helper ที่ SaveScene โดยไม่มี dirty guard ยังต้องตรวจต่อ. git diff --check exit2 จาก whitespace ใน dirty เดิม/Unity serialization. เก็บ stdout/stderr/exit ไม่ใช้เป็น green.

### Current rendered Inspect result

ResearchInspectionRenderChecks เป็น Editor-only fixture ใหม่. ใช้ TEST DTO, inactive controller, external exact registry/run และ active production view/camera. Clock เป็น Stopwatch จริง. ไม่มี timestamp offset, Visible seed, receipt field writes หรือ manual acknowledgement. Camera-disabled Ready negative ไม่เกิด terminal events. เปิด camera แล้ว production SRP/coroutine สร้าง receipt จริง. ลำดับ INSPECT/PASS/VISIBLE/COMPLETED/ENDED มีครั้งเดียวและ payload snapshot เดียวกัน. รออย่างน้อยห้า frames โดยยัง Active แล้ว explicit Continue จึงเข้า checkpoint. Cleanup รอ external exports และคืน scenes/cameras. Logger จบ Incomplete เพราะ fixture นี้ไม่ทำ full research session.

Run1 และ Run2 ได้ PASS_SCOPED_RENDERED_INSPECT_COMPONENT. Run2 ลบ stereoTargetEye assignment ที่ URP แจ้งว่าใช้เฉพาะ built-in renderer. ไม่มีการลด production receipt guard. ผล current อยู่ continuation/render-run-2/result.json. Root เปิดภาพจริงทั้งสี่ของ Run1 และภาพ Run2 ก่อนส่งงาน. ภาพ camera-disabled เป็น backbuffer เดิมเหมือน active จึงไม่ใช่ภาพ Ready feedback ใหม่. ภาพ pass_visible กับ checkpoint เป็นภาพ Game View จริงที่แยกกัน. human_seen=null, hardware_verified=false, physical_mechanics_verified=false และ study_approved=false.

### Saved-study readiness fix

ย้าย bounded link-aware loader เดิมจาก SetupView ไป ResearchOperatorGuard. Readiness และ execution ใช้ข้อมูล directory/hash/operator/participant/session/run เดียวกัน. ไม่มี authorization writes หรือการสร้าง approvals ใน production. Optional directory ส่ง existing fixture override ไป readiness ด้วย. ยังคง boolean/approval refs และ byte limit16KiB, strict UTF-8 และ exact links.

Focused exact source-slice B/M/R ใช้38 cases114checks ได้ failures4/0/4. Canonical helper project rerun114 PASS. Negative missing/tampered/foreign-session/run/operator/participant/approval/malformed/oversize ผ่านตาม expected rejection. Schema v1 ไม่มี TTL. อายุของ matching link เพียงอย่างเดียวไม่ทำให้หมดอายุ. ไม่อ้าง time-expiry protection หรือ actual Study resume/approval.

### Latest execution boundary

Fresh Quest transport ยัง authorized, user0, controllers CONNECTED_INACTIVE. Fresh quest_profile.py identity คืน Foreground activity/profile mismatch, exit1 ใน continuation รอบนี้. ค่า exit3 ก่อนหน้าเป็นหลักฐานรอบเดิม ไม่ใช่ผลล่าสุด. ไม่ติดตั้งหรือเปลี่ยน profile. ยังไม่สร้าง release commit หรือ APK. Remaining gates คือ real Training/mechanics, native UI/reader/spatial/data/performance matrix, selected-profile backups/install และ human approvals.

### Preservation audit พบ drift เพิ่ม

Own modified files25 ยังตรง manifest. จาก unrelated baseline-dirty15ไฟล์ มี11ไฟล์ byte-identical และตรวจพบ4ไฟล์ drift หลังจบ Play checks. EN/TH/TrainingV2Thai assets มี dynamic glyph/atlas data เพิ่ม. ProjectSettings ต่างเฉพาะ preloadedAssets สอง refs เป็นว่างและ global icon เปลี่ยนจาก research asset เป็น sample icon. ยังไม่ระบุ writer/สาเหตุแน่นอน. ไม่ overwrite/revert สี่ไฟล์นี้. เก็บ current bytes และ exact baseline ที่ reconstruct จาก HEAD+baseline-dirty.patch แล้วตรวจ SHA256 ตรงเดิมที่ continuation/unrelated-drift และ drift-baseline-reconstruction. Verification assertion ที่ต้องการทุก unrelated file unchanged ล้มจริง จึงระบุ Source Freeze=NOT_FROZEN ไม่ยกเป็น PASS. ต้องตรวจ drift ก่อน commit/build; current productName และ package ยังเดิม.


## P0 Concept B ล่าสุด วันที่ 23 ตุลาคม 2026

สถานะ PARTIAL / NOT_READY / NOT_FROZEN. Goal เดิมยัง active. ไม่มี commit, APK build, install, profile change หรือ Scene Save ใน P0 continuation นี้.

เลือก Concept B Split Card. Task แสดง goal/safety สองคอลัมน์และรายละเอียดเต็มใน bounded inline ScrollRect. ไม่ลด font และไม่เพิ่ม large Details action. Inspect เป็น primary, Reference เป็น secondary. คง Pause ขนาดเล็กเป็นทางออก. Checkpoint 1/2 ไม่มี participant Continue แม้เป็น technical session. Inspect pass/fail มี Continue เดียว. นำ DEV labels ออกจาก participant copy และซ่อน metadata chip ที่ซ้ำและ overflow.

รายการปุ่ม/ข้อความและ advanced paths อยู่ D:\VR_TMU\transaction-verification-external\s1-final-20261023\p0-ui\REMOVED_CONTROLS_TH.md.

### ผลทดสอบที่รันจริง

- Unity6000.3.20f1 compile ผ่าน. ไม่มี Error ใน Console ตอนตรวจหลังรอบสุดท้าย.
- ResearchCompactUIChecks.Run ครั้งสุดท้าย PASS49checks/20stages. ครั้งก่อนพบ MetaChip/Label overflow จริงหลังเปลี่ยน wording. แก้ด้วยการซ่อน chip ที่ซ้ำ ไม่ลด font หรือผ่อน measurement.
- RunSessionStages รอบ2 PASS68directchecks/20stages. ครบสี่ paragraphs, font32, panel size/pose คงเดิม, เลื่อนถึง paragraph สุดท้าย, Refresh/collapse/reopen คงตำแหน่ง, Reference และ workflow ไม่เปลี่ยน. เป็น direct view fixture ไม่ใช่ native pointer proof.
- Localization PASS392keys x3languages. Source font coverage EN79/TH123/JA482. แก้ Thai B phrase spacing และ Japanese movement line break. Human semantic/optical approval ยังไม่เกิด.
- Pause ownership PASS243checks. Workflow hierarchy PASS95assertions. Held-drag continuity PASS327checks. Default-physics full rectangle/button/synthetic-eye clearance baseline FAIL8/11, modified PASS11/11. ไม่เรียก synthetic mono eye offsets ว่า native stereo acceptance.
- Modal placement รุ่นเดิม PASS264cases/1869checks แต่ขาด taskPorts renderer bounds. หลังเพิ่ม parity คืน FAIL จริงที่ Thai/AB/Task1Ready/Computer/eye1.0, occluded_collider:COL_CASE_PANEL_L_Plate. ถอนการใช้ผลเดิมเป็น full-scene pass. ไม่ลด guard.
- Actual Game View core6 เป็น physical P/eye1.65, 28captures, mainclear28/28. Actual Q seated6 เป็น physical Q/eye1.0, 28captures, mainclear17/28. ทั้ง56framesไม่มี glyph overflow/autosize/own-rect issue และไม่มี visible DEV label ใน texts ที่ capture. Camera FOV60 และ environment469renderersยังอยู่. นี่คือ seeded view-only rendering ไม่ใช่ flow completion, optics หรือ Quest evidence.
- Runtime Q test คืน P แล้ว. Ten option poses เทียบ exact strings ตรงเดิมและ AuthoredBindingsValid=True. Unity หยุด Play. Scene disk SHA256 ยัง 2d752a4aec90130646eccf95ba2faec9912f3eb10efd4379ca23c2515d2cd693 และไม่บันทึก dirty Scene.
- Quest3 transport เชื่อมต่อ. Controller ซ้าย/ขวารายงาน SEARCHING และ tracking ORIENTATION ในการอ่านครั้งนี้. ไม่มี APK รุ่น UI นี้ติดตั้งหรือทดสอบ native buttons. รายละเอียดอยู่ D:\VR_TMU\transaction-verification-external\s1-final-20261023\p0-ui\quest-readiness-current.json.

### Before/after ที่ตรวจเปิดภาพแล้ว

ก่อนแก้เป็น actual SDK Editor Task1Active A ไม่ใช่ concept. ภาพหลังเป็น actual seeded view-only Game View. ตำแหน่ง camera/flow acquisition ไม่ใช่ pixel-matched A/B และไม่ใช่ native headset screenshots.

![ก่อนแก้](D:/VR_TMU/transaction-verification-external/s1-final-20261023/next-unit/sdk-th-ab-p-standing/flow-captures/048_Task1Active_A.png)

![Concept B หลังแก้](D:/VR_TMU/transaction-verification-external/s1-final-20261023/p0-ui/actual-core6/Thai_A_active_DEV_eye1.65_P.png)

ภาพ Task B ล่าสุด, Japanese checkpoint และ Inspect อยู่ D:\VR_TMU\transaction-verification-external\s1-final-20261023\p0-ui\actual-core6. Per-frame measurements, physical layout identity และ blocked list อยู่ D:\VR_TMU\transaction-verification-external\s1-final-20261023\p0-ui\render-summary-current.json.

### ข้อผิดพลาดที่ยังต้องปิด

Seated Q ยังซ่อน main panels11/28, รวม TaskA/Pause/Checkpoint1. ไม่แสดงแผงผ่านวัตถุ ไม่ปิด table/collider ไม่ขยับ XR Origin และไม่แกล้งเพิ่ม FOV. Static Computer_TaskCase/Fan_TaskFan activation ต่างจาก Physical TEST และ Examine ยังชี้ legacy colliders. A/B/A ชั่วคราวปิดเฉพาะ duplicate Computer_TaskCase แล้วคืนเดิมยังไม่แก้ whole-panel failure จึงไม่ถือเป็น sole root cause และไม่ได้บันทึกการเปลี่ยนนี้.

Actual SDK TaskA yaw5degrees ที่ถูก collision reject จากรอบก่อนยังเป็น mechanics failure. Native controller interaction, Details drag/scrollbar ทั้งสองมือ, reader/media matrix, physical P/Q x eye-height matrix, wearer optical review และ study/language approvals ยังไม่ผ่าน. Full release goal ไม่ complete.

ใช้ Fix Root Causes ย้ายการขยายข้อความออกจาก card height. ใช้ Laziness Protocol เก็บ rows เดิมและใช้ ScrollRect/RectMask2D ที่ติดตั้งแล้ว. ใช้ Prove It Works แยกการรันจริง, fixture, native และ wearer ออกจากกัน.
