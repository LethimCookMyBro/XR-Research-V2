# รายงานรอบ S1 MASTER (17–18 ต.ค. 2026)

เอกสารนี้ตอบ `S1_ALL_WORKFLOW_UX_UI_MASTER_20261017_TH.md` ข้อ 15–16 ทุกผลในนี้มาจาก Unity Editor และ PC เท่านั้น **ยังไม่ได้ทดสอบบน Quest และยังไม่มีคนสวม** จึงยังไม่พร้อมใช้กับผู้เข้าร่วมจริง

## 1. ตัวตนของ Source และ APK

| รายการ | ค่า |
|---|---|
| Commit ของ source | `063c106` บน `main` (ต่อจาก `aedd4c8`, `f5ea522`, `e2f9bb4`) |
| APK (ตัวล่าสุด ใช้ตัวนี้) | `Logs/QuestFinalQA/build/ResearchFlowQuest3_20261020_040226.apk` |
| SHA256 | `407c51a78f759654c9da8233edcb1ed38b1ca7fea320afe122df59140643490f` |
| ขนาด | 307,435,337 bytes |
| Build | สำเร็จ, error 0, warning 359 |
| ผูกกับ source | `S1_Unity_PC_20261011/master-20261018/build_063c106/apk-binding.json` (ไฟล์ที่ยังไม่ commit = 0, drift หลัง restore = 0) |
| ไฟล์ที่ build เขียนทับแล้วคืนค่าแล้ว | `ProjectSettings.asset` และ font atlas 4 ไฟล์ หลังจากนั้น git สะอาด |
| APK ตัวก่อน (แทนแล้ว) | `ResearchFlowQuest3_20261018_114649.apk` จาก `aedd4c8` (`67ad621a…22ba`) และ `ResearchFlowQuest3_20261018_072028.apk` จาก `f5ea522` (`6cbe479b…8168`) |
| Package / label | `th.ac.tmu.vr.researchflow.quest3.technical`, label "ResearchFlow Quest3 Technical" และ icon อยู่ทั้งที่ application และ launchable-activity (`aapt2 dump badging`) |
| ติดตั้งบน Quest | **ติดตั้งแล้ว 20 ต.ค. 17:15** ด้วย `install -r` หลังตรวจว่าแอปไม่ได้เปิดอยู่ SHA256 ของ base.apk บนเครื่องตรงกับข้างบน และ registry/protocol-versions มี hash เท่าเดิมก่อนและหลังติดตั้ง (สำรองไว้ที่ `S1_Unity_PC_20261011/QuestInstall_20261020/backup_before`) เปิดแอปครั้งแรก: log มี `E Unity` 0 บรรทัด ไม่มี crash/ANR และสถานะเป็นหน้า Setup ของ DEV (ศีรษะ tracked, พื้นและ logger พร้อม, คอนโทรลเลอร์ยังไม่ถูกหยิบจึงรออยู่) **ยังไม่มีคนสวมทดสอบ** |

ที่มาของ warning (อนุมาน): ส่วนใหญ่เป็น shader variant ของ package Unity Sentis (`ConvGeneric`) และ C# warning ประเภท CS0618 (API เก่า) กับ CS0414 (field ไม่ถูกใช้) ไม่มีจากโค้ด ResearchFlow ที่ทำให้ build ล้ม

## 2. งานที่แก้ (ปัญหา → สาเหตุ → การแก้ → หลักฐาน)

| ID | ปัญหาที่ผู้ใช้เห็น | สาเหตุ | การแก้ | หลักฐาน / สถานะ |
|---|---|---|---|---|
| P0-B1 | ป้าย 準備完了 ในหน้า Details ภาษาญี่ปุ่นแตกเป็นสองบรรทัดทับแถวถัดไป | ป้ายแคบเกินและห่อบรรทัดได้ ส่วน check เดิมวัดทีละป้ายจึงไม่เห็นป้ายชนกัน | การ์ด Setup ใหม่ของรอบก่อนแก้ขนาดป้ายแล้ว รอบนี้เพิ่มการตรวจกรอบตัวอักษรที่แสดงจริงแบบจับคู่ใน `ResearchCompactUIMeasure.cs` | ภาพ `before_after/01_setup_details_ja.jpg` และ mutation test: เมื่อใส่ปัญหาเดิมกลับ check ฟ้อง "Status/Label / Status/Label: text overlaps text" — ผ่านใน Editor |
| P0-B2 | หน้าพักมีทั้ง "ทำต่อ" และ "หยุด" (PlayCheck "Eligible pause exposes Continue only" ล้ม) | กฎซ่อน Stop มีสองชุด ชุดใน `RefreshRecoveryUtility` เปิด Stop กลับมาหลัง layout | รวมเป็นกฎเดียว `StopOffered` คือ ซ่อน Stop เฉพาะตอนผู้เข้าร่วมกดพักเองและกลับมาเองได้ ส่วนการพักจาก tracking หรือ app ยังมี Stop เพราะปุ่ม "ทำต่อ" ยังรอให้อุปกรณ์พร้อม และหน้าพักที่อยู่นอกสายตายังมีแถบเรียกแผงกลับ | PlayCheck TH/JA ผ่านขั้นนี้ ภาพ `05_pause_ja.jpg` — ผ่านใน Editor |
| P0-B3 | หน้ายืนยันจับเวลาทำให้ "ย้อนกลับ" เป็นปุ่มสีน้ำเงินหลัก | ปุ่มแรกถูกกำหนดเป็น primary เสมอ | "เริ่มจับเวลา" เป็นปุ่มหลัก "ย้อนกลับ" เป็นปุ่มรองและอยู่ที่เดิม | `03_timer_confirm_ja.jpg`, `04_timer_confirm_th.jpg` — ผ่านใน Editor |
| P0-B4 | ข้อความพักภาษาญี่ปุ่นเหลือ 「い。」 บรรทัดเดียว และคำว่า 研究者 ถูกตัดกลางคำ | ประโยคแรกซ้ำหัวข้อ และไม่มีจุดตัดบรรทัดตามวลี | ตัดประโยคที่ซ้ำ แล้วตัดบรรทัดตรงรอยต่อวลี (ข้อความภาษาไทยไม่เปลี่ยน) | `05_pause_ja.jpg` — ผ่านใน Editor, **รอผู้ตรวจภาษา** |
| P0-B5 | ใน Training ข้อความ "ผ่านแล้ว" อยู่ใต้ปุ่มซูม PDF (−, 100%, +) | สองส่วนใช้ช่องเดียวกันใต้แท็บ | ย้ายแถวซูมไปท้ายช่องแสดงผล ปุ่ม 100% กว้าง 88 px และไม่ห่อบรรทัด ส่วน PlayCheck ตรวจการชนของข้อความกับปุ่มทุกภาพของ Reader | `06_training_reader_feedback_vs_zoom.jpg` และ mutation: เมื่อย้ายแถวซูมกลับที่เดิม check ฟ้อง — ผ่านใน Editor |
| P0-C1 | Training ให้เครดิต Examine เมื่อจับโมดูลขึ้นมา (โค้ดของรอบก่อนที่ยังไม่ commit) | ใน Task จะบันทึก EXAMINE จากปุ่ม Examine เท่านั้น | เอาทางนี้ออกก่อน commit ไฟล์ `ResearchTrainingStation.cs` จึงเท่ากับ HEAD คือให้เครดิตจากปุ่มอย่างเดียว และโมดูลล็อกจนถึงขั้นใส่โมดูลเหมือนเดิม | การวิ่ง Training ทั้งชุดด้วย SDK ผ่าน — ผ่านใน Editor |
| P0-C2 | ปุ่ม Examine ใน Task อาจไปทับแผงแล้วถูกกดโดยไม่ตั้งใจ | ทาง fallback ข้างแผงใช้ได้ทั้ง Task และ Training | จำกัดให้ใช้ได้เฉพาะ Training | Examine placement และ stability ผ่าน |
| P0-C3 | Reader ในโหมด DEV อาจนับว่า "เห็น" ทั้งที่อยู่ด้านหลังผู้สวม | รอบก่อนเปลี่ยนเงื่อนไขเป็นแค่ "ไม่มี recenter ค้าง" | ต้องอ่านได้จากหัวตามปกติ (เท่ากับ HEAD) หรือเมื่อดึงเข้าใกล้ พื้นที่ 75% ตรงกลางต้องอยู่ในมุมมองทั้งสองตา **เงื่อนไขนี้กำหนดเวลาที่บันทึก exposure ของ Reader** จึงต่างจาก HEAD เฉพาะ session DEV ส่วน Study เหมือนเดิม | spatial readability ผ่าน ยังไม่ได้ตรวจ log exposure กับ Reader ที่ดึงเข้าใกล้บนเครื่องจริง |
| P0-C4 | กด "ทำต่อ" รอบที่ถูกปิดไปแล้ว การ์ด Setup ค้าง | ระบบตั้ง Error ไว้และไม่ล้าง | วาดการ์ดใหม่จาก registry แทนการตั้ง Error | อ่านโค้ด (reviewer) |
| P0-E1 | เปิดวิดีโอครั้งแรกอาจทำให้ภาพกระตุก (อนุมาน ยังไม่ได้วัด) | คำนวณ SHA256 ของไฟล์ 20–31 MB บน main thread | ย้ายไปคำนวณบน `Task.Run` | วิดีโอผ่านเส้นทางนี้ในการวิ่ง full S1 ใน Editor แต่ไม่มีการทดสอบวิดีโอเฉพาะ และยังไม่ได้วัดอาการกระตุกบน Quest = PENDING |
| P0-I1 | กด Play แล้วมี error `ENV_CASE_FINISH_FAILED` ทุกครั้ง | prop ฝาเคสถูกปิดไปแล้ว (ชิ้นส่วนจริงของ Task A มาแทน) | ข้าม prop ที่ปิดอยู่ | หลังแก้ console ขึ้น 0 error ตอนเริ่ม Play |
| T-1 | PlayCheck เดิมล้มตั้งแต่ขั้นพัก | assertion ล้าสมัยหลายจุด (Reader แบบแบ่งหน้า TMP, แท็บแนวนอน, Training 5 ขั้น, reflection ที่ signature เปลี่ยน) | ปรับ assertion ให้ตรงกับพฤติกรรมจริง โดยดูพฤติกรรมจริงก่อนทุกครั้ง **มี 2 ข้อที่อ่อนลง** คือ caption ของรูปภาพ (จาก "ตัวอักษรทั้งหมดอยู่ในกรอบ" เป็น "ถ้าล้นกรอบต้องมี scrollbar ที่มองเห็น" ตามการตัดสินใจ C04 เดิม) และ Training ที่มี FX1 ให้เครดิต Inspect แบบ DEMO_INPUT จึงไม่ได้ตรวจว่าแผง Inspect อยู่ในมุมมอง (การกดผ่าน SDK ครอบคลุมใน flow driver) | assertion เพิ่มจาก 211 เป็น 1,887–1,891 ข้อ |

งานของรอบก่อนหน้า (17 ต.ค.) ที่รวมอยู่ใน commit นี้ด้วย:
- policy `S1_DEV_MOVABLE_UI_20261017` ใช้เฉพาะ DEV ส่วน Study ยังเป็น fixed
- การ์ด Setup มีหน้าหลักและหน้ารายละเอียดที่สูงเท่ากัน
- ปุ่ม Examine เลือกตัวเองอัตโนมัติในขั้นฝึก Examine
- activity มี label และ icon
- ตัดข้อยกเว้นการชนของ `Training_Practice_A` ออก

## 3. ก่อน/หลัง ภาษาไทยและญี่ปุ่น

ภาพเปรียบเทียบอยู่ที่ `Logs/S1Master20261017/before_after/` (โฟลเดอร์ Logs ไม่อยู่ใน Git)
- `01_setup_details_ja.jpg` และ `02_setup_details_th.jpg`
- `03_timer_confirm_ja.jpg` และ `04_timer_confirm_th.jpg`
- `05_pause_ja.jpg`
- `06_training_reader_feedback_vs_zoom.jpg`

ภาพหน้า Setup, Task A/B, Ready และหน้ายืนยัน ทั้งหมด 16 ภาพ อยู่ที่ `Logs/S1Master20261017/compact_final2/synthetic-mono-preview/` ส่วนภาพ flow ภาษาญี่ปุ่นอยู่ที่ `Logs/S1Master20261017/playcheck_ja_final2/final-screenshots/`

## 4. ผลทดสอบ (Editor/PC เท่านั้น)

| ชุดทดสอบ | ผล | หลักฐาน |
|---|---|---|
| Full S1 ไทย AB P (Training ด้วย SDK จริง → A → Inspect → checkpoint → B → Inspect → checkpoint → export → PC QC) บน source สุดท้าย | `PASS_EDITOR_SIMULATED`, `data_complete=true` | `master-20261018/full_th_ab_p_final.log` |
| Full S1 ญี่ปุ่น BA Q | สองรอบแรกวิ่งก่อนแก้ตาม review: รอบแรก QC ล้มเพราะช่องว่างข้อมูลการเคลื่อนไหว 0.2534 วินาที (เกณฑ์ 0.25) รอบที่สอง PASS ส่วนรอบบน source สุดท้ายดูข้อ 4.1 | `full_ja_ba_q.log`, `full_ja_ba_q_r2.log`, `full_ja_ba_q_final.log` |
| Participant safety, Stop fallback, UI control acquisition, Spatial readability, Examine stability และ placement | PASS ทั้งหมดหลังแก้ตาม review | `master-20261018/suites3/` |
| DEV reach | `DEV_REACH_PASS stations=2` (ฟังก์ชันคืนค่าเป็น string ไม่มีไฟล์) | `Logs/S1Master20261017/cable_probe_results.txt` ข้อ 4 |
| Compact UI ไทย/ญี่ปุ่น 16 stage, 39 check | PASS | `Logs/S1Master20261017/compact_final2/` |
| Tools export/QC unittest | exit 0 (เครื่องนี้ไม่มี pytest จึงใช้ unittest) | `Logs/S1Master20261017/tools_unittest.log` |
| PlayCheck แบบเดิม (ไทย/ญี่ปุ่น) | ผ่าน Setup → Pause → Training → Reader ทั้งสี่รูปแบบ → เริ่ม Task ได้ จากนั้น `status: Failed` ใน `VerifyExperimentalUI` เพราะหาข้อความโจทย์ไม่พบ ในการวิ่งภาษาไทยพบว่าแผงเป็นหน้าพัก (tracking pause หลังเริ่ม Task 0.7 วินาทีใน fixture ป้อน input จำลอง) ส่วนภาษาญี่ปุ่นยังไม่ได้วินิจฉัย | OPEN; flow เต็มใช้ harness แบบ native แทน |

คำเตือนจาก QC ที่มีในทุกการวิ่ง Editor (ไม่ใช่ failure): `missing_video_A/B` (ไม่มีการบันทึกวิดีโอใน Editor), `missing_or_incomplete_assessment_A/B` (ยังไม่มีแบบประเมิน เช่น NASA-TLX ซึ่งต้องให้คนกรอก) และ `unverified_information_visibility_A/B` (ใน Editor ยืนยันไม่ได้ว่าคนเห็นข้อมูลจริง)

### 4.1 ญี่ปุ่น BA Q บน source สุดท้าย

ผลคือ `FLOW_PASS_QC_FAIL` (DEV343, bundle `v1_3b56211a942283ab43cc62f1`)
- ทั้ง flow ผ่านครบทุกขั้น
- QC ล้มเพราะข้อมูลการเคลื่อนไหวของ Task A ขาดช่วง 0.403 วินาที (เกณฑ์ 0.25) ทันทีหลัง `GRAB G01` (หยิบ GPU) ทั้ง head และคอนโทรลเลอร์ทั้งสองข้าง
- การวิ่งญี่ปุ่นที่ Task A มาเป็นงานที่สอง มีช่องว่างแบบนี้ 2 ใน 3 ครั้ง ทุกครั้งเกิดหลังการหยิบชิ้นส่วน (อีกครั้งคือ 0.35 วินาทีหลังหยิบ SATA power) ส่วนการวิ่งไทยไม่มีเลย 3 ใน 3 ครั้ง
- ยังแยกไม่ได้ว่าเป็นอาการ Editor หน่วงเป็นครั้งคราว (เคยพบมาก่อน) หรือเป็นการกระตุกจริงตอนหยิบ = **OPEN** ต้องวัด frametime ขณะหยิบบน Quest หรือวิ่งพร้อม `ResearchFrameSpikeRecorder` ถ้าเป็นจริง ผู้สวมจะเห็นภาพค้างราว 0.4 วินาทีตอนหยิบ

เมทริกซ์ TH/JA × AB/BA × P/Q 8 ช่อง (วิ่งครบแล้วทั้ง 8 ช่อง เมื่อ 18 ต.ค. 19:12–20:06 ขณะเครื่องว่าง):

| ช่อง | Flow (Training → Task 1 → Inspect → checkpoint → Task 2 → Inspect → checkpoint → export → PC ingest) | QC ข้อมูล | ช่องว่างข้อมูลการเคลื่อนไหว (task, วินาทีหลังเริ่ม, ความยาว) |
|---|---|---|---|
| TH AB P | ผ่าน | PASS | ไม่มี |
| TH AB Q | ผ่าน | FAIL | A, 4.3 s, 0.279 s |
| TH BA P | ผ่าน | PASS | ไม่มี |
| TH BA Q | ผ่าน | FAIL | A, 178.1 s, 0.325 s |
| JA AB P | ผ่าน | FAIL | A, 12.8 s, 0.260 s |
| JA AB Q | ผ่าน | FAIL | A, 11.1 s, 0.263 s |
| JA BA P | ผ่าน | FAIL | A 92.9 s 0.373 s และ B 10.6 s 0.270 s |
| JA BA Q | ผ่าน | FAIL ในรอบสุดท้าย (PASS ในรอบก่อน) | A, 0.403 s |

หลักฐาน: `S1_Unity_PC_20261011/master-20261018/matrix2/` และ `full_*` ช่อง TH AB Q ที่เคย INCONCLUSIVE เพราะมีคนใช้เครื่อง ได้วิ่งใหม่ครบแล้ว

ข้อสรุปเรื่อง QC (อนุมานจากข้อมูล ยังไม่ยืนยันบนเครื่องจริง):
- กลไกและ flow ผ่านครบทุกช่อง สิ่งที่ไม่ผ่านคือเกณฑ์ข้อมูลการเคลื่อนไหวต่อเนื่อง ที่ห้ามขาดช่วงเกิน 0.25 วินาที
- ช่องว่างทุกครั้งยาว 0.26–0.40 วินาที และเกิดในจังหวะที่ต่างกัน เกือบทั้งหมดอยู่ใน Task A ซึ่งวิ่งนานกว่า Task B ประมาณ 6 เท่า
- ผมวิ่งแบบบันทึกเวลาต่อเฟรม 1 รอบ: ได้ 1 เฟรมยาว 323 ms ใน `Update.ScriptRunBehaviourUpdate` และ 1 เฟรม 188 ms ที่มี GC 27 ms ยังไม่ทราบว่า script ตัวไหนเป็นต้นเหตุ ไฟล์คือ `master-20261018/spike_taskA.csv`
- การอ่าน registry และ hash สื่อ Reader ใช้ไม่เกิน 100 ms ต่อแพ็กเกจ จึงไม่ใช่ต้นเหตุหลัก
- ต้องวัดบน Quest (frametime ระหว่างทำ Task A) ก่อนสรุปว่าเป็นอาการของ Editor หรือภาพค้างจริง = **OPEN**
- 20 ต.ค.: เพิ่มตัวดัก (`ResearchFrameSpikeRecorder.Trap`) ให้เปิด Profiler ตั้งแต่ Task เริ่ม และหยุดที่เฟรมแรกที่ยาวเกิน 200 ms ผลในการวิ่ง native ไทย AB P (DEV370) คือเฟรมยาว 211 ms ใช้เวลาใน `EditorLoop` 206 ms ส่วน `PlayerLoop` ของแอปใช้เพียง 4.6 ms เฟรมนั้นจึงเป็นงานของ Editor (สลับ focus ทุก 10 วินาที, คำสั่ง bridge, repaint) ซึ่งไม่มีบน Quest หลักฐาน: profiler frame 29872 และ `master-20261018/trap_early_th_ab_p.log`
- ช่องว่าง 6 ครั้งในเมทริกซ์ไม่ได้เกิดหลังการหยิบหรือ Inspect: 3 ครั้งไม่มี event ใดในช่วงนั้น อีก 3 ครั้งจบตรงจุดที่ logger บันทึกว่า tracking ของหัวและคอนโทรลเลอร์ทั้งสองหายไปพร้อมกันระหว่าง Task ที่ไม่ได้พักและมี focus (`MOVEMENT_CONTEXT` ที่ `required_provider_validity` เป็น false แล้วกลับเป็น true ใน 30 ms) แบบนี้ตรงกับเฟรมค้างของ Editor ที่ทำให้ตัวจำลอง tracking ขาด (อนุมาน)
- ตัวดักถูกแก้ให้ข้ามเฟรมที่ `EditorLoop` เป็นหลักและรอเฉพาะเฟรมที่ `PlayerLoop` ยาว เพื่อหา script ของเฟรม 323 ms เดิม แต่การวิ่งครั้งถัดไปไม่จบเพราะมีคนใช้เครื่อง (INCONCLUSIVE_FOCUS) = **OPEN** จนกว่าจะวิ่งตอนเครื่องว่าง

การตรวจหน้าจอแบบเดิม (legacy play check, ญี่ปุ่น) หลังปรับให้ใช้มุมกล้อง 90 องศา (ใกล้ Quest 3 แทน 60 องศาของ Editor): ผ่าน 2,806 ข้อ ผ่าน Training, Pause, Reader และเริ่ม Task A ได้ จากนั้นหยุดที่ปุ่มตรวจดูของชิ้นงาน Task A ที่หาที่วางไม่ได้ขณะ Reader เปิดอยู่ (`canvas=False`) = **OPEN**: ต้องตรวจบน Quest ว่าผู้เล่นตรวจดูชิ้นงานได้หรือไม่ขณะเปิดคู่มือค้างไว้ ถ้าไม่ได้ ต้องย่อ Reader ก่อน

ชุดทดสอบเพิ่มเติมที่รันในรอบต่อ (18 ต.ค. บ่าย):

| ชุดทดสอบ | ผล | หลักฐาน |
|---|---|---|
| Logic suites ของ ResearchFlow ทั้ง 13 ชุด (รันนอก Unity) รวม S1 evaluator 280 checks / 120 permutations, timing 73 checks (รวม timeout ของ task ที่สองใน BA), registry 162, behaviour 200, logger resume 76 | PASS ทั้งหมด | `Logs/S1Master20261017/logic_checks/` |
| UIRecovery 16 ชุด | 16/16 PASS: 15 ชุดผ่านตั้งแต่ 18 ต.ค. ส่วน TemporalSequence ผ่านเมื่อ 20 ต.ค. หลังให้ fixture รันใน TEST session จริง (แถบ recovery มีไว้เพื่อให้มี Stop เมื่อไม่มีแผงไหนให้ได้ และ Stop ต้องมี session ที่กำลังรันพร้อมตัวเขียน log) และการตรวจเข้มขึ้น คือต้องเห็นทั้ง "เรียกแผง" และ "หยุด" และต้องไม่มีปุ่มพัก | `master-20261018/suites_final/uirec_*`, `suites_final2/RunTemporalSequence_23` |
| Reader production (TH/JA × 4 รูปแบบ ผ่านการชี้ด้วย SDK) | PASS | `master-20261018/suites4/readerprod` |
| Export lifecycle (task timeout, withdrawal + amendment, completion, การเขียน log ระหว่าง export) | PASS 78 rows | `suites_final/ResearchExportLifecycleChecks_*` |
| Operator outcome (abort, invalidate, registry/auth/terminal write failure, export failure) | 11/11 PASS | `suites_final/ResearchS1OperatorOutcomeChecks_*` |
| Operator confirmation | PASS | `suites_final/ResearchOperatorConfirmationChecks_*` |
| TerminalUI / Pause ownership | 20 ต.ค.: 72/72 และ 177/177 PASS (เดิม 64/72 และ 75/133) ข้อที่ล้าสมัยถูกเปลี่ยนเป็นกฎปัจจุบันพร้อมระบุบรรทัดใน product ที่กำหนดกฎนั้น ได้แก่ Reader ที่ขยายอยู่เป็นเจ้าของปุ่มพัก, แถบ recovery แบบย่อมี "เรียกแผง" กับ "หยุด" และไม่มีปุ่มพัก และปุ่ม Menu ของคอนโทรลเลอร์ใช้พักเมื่อไม่มีแผงไหนให้พักได้ กรณีที่ผู้เข้าร่วมจับแผงหลักที่หันหลังอยู่: product เลื่อนการวางแผงทุกแผงไว้จนกว่าจะปล่อย (`ResearchFlowView.PanelPlacementBusy`) เพื่อไม่ให้แถบเลื่อนมาอยู่ใต้ ray ระหว่างลาก ตัวทดสอบจึงตรวจว่าแถบถูกขอไว้ระหว่างจับ และมีปุ่ม "หยุด" หนึ่งปุ่มทันทีที่ปล่อย รอบที่ยังไม่วางแผงหลังปล่อยล้มที่ข้อนี้ 3 ข้อ แสดงว่าข้อนี้จับการถดถอยได้ (โหมด Study ลากแผงไม่ได้ และโหมด DEV หันแผงเข้าหาหัวระหว่างลาก แผงที่ถูกจับและหันหลังจึงเป็นสถานะที่ตัวทดสอบฉีดเข้าไปเอง) | `suites_final2/Run_21`, `RunPauseOwnership_22` |
| TutorialResume / TutorialAcceptance / RecoveryTemporal | 20 ต.ค.: 3/3, 9/9, 29/29 PASS (เดิม 0/3 และ RecoveryTemporal crash) สาเหตุอยู่ที่ fixture ร่วม ไม่ใช่ product มี 2 ข้อ: (1) fixture ปิดคอนโทรลเลอร์ไว้ ซึ่ง product อ่านว่า "ไม่รู้สถานะ" และกันการวางแผงทั้งหมด จึงเกิด rear_face (2) fixture สร้าง record ของ DEV แต่ไม่ได้ใช้ UI policy ของ record แบบที่ `StartSession` ทำ จึงค้างอยู่ใน policy แบบ Study ที่ลากแผงไม่ได้ ตอนนี้ fixture ใช้ policy ของ record และคืนค่าเดิมเมื่อจบ (ตรวจแล้วว่ากลับเป็น `S1_FIXED_UI_DEMO_20261014`) | `suites_final2/ResearchRecoveryTemporalChecks_11`, `ResearchTutorialResumeChecks_12`, `ResearchTutorialAcceptanceChecks_13` |
| FocusedReaderInput.Run (ชุดเก่าที่ใช้ demo content) | 15 failures เพราะชี้ไปที่แพ็กเกจ demo ที่เลิกใช้ แทนด้วย RunProduction ซึ่งผ่าน | superseded |
| หลังแก้รอบต่อ: safety, stop fallback, UI control, compact UI 39 checks | PASS | `master-20261018/suites4`, `Logs/S1Master20261017/compact_final3` |

## 5. สื่อ Reader ทั้ง 4 รูปแบบ (A/B × ไทย/ญี่ปุ่น = 16 ชุด และ Training)

ตรวจด้วยการวัดจริง ผลอยู่ที่ `Logs/S1Master20261017/reader_audit/inventory.json` (`checks_matrix`):
- ไฟล์ครบทั้ง 7 package และ SHA256 ตรงทุกไฟล์
- A/B มี PDF 8 หน้า, Steps 8 ขั้น และ Images 8 ภาพ
- วิดีโอเป็น H.264 1280×720 ไม่มีเสียง keyframe ห่างไม่เกิน 2 วินาที
- ตัวเลข หน่วย และรหัสชิ้นส่วนในฉบับไทยกับญี่ปุ่นตรงกัน
- ทุก package ยังเป็น `S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW` (ไม่ได้แก้)

ข้อค้างที่ต้องให้คนตัดสิน:
- วิดีโอยาวกว่าเวลา Task 420 วินาทีทุกไฟล์: A_th +9.9, A_ja +78.8, B_th +17.1, B_ja +39.1 วินาที ไม่ได้ตัดเนื้อหาหรือแก้ timer
- วิดีโอ Training เป็น slideshow (ภาพเปลี่ยนเพียง 2.7–4.7% ของช่วงที่สุ่ม)
- caption ของรูปภาพซ้ำข้อความเต็มของขั้นตอน
- ภาพประกอบ B เกือบเหมือนกันทุกภาพ
- ยังมีคำภาษาอังกฤษปนในข้อความไทยและญี่ปุ่น
- ผลบน Quest ยังไม่ได้ตรวจ: วิดีโอ prepare และเล่นได้จริงหรือไม่ = PENDING

## 6. สายไฟ โต๊ะ และระยะเอื้อม

ตัวเลขทั้งหมดในข้อนี้คัดลอกจากผล probe ไว้ที่ `Logs/S1Master20261017/cable_probe_results.txt`

- ตอนวางนิ่ง สายทั้ง 9 เส้นของ Task A/B และสาย FX1 อยู่เหนือพื้นผิวทำงานทั้ง layout P และ Q ไม่มี vertex ต่ำกว่าผิวโต๊ะเลย จุดต่ำสุดคือสาย AC สูง 0.950 ม. บนโต๊ะสูง 0.920 ม. (วัดจริง)
- เมื่อจับหัวปลั๊กที่หยิบได้ (PCIe 8-pin, SATA data, SATA power) แล้ววางชิดโต๊ะในมุมเดียวกับตอนเสียบ ตัวสายจมลงโต๊ะได้ถึง 4.5 มม. ใน 16 จาก 262 ท่า แต่เมื่อยกสูง 5–15 ซม. ไม่จมเลย
- ได้ลองยกกระดูกของสายให้สูงกว่าโต๊ะ 8 มม. แต่ผลไม่ต่าง (ยังเป็น 16/262 และ 4.5 มม.) จึงถอยการแก้นี้ออก สาเหตุที่น่าจะเป็นคือ mesh ห้อยต่ำกว่ากระดูกราว 12 มม. ตอนปลั๊กอยู่ในมุมของตอนเสียบ (อนุมาน)
- ปลั๊กเป็น kinematic และไม่มีแรงโน้มถ่วง ส่วน `ResearchS1HeldCollision` กันไม่ให้ collider ของปลั๊กทะลุโต๊ะ จึงจะเห็นสายจมก็ต่อเมื่อวางปลั๊กห่างโต๊ะไม่เกินประมาณ 2 มม. ในมุมของตอนเสียบ = **OPEN รอคนสวมตรวจ**
- ระยะเอื้อม: ผ่าน DEV_REACH_PASS แต่เป็นการตรวจเชิงเรขาคณิตเท่านั้น ส่วนการเอื้อมจริงรอคนสวม

## 7. Icon ของแอปบน Quest

- APK ที่ติดตั้งอยู่ตอนนี้คือ `ResearchFlowQuest3_20261017_083533.apk` (byte ตรงกัน) มีทั้ง label และ icon
- เอกสารของ Meta หน้า developer mode ระบุว่าแอปที่ติดตั้งเองไม่ขึ้นในตารางแอปหลัก ให้ไปที่ **Library → Unknown Sources** (https://developers.meta.com/horizon/documentation/android-apps/enable-developer-mode/) ส่วนหน้า distribution options เขียนว่าแอป sideload "ไม่ปรากฏใน Library ของ Quest Home" (https://developers.meta.com/horizon/policy/distribution-options/) สองหน้านี้ขัดกันเล็กน้อย คนสวมต้องดูทั้งสองที่
- ข้อความ "App name unavailable" ใน Universal Menu น่าจะเกิดจากไม่มีข้อมูลแอปฝั่ง Store (สมมติฐาน ไม่พบเอกสารยืนยัน)
- ไม่ได้ติดตั้ง APK ใหม่ เพราะ brief ข้อ 16 ห้ามติดตั้งเมื่อไม่ได้ยืนยันว่ามีคนสวมหรือเจ้าของอยู่ และ "proximity CLOSE" มาจาก daemon ไม่ใช่คน
- คำสั่งติดตั้งเมื่อพร้อม (สำรอง registry ก่อน ไม่ uninstall และไม่ล้างข้อมูล):

```
adb -s 2G0YC5ZHBF00FJ install -r Logs/QuestFinalQA/build/ResearchFlowQuest3_20261020_040226.apk
```

  จากนั้นยืนยันว่า SHA256 ของ base.apk บนเครื่องเท่ากับ `407c51a7…490f` และค่า `lastUpdateTime` เปลี่ยน
- รายละเอียดอยู่ที่ `Logs/S1Master20261017/icon_audit/`

## 8. Rive และมาสคอต

ข้อเสนอคือ B: ยังไม่ใส่ Rive runtime เข้าแอปตอนนี้ เหตุผล:
- เป็น v0.5.1 ซึ่ง README ยังเรียกว่า technical preview
- ไม่มีเอกสารเรื่อง VR/OpenXR
- ทำให้ APK ใหญ่ขึ้น 10.6 MB (arm64)
- มี issue บน Android ที่ยังเปิดอยู่
- เงื่อนไขการ export ใน pricing กับในเอกสารขัดกันเอง

มาสคอต Blop และ Robot ทั้งคู่เป็น CC BY 4.0 และมี hash ไฟล์บันทึกไว้แล้ว ถ้าเจ้าของต้องการ ให้ใช้เป็นภาพ PNG หลายเฟรมเฉพาะหน้า DEV Setup, Welcome และ Training เท่านั้น เรื่อง credit และการจ่ายค่าแผนเป็นการตัดสินใจของเจ้าของ รายละเอียดอยู่ที่ `Logs/S1Master20261017/rive_research/DECISION.md`

## 9. สิ่งที่ต้องให้เจ้าของหรือผู้วิจัยทำ

1. **คนสวม Quest**
   - ติดตั้ง APK ข้อ 1 แล้วไล่ทดสอบตั้งแต่ Setup จนจบทั้ง Task A/B
   - หน้าพักต้องมี "ทำต่อ" ปุ่มเดียว
   - ข้อความ 準備完了 และข้อความพักภาษาญี่ปุ่นต้องไม่ทับกัน
   - ปุ่ม Examine ข้างโมดูลเหลือง
   - วิดีโอใน Reader ต้องเล่นได้ และเลื่อน/กรอได้
   - แถวซูม PDF ต้องไม่ทับข้อความ "ผ่านแล้ว"
   - ลากแผงได้ (DEV)
   - ความคมชัดเมื่อมองไกล
   - วางปลั๊ก SATA power หรือ PCIe ชิดโต๊ะแล้วดูว่าสายจมหรือไม่
   - หา icon ใน Library → Unknown Sources
2. **ผู้ตรวจภาษาไทย/ญี่ปุ่น**: ข้อความพักญี่ปุ่นที่แก้ใหม่ และคำภาษาอังกฤษที่ยังปนในสื่อ
3. **ผู้วิจัยหรืออาจารย์ตัดสิน**
   - วิดีโอที่ยาวเกิน 420 วินาที
   - caption ของรูปภาพ
   - การเปิดให้ลากแผงใน Study
   - จำนวนผู้เข้าร่วมและเวลาตาม proposal เทียบกับ TaskSpec
   - การใช้มาสคอต
4. **Git**: commit `f5ea522` อยู่ในเครื่องแล้ว แต่ยังไม่ได้ push

## 10. ความคมชัดเมื่อมองไกล (ข้อ 11 ของ brief)

วัดค่าใน Editor โดยไม่ได้แก้อะไร:
- Android ใช้ quality ระดับ Medium (index 1) กำหนด MSAA 4× และ anisotropic แบบ "Per Texture"
- texture ในฉากขนาด 512 px ขึ้นไปมี 116 ไฟล์ ในนี้ 101 ไฟล์ตั้ง anisoLevel ต่ำกว่า 2 จึงไม่ได้ anisotropic filtering บน Quest
- สิ่งนี้เป็นตัวเลือกที่น่าจะทำให้พื้นผิวที่มองเป็นมุมเฉียง เช่น โต๊ะ พื้น และผนังไกล ดูเบลอ แต่ไม่เกี่ยวกับความคมของตัวอักษร (อนุมาน)
- TMP font atlas เป็น SDF ไม่มี mip ซึ่งเป็นค่าปกติ
- ไม่ได้เปลี่ยนค่าเพราะมีต้นทุน GPU บน Quest ที่ต้องวัด ข้อเสนอคือเทียบ `ForceEnable` หรือ anisoLevel 4 กับค่าปัจจุบันในมุมมองเดียวกัน แล้ววัด frametime p95/p99 และให้คนสวมดูผ่านเลนส์ = **OPEN**

## 11. รายการใน Testing Matrix ที่รอบนี้ยังไม่ได้ทดสอบ (NOT TESTED)

- ข้อ 15.1: ภาพ 1.5x **ทำแล้ว** (compact UI 16 หน้า TH/JA ที่ 1920×1200 ผ่าน 39/39, `Logs/S1Master20261017/compact_1_5x_c`) และพบ/แก้ข้อความญี่ปุ่นบนการ์ด Task ที่เหลือ「ん。」บรรทัดเดียวกับคำในวงเล็บที่ถูกตัด ยังไม่ได้ทำ: ผู้ตรวจภายนอก
- ข้อ 15.2: input จริงบนเครื่องสำหรับการกรอวิดีโอ, การเลื่อน, focus loss และ haptics (ใน Editor ครอบคลุมด้วย SDK จำลองบางส่วน)
- ข้อ 15.3: ไม่ได้แยกรายงานตามความสูงตา 1.0 และ 1.65 ม. (spatial readability suite ใช้ชุดท่าของมันเอง)
- ข้อ 15.4: Timeout ครอบคลุมโดย timing logic suite (73 checks รวม task ที่สองหมดเวลาใน BA) และ export lifecycle (task deadline) ส่วน Inspect Fail ใน Task **ทำแล้ว 20 ต.ค.**: การวิ่ง native ไทย AB P (DEV370) กด Inspect ทันทีที่ Task A เริ่มก่อนประกอบอะไร ได้ `inspect_fail:inspect.s1.A.AssemblyIncomplete` และยังอยู่ที่ `Task1Active` (ไม่ข้ามไป checkpoint) จากนั้นทำ A ต่อจนได้ `inspect.s1.A.Pass`, ทำ B ได้ `inspect.s1.B.Pass` และ export ครบ flow ผ่าน ส่วน QC ล้มเพราะช่องว่างข้อมูลการเคลื่อนไหว 0.254–0.271 วินาที (ดูข้อ 4.1) หลักฐาน `master-20261018/trap_early_th_ab_p/run.json` ส่วน harness ใช้ `EARLY_INSPECT=1`
- ข้อ 15.5: เมทริกซ์ 8 ช่องวิ่งครบแล้ว flow ผ่านทุกช่อง QC ข้อมูลผ่าน 2 ช่อง (ดูข้อ 4)
- ข้อ 15.6: ครอบคลุมแล้วโดย export lifecycle (withdrawal, timeout), operator outcome (writer/registry/export failure), Reader wireframe (แพ็กเกจหาย, registry เสีย, หน้า PDF เสีย) และ registry checks (ไม่มีรหัสซ้ำ) **ยังไม่ได้ทดสอบ** กรณีไฟล์วิดีโอหายบนเครื่องจริง
- ข้อ 15.8: รันครบแล้ว ดูข้อ 4
- ข้อ 15.10–11: ทั้งการทดสอบผ่านเลนส์และการตัดสินใจของคน

## 12. ข้อจำกัดที่ต้องรู้

- ผลทั้งหมดในรายงานนี้คือ Editor PASS ไม่ใช่ Quest PASS
- เมทริกซ์ 8 ช่องวิ่งครบแล้ว flow ผ่านทุกช่อง แต่ QC ข้อมูลการเคลื่อนไหวผ่านเพียง 2 ช่อง (ดูข้อ 4.1)
- ยังไม่ได้วัดความคมชัด เฟรมเรต และอาการภาพสั่นบนเครื่องจริง
- สคริปต์ harness `s1_full_run.py` อยู่นอก Git ที่ `S1_Unity_PC_20261011/claude-takeover-20261015/scripts/` (แก้ timeout ของคำสั่งตรวจสถานะ Play เป็น 180 วินาที และสำรองไฟล์เดิมไว้ที่ `.bak_20261018`; 20 ต.ค. เพิ่มขั้น `EARLY_INSPECT=1` ที่เปิดใช้เมื่อสั่งเท่านั้น สำรองไว้ที่ `.bak_20261020`)
