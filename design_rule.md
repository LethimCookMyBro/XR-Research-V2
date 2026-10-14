# S1 UI / Workflow Design Rules

Version: 1.1 — 2026-10-14

## Latest priority: advisor-controlled UI revision (implementation pending)

Read `Documentation/S1/ADVISOR_UI_AMENDMENT_20261014.md` first. The user now requests four instruction choices at fixed positions, no free participant panel dragging, shorter non-redundant training, and controlled table layout. For the revised technical-demo policy this supersedes the free-drag/grip requirements below. Remove the drag exercise together with the drag interaction; do not leave training impossible to finish. Keep the baseline S1 file and old evidence unchanged, record policy/version differences, and preserve voluntary information selection, task mechanics, safety and researcher gates. The old grip requirements below apply only to retained legacy movable-panel tests, NOT the new participant UI. This document change is not evidence that runtime UI has been fixed.

Cloud start: `Documentation/S1/CLOUD_START_HERE.md`. A cloud checkout cannot assume access to this PC's D: drive, its Unity Editor or USB Quest. Use a verified authenticated connector when available, otherwise keep local/device validation explicitly pending. The owner's 14 October permission allows guarded technical Quest testing; the old Unity/PC-only sentence below is historical and is not the current device scope.
Owner: ผู้วิจัย / ผู้ใช้โครงการ XR-Research-V2
Purpose: ข้อกำหนด UI ที่ใช้ก่อนแก้และก่อนส่งงาน พร้อมรายการตรวจที่ป้องกันการแก้แล้วกลับมาพังซ้ำ
Status: กฎออกแบบและผล audit ไม่ใช่คำรับรองว่า implementation ผ่านแล้ว

## 0. อ่านส่วนนี้ก่อนลงมือ

1. รักษา Task A/B กลไก GPU/สาย/logger ที่ทำได้แล้ว ห้ามย้อนเพื่อแก้หน้าตา UI
2. หนึ่ง visual language: พื้นขาวทึบ ตัวหนังสือเข้ม น้ำเงินสำหรับปุ่มหลัก ฟ้าอ่อนสำหรับปุ่มรอง ไม่ใช้สีซีดแทนทุกสถานะ
3. ทุกแผงที่ลากได้เห็นเพียงขีดสั้น ไม่มีแคปซูลหรือกรอบหนา แต่พื้นที่รับการกดต้องใหญ่พอ
4. หน้าพักปกติมี “ทำต่อ” ปุ่มเดียวเมื่อผู้เข้าร่วมมีสิทธิ; researcher-owned states ต้องมีทางควบคุมของผู้วิจัยจริง
5. หน้างานต้องบอกเป้าหมายชัด ไม่ใช่ย่อหน้ายาวก้อนเดียว และต้องไม่ทำสาระ S1 หายเพราะย่อข้อความ
6. Reference/ตรวจผลเป็นตัวเลือกเท่าเทียมระหว่าง Task ไม่ออกแบบชี้นำว่าต้องอ่านก่อนหรือกดตรวจเมื่อใด
7. แยกหน้าในเกม, หน้าผู้วิจัย, เครื่องมือ Unity และเครื่องมือถ่ายสื่อ อย่าลบสิ่งที่ดูแปลกโดยยังไม่รู้เจ้าของ
8. ตรวจภาพ implementation และการชี้/กด/เลื่อน/ลากจริงหลังแก้ ไม่ใช้ภาพ GEN หรือ Button.onClick.Invoke เป็นหลักฐานแทน
9. ทุกหน้าที่รอ ต้องบอกว่ารอใคร รออะไร และกลับมาอย่างไร; ห้ามมีทางตันที่มีแต่ข้อความรอ
10. ก่อนรายงานเสร็จ อ่านกฎนี้อีกครั้ง ตรวจส่วนที่กระทบ และแก้ข้อผิดที่ทำซ้ำได้; ส่วนที่ยังตรวจไม่ได้ต้องระบุ ไม่ทำเครื่องหมายผ่านแทน

## 1. ลำดับข้อกำหนดและขอบเขต

- ข้อความล่าสุดของผู้ใช้เป็น authority ด้านการออกแบบ; สเปก S1 เป็น authority ของโจทย์ เวลา เกณฑ์ Inspect และข้อมูล
- ภาพอ้างอิงช่วยเรื่องรูปแบบ ไม่เปลี่ยนคำตอบ/ลำดับการทดลอง/สิทธิ และไม่คืนแบบที่ผู้ใช้ยกเลิกแล้ว
- ขาว–ฟ้าแทนแนว navy เก่า; Pause ปุ่มเดียวแทน concept A รุ่นสามปุ่ม; grip ขีดเดียวใช้กับทุกแผง ไม่ใช่เฉพาะ Reader
- ฟ้าอ่อนไม่ผิดในตัวเอง: ผิดเมื่อผู้ใช้แยก enabled/disabled/selected/primary ไม่ออก หรืออ่านข้อความไม่ชัด
- การแก้บั๊ก/layout ไม่ใช่การอนุมัติเปลี่ยนเนื้อหาการทดลอง ย้ายสาระสำคัญไปซ่อนในสื่อที่ต้องเลือกเปิดเอง หรือปรับสูตรวัด
- รอบปัจจุบัน Unity/PC + Build Only; ไม่ deploy/ADB/Quest โดยอาศัยไฟล์นี้เป็นสิทธิใหม่
- ไฟล์นี้ไม่สร้าง watchdog งานเบื้องหลัง หรือสิทธิทดแทนเครื่องมือ; เป็นเกณฑ์ที่ agent ต้องใช้ระหว่างการทำงานจริง
- ไม่ต้อง GEN ทุกหน้า ไม่ต้องสร้าง design system/framework ใหม่ หากปรับ component เดิมได้

## 2. แหล่งที่ต้องใช้ ไม่กวาดอ่านประวัติทั้งโครงการ

Canonical project: `D:\VR_TMU\XR-Research-V2`
Canonical scene: `Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity`
Latest S1 handoff: `D:\VR_TMU\S1_Unity_PC_20261011\CURRENT.json` และ status ที่มันอ้างถึง
Audit baseline report: `D:\VR_TMU\S1_Unity_PC_20261011\claude-takeover-20261013\STATUS.md`
S1: `D:\VR_TMU\S1_Unity_PC_20261011\spec\VR_TaskSpec_S1\VR_TaskSpec_S1_LOCKED_TH.md`
ใช้ `content_contract.json` และ `verification_design.json` ในชุดเดียวกันเพื่อตรวจความครบ

Reference search เริ่มจาก `D:\VR_TMU\Unity_Pic_REF` และ `Logs/QuestFinalIntegration/reader-20261008/GALLERY.html` พร้อม manifest ที่อ้างถึง
Pause ปัจจุบัน: `D:\VR_TMU\S1_Unity_PC_20261011\pause-one-button-20261012`
ภาพ Sept/GEN/fixture ไม่ใช่สถานะปัจจุบันอัตโนมัติ; ระบุภาพใดเป็น reference, before, after, historical หรือ TEST
หาภาพ wireframe/GPT เก่าที่ไม่มีอยู่จริงไม่พบให้บันทึก ไม่แต่ง path และไม่หยุดงานทั้งหมดเพื่อรอภาพ

## 3. สี ตัวอักษร ไอคอน และองค์ประกอบร่วม

### 3.1 บทบาทสี

| บทบาท | กฎ |
|---|---|
| Surface | ขาวทึบหรือขาวอมฟ้าอ่อนสม่ำเสมอ ไม่โปร่งจนฉากรบกวนข้อความ |
| Heading/body | ใช้ Ink เข้ม; Muted เฉพาะข้อมูลรองที่ยังอ่านชัด ไม่ใช้จางทั้งย่อหน้า |
| Primary | น้ำเงิน + ตัวอักษรขาว สำหรับ action หลักที่ระบบรอ เช่น เริ่ม/ทำต่อ |
| Secondary | ฟ้าอ่อน + Ink เข้ม; enabled ต้องยังดูเป็นปุ่มที่กดได้ |
| Selected tab | บอกว่าเลือกอยู่ ไม่ดูเหมือนคำแนะนำว่าควรกดก่อน |
| Disabled | ต่างจาก enabled พร้อมเหตุผลใกล้จุดใช้งาน ไม่ลด alpha ของทั้งหน้าจนอ่านไม่ได้ |
| Success/failure/wait | ใช้ icon + ข้อความ ไม่อาศัยสีอย่างเดียว; สีสถานะมีหน้าที่ ไม่เพิ่ม palette ตกแต่ง |

ใช้ token เดิมใน `ResearchUITheme` เป็นฐาน แล้วตรวจทุกผู้สร้าง UI ใช้บทบาทเดียวกัน
ตรวจสีที่ render จริงหลังคูณ Graphic.color, CanvasRenderer, ColorBlock, parent CanvasGroup และ animation ไม่ตรวจแค่ค่าคงที่
อย่าบังคับ Reference/Inspect ให้ปุ่มหนึ่งน้ำเงินเด่นอีกปุ่มจางเพียงเพื่อมี primary ใน Task

### 3.2 Typography/layout

- Title, goal, body, status, actions ต้องเป็นลำดับที่แยกได้; ไม่ย่อ font เพื่อยัดข้อความจนเสร็จ
- ใช้ช่องไฟและ margin ที่สม่ำเสมอจากระบบเดิม ไม่เพิ่มกล่อง/border ทุกย่อหน้า
- วัดพื้นที่หัวข้อหลังหัก Pause/utility จริงก่อนคำนวณ wrapping และความสูง
- ตรวจไทยรวมสระ/วรรณยุกต์ ญี่ปุ่น และ mixed-script titles ทุก surface; human language review แยกจาก glyph/layout tests
- ข้อความในปุ่มต้องตรงคำที่อธิบายบนหน้าจอ เช่น “ตรวจผล” และ “เอกสารอ้างอิง”
- ก่อนรายงานความอ่านง่าย VR ต้องระบุระยะ/ขนาดจริงและมุมมอง; Game View zoom 1.2x หรือ font pixels ไม่ใช่ความคมผ่านเลนส์
- อนุญาตข้อความหลายบรรทัดเมื่อจำเป็น แต่ต้องจัดกลุ่ม; ไม่บังคับจำนวนบรรทัดจนสาระวิจัยหาย

### 3.3 Icons/input feedback

ไอคอนใช้ family/น้ำหนักเส้น/optical size เดียวกัน พร้อมชื่อสั้นเมื่อความหมายไม่ชัด
ไม่ใช้ตัวอักษร Unicode ที่ fallback กลายเป็นสี่เหลี่ยมแทน asset โดยไม่ตรวจ
Reticle และ controller ray เป็น feedback การเล็ง ไม่ใช่ของตกแต่งที่จะลบโดยอัตโนมัติ
ไม่มี pulse/scale ที่ทำให้ target หนีระหว่างเล็ง; ไม่ smoothing raw tracking เพื่อทำภาพนิ่ง
รักษา haptic เฉพาะ action ที่ยอมรับและเส้นทาง input เดิม; ไม่สั่นทุก hover sample

## 4. Grip และการวางแผง — ครอบคลุมทุกหน้า

- Idle/hover/held: เห็นขีดสั้นเดียว ไม่มี capsule พื้นฟ้า กรอบหนา หรือ plate ใหม่ใต้แผง
- ใช้ generous invisible hit area ที่ไม่ทับปุ่ม เนื้อหา scrollbar หรือ video timeline
- ตรวจขอบ canvas/raycast/collider ว่าครอบคลุม hit area ไม่ใช่เห็นขีดแต่กดไม่ติด
- คำใบ้ “ย้ายแผง” ใช้เมื่อจำเป็น ไม่ลอยค้าง และต้องไม่เปิดคำสั่งฝึกใน Task
- Task/Tutorial/Reference/หน้าที่มี grip ใช้ข้อกำหนดเดียวกัน; Restore ของหน้าต่างที่ย่อเป็นอีก control อย่าทำให้กำกวมว่าเป็น grip
- เปลี่ยนสื่อ เปลี่ยนภาษา เปิดคืน Theme หรือ LateUpdate ต้องไม่สร้าง capsule กลับมา
- แผงอยู่นิ่งหลังวาง ไม่หมุนตามหัวทุกเฟรม ไม่ auto-recenter ระหว่างกด/ลาก/เลื่อน/ถือของ
- หลัง teleport/เปลี่ยนสถานะจัดแผงได้ตามจังหวะที่กำหนดและ collision guards เดิม ไม่ย้ายโลกหรือ XR Origin แทน
- การมองออกจากแผงเพื่อทำงานไม่ใช่เหตุเปิด Recovery bar และไม่ล้างเครดิต Tutorial
- ตรวจปล่อย/ยกเลิก/focus loss/มืออีกข้างแย่ง/เรียกแผงกลับโดยไม่มี stuck-held หรือ snap ที่ไม่อธิบาย

## 5. แยกสามพื้นผิว อย่ายกแบบ Reader ไปทับทั้งระบบ

1. **Participant task surface:** goal/บริบทที่จำเป็น + controls กลาง ไม่เต็มด้วยเอกสารซ่อม
2. **Reference Reader:** header + sidebar สี่สื่อซ้าย + viewer ขวาคงกรอบ ตาม wireframe ผู้ใช้
3. **Researcher controls:** รหัส เงื่อนไข ความพร้อม และการยืนยันช่วงพัก แยกจากผู้เข้าร่วม

รูปร่างต่างได้ตามหน้าที่ แต่ต้องใช้สี/ฟอนต์/ช่องไฟ/ไอคอนตระกูลเดียวกัน
ไม่แสดงหน้าผู้วิจัยกับโจทย์ซ้อนกันในมุมมองผู้เข้าร่วมหลังเริ่มรอบ
ห้ามให้ชั้น capture/debug กิน ray หรือบัง Welcome/Task; ไม่มี public content ก่อนจังหวะที่สเปกอนุญาต

## 6. หน้างาน S1: จัดเนื้อหา ไม่ลบข้อกำหนด

ปัญหาคือใช้ย่อหน้าสเปกทั้งก้อนเป็น active panel ไม่ใช่ว่า S1 ไม่มีโจทย์
รักษาหัวข้อ “งานที่ 1 จาก 2” ตามลำดับจริง ไม่ใช้ A/B ให้ผู้เล่นสับสน
จัดเป็นชื่อ/เป้าหมายเด่นหนึ่งส่วน ข้อกำหนดการใช้งานสั้นเป็นกลุ่ม และแถว controls ที่อ่านออก
แสดงโจทย์ครบในช่วงที่สเปกกำหนดก่อนเริ่มลงมือ และเข้าถึงได้ระหว่าง Task โดยไม่เปลี่ยนการนับ information use
ไม่เพิ่มหน้าอ่านบังคับ ปุ่มยืนยันอ่าน หรือเปิด Reference ก่อนเริ่มเวลาเพียงเพื่อแบ่งข้อความ

### 6.1 สาระ Task A ที่ห้ามหายเมื่อย่อหรือจัดรูปแบบ (§5.1)

- ประกอบจากชุดฐานและชิ้นส่วนที่จัดไว้ ให้ตรง W2 ทั้งหน่วยความจำ หน่วยเก็บข้อมูล และกราฟิก
- ยึดและเชื่อมต่อให้พร้อม แล้วใช้ “ตรวจผล”
- ข้อกำหนดสถานี รายละเอียดชิ้นส่วน และวิธีประกอบอยู่ใน “เอกสารอ้างอิง”
- เลือกวิธี/ลำดับเองได้ ไม่จำเป็นต้องใช้ทุกชิ้น
- ชุดฐานหยุดและแยกไฟแล้ว ไม่ต้องถอด CPU/ชุดระบายความร้อน/PSU และไม่ติดตั้งระบบปฏิบัติการ

จัด semantic mapping ระหว่างต้นฉบับกับ copy ใหม่; ไม่ย้ายข้อจำกัดเหล่านี้ไปซ่อนเฉพาะในคู่มือแล้วถือว่าเทียบเท่า
ตัวเลข W2/คำตอบตัวเลือกยังอยู่ในสื่อทั้งสี่ตาม S1 ไม่เพิ่มลง active toolbar หรือไฮไลต์ชิ้นถูก

### 6.2 สาระ Task B ที่ห้ามหาย (§6.1)

พัดลม F2 รหัส B-04 เพิ่งประกอบหลังขนส่ง มีรายงานเสียงสั่นครูดผิดปกติ; ตรวจและแก้ให้พร้อมทดสอบ
ประกอบส่วนป้องกันครบก่อน “ตรวจผล”; หยุดและแยกไฟขณะจับชิ้นส่วน; สื่อมีสาเหตุ วิธีสังเกต และวิธีแก้
อย่าเผย fault ที่ซ่อนอยู่จาก active UI หรือเปิดบทเฉลยตามชิ้นที่จับ

### 6.3 กฎ active controls

Reference/Inspect เป็น peers กดเมื่อผู้เล่นเลือกเอง; Pause utility เข้าถึงได้
Examine ใช้ชื่อและ icon ต่างจาก Inspect และมีบทฝึกอธิบายผลแต่ละอย่าง
ไม่มี repair checklist ที่ฟ้องชิ้นถูก จำนวนงานแก้ที่เหลือ คะแนนกลยุทธ์ หรือ next-repair อัตโนมัติ
การลดความสูงแผงต้องไม่ทำให้ชนโต๊ะ/บังมือ/เอื้อมไม่ถึงหรือหลุดกล้อง; ตรวจ P/Q และทั้งสองสถานี

## 7. Reader: ข้อกำหนดรายรูปแบบ

| รูปแบบ | Controls ที่จำเป็น | สิ่งที่ไม่ควรมี |
|---|---|---|
| PDF | เลื่อนต่อเนื่อง; เลขหน้ารอง; jump arrows เฉพาะมีเหตุใช้ | ปุ่ม Next ใหญ่บังคับทุกหน้า, progress ว่าอ่านเข้าใจแล้ว |
| Steps | ดัชนี “ขั้นที่ …”; เปลี่ยน/ย้อนตามเนื้อหา | การกดขั้นแล้วได้ repair credit, scrollbar ทั้งที่เนื้อหาพอดี |
| Images | ภาพใหญ่ caption สั้น; เปลี่ยนรูปเมื่อมีหลายรูป | หน้า1/1, infographic เป็น PDF ย่อจนอ่านไม่ออก |
| Video | viewport ใหญ่รักษาสัดส่วน; Play/Pause/Replay ตำแหน่งเดียว; time/seek | generic pager, หน้า1/1, สองปุ่มเล่น/พักเด่นพร้อมกัน, handle สี่เหลี่ยมใหญ่ |

ทุกแบบอยู่ใน viewer คงที่ ไม่ปรับทั้งหน้าต่างกระโดดเมื่อสลับรูปแบบ
Sidebar icon+ชื่อครบสี่แบบ ไม่คืน tabs งาน/ชิ้นส่วน/อาการจาก concept เก่า
Video Pause แยกคำสั่งจาก Session Pause; ปิด/ย่อ/สลับต้องหยุดตาม lifecycle เดิมและไม่มีเสียงหลุด
เวลาจริงต้องตรง duration (8วินาทีไม่ใช่8นาที); skip interval เหมาะกับความยาวคลิป; no autoplay ข้ามสื่อ
รักษา scroll/page/chapter state ตาม document/version/language/task/session; event เก่าไม่เปลี่ยนสื่อใหม่
Loading/error/retry/missing content มีสถานะจริง ไม่เป็นหน้าขาวหรือปุ่มที่กดไม่ได้ไร้เหตุผล
S1 content ทั้ง2Task×TH/JP×4แบบต้องครบตาม contract; DEMO decoder pass ไม่ใช่ content acceptance

## 8. Researcher setup และการรอผู้วิจัย

### 8.1 ตั้งค่ารอบ

- ผู้วิจัยเห็นรหัส ภาษา ลำดับ AB/BA และ layout P/Q ชัด ไม่ต้องเดาว่า Auto เลือกอะไร
- หน้าผู้วิจัยทุกทางต้องแสดง assignment ที่สอดคล้องกัน ไม่ให้ Editor Console มีความสามารถคนละชุดกับ SetupView โดยไม่แจ้ง
- Controller เป็นวิธีของ study; Hand Tracking/Technical Both เป็นเครื่องมือ DEV ขั้นสูง ไม่เป็นคำถามผู้เข้าร่วมหรือทางเปลี่ยน study input
- session ที่เริ่มแล้วล็อก identity/config; แสดงเหตุผลเมื่อ Resume/Start ไม่พร้อม โดยไม่เอา ID summary เขียนทับ readiness failure
- Resume Saved Session กับ Resume หลังพักเป็นคนละ action ต้องใช้ชื่อและสถานะไม่กำกวม
- แผงสรุปยาวต้องอ่านครบด้วย layout/scroll ที่ใช้ได้ ไม่ fix height แล้วตัดท้าย
- UI ผู้วิจัยต้องทำงานบนเส้นทางใช้งานที่ระบุจริง ไม่พึ่ง Display2 ที่ไม่มี output โดยไม่มีทางเลือก

### 8.2 เจ้าของขั้นต่อไป

| สถานะ | ผู้เข้าร่วมเห็น/ทำ | ผู้ดำเนินขั้นต่อไป |
|---|---|---|
| ตั้งค่าก่อนเริ่ม | รอเริ่ม ไม่เห็นข้อมูลผู้เข้าร่วมคนอื่น | ผู้วิจัย Start ผ่าน control ที่แสดงจริง |
| Pause ที่ self-resume ได้ | ชื่อพัก คำสั้น safety และ “ทำต่อ” หนึ่งปุ่ม | ผู้เข้าร่วมเมื่อ readiness อนุญาต |
| Pause เพื่อความปลอดภัย/รอผู้วิจัย | บอกเหตุและแจ้งผู้วิจัย ไม่มีปุ่มข้ามสิทธิ | ผู้วิจัยตรวจแล้ว Resume ตามระบบ |
| Inspect Fail | ผลตาม S1 + กลับไปทำงานเดิม | ผู้เข้าร่วม; ไม่ reset งานทั้งหมด |
| Inspect Pass งานแรก | บอกจบงานแรกและ action ไปช่วงพักตาม flow | ผู้เข้าร่วมจนถึง checkpoint แล้วผู้วิจัยรับช่วง |
| PostTask1Checkpoint | บอกพัก/ประเมิน และผู้วิจัยจะเริ่มขั้นต่อไป ไม่ให้เดาว่าค้าง | ผู้วิจัยยืนยันจริง แล้ว Task2Ready |
| PostTask2Checkpoint | จบงาน VR/ขั้นประเมินท้ายรอบตาม flow ไม่สั่งกดในแว่นหลังสั่งถอดแล้ว | ผู้วิจัยยืนยันและระบบ finalize |
| Tracking/Logger error | บอกสาเหตุและวิธีรับความช่วยเหลือ ไม่ใช้ spinner ลวง | readiness/system/researcher ตามเหตุ; ไม่ resume เงียบ ๆ |

Pause ปกติ: “พักการทดลอง” / “เมื่อพร้อม เลือก ‘ทำต่อ’” / “หากรู้สึกไม่สบาย ให้ถอดแว่นและแจ้งผู้วิจัย”
ผู้ใช้ยืนยันให้ถอดแว่นและแจ้งผู้วิจัยเป็นช่องทางหลัก ไม่ต้องเพิ่ม Help button ที่บันทึก log แต่ไม่ส่งแจ้งเตือนจริง
Recall ผ่าน app Menu ต้องทำงานเมื่อแผงอยู่นอกมุมมอง โดยไม่ Resume
ข้อความ waiting ต้องบอก owner/reason/next; สัญลักษณ์รอคือรอคน ไม่แกล้งเป็น loading ที่ไม่มีงานระบบกำลังรัน

### 8.3 ทางที่ตรวจพบใน Editor วันที่ 13 ต.ค.

เมนู `VR Research > Researcher Session Console` มีปุ่ม `NASA-TLX / break complete - Resume` ที่ checkpoint
และ `Safety check complete - Resume` ในสถานะพักอื่น; เรียก `ResearcherResume()` เดิม
ใช้เฉพาะหลังประเมิน/ตรวจความพร้อมจริงในขอบเขตนั้น ไม่ตั้งค่าแบบประเมินหรือ consent ให้เสร็จแทนคน
นี่เป็นทาง Editor ไม่ใช่หลักฐานว่า standalone Quest มี researcher transport พร้อมแล้ว
ต้องทดสอบคลิก control นี้จริง ไม่เรียก method ตรงแล้วอ้างว่าผู้วิจัยหาปุ่มพบและกดได้

## 9. Training, errors และหน้าที่มักหลุดจากการตรวจ

| Surface family | จุดตรวจเฉพาะ |
|---|---|
| Welcome/Preparing | หนึ่งเป้าหมาย/Start; first-use control ชัด; ไม่มีเอกสารเฉพาะงานเปิดทับ |
| Teleport/Snap/Head movement | ภาพบอกปุ่มและทิศถูก; แยกหมุนมุมมองจากหมุนร่างกาย; ไม่กด quota ซ้ำ |
| Select/Grab/Lift/Rotate/Release | target ฝึกชัด; credit retained; ปล่อยก่อนเวลาไม่รีเซ็ตทั้งบท |
| Fasten/Socket/Cable/Examine/Inspect | ตรวจทุกกลไก S1 ที่บทฝึกต้องสอนด้วย input จริง; ไม่ข้ามด้วย DEMO_INPUT |
| Reference exercise | cue เดียวไม่บังเนื้อหา; success ไม่หายเร็วจนคิดว่าต้องทำซ้ำ; ไม่โชว์ cue ใน Task |
| Ready A/B ทั้งลำดับ | task name/neutral goal/Start; ไม่บอกเปิด Reference ตอนที่เปิดไม่ได้ |
| Active A/B | brief ครบแต่ไม่รก; controls มองเห็น/เข้าถึง; ไม่มี solution leakage |
| Checking/Fail/Pass/Timeout | ตามเวลาจริงและผลจริง; clear owner/return; ไม่เรียก technical failure ว่าผู้เล่นทำผิด |
| Pause/Checkpoint/Final | participant/researcher roles ชัดครบ; ปุ่มเท่าที่มีสิทธิ; ไม่มี dead end |
| Minimized/Restore/Recall | รู้ว่าเปิดคืนอะไร; hit targets ถูก; ไม่มี duplicate window |
| Media loading/error/retry | ไม่มี control ค้างข้าม format; duration และ state แสดงตรงจริง |
| Setup resume/error/empty registry | เหตุที่เริ่มไม่ได้อ่านได้; identity ไม่เปลี่ยนกลาง session; operator route ไม่สูญหาย |
| Tracking loss/log failure/cancel | ไม่แสดงข้อมูลเก่าเป็น valid; รักษาสิทธิพักและการยุติ; state/log ไม่หลุด |

ตารางนี้คือ coverage checklist ไม่ใช่รายการที่ผ่านแล้ว ต้องบันทึก NOT TESTED ให้แต่ละแถวที่ยังไม่มีหลักฐาน
ตรวจหน้าเหล่านี้ใน TH/JP และ EN fallback ที่มีจริง; functional matrix TH/JP×AB/BA×P/Q ระบุช่องที่รันจริง ไม่เหมารวม

## 10. Unity Editor ไม่ใช่ UI ของผู้เข้าร่วม

- Game View Display1/2 เป็น output selector; Play/Pause/Step, Scale, Gizmos เป็น toolbar ของ Editor
- ข้อความ `Display 2 / No cameras rendering` ไม่ใช่ข้อความใน ResearchStrings; ตรวจ targetDisplay/camera/Canvas render mode ก่อนสรุปว่าเกมเสีย
- ScreenSpaceOverlay อาจมี UI โดยไม่พึ่ง scene camera จึงห้ามเพิ่มกล้อง XR ตัวที่สองแบบสุ่มเพื่อเอาข้อความเตือนออก
- ตรวจ Preview/Editor view ให้ดูได้สะอาดผ่านการตั้งค่าที่เหมาะสมหรือ operator EditorWindow เดิม ไม่ลบ researcher controls เพื่อซ่อนปัญหา
- Gizmos/probe icons/selection handles ต้องแยกจาก runtime geometry; toggle ในงานตรวจเมื่อไม่รบกวนผู้ควบคุมและคืนค่าถ้าจำเป็น
- ห้ามลบ ReflectionProbes, colliders หรือ SDK models เพราะเห็นวง/ลูกบอลใน stopped Game View เพียงภาพเดียว
- ยืนยัน renderer/canvas เจ้าของและเปรียบเทียบ Game View ที่ปิด Gizmos กับ runtime capture ก่อนรายงานว่า visual รั่วเข้า build
- Screen capture ของ Editor ไม่เท่ากับกล้องเกมหรือผ่านเลนส์; บันทึก source/scene/state/mode ของภาพ

## 11. ข้อมูล การทำความสะอาด และการคงงานที่ผ่านแล้ว

การเปลี่ยน UI ต้องไม่เปลี่ยน task evaluator, 420s active timing, writer/clock, identity หรือ information-use semantics เงียบ ๆ
แยก User/Researcher/System/Tester, action accepted/rejected, visibility time และ media state จริง
การขอข้อมูล การมองเห็น และการเข้าใจเป็นคนละสิ่ง; อย่าเพิ่มตัววัด gaze/ข้อมูลส่วนตัวนอก S1
เก็บ raw ที่เกิดจริงเท่านั้น; อย่าแก้ missing เป็น0หรือเรียก movement QC แดงว่าผ่านเพราะตัวชี้วัดอื่นผ่าน
ตัวรันทดสอบอาจสร้าง hitch ต้องวัดแยก ไม่สรุปว่าเป็น runner จนมีหลักฐานตรง; ไม่ลดเกณฑ์ให้ green

Cleanup: แยก KEEP/ARCHIVE/REMOVE พร้อมเหตุผลและ mapping; backup ก่อนเอาออกจาก active project
ตรวจ GUID/.meta, serialized/string/reflection loads, Resources/StreamingAssets, build scripts และ conditional paths
รักษา original model/RAW/spec/failure evidence และ candidate ที่ active worker ใช้; ไม่ broad reset/clean หรือเขียนทับ unsaved scene
ไม่ลบ TrainingV2/HandsLabQuest3/XRI_Examples/Fonts/SDK เหมารวม; refactor เฉพาะ responsibility ที่เกี่ยว ไม่สร้าง renderer อีกชุด
ตรวจ build inclusion ของ DEMO แยกจาก runtime visibility; technical build != participant-ready build

## 12. ก่อนและหลังทุก batch: ใช้กฎนี้ให้เกิดผลจริง

ก่อนแก้: อ่านข้อที่เกี่ยว → บันทึก scene/source/dirty/owner → ดูภาพจริง → reproduce → ระบุแพตช์ที่เล็กพอ
หลังแก้: compile → focused tests → ดูภาพ full-resolution → ชี้/กด/เลื่อน/ลาก → ตรวจ raw ที่กระทบ → save/reopen เมื่อได้รับสิทธิ → ตรวจซ้ำ
หลังรวม Main และก่อน Build/รายงานจบ: อ่านข้อ0อีกครั้งและตรวจ surface ที่ใช้ component ร่วมกันทั้งหมด ไม่ตรวจแค่หน้าที่แก้

เกณฑ์บังคับ:
- ไม่มี capsule ทุก grip; header/body/action จัดกลุ่มและไม่ล้น; enabled controls อ่านและแยก state ได้
- สีคงบทบาทหลัง hover/pressed/disabled/state change/theme refresh/restore ทั้งภาษา
- ผู้เล่นรู้ขั้นถัดไป; researcher waiting มี control จริงฝั่งผู้วิจัยและทดสอบเส้นทางกลับ
- ไม่มีแผง/ปุ่มโปร่งใสค้างรับ ray; context changed ปฏิเสธ command เก่า แต่ขยับหัวตามปกติยังคลิกครั้งเดียวได้
- ตรวจชุด before/reference/after ที่ระยะ/กล้องเทียบกันได้ ไม่ zoom แอบเพื่อให้ดูใหญ่ชัดขึ้น
- ภาพ timeline ของ scroll/seek/drag จำเป็น ไม่ใช้ภาพนิ่งจัดไว้สวยแทน
- Full workflow ต้องผ่าน Training ด้วย action จริง; แยก direct method/programmatic callback tests ออกจาก physical pointing
- แก้ P0/P1 ที่ทำซ้ำได้ก่อนปิดงานย่อย; ถ้ายังไม่ผ่านหรือพึ่งคน/อุปกรณ์ให้แสดง OPEN/NOT TESTED ไม่ลดเกณฑ์
- ไม่รื้อสื่อหรือ Task ที่ผ่านเพราะ UI หน้าเดียว; ไม่เพิ่มรายงาน/check framework ใหม่ซ้ำทุก margin

ผลส่งมอบ: ใช้ STATUS/issue ledger เดิมรวม requirement ID, defect, source/ภาพ, fix, test method, outcome และข้อค้าง
หลักฐานขั้นต่ำต้องพอพิสูจน์แต่ละ claim ไม่ใช่จำนวนไฟล์/tests/BMR/เวลา; ไม่ลบหลักฐาน FAIL เก่า
`visual reviewed`, `programmatic UI`, `SDK pointer exercised`, `full Training`, `Task mechanics`, `data QC`, `Quest/wearer` เป็นคนละขอบเขต
ไม่มีเปอร์เซ็นต์หรือคำว่า full UI passed หากช่อง coverage ยังไม่ตรวจ

## 13. Audit baseline — 13 ต.ค. 2026 (อ่าน source + ภาพ ไม่ได้แพตช์ runtime)

ตัวอักษร F = พบในภาพ/source จริง; R = ความเสี่ยงที่ต้อง reproduce; U = ยังไม่ตรวจรับ
หมายเลขบรรทัดอ้าง source ตอน audit อาจเลื่อนหลังแพตช์ ให้ค้นชื่อ method และเงื่อนไขร่วมด้วย

| ID | ระดับ/หลักฐาน | Finding และการแก้ที่ต้องติดตาม |
|---|---|---|
| D01 | F/P1 — View.LayoutToolbar ประมาณ1691–1700 | pill เป็น clear เฉพาะลูกของ referenceExpanded; Task/TutorialยังElevated จึงมี capsule จริง. เปลี่ยนกฎร่วมทุก movable surface รักษา hit area |
| D02 | F/P1 — View.Refresh active1235 + LayoutSecondPass1374 | ยก task.body ทั้งย่อหน้า; bodyHeight ขยายตามเนื้อหา จึงสูงรก. จัดกลุ่มสาระ S1 ไม่ตัดข้อกำหนดหรือย่อฟอนต์ |
| D03 | F+R/P1 — ภาพ Task/Setup, Theme.Apply และ Canvas state | สีอ่อนและตัวเล็กเห็นจริง; สาเหตุ disabled/alpha/tint ต้องวัด runtime สถานะนั้น ไม่สรุปจาก RGB token. ตรวจหลาย builder ให้บทบาทตรงกัน |
| D04 | F/P1 — ResearcherSetupView42–67; live camera/display | operator Overlay display1 (จอ2), กล้องเกมdisplay0. ภาพมี No cameras rendering. ทำทาง operator ที่ดู/กดได้จริง ไม่เพิ่ม XRcameraแบบสุ่ม |
| D05 | F+R/P1 — ResearcherSetupView209–234,638 | readiness status ถูก summary เขียนทับได้และ resumeถูกซ่อนเมื่อไม่interactable. แสดงสาเหตุ/owner ไม่ให้รอไร้ทางไป |
| D06 | F/P1 — ResearchS1FlowDriver170–214 | checkpointเรียกResearcherResumeตรง; Clickใช้OnPointerDown/Up+onClick.Invoke พร้อม labelว่าNotPhysicalRaySelection. สิ่งนี้ไม่พิสูจน์ ray hit/ค้นพบปุ่ม/Training ครบ |
| D07 | F/U — live stopped GameView m_Gizmos=true; มี5ReflectionProbes | spheres/ringอาจเป็นEditorgizmos; ยังไม่ได้ toggleเพื่อระบุทุกชิ้น ห้ามลบ runtime/probes ตามภาพเดียว |
| D08 | R/P2 — View1420 | คำนวณ title wrapping ก่อนหัก utility width: ตรวจหัวข้อยาวกับPauseไม่ชนและวัดใหม่หลังจัดwidth |
| D09 | R/P2 — SetupView475–516 | summary fixedheight และScrollRect.vertical=false: ตรวจ longstatus/readinessไม่ถูกตัด |
| D10 | F/P2 — Console order vs Setup order | Consoleมี Auto/AB/BA แต่SetupมีAB_P/AB_Q/BA_P/BA_Q. ทำ assignment/label parity; ไม่ใช่หลักฐานว่ารอบที่รันP/Qผิด |
| D11 | R/P2 — View1610 | checkpoint formattingตัดอักษรแรก/แทรกขึ้นบรรทัดจากเครื่องหมาย. เปลี่ยนเป็นlocalization fields/structured layout เมื่อreproduceว่าทำcopyเสีย |
| D12 | U/P1 — STATUSสิ่งที่ยังติด | Trainingยังbypass,ไม่มีExaminepractice,สื่อReaderยังDEMO,main-scene matrixไม่ครบ,MovementQCยังแดง. ไม่ปิดจากTaskPassหรือAPKสำเร็จ |

### 13.1 ขอบเขตภาพที่ตรวจใน audit นี้

ภาพผู้ใช้3ภาพ: ResearcherSetup+Display2warning, TaskAbrief+capsule, stoppedGameView+spheres/ring
ภาพ saved implementation ที่เปิด6ภาพจาก `D:\VR_TMU\S1_Unity_PC_20261011\claude-takeover-20261013\runs`:
- `canon-th-ab-q-r3/task-B/00-initial.png` — canonical B panel/capsule
- `full-ja-ba-q-r1/task-B/00-initial.png` — TEST Japanese B panel/capsule
- `a-gpu-r1/flow/final-screenshots/Thai_AB_101003670_003_02_tutorial_welcome.png`
- `a-gpu-r1/flow/final-screenshots/Thai_AB_101003670_004_pause_participant_tutorial.png`
- `a-gpu-r1/flow/final-screenshots/Thai_AB_101003670_007_tutorial_movement_snap_prompt.png`
- `a-gpu-r1/flow/final-screenshots/Thai_AB_101125047_000_01_researcher_setup.png`
ภาพต่างเวลา/TESTไม่ใช่ snapshotเดียวกันทั้งหมด; ใช้ผูกข้อค้นพบ ไม่อ้าง finalUIครบทุกหน้า

Live read-only: canonical scene stopped/clean, camera CenterEyeAnchor display0; ReferencePanel inactive; Researcher Display2 Overlay disabled; GameView Gizmosเปิด
ไม่เริ่มPlay/Resume ไม่แก้scene ไม่Build/install ไม่รันmatrix/UIrayใหม่ใน audit นี้
ยังไม่ได้เปิดตรวจทุก state/ทุกภาษา/ทุกorder/ทุกlayout: ตารางข้อ9ต้องใช้ปิด coverage ต่อจริง

### 13.2 Source locators

`Assets/VRMaintenanceResearch/ResearchFlow/UI/ResearchFlowView.cs` — Refresh, LayoutSecondPass, LayoutToolbar, LayoutReaderShell
`.../UI/ResearchUITheme.cs`, `ResearchUIFeedback.cs`, `ResearchDragHandle.cs`, `ResearcherSetupView.cs`
`.../Runtime/ResearchSessionFlowController.cs` — ParticipantMayResume, ResearcherResume
`.../Editor/ResearcherConsole.cs` — เมนูและปุ่มผู้วิจัย; `ResearchS1FlowDriver.cs` — วิธีขับtestsจริง
`D:\VR_TMU\S1_Unity_PC_20261011\claude-takeover-20261013\STATUS.md` — Taskผลใหม่และข้อจำกัด ไม่ใช้reportเก่าฟันธงGPUยังติด

ห้ามใช้ audit นี้ย้อนแก้ Task ที่ Claude ทำผ่านแล้ว หรือบอกว่า report TaskPassเป็นเท็จทั้งหมด
คำที่ตรงคือ Task mechanics/selected flowsมีหลักฐานตามรายงาน แต่ยังไม่ใช่ full Training+pointing+visual UX+study readiness

## 14. เมื่อแก้แล้วต้องคงกฎ

แก้ source/test แล้วอัปเดตสถานะของ D01–D12 ใน issue ledger เดิม พร้อมหลักฐาน ไม่ลบข้อกำหนดที่เพิ่งทำไม่ผ่าน
การเปลี่ยนกฎต้องอ้างคำตัดสินใหม่ของผู้ใช้หรือข้อขัดแย้งกับ S1 ไม่แก้กฎให้ตรงกับบั๊ก
ก่อนส่งผลสุดท้าย อ่านไฟล์นี้และเปิดภาพหลังแก้จริงอีกครั้ง; ปิดเฉพาะข้อที่ตรวจแล้ว
