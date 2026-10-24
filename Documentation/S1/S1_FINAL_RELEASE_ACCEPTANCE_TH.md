# S1 — สถานะตรวจรับปัจจุบัน 24 ตุลาคม 2026

## สถานะล่าสุด 2026-10-24 — Movement / hidden slate capture / teardown

**Final Verdict: NOT_READY — Engineering Verification เท่านั้น ไม่ใช่ Technical RC ที่ผ่านครบ**
**Study Verdict: NOT_READY**

Canonical local `main` เริ่มรอบนี้ที่ `d7d23df1001ae3f770a1dc55eb30cbb464e14912` พร้อมงานค้าง13ไฟล์ที่ hash ตรง manifest เดิมครบ13/13 ไม่ reset/clean/stash และไม่เปลี่ยน Task Rules/QC thresholds/Study approval.
Source freeze commit, APK/installed hashes และผลติดตั้งที่ตรวจจริงให้อ่านจาก `D:/VR_TMU/transaction-verification-external/s1-movement-20261024/` โดยเฉพาะ `SOURCE_FREEZE.json`, `delivery/README_TH.md`, `VERIFICATION.txt` และ `COVERAGE_MATRIX.json` ไม่ใช้ APK เก่าเป็นหลักฐาน Source ปัจจุบัน.

### Root causes ที่แก้และตรวจแล้ว
- `ResearchSlateFocus.Update`: Canvas ที่ถูกซ่อนไม่ได้รับ PointerUp แต่ MonoBehaviour ยัง enabled ทำให้ held capture ค้างและขวาง Inspect Feedback. ยกเลิกเฉพาะ capture เมื่อ enclosing Canvas disabled; visible hold/exit และ physical/drag/clearance guards คงเดิม. Baseline cancelledHidden=false; regression cancelledHidden=true และ visible/exit/release ผ่าน. Actual TH AB P/Q แสดง Inspect Feedback และจบ workflow ได้จริง.
- `ResearchS1FlowDriver.ExamineCurrentTaskRoutine`: ray ของ actor ที่ระดับตา1.0m ชนขอบโต๊ะก่อน workpieceจริง. ยกมือ SDK จริงและเล็ง collider ของชิ้นงานจริง ไม่ปิด collider หรือย้าย XR Origin.
- `ResearchS1FlowDriver.Abort/AbortSaved`: ปิด DEV session แล้วแต่ retained BLOCKED_FOCUS routine ยังถือ owner. เพิ่ม teardown finally ที่ยกเลิก Tick/ล้าง routine; ยังปฏิเสธ Study record และไม่กลืน production abort exception. Fixture และ actual failed-trial cleanup ได้ status Aborted.
- `ResearchFocusedOperatorUIChecks`: เพิ่ม regression สองจุดข้างต้น ไม่ลด assertion เดิม.

### Movement QC — แยก dataset เก่าที่ FAIL จากข้อมูลใหม่
`conditions/Thai_BA_P/matrix-v1` = **PASS_EDITOR**: complete bound SDK workflow → export → actual host ingest/verify; movement_complete=true, data_complete=true, s1_analysis_complete=true, raw unchanged, verify exit0. `host-qc-result.json` ระบุ input hashes และ bundle จริง. เป็น simulated technical DEV data ไม่ใช่ native/wearer/participant acceptance.
Cold/warm-up datasets เดิมที่มี gaps ยังคง FAIL ไม่แก้ raw หรือผ่อน threshold. Profiler เฟรม1683 พบ Main Thread428.213ms, bridge417.061ms, Mono.JIT377.335ms ตรง acquisition gap433.9764ms; sampler due-work ในรอบก่อนสูงสุด22.1513ms. ปัญหาหลักที่ยืนยันได้คือ cold Editor actor JIT ไม่ใช่หลักฐาน writer ทำข้อมูลหล่น. โค้ด precompile สองวิธีที่ไม่ช่วยถูกถอดออก; `ResearchFlowPlayChecks.cs` กลับตรง original byte-for-byte.

### Coverage ที่ยังไม่ผ่านครบ
- 8 condition cells: TH BA P ผ่าน full workflow+Host QC; TH AB P/Q มี full bound workflow แต่ measured coverage ของชุดครบ8ยังไม่ผ่าน. Cell อื่นถูกขัดจังหวะด้วย foreground loss/minimized Unity หรือยังไม่ได้รันครบ. Passive trace จับ Explorer ForegroundStaging; native rectangle ของ canonical Unity อยู่ตำแหน่ง minimized -32000. Guard พักถูกต้อง ไม่ auto-resume/วนแย่ง focus/ปิด Guardian.
- TH A/B×4formats ผ่าน actual SDK core tour. `reader-regression/reader-production.json` ผ่าน438 fixture rows/28mono captures สำหรับ production-path A/B TH/JA. Fixture ใช้ pointer/ScrollRect callbacks; ไม่ใช่ native acquisition หรือ video decoded-playback approval. JA SDK tour และ replay/focus/error lifecycle บางส่วนยัง BLOCKED/NOT_TESTED.
- `placement-regression`:16/16 PASS_EDITOR; ไม่ใช่ all16 spatial/stereo/native acceptance. Human seated/standing/reach/Quest controller ยังไม่ผ่าน.
- XR startup ถูกคืน TRUE; asset และ canonical scene clean หลังหยุด owned SDK input/Play. ไม่ save/discard scene ของผู้อื่น.

Engineering APK ที่อาจสร้างจาก source freeze มีไว้ตรวจ installation/cold launch เท่านั้น. Build success ไม่ยกระดับ Gate D/E/G/H เป็น PASS และไม่เปิด Study gate. ไม่มีข้อมูลวิจัย/consent/raw video ถูก push.

### การตรวจที่ยังต้องทำ
1. ทำ Unity ให้ visible/unminimized และรักษา Game View foreground แล้วรันเฉพาะ missing conditions จาก manifest ด้วย directory ใหม่; ไม่ overwrite failed evidence และไม่ใช้ force completion.
2. ตรวจ JA reader playback/remaining lifecycle, full standing/seated stereo/ray/reach matrix และ remaining negative paths.
3. ตรวจ native Touch Plus, fresh native movement/export/Host QC, wearer และ performance โดยไม่ข้าม Guardian.
4. TH/JA human content review, researcher/protocol/ethics/consent sign-off ก่อน Study.

## หลักฐานรอบก่อน — historical เท่านั้น
ข้อความต่อจากนี้เป็นสถานะก่อน continuation นี้ ให้ใช้สถานะล่าสุดและ manifest ข้างบนตัดสิน ไม่รวมจำนวน PASS เป็นการรับรอง hardware/Study.


**Technical verdict: NOT_READY · Study verdict: NOT_READY**

Source: `main`, HEAD `d7d23df1001ae3f770a1dc55eb30cbb464e14912` + งานแก้ที่ยังอยู่ใน working tree

หลักฐานปัจจุบัน: `D:/VR_TMU/transaction-verification-external/s1-release-20261024`

Build/Install รุ่นใหม่: **NOT_TESTED** — Gate C–F ยังไม่ครบ จึงยังไม่มี final source freeze หรือ APK รุ่นใหม่จากชุดนี้ APK SHA256 ที่รายงานในหัวข้อเก่าเป็นรุ่นก่อนแก้ First-launch ไม่ใช่ APK ของ working tree ปัจจุบัน

## Acceptance matrix ปัจจุบัน

| Gate | ผลจริง | หลักฐาน / สิ่งที่ยังค้าง |
|---|---|---|
| A Source/Build | PASS_EDITOR เฉพาะ compilation/targeted checks; final Build NOT_TESTED | Source เดิมเก็บครบ, scene contract exit0, canonical scene clean; commit/freeze/new APK/signing/install hash ยังค้าง |
| B UX/UI | PASS_EDITOR เฉพาะส่วนที่ตรวจ | Setup SDK ray/trigger และ Welcome render, safe Resume identity 3/3; UI checks 64/64, Reader render 28 states, anchor regression 16/16; TH/JA semantic approval ยังรอคน |
| C Mechanics | PASS_EDITOR แบบจำกัด; full matrix BLOCKED | `Thai_AB_P/verified` ทำ A(S02)/B และ FX1 ผ่าน actual bound SDK simulation; seated Lift/Rotate/Release และ FX1 cable connect/disconnect มีหลักฐานเพิ่ม; ทั้ง 8 cells ยังไม่ผ่านครบ |
| D Workflow | PASS_EDITOR หนึ่ง DEV E2E; coverage ที่เหลือ NOT_TESTED/BLOCKED | Task1→Inspect→Checkpoint→Task2→Completion→Export ผ่านหนึ่งรอบ; มี initial B fail/retry, pause/stop/tracking recovery; comprehensive normal/error matrix ยังไม่ครบ |
| E Media | PASS_EDITOR asset integrity/render; interaction coverage ยังไม่ครบ | 16 task-format cells + 8 Training cells ตรวจ resource/hash/text, MP4 6 ตัว decodeผ่าน; B_TH มี playback/seek/pause/replay จริงใน Editor; runtime lifecycle ของชุดอื่นยังค้าง; ทุก package ยังเป็น ENGINEERING_DRAFT |
| F Research data | **FAIL** | completed DEV export นำเข้า/verify hash ได้, global sequence ไม่มีช่องว่าง, S1 analysis complete; movement coverage ขาดช่วงเกิน 0.25s จึง data_complete=false; raw ไม่ถูกแก้ |
| G Quest hardware | **BLOCKED** | `adb-final-devices.txt` มีเพียง `List of devices attached`; native controller/installed-current hash/standing-seated/performance/wearer ยังไม่รับรอง |
| H Human study | **BLOCKED** | bilingual language review, researcher/protocol/ethics/consent approvals ยังเป็น human gates; ไม่มีการเปิด Study approval เอง |

## Root causes และการแก้เฉพาะจุด

| ประเด็น | ก่อน / root cause | เปลี่ยนไฟล์และพฤติกรรม | หลัง / regression |
|---|---|---|---|
| Working slate ย้ายเองเมื่อถูกบัง | late retry, EnsureClear และ RestorePose เลือก anchor ใหม่หลังยอมรับตำแหน่งแล้ว | `ResearchPanelMotion.cs`, blocked Reader reopen ใน `ResearchFlowView.cs`, fixture `ResearchPanelClearanceP0Checks.cs`: คง accepted working pose แต่ยังซ่อน Canvas/raycast เมื่อ guard ปฏิเสธ; explicit Recall ยังตรวจ clearance | `anchor-baseline`, `anchor-refresh-baseline` มี FAIL; `anchor-final/result.json` 16/16 PASS_EDITOR; guards/colliders/XR Origin คงเดิม |
| Reader metadata ตัดอักษร | rect สูง26 แต่ TH/JA preferred height มากกว่า30 | `ResearchFlowView.cs`: HeaderMetadata rect26→34 ทั้ง Build/live path; font/center/content เดิม | `reader-production` FAIL28; `reader-final/reader-production.json` PASS28 actual rendered fixture states |
| Seated Lift ทำถูกแต่ไม่ได้เครดิต | วัตถุที่กำลังยกบังคำสั่งที่เคยแสดงแล้ว ทำให้ epoch ถูก invalidate | Controller ใช้ shared `TutorialInstructionAvailable`; View `TutorialHeldActionOccluded` จำกัด **move เท่านั้น**, ต้องเป็น fact เดิมที่ armed แล้ว, object ยัง held, placement blocked และ content/alpha/parent ยัง active; initial arm, safety/tracking/logger/epoch/physical threshold เดิม | ก่อน `seated-training-move-repro.json` ยก0.18277mแต่ค้าง; หลัง raw `heldforeground`, `seatedready`, `liftdata` ให้ Lift ครั้งเดียว; Rotate ที่ยังไม่แสดงยังไม่ armed; focused UI64 checksผ่าน |
| Training actor pinch ไปโดน UI | หลังขันสกรู Ray ค้างบน ParticipantPanel; trigger จึงถูก UI-priority guard กัน grab | `ResearchS1FlowDriver.Acquire`: ใช้ pivot ข้อมือหลบ UI แบบเดียวกับ Task A runner โดยรักษา pinch point; ไม่แก้ collider/grab tolerance | `pinchdata` steps ระบุ `pinch ray over UI before TrainingLeadHead`; จากนั้น actual socket Guided/Seat และ disconnect Empty ผ่าน; ชุดนี้ภายหลัง BLOCKED_FOCUS จึงไม่ใช่ full E2E PASS |
| B runner รอ terminal state ก่อนส่ง overshoot | progress ใกล้0 แต่ยังเหลือ6องศาตามแผน | `ResearchS1TaskBSdkRunner`: รอ terminal state เมื่อส่งส่วนที่เหลือหมดจริง; production kinematics เดิม | full bound B ใน `Thai_AB_P/verified` ผ่าน; ไม่ขยาย tolerance/ให้ state credit |
| Setup blocker หลุดอังกฤษ / legacy labels | canonical approval reason ไม่มี localized mapping; scope textยังแสดง TEST/PILOT | `ResearcherSetupView`, `ResearchStrings`: localized reason, scopeบอกเฉพาะ DEV; Registry compatibility/stable IDs/approval guardเดิม | localization393×3ผ่าน; UI semantic reviewยังรอมนุษย์ |
| Gallery DEV fixture เลือกกลุ่มผิด | fixture คาด DEV แต่ค่าเริ่มต้นใหม่เป็นกลุ่มไทย | `ResearcherLocalizationChecks`: เลือก DEV ใน owned display fixture อย่างชัดเจน; visibility assertionsเดิม | ก่อน Thai gallery FAIL; หลัง TH/JA galleryผ่านและเปิดภาพตรวจแล้ว; ไม่ใช่ native interaction |

First-launch patch เดิมยังคงอยู่: Researcher Setup เป็นทางเข้าหลัก, ไม่ dispatch TechnicalSetup เป็น default, popup/display0, one main Start, study Auto balancing, identity freeze และ researcher checkpoint guards คงเดิม

## Coverage และข้อมูลที่ต้องตีความตรงตาม Contract

- `conditions/Thai_AB_P/verified`: **PASS_EDITOR workflow/mechanics เท่านั้น**; Host QC **FAIL movement**. ห้ามนำจำนวน checks มาประกาศ Technical Ready
- Thai_AB_Q: basic handling และ FX1 ส่วนที่ระบุมี actual input evidence; full E2E **BLOCKED**. อีก6 condition cells **NOT_TESTED**. `condition-matrix-clean8*-results.json` เป็น precondition/foreground failures ไม่ใช่8ผลผ่าน
- Standing/seated render เก่า112 states มีอยู่ในชุด continue; delta accepted-pose มี targeted16 checks แต่ full stereo/native spatial matrix หลังแก้ยังค้าง
- `media-integrity-matrix.json`: 24 cells เป็น **asset integrity only**, ไม่ใช่การกด/scroll/video lifecycleครบ24ครั้งและไม่ใช่ content approval
- H1 A เป็น `action_first` แม้มี Examine ก่อนได้ตาม Locked TaskSpec §14: Examine แยกจาก Reference ใน denominator. ห้ามแปลว่าไม่มีการหาข้อมูลเลย หรือแก้ derived label ให้ดูผ่าน
- H1–H4 ใช้ rawจริงของ DEV simulation; head/left/right เกิด gapพร้อมกันระหว่าง frame_id ต่อเนื่อง. มีสอง B gaps คร่อม screenshot request→written; สาเหตุทุก gap ยังไม่ยืนยัน จึงคง FAIL โดยไม่เติมแถว/ลด threshold
- Managed tracking FaultTour เป็น PASS_EDITOR; OpenXR/native tracking loss, Touch Plus click, actual wearer และ Guardian acceptance ยังไม่ผ่าน

## Environment checkpoint / งานถัดไป

Unity หยุด Play, canonical scene `isDirty=false`, `XRGeneralSettings.InitManagerOnStart=true`, XR asset dirty=false หลังคืนค่าจาก managed simulation scope (`final-editor-restored.json`). ไม่มีการ Save/Discard scene, ปิด Guardian, เปลี่ยน Android user, Clear Data หรือ Uninstall

ชุด clean data run ถูกบล็อกที่การได้ real Game View foreground (`Measured Game View point is covered` / native focus acquisition); ไม่มีการแก้ `Application.isFocused` หรือปิด focus guard เพื่อทำ PASS. รอบถัดไปต้องให้ Unity Game View มี focusจริงและกันกิจกรรมควบคุม desktop อื่นออกจากช่วงวัด แล้วรัน fresh attempt ผ่าน `run_clean_matrix.py`; ตรวจ Host QC ก่อน build

ลำดับที่ยังต้องปิด: clean 8-condition E2E + movement/profiler attribution → full spatial/native input → remaining Reader/Training lifecycle/error paths → reviewed freeze/commit/build → fresh Quest binding/backup/install/hash/native acceptance → human approvals

เอกสารด้านล่างเป็นหลักฐานย้อนหลัง ต้องใช้ขอบเขตและสถานะปัจจุบันข้างบนตัดสินความพร้อม

---

# First-launch P0 — 23 ตุลาคม 2026 (หลังชุดติดตั้งเดิม)

Source patch นี้ยังไม่ Build/Install และไม่ใช่ Technical Ready / Study Ready.
หลักฐาน: `D:/VR_TMU/transaction-verification-external/s1-first-launch-20261023`

| รายการ | สถานะ | หลักฐานและขอบเขต |
|---|---|---|
| เปิด Researcher Setup แทน DEV fallback | PASS_EDITOR | `fresh-entry-settled.json`, `fresh-thai.png`; actual Editor Play + Meta XR Simulator |
| เปลี่ยนกลุ่มเป็นญี่ปุ่น / ภาษาอัตโนมัติ | PASS_EDITOR | `japanese.json`, `fresh-japanese.png`; UI Toggle callback ไม่ใช่ native controller click หรือ Japanese cold launch แยกครั้ง |
| Dropdown popup / blocker Display 0 | PASS_EDITOR | `group-popup.json`, `id-popup.json`, `order-popup.json`; actual generated popups; Language popup ตรวจด้วย MCP |
| Auto balancing / idle Back | PASS_EDITOR | `balancing-back.json`: DEV cell 4 กลับเป็น study Auto 0; idle Back ไม่คืน Technical Setup |
| Start → TutorialWelcome | PASS_EDITOR | `start-callback.json`; callback บน isolated DEV registry เท่านั้น; ภาพหลัง Start ทันทีเป็นสีดำระหว่าง transition จึงยังไม่รับรอง Welcome rendered |
| Focused regression / core | PASS_EDITOR | `focused-dropdown/operator-ui.json` 43 checks; `core-final.txt` 163/163 pure state/disk checks |
| Tracking Not Ready | NOT_TESTED | `tracking-not-ready.json`: ปิด active controllers ผ่าน Simulator แล้ว DeviceReady ยัง true; ยังสร้าง tracking-loss ที่ยืนยันได้ไม่สำเร็จ |
| Quest cold launch / native click / Resume / wearer | BLOCKED | Quest user 0 ยังอยู่ Guardian; ไม่มี native acceptance ของ patch นี้; Resume และ wearer ยังไม่ทดสอบ |

Root causes: startup coroutine ถูกจำกัดเฉพาะ Android, idle setup dispatch ไป DEV card, mounted child Canvas ถูกปิด, popup template ถูกปิดและ custom dropdown hardcode Display 2, serialized balancing options มีเพียง 3 แทน 5.
ยังคง Start/Consent/Readiness/Study gates เดิมและไม่แก้ Session ID, task rules หรือข้อมูลวิจัยเดิม.

# สถานะชุดติดตั้งก่อนหน้า — 23 ตุลาคม 2026

**ติดตั้ง engineering DEV แล้ว แต่ยังไม่ Technical Ready / Study Ready**

หลักฐาน: `D:/VR_TMU/transaction-verification-external/s1-continue-20261023`
ส่วน Historical ด้านล่างเป็นหลักฐานเก่า ไม่ใช่การรับรองรุ่นปัจจุบัน

| รายการ | สถานะ | หลักฐานและขอบเขต |
|---|---|---|
| Legacy / Examine | PASS_EDITOR | A.module → ServicePivot (31 nontrigger colliders), B.module → ResearchS1TaskB (11); ปิด legacy สองตัว; stable IDs เดิม |
| Spatial TH/JA × P/Q × eye 1.0/1.65 | PASS_EDITOR | delivery-* 112/112 main clearance/readability; 216 active button rectangles อยู่ใน mono viewport; ไม่มี text overflow/glyph-bound flags; ไม่ใช่ stereo/wearer acceptance |
| Compact single-focus / Details | PASS_EDITOR | neutral-actions-compiled/session-stages.json: 72 checks; Reference/Inspect เป็น Secondary เท่ากัน; Details คงเนื้อหาครบและไม่ย้ายแผง |
| Localization | PASS_EDITOR | localization-delivery.txt: 392 keys × 3 ภาษา; ยังไม่ใช่ human language approval |
| Core / approval | PASS_EDITOR | core-delivery.txt 163/163; approval-delivery-corrected.txt 38 cases/114 checks; เป็น pure state/disk tests; study gate ยังปิด |
| FX1 dropped recovery | PASS_EDITOR | training-recovery-results.json: 4 กรณีผ่านใน cloned stopped-Editor fixture; ของตกเร็วถูกกู้ ของเคลื่อนด้านข้างไม่ถูกดึงกลับก่อน dwell; ไม่ใช่ native grip/physics acceptance |
| Simulator OpenXR | PASS_EDITOR | simulator-unity.json, simulator-runtime.json, simulator-game.png: active display, Head + Touch Plus สองข้าง; ยังไม่ผ่าน full controller sequence |
| Fixed-position transient occlusion | FAIL | inherited clearance_retry อาจเลือก anchor ใหม่หลังถูกบังชั่วคราว; ต้องรักษา clearance guard และตรวจ recovery ตาม policy |
| Full Training / Task A/B / Controller | NOT_TESTED | ยังไม่มี current-build full native/wearer acceptance |
| Reader / H1–H4 / Movement / Host QC | NOT_TESTED | ยังไม่มี current-build end-to-end dataset acceptance; backup hash ไม่ใช่ research QC |
| APK replacement install | PASS_QUEST_NATIVE | install-result.json และ installed-recheck.json: exit 0 และ local/installed SHA256 ตรงกัน; รับรองเฉพาะการติดตั้ง |
| Native cold launch | BLOCKED | native-coldlaunch-gate.json: Guardian ครอง focus และไม่มี app main process; ไม่ปิด boundary และไม่สรุปว่า product crash |
| Data preservation | PASS_QUEST_NATIVE | postinstall-data-check.json: Registry/Logs/Exports 112 ไฟล์ รวม 3,067,709,449 bytes มี hash เดิมหลังติดตั้ง ก่อน launch |
| Human language / protocol / ethics / wearer | NOT_TESTED | ผู้ใช้แจ้งไม่อยู่ทดสอบ; Simulator ไม่แทนผู้สวม |

## สิ่งที่แก้และสิ่งที่รักษา

แก้ legacy ซ้อนและ Examine binding, เพิ่ม elevated seated computer fallback, ลด split-card เป็น single-focus พร้อม inline Details, ทำ Reference/Inspect ให้มีน้ำหนักเท่ากัน และแยก fallen recovery ออกจากเงื่อนไข settled velocity

ไม่เปลี่ยน Task evaluator, stable IDs, Inspect timing, H1–H4, furniture colliders หรือ XR Origin ข้อกำหนดเต็มรวม OS exclusion ยังอยู่ใน Details

Glossary รอ human review: Reference = เอกสารอ้างอิง / 参照資料; Inspect = ตรวจผล / 検査; Examine = ดูชิ้นงาน / 部品を見る; Pause = พัก / 一時停止; Continue = ทำต่อ / 続ける
Examine คือการดูวัตถุ ส่วน Inspect คือการขอตรวจผล ไม่เปลี่ยน event semantics ภาษาไทย/ญี่ปุ่นยังรอ semantic approval จากมนุษย์

## APK ที่ติดตั้ง

- Source commit: `ff797b7caad04906c83af17c32bfb4a004371dc1` บน main; ไม่มี push
- APK: `Logs/QuestFinalQA/build/ResearchFlowQuest3_20261023_144418.apk`
- Local APK = installed base.apk SHA256: `034627064f86a8496b3613ac4943dab12052512dae8e1ded3169edf51ed4ad95`
- Package: `th.ac.tmu.vr.researchflow.quest3.technical`; label: `VR Maintenance Lab`; Activity: `com.unity3d.player.UnityPlayerGameActivity`
- ตรวจภาพ icon ที่ extract จาก APK แล้ว เป็นขาว–ฟ้า
- Android debug certificate SHA256: `1028ed54c1dce73aed7ba0994dbd9eb5f19d60e5c299fc4416f88bb46ba6b542`; ตรงกับ APK เดิม; apksigner exit 0
- Unity build สำเร็จ: 0 errors, 362 warnings, 487.14 seconds; warnings ยังอยู่ใน build log และไม่ถือว่า full acceptance
- ใช้ `adb install -r --user 0`; ไม่ uninstall/clear และไม่เปิด session user 10; ข้อมูล user 10 ยังไม่ได้ตรวจ hash แยก ทั้งสอง profile ยัง installed/stopped ณ final check
- สำรอง APK เดิมและ private tar ไว้ในเครื่อง Registry/Logs/Exports ตรวจ hash ครบ ส่วน all-files copy ที่หยุดกลางทางไม่ถูกนับเป็น backup ที่ผ่าน
- Build ทำให้ dynamic TMP caches ถูกล้างและบันทึก temporary settings จึงสำรอง generated state ไว้ใน `build-generated/` แล้วคืนเฉพาะ 5 ไฟล์ที่ Build เปลี่ยนจาก frozen commit; scene byte comparison เหมือนเดิม
- Simulator 207.1 ติดตั้งแบบ per-user และทดสอบ Unity OpenXR แล้ว ค่า `XR_RUNTIME_JSON` ระดับ process คืนเป็น null; native DLL อาจยัง cache runtime จน restart Editor ไม่เปลี่ยน global OpenXR runtime
- SDK synthetic flow fixture ไม่เริ่ม เพราะ OpenXR มี ownership ของ input อยู่แล้ว ไม่ลด guard เพื่อให้ทดสอบผ่าน
- Hook failure เดิม: NOT_TESTED; pstack hook ครั้งนี้ exit 0 และไม่มี hook ถูกปิด

## จุดทำต่อ

ยืนยัน Guardian บน Quest ก่อน แล้ว bind serial/user/package/PID/boot/build ใหม่สำหรับ cold-launch/native tests แก้และทดสอบ fixed-panel temporary-occlusion recovery โดยไม่ปิด clearance จากนั้นตรวจ Training/Task A/B, reader/lifecycle, timing/feedback receipt, movement และ Host QC ให้ครบก่อน Technical Ready และผ่าน human language/protocol/ethics/wearer ก่อน Study Ready

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
