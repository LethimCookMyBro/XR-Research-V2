# รายงานรอบ S1 MASTER (17–18 ต.ค. 2026)

เอกสารนี้ตอบ `S1_ALL_WORKFLOW_UX_UI_MASTER_20261017_TH.md` ข้อ 15–16 ทุกผลในนี้มาจาก Unity Editor และ PC เท่านั้น **ยังไม่ได้ทดสอบบน Quest และยังไม่มีคนสวม** จึงยังไม่พร้อมใช้กับผู้เข้าร่วมจริง

## 1. ตัวตนของ Source และ APK

| รายการ | ค่า |
|---|---|
| Commit ของ source | `f5ea522` บน `main` (ต่อจาก `e2f9bb4`) |
| APK | `Logs/QuestFinalQA/build/ResearchFlowQuest3_20261018_072028.apk` |
| SHA256 | `6cbe479b973dff6f24429e6d10f9f8c567489744a1f44faa43b0823a60828168` |
| ขนาด | 307,435,337 bytes |
| Build | สำเร็จ, error 0, warning 360, 214.8 วินาที |
| ผูกกับ source | `S1_Unity_PC_20261011/master-20261018/build_f5ea522/apk-binding.json` (ไฟล์ที่ยังไม่ commit = 0, drift หลัง restore = 0) |
| ไฟล์ที่ build เขียนทับแล้วคืนค่าแล้ว | `ProjectSettings.asset` และ font atlas 4 ไฟล์ |
| Package / label | `th.ac.tmu.vr.researchflow.quest3.technical`, label "ResearchFlow Quest3 Technical" และ icon อยู่ทั้งที่ application และ launchable-activity (`aapt2 dump badging`) |
| ติดตั้งบน Quest | **ยังไม่ได้ติดตั้ง** ดูข้อ 7 |

ที่มาของ warning (อนุมาน): ส่วนใหญ่เป็น shader variant ของ package Unity Sentis (`ConvGeneric`) และ C# warning ประเภท CS0618 (API เก่า) กับ CS0414 (field ไม่ถูกใช้) ไม่มีจากโค้ด ResearchFlow ที่ทำให้ build ล้ม

## 2. งานที่แก้ (ปัญหา → สาเหตุ → การแก้ → หลักฐาน)

| ID | ปัญหาที่ผู้ใช้เห็น | สาเหตุ | การแก้ | หลักฐาน / สถานะ |
|---|---|---|---|---|
| P0-B1 | ป้าย 準備完了 ในหน้า Details ภาษาญี่ปุ่นแตกเป็นสองบรรทัดทับแถวถัดไป | ป้ายแคบเกินและห่อบรรทัดได้ ส่วน check เดิมวัดทีละป้ายจึงไม่เห็นป้ายชนกัน | การ์ด Setup ใหม่ของรอบก่อนแก้ขนาดป้ายแล้ว รอบนี้เพิ่มการตรวจกรอบตัวอักษรที่แสดงจริงแบบจับคู่ใน `ResearchCompactUIMeasure.cs` | ภาพ `before_after/01_setup_details_ja.jpg` และ mutation test: เมื่อใส่ปัญหาเดิมกลับ check ฟ้อง "Status/Label / Status/Label: text overlaps text" — ผ่านใน Editor |
| P0-B2 | หน้าพักมีทั้ง "ทำต่อ" และ "หยุด" (PlayCheck "Eligible pause exposes Continue only" ล้ม) | กฎซ่อน Stop มีสองชุด ชุดใน `RefreshRecoveryUtility` เปิด Stop กลับมาหลัง layout | รวมเป็นกฎเดียว `StopOffered` คือ ซ่อน Stop เฉพาะตอนผู้เข้าร่วมกดพักเองและกลับมาเองได้ ส่วนการพักจาก tracking หรือ app ยังมี Stop เพราะปุ่ม "ทำต่อ" ยังรอให้อุปกรณ์พร้อม และหน้าพักที่อยู่นอกสายตายังมีแถบเรียกแผงกลับ | PlayCheck TH/JA ผ่านขั้นนี้ ภาพ `05_pause_ja.jpg` — ผ่านใน Editor |
| P0-B3 | หน้ายืนยันจับเวลาทำให้ "ย้อนกลับ" เป็นปุ่มสีน้ำเงินหลัก | ปุ่มแรกถูกกำหนดเป็น primary เสมอ | "เริ่มจับเวลา" เป็นปุ่มหลัก "ย้อนกลับ" เป็นปุ่มรองและอยู่ที่เดิม | `03_timer_confirm_ja.jpg`, `04_timer_confirm_th.jpg` — ผ่านใน Editor |
| P0-B4 | ข้อความพักภาษาญี่ปุ่นเหลือ 「い。」 บรรทัดเดียว และคำว่า 研究者 ถูกตัดกลางคำ | ประโยคแรกซ้ำหัวข้อ และไม่มีจุดตัดบรรทัดตามวลี | ตัดประโยคที่ซ้ำ แล้วตัดบรรทัดตรงรอยต่อวลี (ข้อความภาษาไทยไม่เปลี่ยน) | `05_pause_ja.jpg` — ผ่านใน Editor, **รอผู้ตรวจภาษา** |
| P0-B5 | ใน Training ข้อความ "ผ่านแล้ว" อยู่ใต้ปุ่มซูม PDF (−, 100%, +) | สองส่วนใช้ช่องเดียวกันใต้แท็บ | ย้ายแถวซูมไปท้ายช่องแสดงผล ปุ่ม 100% กว้าง 88 px และไม่ห่อบรรทัด ส่วน PlayCheck ตรวจการชนของข้อความกับปุ่มทุกภาพของ Reader | `06_training_reader_feedback_vs_zoom.jpg` และ mutation: เมื่อย้ายแถวซูมกลับที่เดิม check ฟ้อง — ผ่านใน Editor |
| P0-C1 | Training ให้เครดิต Examine เมื่อจับโมดูลขึ้นมา | รอบก่อนเพิ่มทางนี้ไว้ แต่ใน Task จะบันทึก EXAMINE จากปุ่ม Examine เท่านั้น | ให้เครดิตจากปุ่ม Examine อย่างเดียว และล็อกโมดูลไว้จนถึงขั้นใส่โมดูล | การวิ่ง Training ทั้งชุดด้วย SDK ผ่าน — ผ่านใน Editor |
| P0-C2 | ปุ่ม Examine ใน Task อาจไปทับแผงแล้วถูกกดโดยไม่ตั้งใจ | ทาง fallback ข้างแผงใช้ได้ทั้ง Task และ Training | จำกัดให้ใช้ได้เฉพาะ Training | Examine placement และ stability ผ่าน |
| P0-C3 | Reader ในโหมด DEV อาจนับว่า "เห็น" ทั้งที่อยู่ด้านหลังผู้สวม | รอบก่อนเปลี่ยนเงื่อนไขเป็นแค่ "ไม่มี recenter ค้าง" | ต้องอ่านได้จากหัวตามปกติ หรือเมื่อดึงเข้าใกล้ พื้นที่ 75% ตรงกลางต้องอยู่ในมุมมองทั้งสองตา | spatial readability ผ่าน |
| P0-C4 | กด "ทำต่อ" รอบที่ถูกปิดไปแล้ว การ์ด Setup ค้าง | ระบบตั้ง Error ไว้และไม่ล้าง | วาดการ์ดใหม่จาก registry แทนการตั้ง Error | อ่านโค้ด (reviewer) |
| P0-E1 | เปิดวิดีโอครั้งแรกอาจทำให้ภาพกระตุก | คำนวณ SHA256 ของไฟล์ 20–31 MB บน main thread | ย้ายไปคำนวณบน `Task.Run` | Reader suites ผ่าน ส่วนผลบน Quest = PENDING |
| P0-I1 | กด Play แล้วมี error `ENV_CASE_FINISH_FAILED` ทุกครั้ง | prop ฝาเคสถูกปิดไปแล้ว (ชิ้นส่วนจริงของ Task A มาแทน) | ข้าม prop ที่ปิดอยู่ | หลังแก้ console ขึ้น 0 error ตอนเริ่ม Play |
| T-1 | PlayCheck เดิมล้มตั้งแต่ขั้นพัก | assertion ล้าสมัยหลายจุด (Reader แบบแบ่งหน้า TMP, แท็บแนวนอน, Training 5 ขั้น, reflection ที่ signature เปลี่ยน) | ปรับ assertion ให้ตรงกับพฤติกรรมจริง โดยดูพฤติกรรมจริงก่อนทุกครั้ง และไม่ทำให้ check อ่อนลง | assertion เพิ่มจาก 211 เป็น 1,891 ข้อ และผ่านถึงช่วงเริ่ม Task |

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
| Full S1 ไทย AB P (Training ด้วย SDK จริง → A → Inspect → checkpoint → B → Inspect → checkpoint → export → PC QC) | PASS, `data_complete=true` | `master-20261018/full_th_ab_p_final.log` |
| Full S1 ญี่ปุ่น BA Q | รอบแรก QC ล้มเพราะช่องว่างข้อมูลการเคลื่อนไหว 0.2534 วินาที (เกณฑ์ 0.25) จาก Editor หน่วงครั้งเดียว รอบที่สอง PASS, `data_complete=true` | `full_ja_ba_q.log`, `full_ja_ba_q_r2.log` |
| Participant safety, Stop fallback, UI control acquisition, Spatial readability, Examine stability และ placement, DEV reach | PASS ทั้งหมด | `master-20261018/suites3/` |
| Compact UI ไทย/ญี่ปุ่น 16 stage, 39 check | PASS | `Logs/S1Master20261017/compact_final2/` |
| Tools export/QC unittest | exit 0 | `Logs/S1Master20261017/tools_unittest.log` |
| PlayCheck แบบเดิม (ไทย/ญี่ปุ่น) | ผ่าน Setup → Pause → Training → Reader ทั้งสี่รูปแบบ → เริ่ม Task ได้ จากนั้นหยุดที่ fixture ป้อน input จำลอง (tracking pause หลังเริ่ม Task 0.7 วินาที) | OPEN; flow เต็มใช้ harness แบบ native แทน |

เมทริกซ์ TH/JA × AB/BA × P/Q 8 ช่อง: รอบนี้วิ่ง 2 ช่อง (TH AB P, JA BA Q) อีก 6 ช่องยังไม่ได้วิ่ง

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

- ตอนวางนิ่ง สายทั้ง 9 เส้นของ Task A/B และสาย FX1 อยู่เหนือพื้นผิวทำงานทั้ง layout P และ Q ไม่มี vertex ต่ำกว่าผิวโต๊ะเลย จุดต่ำสุดคือสาย AC สูง 0.950 ม. บนโต๊ะสูง 0.920 ม. (วัดจริง)
- เมื่อจับหัวปลั๊กที่หยิบได้ (PCIe 8-pin, SATA data, SATA power) แล้ววางชิดโต๊ะในมุมเดียวกับตอนเสียบ ตัวสายจมลงโต๊ะได้ถึง 4.5 มม. ใน 16 จาก 262 ท่า แต่เมื่อยกสูง 5–15 ซม. ไม่จมเลย
- ได้ลองยกกระดูกของสายให้สูงกว่าโต๊ะ 8 มม. แต่ผลไม่ต่าง (ยังเป็น 16/262 และ 4.5 มม.) จึงถอยการแก้นี้ออก สาเหตุที่น่าจะเป็นคือ mesh ห้อยต่ำกว่ากระดูกราว 12 มม. ตอนปลั๊กอยู่ในมุมของตอนเสียบ (อนุมาน)
- ปลั๊กเป็น kinematic และไม่มีแรงโน้มถ่วง ส่วน `ResearchS1HeldCollision` กันไม่ให้ collider ของปลั๊กทะลุโต๊ะ จึงจะเห็นสายจมก็ต่อเมื่อวางปลั๊กห่างโต๊ะไม่เกินประมาณ 2 มม. ในมุมของตอนเสียบ = **OPEN รอคนสวมตรวจ**
- ระยะเอื้อม: ผ่าน DEV_REACH_PASS แต่เป็นการตรวจเชิงเรขาคณิตเท่านั้น ส่วนการเอื้อมจริงรอคนสวม

## 7. Icon ของแอปบน Quest

- APK ที่ติดตั้งอยู่ตอนนี้คือ `ResearchFlowQuest3_20261017_083533.apk` (byte ตรงกัน) มีทั้ง label และ icon
- เอกสารของ Meta ระบุว่าแอปที่ sideload จะไม่ขึ้นในตารางแอปหลัก แต่จะอยู่ใน **Library → Unknown Sources** แหล่งที่มา: https://developers.meta.com/horizon/documentation/android-apps/enable-developer-mode/ และ https://developers.meta.com/horizon/policy/distribution-options/
- ข้อความ "App name unavailable" ใน Universal Menu น่าจะเกิดจากไม่มีข้อมูลแอปฝั่ง Store (สมมติฐาน ไม่พบเอกสารยืนยัน)
- ไม่ได้ติดตั้ง APK ใหม่ เพราะ brief ข้อ 16 ห้ามติดตั้งเมื่อไม่ได้ยืนยันว่ามีคนสวมหรือเจ้าของอยู่ และ "proximity CLOSE" มาจาก daemon ไม่ใช่คน
- คำสั่งติดตั้งเมื่อพร้อม (สำรอง registry ก่อน ไม่ uninstall และไม่ล้างข้อมูล):

```
adb -s 2G0YC5ZHBF00FJ install -r Logs/QuestFinalQA/build/ResearchFlowQuest3_20261018_072028.apk
```

  จากนั้นยืนยันว่า SHA256 ของ base.apk บนเครื่องเท่ากับ `6cbe479b…8168` และค่า `lastUpdateTime` เปลี่ยน
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

## 11. ข้อจำกัดที่ต้องรู้

- ผลทั้งหมดในรายงานนี้คือ Editor PASS ไม่ใช่ Quest PASS
- เมทริกซ์ 8 ช่องวิ่งไปเพียง 2 ช่อง
- ยังไม่ได้วัดความคมชัด เฟรมเรต และอาการภาพสั่นบนเครื่องจริง
- สคริปต์ harness `s1_full_run.py` อยู่นอก Git ที่ `S1_Unity_PC_20261011/claude-takeover-20261015/scripts/` (แก้ timeout ของคำสั่งตรวจสถานะ Play เป็น 180 วินาที และสำรองไฟล์เดิมไว้ที่ `.bak_20261018`)
