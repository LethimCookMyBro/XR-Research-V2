# Reader B6, DEV Reader ต้นแบบตะแกรงหน้า และความเทียบเท่าของภาษา — 4 พฤศจิกายน 2026

สถานะ: ต้นแบบตะแกรงหน้าเป็น Production Reference Prototype ตามที่ owner อนุมัติ ยังไม่อนุมัติให้ใช้เก็บข้อมูลวิจัยจริง
ภาษาญี่ปุ่นทุกจุดในเอกสารและสื่อนี้เป็นร่างจาก AI ยังไม่มีผู้ตรวจภาษาจริง
ไม่ได้ commit, ไม่ได้ติดตั้ง APK, ไม่ได้ผลิต Area อื่นหรือ Task A

## 1. Reader B6: ปิดประโยคชี้นำและปรับความสมดุลสาม Area

แก้ที่ข้อมูลต้นทาง `Tools/s1_reader_v2/content_B6.json` (สคริปต์แก้: `S1_Delivery_Work_20261028/b6_leakfix_20261104/edit_b6.py` และ `split_video3.py`) แล้วสร้าง B6 ใหม่ด้วย `build.py build --task B --training-version v6 --shots Logs/CloseInteraction20261030/shots --install`
ชุด shots นี้เป็นชุดเดียวที่ capture digest ตรงกับ B6 เดิม (`7aa3bc66dff54e59`) ภาพจากโมเดลจึงไม่เปลี่ยน
เสียงที่ไม่ได้แก้ใช้ไฟล์ TTS เดิมจาก cache; สังเคราะห์ใหม่เฉพาะประโยคที่เปลี่ยน

| จุด | ก่อน | หลัง |
|---|---|---|
| Manual หน้า 6 “ข้อควรรู้” | การถอดชิ้นที่บังอยู่ยังไม่ซ่อมจุดหลวม | ชิ้นที่ใส่อยู่หรือเข้าเกลียวแล้วอาจยังหลวมหรือไม่เข้าที่ “ดูสภาพชิ้นส่วน” บอกสภาพเท่านั้น ไม่ได้ซ่อม การถอดชิ้นส่วนเป็นการเปิดทางให้ตรวจด้านใน ถ้าพบชิ้นที่หลวมหรือไม่เข้าที่ ให้แก้ก่อนประกอบกลับ |
| Video ฉาก 4 (ถอดตะแกรงหน้า) | ประโยคเดียวกับข้างบนต่อท้ายการถอด | แยกเป็น 2 ประโยค: วิธีถอดเล่นพร้อมภาพเคลื่อนไหวจริง แล้วประโยคกลางเล่นบนภาพสุดท้ายที่ค้างไว้ (คำบรรยายญี่ปุ่นเหลือ 2 บรรทัด ไม่บังคลิป) |
| “ใส่อยู่/เข้าเกลียว ≠ แน่น” | มีเฉพาะตะแกรงหลัง (Steps, Illustrated + footer, Video) | มีครบทั้งสาม Area ในการ์ดตรวจ: ตะแกรงหน้า “ใส่อยู่แต่เครื่องหมายเยื้องหรือคลิปเปิด ยังไม่ล็อก”, ใบพัด “ฝาล็อกที่เข้าเกลียวแล้วอาจยังไม่แน่น”, ตะแกรงหลัง “วงแหวนที่เข้าเกลียวแล้วอาจยังไม่แน่น”; ลบ footer ที่มีเฉพาะตะแกรงหลัง |
| หัวการ์ด Illustrated 7 | ถ้าตะแกรงหลังหลวมหรือเอียง | จัดตะแกรงหลังใหม่ |
| Steps 8 | ติด tag present_not_tight ทั้งที่ข้อความไม่ได้พูด | ลบ tag ให้ตรงกับข้อความ |

ผลตรวจ (หลักฐาน `b6_leakfix_20261104/balance_report.md`, `review/`):
- `build.py check`: essential units 19/19 ครบทั้ง Manual, Steps, Illustrated, Video
- ไม่พบประโยค “บังอยู่/ไม่ซ่อม/直らない/手前の部品” ในไฟล์ต้นทางอีก
- ไฟล์ที่เปลี่ยนจริงในแอป: ภาพ Illustrated 02, 03, 04, 07 และ Manual หน้า 6 ทั้ง TH/JA/EN, ข้อความ Steps และวิดีโอ (เปลี่ยนเฉพาะฉาก 2, 4, 5)
- ความยาววิดีโอ: TH 119.3 → 129.9 วินาที, JA 175.3 → 194.4, EN 151.2 → 164.1
- เวลาวิดีโอต่อ Area (TH หลังแก้): ตะแกรงหน้า 33.5, ใบพัด 38.3, ตะแกรงหลัง 23.9 วินาที ตะแกรงหลังได้เวลาน้อยที่สุด จึงไม่ถูกเน้น ฉากตรวจของสาม Area ยาวใกล้กัน (8.4 / 10.1 / 8.6)
- ข้อจำกัดที่ยังเหลือ: โครงสร้าง B6 ยังไม่แบ่งตาม Job ต่อ Area แบบต้นแบบ (ตะแกรงหลังมีภาพ “จัดใหม่” แยก ส่วนตะแกรงหน้าและใบพัดอยู่ในภาพประกอบกลับรวม) จะปรับเมื่อผลิต Area อื่นตามต้นแบบ
- ไม่ได้สร้าง APK เก็บข้อมูลจริง; B6 ใหม่อยู่ใน source และ Editor เท่านั้น

## 2. Reader สำหรับ DEV: ต้นแบบตะแกรงหน้า

### การแยกจาก Reader ผู้เข้าร่วม
- แพ็กเกจต้นแบบสร้างจาก `out_v3` แบบคัดลอกไบต์ ไม่ render ใหม่ (`proto_front_guard/package_dev_reader.py`) อยู่ใต้ root แยก `Resources/ResearchReaderS1Dev/B_FG_{th,ja}` และ `StreamingAssets/ResearchReaderS1Dev/`
- approval `S1_DEV_PROTOTYPE_NOT_STUDY` ใช้ได้เฉพาะใต้ root นี้; viewer ปฏิเสธแพ็กเกจ DEV ที่อยู่ผิด root และแพ็กเกจผู้เข้าร่วมที่ติด approval DEV (ทดสอบแล้ว)
- `ResearchBuildFlavor`: รุ่น DEV (define `RESEARCH_DEV_READER_PROTOTYPE` ใส่ตอน build เท่านั้น) เริ่มได้เฉพาะกลุ่ม DEV/TEST; กลุ่มไทย ญี่ปุ่น และ Pilot ถูกปฏิเสธพร้อมเหตุผลบนหน้า Researcher Setup; Task B ใน DEV/TEST ใช้แพ็กเกจต้นแบบ, Training และ Task A ไม่เปลี่ยน
- รุ่นปกติ (ไม่ใส่ define) ทำงานเหมือนเดิมทุกอย่าง

### ความสามารถที่เพิ่มใน Reader
- สารบัญ: ปุ่ม “สารบัญ (n)” ใต้แท็บสี่รูปแบบ เปิดรายการหัวข้อในกรอบ viewer; แถวปัจจุบันมีแถบข้าง; ปุ่ม “ปิด”
- Manual: 6 หัวข้อของหน้า (ตะแกรงหน้าและคลิป, ส่วนที่ต้องรู้จัก, ปกติ/ผิดปกติ, ปุ่มที่ใช้, การทำงานของตัวล็อก, ข้อควรระวัง)
- Steps: เลือก ตรวจ / ถอด / ใส่และล็อก ได้อิสระ (การเลือกไม่ให้เครดิตงาน)
- Illustrated: เลือกภาพตามงาน 4 ภาพ
- Video: เลือก Chapter ตามชื่อ (ตรวจ / ถอด / ใส่และล็อก) พร้อมเวลาเริ่ม; ถ้ากำลังเล่นจะเล่นต่อจาก Chapter ใหม่; เล่น หยุด เลื่อนเวลา และย้อน/ถัดไปทำงานเหมือนเดิม
- หัวแผงแสดงหัวข้อปัจจุบัน เช่น “手順 · 前ガード：付けて固定”
- ไม่มีการบังคับลำดับ ผู้เข้าร่วมเปิดรูปแบบและหัวข้อใดก่อนก็ได้

### การบันทึกเหตุการณ์ (รองรับ H1–H4)
- ทุก event ของ Reader มี `section_id` ของหัวข้อที่อยู่ในมุมมอง
- `REFERENCE_VIEWER_CONTENTS_OPENED/CLOSED`, `REFERENCE_VIEWER_SECTION_SELECTED` (from_section, to_section), `REFERENCE_VIEWER_SECTION_VISIBLE` เมื่อหัวข้อในมุมมองเปลี่ยน (เลื่อน, เปลี่ยนขั้น, เล่น, seek)
- เวลาที่ใช้: `REFERENCE_VIEWER_ACTIVE_END.active_seconds` และ INFORMATION_OPEN/CLOSE เดิม; แยกเวลาต่อหัวข้อได้จากลำดับ SECTION_VISIBLE
- `SESSION_PROTOCOL_VERSIONS` บันทึก `build_flavor` และ `task_b_reader`

## 3. ความเทียบเท่าของภาษา (ต้นแบบ TH 27.8 วินาที, JA 40.6 วินาที)

| Chapter | ภาพที่บันทึกจริง (TH = JA) | เสียง TH | เสียง JA | ความยาว TH | ความยาว JA |
|---|---|---|---|---|---|
| ตรวจ | 1 ภาพนิ่ง, กล้อง clip/guard/mark | 9.1 s | 15.8 s | 10.2 s | 16.8 s |
| ถอด | 114 เฟรม, runner steps เดียวกัน | 7.9 s | 12.8 s | 9.3 s | 13.9 s |
| ใส่และล็อก | 103 เฟรม, ตัดช่วงยก 182 เฟรมเท่ากัน | 5.8 s | 8.1 s | 8.4 s | 10.0 s |

- ภาพ ลำดับ กล้อง จำนวนเฟรมที่บันทึก และช่วงที่ตัดเหมือนกันทุก Chapter; ส่วนต่างทั้งหมดมาจากความยาวเสียงบรรยาย (TH รวม 22.8 s, JA 36.7 s, ต่าง 13.9 s ใกล้ส่วนต่างของวิดีโอ 12.8 s)
- ข้อความบรรยายมีหน่วยข้อมูลเดียวกันทุก Chapter (สภาพปกติ, ใส่อยู่ ≠ ล็อก, ทิศหมุน, ปิดคลิป)
- B6 มีอัตราส่วนเดียวกัน (TH 129.9 s, JA 194.4 s)
- ข้อจำกัดการวิเคราะห์: ห้ามเทียบเวลาดูวิดีโอดิบข้ามภาษา; ใช้สัดส่วนของ Chapter ที่ดู/ความครอบคลุมหัวข้อ หรือ normalize ด้วยความยาว Chapter ของภาษานั้น; เวลาเปิด Manual/Steps/Illustrated ก็ได้รับผลจากความยาวข้อความญี่ปุ่นเช่นกัน
- ภาษาญี่ปุ่นยังเป็น AI draft จนกว่าผู้ตรวจภาษาจริงจะตรวจ

## 4. ผลทดสอบ แยกระดับ

### Source
- `build.py check` 19/19 ทุกรูปแบบ (A 17/17, T 18/18 ไม่เปลี่ยน)
- B6 สร้างใหม่และติดตั้งใน project สำเร็จทั้ง TH/JA/EN; ภาพ/หน้า/วิดีโอที่เปลี่ยนตรวจด้วยตาแล้ว
- แพ็กเกจ DEV: TH/JA ผ่าน ValidatePackage, EN ไม่มี (ตั้งใจ)

### Unity Editor
- Compile ผ่าน ไม่มี error
- DEV Reader tour (`ResearchS1FlowDriver.DevReaderTour`, คลิกด้วย simulated controller ray ไม่ใช้ onClick.Invoke) ในฉากจริง: ไทย Done, ญี่ปุ่น Done; ไฟล์ฉากไม่เปลี่ยน (SHA-256 เท่าเดิม)
  - ครบ Manual / Steps / Illustrated / Video: เปิดสารบัญ, กระโดดไปหัวข้อสุดท้าย แล้วย้อนไปหัวข้อที่ 2; Video เล่น → เลือก Chapter ระหว่างเล่น (เล่นต่อ) → หยุด
  - ปิด Reader แล้วกลับมา Task B ที่ยังทำงานอยู่ ไม่พัก
  - Log ต่อภาษา: CONTENTS_OPENED 8, SECTION_SELECTED 8, SECTION_VISIBLE 12, section_id ว่าง 0 แถว, input = SimulatedController, content_id = `s1.reader.dev.B_front_guard.{th,ja}`, `build_flavor=dev_reader_prototype` (หลักฐาน `reader_dev_20261104/tour_events.md`, ภาพ `D:\VR_TMU\ev1104\devtour6_*`)
- Researcher Setup ในรุ่น DEV: แสดง “ผู้วิจัย | รุ่น DEV: Reader ต้นแบบ Task B · ปิดการเก็บข้อมูลจริงและ Pilot”; ไทย/ญี่ปุ่น/Pilot ถูกปฏิเสธพร้อมเหตุผล; DEV/TEST เริ่มได้
- ข้อบกพร่องที่พบระหว่างทดสอบและแก้แล้ว: หัวข้อท้ายหน้า Manual ไม่ถูกนับเป็นหัวข้อปัจจุบัน; ปุ่มสารบัญของ Video ไม่อัปเดตหลังวิดีโอโหลดเสร็จ; section_id ว่างระหว่าง seek; ข้อความ Setup ถูกทับด้วย layout
- Suites (`D:\VR_TMU\ev1104\final`): Video Chapter 10, Reader Optional Picture 270/0 fail, Reader Wireframe PASS, Compact UI PASS, Focused Operator UI 67, UI Control Acquisition PASS, Content Clarity 428/0 fail, V4 Part Feedback 176, Researcher Localization TH/JA PASS
- ยังไม่ผ่านแต่ไม่ใช่ regression: Reader Refinement 16, Focused Reader Input 15, PDF Zoom 8 — รันบน source ก่อนแก้วันนี้ได้ชื่อข้อที่ล้มเหมือนกันทุกข้อ (fixture เก่าที่ Reader อยู่ในสถานะ Blocked)

### APK
- `Logs/QuestFinalQA/build/ResearchFlowQuest3_DEV_20261104_065328.apk` SHA-256 `6393ac534b36d3a13b6115881c7235e21b6a87a96cba982f626e291195bb8652` (785 MB)
- package id เดิม (`...quest3.technical`) ติดตั้งแบบ update ได้ ไม่ต้องถอนแอป; ชื่อแอป “VR Maintenance Lab DEV”, version 3.4.1.20261104.0653-dev / code 442493
- ภายใน APK มีวิดีโอต้นแบบ TH/JA และ B6 รุ่นใหม่ (ตรวจขนาดไฟล์แล้ว)
- ค่าที่ build เปลี่ยนชั่วคราวคืนครบ; ก่อน build ต้องคืน `ProjectSettings/UnityConnectSettings.asset` บรรทัดเดียว (Cloud Diagnostics = 1 จาก Editor crash วันที่ 2 พ.ย.) กลับเป็นค่า HEAD (สำเนาก่อนคืนอยู่ใน `reader_dev_20261104/`)
- ยังไม่ได้ติดตั้ง รอการอนุญาต

### Quest
ยังไม่ได้ทดสอบ: ความคมของข้อความ/ภาพขยาย/ลูกศร, เสียง, การเลือก Chapter ด้วยคอนโทรลเลอร์จริง, Grip/Trigger ใน Task B หลังกลับจาก Reader, Researcher Setup บนแว่น, การเขียน log บนเครื่อง

## 6. ติดตั้งบน Quest 3 (4 พ.ย. 2026, ได้รับอนุญาตจาก owner)
- สำรองก่อนติดตั้ง: `S1_Delivery_Work_20261028/quest_preinstall_20261104/` ไฟล์ 143 ไฟล์ 1,691,637,462 ไบต์ log 46 รอบ hash ตรงกับบนแว่นทุกไฟล์ (`device_manifest.txt` = `pc_manifest.txt`)
- ติดตั้ง `ResearchFlowQuest3_DEV_20261104_065328.apk` ด้วย `pm install -r` (ไม่ uninstall ไม่ clear data) ผล Success เวลา 15:22
- หลังติดตั้ง: versionCode 442493 / 3.4.1.20261104.0653-dev, SHA-256 ไฟล์ที่ติดตั้ง `6393ac53…bb8652`, firstInstallTime เดิม (2026-09-22), log 46 รอบและ registry.json hash เดิม
- รุ่นเดิมบนแว่นคือ 440066 (`ResearchFlowQuest3_20261102_142658.apk`) ยังอยู่บน PC; รุ่นปกติสำหรับกลับคืน `ResearchFlowQuest3_20261104_071602.apk` (442516)
- ไฟล์ APK ชั่วคราวบนแว่นยังอยู่ที่ `/data/local/tmp/` (ไม่ลบตามเงื่อนไขห้ามลบหลักฐาน)
- เปิดแอปไม่ขึ้นเมื่อไม่มีผู้สวม (proximity=false, ระบบไม่วางหน้าต่าง) จึงยังไม่ได้ตรวจบนแว่น: รุ่น DEV ใน Setup, การปฏิเสธ Study/Pilot, Reader ทั้ง 4 รูปแบบ, เสียง, Grip/Trigger, log
