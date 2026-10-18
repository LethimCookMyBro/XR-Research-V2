# S1 MASTER — UX/UI, Workflow, 4 Media, VR Interaction, Quest Acceptance
เวอร์ชัน 1.0 | 17 ตุลาคม 2026 | ข้อกำหนดจากผู้ใช้เพื่อให้ Claude Code ทำงานด้วย /goal
โครงการ: D:\VR_TMU\XR-Research-V2
สถานะเอกสาร: ข้อกำหนดทางวิศวกรรมและเกณฑ์ตรวจรับ ไม่ใช่หลักฐานว่าโปรแกรมผ่านแล้ว และไม่ใช่การอนุมัติเนื้อหา/จริยธรรมการวิจัย

## 0. เป้าหมายและกติกาสั่งงาน

เป้าหมาย: ให้ผู้ทดสอบและผู้เข้าร่วมที่ไม่เคยเห็นระบบเข้าใจการใช้งาน VR ได้ง่าย ข้อความอ่านออกทุกภาษา ไม่มีเมนูรก ไม่มีปุ่มหรือสถานะกำกวม จับ/ต่อ/ซ่อมของได้จริง สื่อสี่แบบใช้งานและบันทึกข้อมูลได้จริง ภาพชัดเท่าที่อุปกรณ์เอื้อ และไม่มีการเปลี่ยนวิธีวิจัยโดยพลการ

วิธีทำ: ตรวจสถานะจริง -> เก็บหลักฐานก่อนแก้ -> หา root cause -> เปลี่ยนเล็กที่สุดที่แก้ปัญหา -> ทดสอบทั้ง logic / Unity rendered frames / pointer / physical geometry -> ทดสอบ regression -> บันทึกก่อน-หลัง -> ดำเนินขั้นถัดไปอย่างต่อเนื่อง ไม่หยุดเพียงแผนหรือรายงาน

เกณฑ์คำว่าเสร็จ: ทุกสิ่งที่ทดสอบได้ด้วย PC/Unity/Build ต้องมีหลักฐานและผ่านจริง ส่วนที่ต้องใช้ Quest ที่เชื่อมต่อและคนสวม ผู้ตรวจภาษา ผู้วิจัย และการอนุมัติจริยธรรม ให้รายงาน PENDING แยก ห้ามแต่งผลลัพธ์หรืออ้างว่า Quest ผ่านจาก Editor

หนึ่ง writer ต่อ checkout: ตรวจ Claude/Codex/Unity process/active session ก่อนแก้ source; ห้ามทำงานชนกัน ห้าม kill/Stop/Save/Play ฝืน. สถานะ Git branch main ต้องรักษาไฟล์ที่แก้ค้าง, meta GUID, Font Atlases, ProjectSettings, scene state, data/registry/receipts. ห้าม git reset/clean/stash/force push, ล้าง Quest หรือ overwrite โมเดลต้นฉบับ. เก็บ snapshot/diff ก่อน batch, commit เฉพาะ reviewed changes ลง main ตาม AGENTS.md เมื่อปลอดภัย

สิ่งที่ควรทำกลางคืน: ทำครบทุกส่วนที่ตรวจได้อัตโนมัติอย่างปลอดภัยก่อน เชื่อมโยง issue กับ evidence; หากไม่มี Quest หรือคนสวม ห้ามแอบติดตั้ง/ประกาศ wearer PASS แต่ให้สร้าง APK/Runbook ที่ตรวจได้แล้ว ทำงานส่วนอื่นต่อ ไม่วนรอทั้งคืน

## 1. เอกสารและข้อขัดแย้งที่ต้องรักษา

ต้องอ่านอย่างเจาะจงก่อนแก้:
- AGENTS.md, CLAUDE.md, design_rule.md
- Documentation/S1/ADVISOR_UI_AMENDMENT_20261014.md
- Documentation/S1/VR_TaskSpec_S1_LOCKED_TH.md
- Documentation/S1/content_contract.json และ verification_design.json
- Documentation/S1/CLOUD_START_HERE.md (บางส่วนเป็นข้อความก่อน 17 ต.ค. ให้เทียบสถานะใหม่)
- D:\VR_TMU\S1_Unity_PC_20261011\QuestWearReview_20261017\QUEST_FIX_VERIFICATION_REPORT_20261017.md และคลิป/ภาพ Quest จริงที่เกี่ยวข้อง
- .audit/s1-quest-readiness.tsv และ todo.md, relevant recent PlayChecks
- ข้อมูล approved ethics/proposal ที่ผู้วิจัยยืนยันก่อนเริ่ม STUDY; ไม่ใช้ engineer's source flag แทนอนุมัติ

ข้อขัดแย้งวิจัย (BLOCKER ต่อ Study ไม่ใช่ blocker ต่อ DEV):
- Proposal มิ.ย. เดิมรับสมัครไทย 8 ญี่ปุ่น 8, Task A เปลี่ยนชิ้นส่วน, เวลา Task A 16 นาที Task B 20 นาที; TaskSpec S1 10 ต.ค. เปลี่ยนเป็น Task A ประกอบคอม W2 + Task B ซ่อมพัดลม F2, เวลา active 420 วินาทีต่อ task, มีแผนไทย/ญี่ปุ่น 10+10 และแบ่งสาขา 5+5. ห้ามให้ AI เปลี่ยนจำนวนคน เวลา เกณฑ์หรือเอกสารอนุมัติเอง; บันทึกรายการที่ต้องให้อาจารย์/ผู้วิจัยลงมติ
- Advisor amendment 14 ต.ค. เลือก fixed-position UI สำหรับ revised study demo แต่ผู้ใช้ 17 ต.ค. ต้องการลาก Reader/Main/Inspect เข้ามาใกล้ได้; โค้ดแยก DEV_Movable กับ Study_Fixed. รักษาสอง policy และบันทึก policy/protocol version ไม่เปิดลากใน Study เองจนได้รับอนุมัติ
- แพ็กเกจ Reader A_th, A_ja, B_th, B_ja, T_* มีสถานะ ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW; ไม่แก้ flag ให้ approved ด้วยตนเอง

อย่าเปลี่ยนเกณฑ์ Task S1, fault B ที่ล็อกไว้, ตัวเลือก parts, scoring, timer, H1-H4, input mode, safety gate, language exposure, data retention หรือเปิดฟีเจอร์ Cloud ใหม่เพื่อให้หน้าตาดูดี

## 2. ภาพ REF และรูปแบบหน้าจอ

Visual reference หลัก: D:\VR_TMU\Unity_Pic_REF — ตรวจว่าปัจจุบันมีภาพอะไรอยู่, เลือกภาพที่เกี่ยวข้องพร้อมชื่อ/ภาพรวม, ใช้ hierarchy/spacing/visual language ไม่ต้องลอกสีกรมท่า. บางรายงานเคยเห็น 7 ภาพ; ไม่สมมติว่ามีไฟล์ ui_concept_01/02 เมื่อค้นหาไม่พบ
Reference ด้านสื่อ: progress slide 24 ก.ย. หน้า 3 Information Access Options และหน้า 4 Proposed Information Formats; นี่คือข้อกำหนดสี่สื่อ ไม่ใช่หน้าจอให้เอาตาราง/ข้อความ planned records มาใส่ในแว่น
โทนสีตามผู้ใช้: ขาว-ฟ้า, Ink น้ำเงินเข้ม, Primary ฟ้าน้ำเงินชัด, Secondary ฟ้าอ่อน/ขาวขอบฟ้า, selected แยกจาก disabled; ห้ามสีเทาหม่น ฟ้าอมม่วงโปร่ง หรือ text ซีดจนอ่านยาก
- Surface ทึบพออ่านบนฉากโต๊ะจริง; margin/gap สม่ำเสมอ ไม่เพิ่มกล่อง/เส้นรอบทุกย่อหน้า
- ชื่อ, ขั้นปัจจุบัน, เนื้อหา, คำเตือน, CTA มีขนาด/น้ำหนักต่างกัน; อย่าย่อ font เพื่อยัดให้ผ่าน unit check
- ไอคอนชุดเดียวกัน น้ำหนักเส้นใกล้กันและมี label, ไม่ใช้ Unicode ที่ font ขาดเป็น icon
- Animation ใช้เฉพาะช่วยเรียนรู้ action/feedback/สถานะ. ไม่กระพริบหรือเลื่อนปุ่มตอน ray ชี้ ไม่สอนคำตอบ
- ตรวจ localised text ไทย-ญี่ปุ่น รวมสระ/วรรณยุกต์, คันจิ, break line, TMP glyph/fallback, RectTransform, mask, 3D canvas depth, parent scale; ตรวจจากภาพ rendered ของทุก state จริง ไม่ใช่ GetPreferredValues ผ่านแล้วจบ

## 3. มีหน้า Menu ไหม? รูปแบบที่อนุญาต

มีหน้า Landing/Setup หนึ่งขั้นสำหรับ DEV หรือผู้วิจัยเท่านั้น ไม่ต้องสร้าง Main Menu แบบเกมหลายชั้นในระหว่าง Session:
  [ผู้วิจัยตั้งค่า / readiness จริง / New vs Resume / เริ่ม]
       -> [Welcome/Training]
       -> [Task แรก] -> [พัก+NASA-TLX/ผู้วิจัย]
       -> [Task ที่สอง] -> [ประเมินท้ายรอบ/จบ]
       -> [Local Export/Receipt/PC (Researcher only)]

ใน Task participant ต้องมีตัวเลือกเท่าที่จำเป็น Reference / ตรวจผล (peers) / Pause หรือ Stop ตามสิทธิ; ไม่แสดง DEV/TEST/ภาษา/AB/BA/ผัง/ข้อมูล researcher, data metrics, fault answer หรือ H1-H4
ใน DEV Setup แสดง: ชื่อระบบ, สถานะพร้อม/รอแบบหนึ่งบรรทัด, เลือก New/Continue latest safe saved, ID ที่จะใช้, Start primary, Details secondary
ใน Details ค่อยแสดงภาษา, AB/BA, layout P/Q (auto/read-only เมื่อ protocol ระบุ), readiness 5 รายการ, Advanced Researcher. จะใช้ segmented control เมื่อมีเพียง 2 ค่า, dropdown เมื่อรายการยาวจริง. ห้ามมีปุ่มหลายสิบอันให้กดวน DEV
Resume จากเซสชันที่กำลัง Task อยู่ไม่ปลอดภัยต้องถูกบล็อกด้วยเหตุผล; กด New ต้องไม่สร้าง ID ใหม่ผิดหรือทิ้งข้อมูลเดิม
Status แสดงตรงกับ Start gating: ไม่มีคอนโทรลเลอร์จริงใน Editor ให้ขึ้น waiting ไม่ spoof ว่าพร้อม; Disabled ยังอ่านคำอธิบายได้

## 4. ตารางหน้าจอและ Workflow CONTRACT — ทุกหน้าต้องตรวจจริง

| หน้า/สถานะ | สิ่งจำเป็นที่ผู้ใช้เห็น | พฤติกรรมผ่านเกณฑ์ |
|---|---|---|
| DEV/Researcher Setup | Mode+ID+Ready+Start+Details | New/Resume ถูก ID, selection จริง, Study gate ไม่หลุด |
| Setup Details | Language, AB/BA, P/Q, readiness 5, advanced+Back | ปุ่มไม่ทับ, status ไม่ซ้อน, ปิดกลับตำแหน่งเดิม |
| Welcome | เป้าหมายบทฝึก, เริ่ม, ความปลอดภัยสั้น | ไม่มีข้อมูลเฉลย Task หลุดมา |
| Training movement | การเดิน/วาร์ป/หมุน, ภาพปุ่มที่ถูก | controller input จริงและ credit จริง |
| Training grab | หยิบ/ยก/หมุน/ปล่อย object ฝึก | ไม่มี credit ซ้ำลวง, ไม่ต้องทำหลายรอบโดยไร้เหตุ |
| Training FX1 | Examine, insert wrong/correct, lock/unlock, screws, cable | ทุกการจับ/ต่อ/ถอดทำได้ด้วย object จริง ไม่จมโต๊ะ/ไม่ทะลุ |
| Training Reference | เปิด/เปลี่ยนสื่อทั้ง 4 และปิดกลับ | tutorial ไม่ครอบ viewer, ไม่บังคับอ่านเฉลย |
| Training Inspect | ตัวอย่าง Pass/Fail ของโมดูลกลาง | Examine != Inspect, ไม่ auto-pass |
| Task A/B Ready | งาน 1/2 ตามลำดับ AB/BA จริง, neutral brief, เริ่ม | โจทย์ตาม S1 ครบ ไม่บอกชิ้นที่ถูก |
| Task Active | Goal สั้น+เงื่อนไขสำคัญ+Reference/Inspect/Pause | ผู้เล่นเลือกลำดับ/สื่อเอง, UI ไม่บังวัตถุ |
| Reader PDF | Sidebar 4 formats + เอกสารอ่านได้ + เลื่อน/ซูม | ไม่ต้องกด Next ใหญ่ทุกหน้า; scroll ใช้จริง |
| Reader Steps | ดัชนีขั้น + ภาพ/เนื้อหา + ก่อน/ถัดไป | ไม่เกิด repair credit จากการกด Next |
| Reader Images | ภาพใหญ่ชัด+caption สั้น+เปลี่ยนภาพ | ไม่เอาหน้า PDF จิ๋วหรือหน้า1/1 โดยไม่จำเป็น |
| Reader Video | Play/Pause/Seek/Replay+เวลา+ภาพเคลื่อนไหว | ไม่มี loading ค้าง/เสียงค้าง/ปุ่มกดแล้วเงียบ |
| Inspect checking | กำลังตรวจแบบ 3 วินาทีตาม S1 | ระยะเวลา/สถานะจริง, ไม่เร่งให้ pass |
| Inspect Fail/Pass | feedback ตามผลจริง + next action | ไม่เผย hidden fault, Fail กลับไปทำใหม่ไม่ reset |
| Timeout/Stop/Withdraw | เหตุผลและสิทธิที่ทำต่อได้ | ไม่มี dead end หรือ fake failed-as-user-error |
| Pause self resume | พัก + หนึ่ง primary "ทำต่อ" เมื่อสิทธิอนุญาต | แก้ bug pause ที่มี Stop ซ้อนและยัง fail PlayCheck |
| Researcher checkpoint | รอประเมิน/พัก, บอกว่าผู้วิจัยต้องทำอะไร | Researcher Resume จริง, ไม่ทำ TLX/consent เอง |
| Tracking/Floor/Logger error | บอกสาเหตุ/ทางกลับ/แจ้งผู้วิจัย | ห้ามใช้ spinner ลวง, ข้อมูล raw/clock รักษาความจริง |
| End/Export | จบ VR แล้ว, researcher นำข้อมูลไปตรวจ | ไม่โชว์ raw participants, Receipt/Hash ถูก |

ตรวจเงื่อนไขระหว่างหน้า: สลับ Reader, ย่อ/คืนแผง, teleport, กลับจาก Stop/Continue, video pause vs session pause, หยิบถือ object ระหว่าง pause, Start confirmation, task timeout, task success, late export, app losing focus
ทุก state ที่รอ ให้บอกใครต้องทำอะไรต่อ ไม่ใช้ปุ่มไร้สิทธิหรือปิดบังเหตุผล

## 5. จุดผิดที่เห็นแล้วห้ามถือว่าผ่าน (P0)

- ภาพล่าสุดภาษาญี่ปุ่นบน Details: status badges สีเขียว "準備/完了" แตกบรรทัดทับ row ถัดไป แม้ Claude อ้าง UI stage tests passed. ถือ FAIL จนภาพใหม่เต็มหน้าจริงไม่ซ้อน ตรวจ text lineMetrics/TMP/Font, RectTransform/parent scale/clipping/lineHeight; อย่าแค่ลด text จนอ่านไม่ออก
- การ์ดบาง state สูงมากจนเคยถูกจัดไปไกล 1.40–1.79 เมตร; หน้าหลัก/Details ไม่ควรทำแผงโดด, ปุ่ม Back อยู่ตำแหน่งเดิม
- ปุ่ม Start/Resume/New ยังต้องแยก selected/disabled/primary ให้ชัด; เมื่อยังไม่พร้อม ห้ามกดและมีคำอธิบาย
- Play Mode เคย fail "Eligible pause exposes Continue only" เพราะ Pause มี Stop ซ้อน; ต้องแก้ด้วยกฎสิทธิ user/researcher ไม่ลบ safety gate
- Icon แอปมี resource ใน APK แต่ผู้ใช้ยังไม่เห็นบน Quest Home/Universal Menu ต้องพิสูจน์จากอุปกรณ์จริง
- Reader Video เคยขึ้น "กำลังโหลดวิดีโอ" ค้างหลัง app focus false; ตรวจ Prepare/Play/Pause/Seek/End/Reopen native
- FX1 สีเหลืองเคย Grab/Examine ไม่ได้เพราะ Trainer_Body obstruct; source patch ไม่พอ ต้อง user-controller actual test
- panel collision เคยยกเว้น Training_Practice_A ทำให้ 116 จาก 432 pose intersect โต๊ะผ่าน; ตรวจ patch หลังแก้, test no table penetration ทั้ง P/Q และ eye heights
- สาย FX1/สายคอมจมโต๊ะ ชิ้นส่วน/ฝาเคสดำกินพื้นที่/บดบัง, สกรู สลัก ชิ้นที่ใกล้กันจับผิด
- ระยะไกล: ภาพเบลอ/เส้นสั่นคล้ายไอร้อน, มือไม่เสถียร, Reader text unreadable through lens. ไม่อ้าง screenshot sharp เป็น physical pass

## 6. Reader ทั้ง 4 รูปแบบตามสไลด์ของอาจารย์ (ไม่ลดเหลือ 3)

ตาม ResearchReferenceScheme.Active: user_manual (PDF), step_by_step, illustrated, video
ทุกหน้าต้องมีตัวเลือกสื่อ 4 ไอคอน+ชื่อคงที่ใน sidebar; เมื่อเลือกเปลี่ยน viewport เดียว ไม่เปิด UI แยกหลายหน้าที่ต้องจำ
ผู้เข้าร่วมเลือก WHEN และ WHICH เอง; ไม่บังคับอ่านก่อนทำงาน ไม่ auto-suggest หลัง error หรือ highlight สื่อที่นำไปสู่คำตอบ
ตรวจ A/B × Thai/Japanese × สี่รูปแบบ = 16 combinations และ Training TH/JA (+ EN technical เฉพาะที่มีจริง)
ที่เคยตรวจ: A_th, A_ja, B_th, B_ja มี PDF pages 8, steps 8, images 8 ต่อ package; Videos H.264 1280x720 ใน Assets/StreamingAssets/ResearchReaderS1
วิดีโอที่เคย ffprobe: A_th 429.875s; A_ja 498.833s; B_th 437.125s; B_ja 459.125s; S1 active task 420s. ต้องอ่าน current files ยืนยันอีกครั้ง, ดูความยาว/การแบ่งหัวข้อ/การ seek และความเทียบเท่าสื่อ, ไม่ตัด content หรือแก้ตัวจับเวลาโดยพลการ
ทุกแพ็กเกจอาจอยู่ S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW, ห้ามเลื่อน approval เอง

ตรวจ: missing media, file hashes, title/script, exact steps+images+PDF fact parity, Thai/Japanese same meaning, font/glyph/diacritics, image readability/zoom, PDF scrollbar scroll thumbstick/restore, Steps Index, Images pager, Video prepare/play/pause/seek/end/replay, Audio/no lingering playback, lifecycle on panel hidden/paused/late app focus, error/retry/no infinite spinner
log INFORMATION_OPEN/CLOSE, first format, duration, switches/returns, action timestamps, playback state; ใช้ code path จริงไม่ใช่บังคับ event ให้ดูผ่าน
ไม่แสดง planned records หรือ H1–H4 ให้ participant เห็น

## 7. Training ต้องสอนเข้าใจ แต่ไม่เฉลย Task

ใช้ neutral Trainer_FX1 ไม่สอนความถูกต้องของชุด W2 หรือ fault F2
ฝึกกลุ่มทักษะจริง: Locomotion+turn, select/grab/orient/release, simple reference 4 choices and close/reopen, Examine, insert right/wrong, lock/unlock, screw/unscrew, cable connect/disconnect, Inspect practice; เครดิตย่อยที่จำเป็นยังครบแต่ UI ไม่โชว์ 24 micro steps เป็นกำแพง
หนึ่งครั้งของ action ที่ถูกได้รับเครดิตครั้งเดียว; อย่าสั่งทำซ้ำเพียงเพราะ panel refresh; progress เป็นกลุ่มที่เข้าใจได้
แอนิเมชันการฝึกใช้โมเดล/Controller จริงของขั้นนั้น ไม่ใช้ generic hand/controller ภาพเดียวทุกขั้น; แสดงทิศ Grip/Trigger และการปล่อยที่เหมาะสม
feedback ผิดด้าน/เอียง/ใส่ไม่สุดต้องมาจาก physical socket state, ไม่ auto-snap ชิ้นผิดให้ถูก; log rejected attempts แต่ไม่โยนว่าเป็นความผิดผู้เข้าร่วมเมื่อ collider ผิดจริง
ต้องทดลองหยิบโมดูลสีเหลือง, click Examine chip, training lead/screw ลอยหรือติด/ทะลุหรือไม่ทั้งสองมือ; รักษาบทฝึก safety

## 8. Task A W2: อุปกรณ์คอมพิวเตอร์จริงของสเปก S1

เลือกและติดตั้ง RAM + SATA SSD + GPU จากชุดตัวเลือก ไม่สั่งลำดับ RAM→SSD→GPU; base CPU/cooler/PSU พร้อม; ไม่ให้ทำ OS/ปิดฝา
RAM ข้อกำหนด W2 จำนวนสองแผง DDR4 8 GB pair A2/B2, SSD 2.5 SATA อย่างน้อย 500GB+600TBW, GPU ≥8GB และ ≤150W ตาม stimulus fictional; ชิ้นที่ไม่ตรง requirement แต่ mechanically compatible ยังต้องใส่ได้ตามกลไก แล้ว Inspect จึงรายงาน mismatch (ตาม S1)
ไม่แสดง correct part ID ใน UI participant, ชิ้นผิดไม่เปลี่ยนสีเป็นแดงก่อน Inspect
ตรวจ model hierarchy และ actual slots, 1 movable latch ตาม board, SSD tray จริงด้านหลัง, SATA data/power แยก, GPU bracket/power, socket insertion, grabbing/releasing, cable slack, case service rotation (หมุน workpiece ไม่หมุน XR origin), ฝาเคสที่พักไม่บังโต๊ะ
P/Q สลับตัวเลือกภายในหมวดตาม protocol, ไม่ทำให้ตำแหน่งถูกพอดีมือกว่าอีก layout
Inspect pass state-based genuine snapshot; ไม่ bypass condition เพื่อให้ flow เขียว

## 9. Task B F2: diagnosis & repair

พัดลม F2/B-04 เสียงสั่น/ครูดหลังประกอบ; ผู้ร่วมเลือกดู/ตรวจ/แก้ได้ด้วยตนเอง; fault เริ่มต้นตาม S1 ไม่ random เพิ่ม
ต้องถอด/ใส่/ล็อกตะแกรงหน้า ใบพัด cap ตะแกรงหลัง ring ตาม physical access ที่กำหนด ไม่เห็น/หยิบผ่านชิ้นที่บังอยู่
ทิศ thread ระหว่าง blade cap และ rear ring ถูกจริง; ปล่อยครึ่งทางไม่กลายเป็น sealed; การเข้าถึงชิ้นในไม่แก้ fault ให้ผู้เล่นเอง
Inspect ห้าม simulate rotation เมื่อ guard/blade ไม่อยู่ในสถานะปลอดภัย; แยก unsafe/not-testable vs testable symptom fail vs Pass
ทดสอบทั้ง left/right, P/Q, calibration, error state, repeated inspections; ไม่เพิ่ม hint บอกว่า fault อยู่ตรงไหน

## 10. สายไฟจมโต๊ะ / โต๊ะและตำแหน่งสิ่งของ / Panel Drag

ตรวจ Unity scene hierarchy, meshes, animation curves/bones, colliders, authored transforms, cable endpoints, contact, release physics, overextension, gravity sag, table height; เปรียบเทียบ actual rendered frames กับ runtime events ก่อนแก้
ต้องไม่มีสายไฟจมโต๊ะทั้งตอน idle/held/moving/after release, ไม่มีสายทะลุเคส/ฝาพัดลมที่ผิดกลไก, ไม่ย้ายฐานโมเดลมั่ว ๆ, ไม่ลบ collider จน Grab สำเร็จปลอม
ทุกตัวเลือกบนโต๊ะควรมองเห็น/อ่านฉลากได้และอยู่ในระยะเอื้อมจริง ไม่กองทับ ไม่เป็นการใบ้คำตอบ
ตรวจระดับตา 1.0 m และ 1.65 m (ตำแหน่งที่เคยมี occlusion issue) รวมทั้ง calibrated pose อื่นที่สเปกใช้; ทดสอบ stereo BOTH EYES และ extent ของแผง ไม่ตรวจเฉพาะจุดกึ่งกลาง
DEV-movable Main/Reader/Inspect ใช้ด้ามจับมองเห็น ขยับหน้า/หลัง ซ้าย/ขวา สูง/ต่ำ เมื่อปล่อยต้องนิ่ง/ไม่ทะลุโต๊ะ/ไม่กลับ anchor ผิด ไม่มีการหันหัวแล้ว panel ย้ายเอง
Study-fixed รักษา policy/station anchors และเหตุผล UI version; ไม่เปิด draggable ใน Study โดยไม่อนุมัติ
ปุ่ม Recall/recenter must work when panel out of view and not resume paused task; PDF/Video input must not steal drag
ห้ามลบ collision guard เพื่อให้ clear closer pose ผ่าน; อย่าแก้ปัญหาวัตถุมองไม่เห็นโดยย้ายตำแหน่ง answer-dependent

## 11. Render clarity/performance: มองไกลเบลอ / ภาพเหมือนไอร้อน

ค่าเริ่มเก่า Quest3 URP scale 1.1, MSAA 4, refresh 72Hz, foveation 0 จากรุ่นที่เคยตรวจ ไม่ใช่ผลรับรอง
วัด Quest XR eye resolution, shader aliasing, texture/filter/mipmaps/aniso, TMP SDF atlases/font fallback, UI canvas world size and angular text, CPU/GPU frametime P95/P99 spikes, tracking/reprojection/foveation/antialiasing; compare same task & viewpoint before-after
อย่าเพิ่ม render scale หรือ MSAA แบบสุ่มจน frame หน่วง; 72Hz มี frame budget ประมาณ 13.9ms แต่ต้องวัด frametime จริง
ดูผู้สวมผ่านเลนส์: อ่าน Japanese/Thai, มองไกล/ใกล้, สายขอบ case, screen shimmer, hand jitter, comfort and ability to see tools; screen capture editor/streaming ไม่ใช่ wearer pass
ไม่ smooth/แก้ raw movement logs เพื่อหลอกให้ hands ดูนิ่ง

## 12. App icon และ APK launcher บน Quest 3

อาการก่อนหน้า: user ยังไม่เห็นไอคอนแอปและบาง overlay ขึ้น App name unavailable
ต้องตรวจ ProjectSettings icon settings, AndroidManifest merged application/activity label and icon, Quest-specific package, Android 13+ resources, launcher intent/categories, Unknown Sources grouping, installed package/user profile, APK actual aapt2 dump badging
ทำไอคอนขาว-ฟ้าที่สื่อถึง VR Maintenance เรียบและอ่านได้บน Quest, อนุญาตใช้โลโก้สถาบันเฉพาะเมื่อสิทธิชัด, หลีกเลี่ยงภาพมีลิขสิทธิ์ไม่มีสิทธิ
การมี icon resource ใน APK เป็น Build-level เท่านั้น; ต้องตรวจว่าแสดงจาก Quest Universal Menu / Apps / Unknown Sources จริง ข้อจำกัด sideloaded apps ต้องอธิบาย ไม่เดาว่า icon หายเพราะ Unity อย่างเดียว
ทำ Build candidate เดียวที่ระบุ commit/source+APK SHA256; ตรวจก่อนหลัง ProjectSettings/Fonts/app data. ห้าม uninstall/clear, เปลี่ยน profile, bypass PIN/Guardian, stop session คนกำลังเล่น

## 13. Icons / Animations / Character จาก Rive แบบมี fallback

ต้องการ UI สวย ชัด เป็นมิตร; icon เป็น requirement, character เป็นความสวยงามเสริม ไม่ใช่สิ่งที่ทำให้ Task ถูกหรือผิด
Rive candidates: https://rive.app/marketplace/28325-53495-blop-character-mascot/ (Blop CC BY listing), https://rive.app/marketplace/27784-52502-robot-mascot/ ; verify creator, license/credit, download permissions, actual file, hash, ability to ship/export
Rive Unity runtime official https://github.com/rive-app/rive-unity ระบุ technical preview พร้อม Unity 6/Vulkan Android support; ทดสอบทางเลือกอย่าง isolated before production. Rive pricing https://rive.app/pricing อาจมี plan/export/ship restrictions; อย่าซื้อ subscription/ทำบัญชี/คลิกยอมรับข้อผูกพันแทนผู้ใช้
แนวทางต่ำความเสี่ยง: ลอง Rive ใน Scene/fixture แยก วัด memory/draw calls/frame time/Android shader. ถ้าไม่ผ่านหรือโหลดไม่ได้ เลือก mascot asset ที่ license เหมาะสมแบบ static sprite/limited Unity animation แทน
วาง mascot เล็ก ๆ เฉพาะ DEV Setup/Welcome/Training neutral, ห้ามทับ buttons/Reader/hands และห้ามแสดงท่าชี้/สถานะที่ใบ้ชิ้นถูกใน Task A/B
Animation สำคัญที่ควรทำก่อน mascot: hover/pressed/success/error feedback, clear grabbed/released, snapped/locked/unlocked, safe Inspect progress, Training short real-object cue. ไม่ใช้ autoplay decoration ดูดสายตา/เพิ่ม cybersickness
เก็บ license source URL/credit file path/author/version/hash สำหรับ asset ที่นำเข้าจริง ไม่ขโมย asset จากภาพ preview หรือสรุปว่า CC BY แปลว่าทุกการ export ฟรี

## 14. งานวิจัยและข้อมูล H1–H4 ไม่ให้ UX ทำลายผล

H1 ก่อนลงมือ: first reference open vs first action time และเวลาเปิดก่อนลงมือ
H2 การสำรวจก่อนแก้/ประกอบ: targets visited/examined และลำดับก่อน first repair / first assembly attempt
H3 การตรวจความคืบหน้า: state at first Inspect, actions between Inspect, repeat checks
H4 หลังไม่ผ่าน: next action repeat/change target/return information พร้อม timestamp และ target จริง
บันทึก SessionID/participant group pseudonymous/locale/task order/layout version/input/policy version, headset/controllers raw positions+rotation/time, task events, Errors, Inspect Fail/Pass, retries, info open/close/type/duration, status termination
NASA-TLX/อาการ/สัมภาษณ์/consent video capture ตาม approved documents เท่านั้น ไม่ประดิษฐ์คำตอบหรือแปลง missing movement เป็น0
local-first event/movement/export, hash+receipt+idempotency, PC import/QC จริง, Cloud ไม่จำเป็นและห้ามเปิดใหม่เอง; data and raw video ต้องไม่ push repo/public/cloud AI
อย่าเปลี่ยน event definitions/timing/sampling/analysis_version เงียบ ๆ เพื่อให้ tests green; แต่ละ DEV/UI policy ที่ต่างกันต้องระบุใน logs และไม่รวมกับ study results แบบไม่แยก

## 15. ตารางทดสอบที่ต้องทำ ไม่ใช่รายการให้ติ๊กว่า PASS เอง

1. UI render QA: TH+JA ทุกหน้าตาม §4 รวม Main/Details, Error/Stop/Pause/Checkpoint, 1x และ 1.5x Game View, current fonts, all display sizes that matter; ตรวจภาพ pixel-level + text bounds/line height/overlap/truncation + external reviewer. ถ้า screenshot เห็น label ซ้อน แม้ unit test ผ่าน = FAIL
2. VR input QA: ray/trigger/grip/controller SDK ทำงานจริง, One click, drag, scrolling, seek, focus loss, haptics/cancellation; ห้ามนับ onClick.Invoke เป็น native input evidence
3. Spatial QA: P/Q, head y=1.0/1.65m, both-eye panel corners, table occlusion, hand reach, cable intersection, reassembly geometry, UI outside viewport and Recall
4. Functional full flow: DEV New/Resume -> Welcome -> full Training credits -> first task -> Inspect Pass/Fail/Timeout -> researcher checkpoint -> second task -> final -> local export, verify raw events/movement/receipts
5. Conditions: TH/JA × AB/BA × P/Q = 8 end-to-end cells (Editor/sim vs Quest explicit). Reader: 2 Tasks × 2 languages × 4 formats = 16 checks + Training media, no stale metadata
6. Error/failure coverage: device not ready, tracking loss, User Stop and researcher stop, duplicate DEV ID, resume unsafe Task, missing video, PDF fail, logger fault, paused export, participant withdraw, late callback/scene refresh
7. Japanese screenshot case: readiness badges must NOT overlap; fixes must retest against old failing image; text geometry suite must detect this regression, not only measure one-line labels
8. Unity warnings/errors and tests: run scoped play checks, compact UI, UIControlAcquisition, SpatialReadability, UIRecovery, Reader production, ParticipantSafety, code-level S1 evaluator, export QC. Do not delete stale failing tests blindly: fix fixtures without weakening assertions
9. Build: 0 compile errors, warnings explained, correct APK package name/main activity icon/label, no side effects, SHA256; optional install only with direct Quest preflight and user-preserving guards
10. Quest lens run: device actually connected and NOT worn before install, original data backed up and rehashed; wearer tests legibility, comfort, grip, reader video and all Task A/B controls. Mark this PENDING when device unavailable
11. Human decision gates: TH/JA language equivalence, revised ethics/task sample/time/policy, raw video consent, final participant suitability. Not something that Claude can auto-approve

Quality rubric for each state: Content correct? Text readable? Current action obvious? Too many choices? Visual hierarchy? Hit target actually works? Task outcome correct? Research logs correct? Fits both eyes? Does not obstruct physical work? No unwanted learning/bias? Show evidence

## 16. Priority and checkpoints for /goal run overnight

P0-A baseline/git ownership/current frame evidence and preservation
P0-B Japanese/TMP overlap + all Setup/main/details + Pause/Continue bug; before-after screenshots TH/JA
P0-C whole workflow with actual UI commands, Training interactions, researcher gates and error paths
P0-D fix cable-in-desk, collider/parts occlusion, P/Q, panel readability, all physical mechanics of Task A/B
P0-E four-media availability/content QA/native video+logs; 16 combinations
P0-F far clarity/frames and clean research UI after measured adjustments
P1-G app icon verified in APK; actual Quest verification when available
P1-H coherent icons + small optional mascot/Rive proof with license/performance/fallback
P0-I full regression, error tests, final Build bound to reviewed source, export/hash, guarded Quest run and full wearer test if physically possible

At each checkpoint keep (1) issue id/root cause (2) file/line changed (3) original evidence (4) actual test command/results (5) screenshots/log/hash (6) blockers+next step.
If no Quest or owner is away overnight: continue safe Editor/Build work and prepare exact wearable test, do not lie that human approved. Do not create endless watchdog / loop that generates 100s of DEV sessions or large movement logs.

Definition of Done:
- P0 work demonstrably works in supported environment, all relevant states and languages reviewed; no false positive tests or text collisions
- No broken input/colliders/table cables, no policy violation, neutral information seeking preserved
- Asset/animations legal and performant with provenance; app icon packaged properly
- Source+APK version/hash and install/wear status documented; dataset preserved with receipt
- Clear NOT TESTED/PENDING for Quest, human language/ethics as applicable — not advertised participant-ready until resolved
- Final report D:\VR_TMU\XR-Research-V2\Documentation\S1 or suitable ignored Logs folder with issue-by-issue matrix and evidence; optional docs in Git must never contain participant PII/video/registry/secret

## 17. Links for technical/licensing research

Official Rive Unity: https://github.com/rive-app/rive-unity
Blop listing: https://rive.app/marketplace/28325-53495-blop-character-mascot/
Robot Mascot listing: https://rive.app/marketplace/27784-52502-robot-mascot/
Rive plans/export restrictions: https://rive.app/pricing
Meta app manifests and sideloading rules: https://developers.meta.com/vr/resources/publish-mobile-manifest/

This brief is NOT authority to modify approved Study methods, manufacture participant outcomes, purchase services, export private data to public repos, or fabricate Quest/wearer acceptance.
