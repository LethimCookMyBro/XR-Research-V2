# ชุดติดตั้งและเก็บข้อมูล VR Maintenance Lab (S1) สำหรับ Quest 3 และ Windows PC

ชุดนี้ใช้ได้บน PC เครื่องอื่นโดยไม่ต้องใช้เครื่องผู้พัฒนาเดิม ข้อมูลหลักคือสำเนาบน PC ของผู้วิจัย Cloud เป็นสำเนาที่สองเท่านั้น

## โครงสร้างชุดส่งมอบ

| โฟลเดอร์ | เนื้อหา |
|---|---|
| `apk\` | APK สำหรับ Quest 3 และ `BUILD_IDENTITY.json` (Build ID, versionName, versionCode, SHA-256 ของ APK, SHA-256 ของใบรับรองที่เซ็น) |
| `scripts\` | สคริปต์ PowerShell ในเอกสารนี้ |
| `tools\` | `research_export.py`, `quest_profile.py`, `research_cloud_sync.py`, `s1_session_report.py`, `drive.config.example.json` |
| `docs\` | คู่มือนี้ คู่มือทดสอบ Quest และรายงานผลทดสอบของรุ่นนี้ |
| `signing\` (ส่งแยก ห้ามขึ้น Git สาธารณะ) | กุญแจที่ใช้เซ็น APK รุ่นนี้ จำเป็นสำหรับการสร้างรุ่นอัปเดตต่อ |

## 1. เตรียม PC (ครั้งเดียว)

1. ติดตั้ง Python 3.11 ขึ้นไป (เลือก "Add python.exe to PATH") เครื่องมือใช้เฉพาะไลบรารีมาตรฐาน ไม่ต้อง `pip install`
2. ติดตั้ง Android SDK Platform-Tools จากเว็บ Android แตกไฟล์ แล้วเพิ่มโฟลเดอร์ที่มี `adb.exe` ลงใน PATH
3. เปิด PowerShell ในโฟลเดอร์ `scripts\` แล้วอนุญาตสคริปต์ในหน้าต่างนี้: `Set-ExecutionPolicy -Scope Process Bypass`
4. เลือกโฟลเดอร์ข้อมูลบน PC เช่น `D:\ResearchData` ใช้โฟลเดอร์เดิมตลอดโครงการ และสำรองโฟลเดอร์นี้ตามนโยบายของหน่วยงาน

## 2. เตรียม Quest 3

1. เปิด Developer Mode ของแว่นผ่านแอป Meta Horizon บนมือถือของบัญชีที่ใช้กับแว่น
2. เสียบ USB-C เข้ากับ PC ใส่แว่นแล้วกด "Allow USB debugging" (ติ๊ก Always allow)
3. ตรวจ `adb devices` ต้องเห็นแว่นหนึ่งเครื่องสถานะ `device`
4. ชาร์จแว่นให้มากกว่า 50% ก่อนติดตั้งหรือเริ่ม session

## 3. ติดตั้งครั้งแรก หรืออัปเดตรุ่นใหม่

```
.\Install-Quest.ps1 -Apk ..\apk\<ไฟล์>.apk
```

- สคริปต์ตรวจ SHA-256 ของ APK กับ `BUILD_IDENTITY.json` ก่อน
- ถ้าแว่นมีแอปอยู่แล้ว จะสำรองข้อมูลทั้งหมดก่อน (ข้อ 4) และติดตั้งต่อเฉพาะเมื่อสำรองครบและ hash ตรง
- ไม่ถอนแอปและไม่ล้างข้อมูลแอปในทุกกรณี ถ้าแอปกำลังเปิดอยู่ สคริปต์จะหยุด ให้จบหรือยุติ session ในแอปก่อน
- หลังติดตั้งจะตรวจ versionCode และ SHA-256 ของ APK ที่ติดตั้งจริงบนแว่น ผลต้องเป็น `INSTALL_VERIFIED`
- การอัปเดตทับได้เฉพาะ APK ที่เซ็นด้วยกุญแจเดียวกัน ถ้าสร้าง APK บนเครื่องที่ไม่มีกุญแจใน `signing\` แว่นจะปฏิเสธการอัปเดต อย่าแก้ด้วยการถอนแอป เพราะข้อมูลบนแว่นจะหาย

## 4. สำรองและกู้คืนข้อมูลบนแว่น

```
.\Backup-Quest.ps1 -Out D:\QuestBackups
```

คัดลอก `ResearchSessionRegistry` ทั้งหมด (registry, log ทุก session, export ที่รอส่ง) และ APK ที่ติดตั้งอยู่มาไว้ที่ PC แล้วเทียบรายชื่อไฟล์และ SHA-256 กับบนแว่นทุกไฟล์ ผลต้องเป็น `BACKUP_VERIFIED` สคริปต์ไม่เขียนหรือลบอะไรบนแว่น

การกู้คืน: ไม่เขียนทับ registry บนแว่นด้วยสำเนาเก่า ข้อมูลที่สำรองแล้วใช้วิเคราะห์ได้โดยตรงด้วย `research_export.py ingest` ถ้าจำเป็นต้องคืนไฟล์ขึ้นแว่นจริง ให้ติดต่อผู้ดูแลระบบและทำบนสำเนา โดยไม่ลบของเดิม

## 5. เริ่ม session (ผู้วิจัย)

1. ตรวจว่าแว่นชาร์จแล้ว คอนโทรลเลอร์ทั้งสองข้างพร้อม และไม่มีหน้าต่างอื่นค้าง
2. เปิดแอป "VR Maintenance Lab" บนแว่น หน้าแรกคือ Researcher Setup
3. เลือกกลุ่ม (Thai / Japanese / Pilot / DEV) แล้วสร้างหรือเลือกรหัสผู้เข้าร่วม ลำดับงาน AB/BA และ layout P/Q ถูกจัดสมดุลโดยระบบ แก้ด้วยมือได้เฉพาะ session ทดสอบ
4. ยืนยันความยินยอมนอกแอปตามเอกสารจริยธรรมฉบับที่ใช้จริง แอปไม่ได้แทนเอกสารยินยอม
5. กด Start แล้วส่งแว่นให้ผู้เข้าร่วม ลำดับในแอป: Training สั้น (ควบคุม Grip/Trigger/ปล่อย/ติดตั้ง/ดูสภาพชิ้นส่วน/ตรวจผลงาน/เปิด-ปิดข้อมูลอ้างอิง/พัก) → Task แรก → จุดพักผู้วิจัย → Task ที่สอง → จบ
6. ระหว่าง session ผู้เข้าร่วมกด "พัก" ได้ทุกเมื่อ เวลา Task ไม่นับระหว่างพัก
7. เมื่อจบ แอปปิด log และสร้าง export ให้อัตโนมัติ ไม่ต้องใช้อินเทอร์เน็ต ถ้าถอดสายหรือไม่มีเน็ต ข้อมูลยังอยู่บนแว่นและรอส่ง

## 6. ส่งข้อมูลจากแว่นเข้า PC และตรวจความครบ

```
.\Export-Sessions.ps1 -DataRoot D:\ResearchData
.\Check-Session.ps1  -DataRoot D:\ResearchData
```

- `Export-Sessions` ดึงเฉพาะ bundle ที่จบแล้ว ตรวจ hash ทุกไฟล์ เขียนใบรับบน PC แล้วจึงแจ้งแว่นว่าส่งสำเร็จ รันซ้ำได้ ไม่สร้างข้อมูลซ้ำ ถ้าสายหลุดกลางทางให้รันใหม่ จะส่งต่อจากที่ค้าง
- `Check-Session` ตรวจ hash ซ้ำและแสดง `data_complete`, ลำดับงานจริง, ความผิดพลาด และรายการที่ขาด (เช่น แบบสอบถาม วิดีโอ FPV) ข้อมูลที่ขาดคงเป็น missing พร้อมเหตุผล ไม่มีการเติมค่า
- ไฟล์ใน bundle: `raw\` (ไฟล์ต้นฉบับจากแว่น ไม่แก้ไข), `task_A\`, `task_B\` (events, movement, metrics รวม H1–H4), `task_summary.json`, `failure_episodes.json`, `event_timeline.csv`, `qc\report.json`, `manifest.json`
- แบบฟอร์ม NASA-TLX, อาการ, สัมภาษณ์ และความยินยอม กรอกนอกแอปแล้วผูกกับ participant/session ด้วย `research_export.py ingest --questionnaires <ไฟล์>` ตาม `research-questionnaire.schema.json`
- วิดีโอ FPV ใช้เฉพาะผู้เข้าร่วมที่ยินยอมจริง ไม่มีความยินยอมให้คงเป็น missing

## 7. Cloud (สำเนาที่สอง)

1. ติดตั้ง client ของผู้ให้บริการที่ได้รับอนุมัติ (Google Drive for desktop หรือ OneDrive ของหน่วยงาน) และ sign-in ด้วยบัญชีโครงการ ไม่ใช้บัญชีส่วนตัว
2. คัดลอก `tools\drive.config.example.json` ไปไว้นอกโฟลเดอร์โปรเจกต์ (เช่น `C:\private\`) แล้วแก้ `target_root` เป็นโฟลเดอร์ sync, `drive_folder_id`, `site_code` และ `enabled: true` ไฟล์นี้ห้ามมีรหัสผ่านหรือ token (สคริปต์จะปฏิเสธ)
3. ข้อมูลผู้เข้าร่วมจริง (TH/JP) จะค้างที่ `cloud_pending_approval` จนกว่าจะใส่ `allow_study: true` พร้อมข้อมูลอนุมัติจริง (`approved_by`, `ethics_ref`, `approved_utc`, `region`, `access_scope`, `encryption_at_rest: true`) กรอกจากเอกสารอนุมัติเท่านั้น
4. คำสั่ง:
```
.\Sync-Cloud.ps1 -DataRoot D:\ResearchData -Config C:\private\drive.config.json -Action sync
.\Sync-Cloud.ps1 -DataRoot D:\ResearchData -Config C:\private\drive.config.json -Action provider-verify
```
`cloud_folder_verified` แปลว่าไฟล์ในโฟลเดอร์ sync ตรงกับ PC ยังไม่ใช่หลักฐานว่าอัปโหลดเสร็จ ต้องได้ `provider_verified` (ดาวน์โหลดกลับจาก Google Drive ตาม file ID และเทียบ SHA-256) ถ้าออฟไลน์จะเป็น `cloud_queued_offline` และรันซ้ำภายหลังได้ ไม่มีคำสั่งใดลบข้อมูลบน PC

## 8. Rollback และข้อจำกัด

- เก็บ APK ทุกรุ่นที่เคยติดตั้งพร้อม `BUILD_IDENTITY.json` (`Backup-Quest` เก็บ `installed.apk` ให้ทุกครั้ง)
- การกลับไปรุ่นเก่าใช้ `Install-Quest.ps1 -AllowDowngrade` ได้เฉพาะเมื่อ: ไม่มี session ค้าง (InProgress), ข้อมูลทั้งหมดส่งเข้า PC และตรวจครบแล้ว, และเซ็นด้วยกุญแจเดียวกัน
- APK รุ่นส่งมอบนี้เป็น release build (ไม่ใช่ debug) Android จึงไม่ยอมให้ติดตั้งรุ่นที่ versionCode ต่ำกว่าทับ ถ้าต้องกลับไปใช้รุ่นเก่า ให้ผู้พัฒนาสร้าง APK แก้ไขที่มี versionCode สูงกว่าแทน ห้ามถอนแอปเพราะข้อมูลบนแว่นจะหาย
- session ที่เริ่มในรุ่นใหม่ห้าม resume ในรุ่นเก่า รุ่นเก่าไม่รู้จัก training/layout version ใหม่ ให้ยุติ session นั้นในรุ่นที่เริ่ม
- ห้ามแก้ registry เพื่อให้รุ่นเก่าเปิดได้ และห้ามถอนแอปเพื่อแก้ปัญหาการเซ็น

## 9. สำหรับผู้พัฒนาต่อ

- Unity 6000.3.20f1, Android build support, URP 17.3, Meta XR / Interaction SDK และ OpenXR ตาม `Packages/packages-lock.json`
- ฉากหลัก `Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity`; build ผ่านเมนู `VR Research/Research Flow/Build Quest APK`
- ฟอนต์ภาษาญี่ปุ่นต้นฉบับต้องดาวน์โหลดตาม `Assets/VRMaintenanceResearch/ResearchFlow/UI/Fonts/SOURCE.md`
- ใช้กุญแจใน `signing\` เพื่อให้ APK ใหม่อัปเดตทับรุ่นที่อยู่บนแว่นได้
- ทดสอบเครื่องมือ PC: `cd Tools` แล้ว `python -B -m unittest discover -p "test_*.py"`
