# อัปโหลดอัตโนมัติจาก Quest ขึ้น Cloud (ไม่ต้องตั้งค่าแว่น ไม่ต้องเปิด PC)

ปลายทางหลัก (ตัดสินใจ 2026-11-08): **Railway Storage Bucket** `s1-research-intake` (ภูมิภาค Singapore) ใน project เดียวกับ relay
Google Drive ยังเป็นทางเลือกสำรองได้ (หัวข้อท้ายไฟล์) และเส้นทาง PC (`Export-Sessions.ps1`) ยังใช้ได้ตามเดิม

## ใช้งานอย่างไร
1. ติดตั้ง APK บนแว่นเครื่องไหนก็ได้ (`Install-Quest.ps1`) ไม่ต้อง provision ไม่ต้องต่อสายเพิ่ม
2. แว่นต่อ Wi-Fi ครั้งแรก → แอปลงทะเบียนตัวเองกับ relay และได้ชื่อโฟลเดอร์ตามลำดับเครื่องที่ติดต่อเข้ามา: `Quest_01`, `Quest_02`, ...
   ติดตั้งแอปใหม่บนแว่นเครื่องเดิม (กุญแจเซ็น APK เดิม) ได้โฟลเดอร์เดิม; factory reset แว่น = ได้หมายเลขใหม่
3. ผู้เข้าร่วมทำเสร็จ → แอปสร้าง bundle ที่แก้ไขไม่ได้บนแว่นทันที (ไม่ต้องมีเน็ต) แล้วส่งขึ้น relay ทุก 45 วินาทีเมื่อมีเน็ต ทีละชิ้น 256 KiB พร้อม SHA-256
   เน็ตหลุด/ปิดแอป → ส่งต่อจากจุดเดิม ไม่ส่งซ้ำ ไม่ลบข้อมูลบนแว่น
4. Relay ตรวจ SHA-256 แล้วเขียนลง bucket จากนั้น **อ่านกลับจาก bucket** เทียบ SHA-256 กับ manifest ของแว่น ตรงกันจึงเป็น `provider_verified`
   และแว่นเก็บใบรับ `CloudReceipts/<bundle>.json`

โครงสร้างใน bucket:
```
Quest_01/cohort_DEV/participant_DEV001/session_<id>/run_<id>/bundle_<id>/manifest.json, COMPLETE.json, raw/...
Quest_02/cohort_DEV/participant_DEV001/...   ← รหัสเดียวกันจากอีกเครื่อง ไม่ทับกัน
```
สถานะบนแว่น (`CloudStatus/<bundle>.json`): `queued` / `uploading` / `staged_verified` (relay มีสำเนาที่ตรวจแล้ว รอ bucket) / `provider_verified`

## ผู้วิจัยดึงข้อมูลลง PC
```
.\Pull-Cloud.ps1 -Dest D:\S1_Cloud_Pull
```
ดาวน์โหลดทุกอย่างแยกโฟลเดอร์ต่อเครื่อง ข้ามไฟล์ที่มีแล้ว ตรวจ SHA-256 ทุก bundle และเขียน `PULL_REPORT.json`
ต้องมี Railway CLI ที่ล็อกอินแล้ว (`railway login`) และ `pip install boto3` กุญแจ bucket ถูกส่งเข้าโปรแกรมผ่าน `railway run` ไม่บันทึกบน PC
ดูไฟล์ผ่านเว็บได้ที่ Railway dashboard → project S1-Quest-Research-Relay-DEV → bucket `s1-research-intake`

ตรวจว่าแว่นเครื่องหนึ่งยังมี session ค้างส่งไหม: `.\Check-CloudQueue.ps1`

## ขอบเขตความปลอดภัย (อ่านก่อนใช้กับข้อมูลจริง)
- APK มี "กุญแจติดตั้ง" ฝังอยู่ (`Resources/S1CloudRelay.json` ไม่อยู่ใน Git; สำเนาที่ `%USERPROFILE%\.s1_relay\enroll_key.json`)
  กุญแจนี้ทำได้อย่างเดียวคือลงทะเบียนแว่น จำกัดไม่เกิน `S1_RELAY_MAX_DEVICES` (ตั้งไว้ 20) เครื่อง
  แต่ละแว่นได้ token ของตัวเอง ส่งได้เฉพาะเข้าโฟลเดอร์ของตัวเอง **ไม่มีใครอ่าน แสดงรายการ หรือลบข้อมูลผ่าน relay ได้**
- ความเสี่ยงที่เหลือ: คนที่ได้ไฟล์ APK ไปสามารถลงทะเบียนเครื่องปลอมและส่งข้อมูลขยะ (DEV) เข้ามาได้จนครบโควตา
  เปลี่ยนกุญแจ: สร้างกุญแจใหม่ ตั้ง `S1_RELAY_ENROLL_KEY_SHA256` ใหม่ และ build APK ใหม่ (แว่นที่ลงทะเบียนแล้วยังใช้ token เดิมได้)
- **ข้อมูลผู้เข้าร่วมจริง (TH/JP) ยังไม่ถูกส่ง** จนกว่า relay จะมี `S1_RELAY_STUDY_APPROVAL_JSON` จากเอกสารจริยธรรมจริง
  (approved_by, ethics_ref, approved_utc, region, access_scope, encryption_at_rest=true) รวมถึงอนุมัติที่เก็บข้อมูล Railway ภูมิภาค Singapore
  เมื่อใส่แล้ว แว่นจะรู้เองในการลงทะเบียนรอบถัดไป ไม่ต้องตั้งค่าแว่นใหม่ — ตอนนี้ส่งได้เฉพาะ DEV/TEST/PILOT
- Relay เก็บสำเนาที่ตรวจแล้วบน volume ส่วนตัวของ Railway ด้วย

## ทดสอบ relay จริงโดยไม่ใช้แว่น (ข้อมูลสังเคราะห์เท่านั้น)
```
python Tools\s1_cloud_relay\simulate_quests.py
```
จำลองแว่น 2 เครื่องส่ง DEV001 พร้อมกัน เครื่องหนึ่งเน็ตหลุดกลางทางแล้วส่งต่อ ผ่านเมื่อทั้งสองเป็น `provider_verified`
เครื่องจำลองได้ชื่อ `SIM_NN` ไม่กินหมายเลข `Quest_NN` ของแว่นจริง

## ทางเลือก: provision ทีละเครื่อง / Google Drive
- `Provision-QuestCloud.ps1` ยังใช้ได้ (ไฟล์ provision บนแว่นมีลำดับก่อนการลงทะเบียนอัตโนมัติ) ใช้เมื่อต้องการ token ที่ไม่ผ่านกุญแจใน APK
- Google Drive: ถ้าไม่ได้ตั้งค่า bucket relay จะใช้ Drive แทน ขั้นตอน: `setup_google_drive.py` (โฟลเดอร์ใหม่แบบ Restricted, scope drive.file,
  OAuth app ต้อง "In production") แล้ว `Set-RelayEnv.ps1 -Google` — ดู docstring ใน `Tools/s1_cloud_relay/setup_google_drive.py`
