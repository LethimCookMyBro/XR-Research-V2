# S1 UX/Researcher Setup / Training / Teleport — ผลแก้ไข 22 ต.ค. 2026

สถานะเอกสาร: TECHNICAL EDITOR VERIFIED บางส่วน / PENDING QUEST WEARER / NOT STUDY APPROVED
อ้างอิง: `design_rule.md`, `Documentation/S1/S1_ALL_WORKFLOW_UX_UI_MASTER_20261017_TH.md`, `D:\VR_TMU\Unity_Pic_REF` และภาพที่เจ้าของส่งมาในแชท
Source ก่อนรอบนี้: `ef23b25` บน `main`; ห้ามสรุปว่า Build/Quest ใหม่ผ่านก่อนมีหลักฐาน

## 1. แก้แล้ว (PC/Editor)

| เรื่อง | การแก้ | ความเสี่ยงที่ยังต้องพิสูจน์ |
|---|---|---|
| หน้าแรก Quest สำหรับผู้วิจัย | จากหน้า DEV แบบย่อ เปลี่ยนให้พยายามเปิด `ResearcherSetupView` ที่มีตัวเลือก **กลุ่ม / รหัส / ภาษา / ลำดับงาน** และปุ่มเริ่มหลัก โดยรักษา `ResearchOperatorGuard` เดิมไว้; รอจนแผงวางในมุมมองได้หากแว่นยังไม่ tracked | ต้องกด Dropdown ด้วย Controller จริงบน Quest; Study ยังต้องสิทธิอนุมัติ |
| UX สีและไอคอน | การ์ดขาว–ฟ้า; แถว 4 ตัวเลือกมีไอคอนกลุ่ม/คน/ภาษา/ลำดับ; ลูกศร dropdown เป็นกราฟิกแทน glyph `v`; ช่องว่าง/ตัวอักษรเทียบจาก REF | ความคมผ่านเลนส์ยังไม่ยืนยัน |
| Training FX1 | คำสั่งสามภาษาให้แยก **ฝึกด้านที่ใส่ไม่ได้** กับ **ใส่ด้านที่ตรงกัน**, บอก Grip, มุมบาก และผลที่ต้องสังเกต; ภาพประกอบเพิ่มจุดสีน้ำเงินบอกมุมบากของชิ้นกับของช่อง | ยังต้องลองจับ/เสียบผิดและถูกจริง; human Japanese review |
| วัตถุฝึกตกไกล | เสริมการกู้วัตถุของ Training เท่านั้น: ตกพื้นไกลจากจุดพัก >2.25 ม., ห่างจากผู้สวม >1.15 ม., ความเร็วไม่เกิน 0.12 ม./วินาที แล้วอยู่นิ่งเกิน 8 วินาทีจึงกลับจุดเดิม พร้อม System recovery log | ยังไม่ผ่านฟิสิกส์/คอนโทรลเลอร์บน Quest; Task A/B ไม่ถูกรีเซ็ตอัตโนมัติ |
| วงเทเลพอร์ต | เปลี่ยนเส้นวงที่ **มองเห็น** 30 จุดจาก 0.584 ม. เป็น 0.780 ม. โดยไม่เปลี่ยน Native Teleport Collider ขนาด 1 ม. หรือจุดลงเท้า | ต้องทดลองขอบเขตการเลือกและระยะยืนผ่านแว่น; Study UX change ต้อง review |
| ตัวทดสอบการเคลื่อนที่ | แก้ชุด callback ที่จำลอง `TaskActive` โดยไม่ Start นาฬิกา Task ซึ่งทำให้ gate ปิดโดยถูกต้อง ให้ทดสอบบนสถานะ Task clock ที่เริ่มแล้วเหมือนแอปจริง | ไม่ใช่การพิสูจน์ตำแหน่งคนจริง |

## 2. หลักฐานตรวจที่บันทึกแล้ว

- `RESEARCHER_LOCALIZATION_PASS` ทั้ง Thai/Japanese (actual isolated ShowOnQuest render, ไม่ใช่ headset)
  `D:\VR_TMU\S1_Unity_PC_20261011\QuestResearcherRef20261022\th_v5\researcher-localization.json`
  `D:\VR_TMU\S1_Unity_PC_20261011\QuestResearcherRef20261022\ja_v5\researcher-localization.json`
  ภาพอยู่ใน `Researcher_DEV_selector/synthetic-mono-preview/` ของแต่ละชุด
- `FOCUSED_OPERATOR_UI_PASS 31`: `D:\VR_TMU\S1_Unity_PC_20261011\QuestResearcherRef20261022\operator_checks_v4\operator-ui.json`
- `TUTORIAL_ACCEPTANCE_PASS 9/9`: `Logs/ResearchFlowEvidence/bridge/response_CG_TUTORIAL_AUDIT_20261022.json`
- `ResearchObjectRecoveryChecks.Run`: เรียกสำเร็จผ่าน Bridge; ข้อทดสอบใหม่ครอบคลุม near/far/held/moving/near-wearer; ผลไฟล์ fixture `Logs/QuestFinalIntegration/object-recovery-fixture/result.json`; ต้องตรวจ actual VR เพิ่ม
- `PARTICIPANT_SAFETY_PASS`: `D:\VR_TMU\S1_Unity_PC_20261011\QuestResearcherRef20261022\safety_final\participant-safety.json`
- `ResearchLocomotionChecks.RunDestinations`: ผ่านหลังแก้ Scene (ตรวจ 30 จุดที่รัศมีตัวละคร 0.25 ม., ไม่ใช่ VR wearer)
- `RESEARCH_LOCOMOTION_CALLBACK_PASS checks=36`: `Logs/ResearchFlowEvidence/bridge/response_CG_CALLBACKS_TASKCLOCK_FIXED.json`
- `TELEPORT_MARKERS`: `Logs/ResearchFlowEvidence/bridge/response_CG_ENLARGE_MARKERS_20261022.json`; rerun idempotent `modified=0`: `response_CG_MARKER_IDEMPOTENCE_FINAL.json`; Scene Git Diff มีเฉพาะ `m_LocalScale` ของวง 30 จุด (ตรวจแล้ว)
- ก่อนแก้ Scene สำรองที่ `Logs/QuestFinalIntegration/teleport_marker_visual_before_20261022.unity`; เซฟแล้วตรวจว่าจุดลงเท้า/trigger collider ไม่เปลี่ยน
- Unity UI Test ทำให้ Serialized TMP Caches dirty; ตรวจชนิดของ dirty components แล้ว reload Scene ที่เซฟเพื่อไม่ส่ง side effects เข้าฉาก; สำรอง evidence `Logs/QuestFinalIntegration/20261022_preview_dirty_objects.json`; แก้ incidental YAML 10 hunks ออกด้วย selective Git patch (ที่ `Logs/QuestFinalIntegration/20261022_unwanted_scene_serialization.patch`)
- Font Assets จาก Editor dynamic atlas ถูกสำรองไว้ `Logs/QuestFinalIntegration/20261022_generated_font_caches/` แล้วคืนค่า HEAD ทั้งสามอย่างเจาะจง; ProjectSettings/UnityConnectSettings กลับไป `m_EnableCloudDiagnosticsReporting: 0` แล้ว

## 3. เรื่องที่ยังไม่ผ่านและไม่ควรเรียกว่าพร้อมเก็บข้อมูลวิจัย

- `ResearchRoomTeleport.VerifyOpenDoorwayAccess` (audit ที่ใช้รัศมี 0.4 เมตร + bounds ของ furniture) ยัง FAIL ที่ Computer station เพราะตรวจเจอ `Computer_BenchFrontEdge/Computer_Main`. ขณะที่ `ResearchLocomotionChecks.RunDestinations` ใช้รัศมี body 0.25 เมตรผ่าน 30 จุด. **ไม่ลดรัศมีเพื่อบังคับให้ audit ตัวแรกผ่าน**; ต้องตรวจ collision/crouching and reach บน Quest จริง
- ผู้ใช้รายงานว่าโมดูลฝึก FX1 เสียบ "คิดว่าถูกแล้วยังผิด"; copy/marker ที่แก้ยังไม่พิสูจน์ว่าการถือด้วยมือจริงเข้า socket ได้. อย่าเปิด Study จน UX comprehension ตรวจโดยคนจริง
- Task A/B `Examine` ไม่ขึ้นบางครั้ง, ระดับตานั่ง ~1.0 ม. `Inspect`/การ์ด Task ที่สถานีคอมมีปัญหา และ Editor QC movement gaps 6/8 cells ยังอยู่ในรายการเปิดจากรอบก่อน; ยังไม่ได้แก้ในรอบนี้
- การอนุมัติเนื้อหา 4 Reader formats, TH/JA human review, consent/IRB/task limits, UI policy version ต้องแก้ให้ครบก่อน participant recruitment
- UX แบบ Study Fixed vs DEV Movable ให้คงแยกตาม `ADVISOR_UI_AMENDMENT_20261014.md`. ไม่เปลี่ยน S1 Task Rules, Event Schema, Timing หรือ Authorization
- **APK ที่ติดตั้งบน Quest ตอนเริ่มรอบนี้เป็นรุ่นเดิม** `9c6cdb4c…0cc3` จาก commit `b22b943`; Source ของงานรอบนี้ยังไม่อยู่ใน APK รุ่นนั้น ต้องสร้าง candidate ใหม่และทดสอบบน Quest ก่อนประกาศผ่าน

## 4. Checklist สวม Quest สำหรับตรวจรับหลัง Build

ผู้วิจัยใช้ DEV/TEST เท่านั้น: เปิดหน้า Setup กลุ่ม/ID/ภาษา/ลำดับให้ถูก ทดลองเปลี่ยนค่าด้วยคอนโทรลเลอร์ ไม่สร้าง Study ID จริงเมื่อยังไม่ได้รับอนุมัติ; ตรวจ Start readiness และสถานะอุปกรณ์; ฝึก Teleport ไปวงที่เห็นได้/ปลอดชน โต๊ะ+ตู้/ประตู, หยิบหินหรือแก้วแล้ววางไกล ตรวจการกู้เมื่อสงบนิ่งและอยู่ไกล จากนั้น FX1 ใช้ Grip หมุนชิ้นใส่ผิด/ถูกตามภาพ ลอง Stop/Resume ด้วยอุปกรณ์จริง. ตรวจ Reader PDF/Steps/Images/Video, Examine/Inspect และความคมตัวหนังสือใกล้/ไกล; ทดสอบระดับตา 1.0 และ 1.65 ม. แยกกัน. หากเกิดอาการไม่สบาย/ไม่ปลอดภัยหยุดทันที

ผล Editor/PC ไม่ใช่ผล wearer/Quest. เก็บข้อมูลวิจัยในเครื่องเท่านั้น ห้ามล้าง Registry/Raw Logs, ถอนแอป หรือแก้ Study Gates เพื่อบังคับ Test PASS.
