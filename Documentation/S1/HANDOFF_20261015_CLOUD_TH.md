# ส่งมอบงาน S1 — รอบ Cloud คืน 14 ต.ค. 2026 (สำหรับรับตรวจเช้า 15 ต.ค.)

สถานะรวม: **PARTIAL — แก้ source แล้วบางส่วน ตรวจได้เฉพาะระดับโค้ด**
**DEVICE_TEST_BLOCKED** และ **EDITOR_TEST_BLOCKED** ตลอดรอบนี้
ไม่มี APK ใหม่ ไม่มีการติดตั้ง ไม่มีผลบน Quest ไม่มีผล Play Mode
ห้ามอ่านรายงานนี้ว่า "พร้อมเก็บข้อมูลผู้เข้าร่วม"

---

## 1. ผลตรวจเครื่องมือ (ตรวจจริงตอนเริ่ม session)

| รายการ | ผล |
|---|---|
| Source | `main` = `a6a6cf8` ตอนเริ่ม (local, origin/main และ branch ของ session ตรงกัน) working tree สะอาด |
| LFS | 94 ไฟล์เดิมเป็น pointer → `git lfs pull` สำเร็จ ครบ 94 ไฟล์ |
| Unity Editor | **ไม่มี** ใน container และไม่มี bridge/MCP ไปเครื่องเจ้าของ; โดเมนดาวน์โหลด Unity และ package registry ของ Unity/Meta ถูก proxy ปฏิเสธ (403) |
| ADB / Quest | **ไม่มี** `adb` ไม่มี USB; environment มีแค่ `anthropic_cloud` ไม่มี self-hosted pool หรือ Remote Control session |
| แบต/สภาพแว่น, ผู้ใช้ Unity/Quest คนอื่น | **ตรวจไม่ได้** (ไม่มีทางเข้าถึง); ใน cloud ไม่มี Claude session อื่นทำงานอยู่ |

ผลที่ตามมา: งาน Build, ติดตั้ง, เล่นบน Quest, Play Mode, จับภาพ/คลิปในแอป, วัด performance
และผลิตวิดีโอสาธิตจากโมเดลจริง (ต้องใช้ Unity Editor) **ทำไม่ได้ในรอบนี้**

ทางแก้สำหรับรอบหน้า: เปิด Claude Desktop บน PC ที่ต่อ Quest หรือรัน `claude remote-control`
ในโฟลเดอร์โปรเจกต์บน PC นั้น แล้ว session จะเข้าถึง Unity/ADB ของเครื่องได้จริง

## 2. เครื่องมือตรวจที่ใช้แทนได้ในรอบนี้ (และขีดจำกัด)

1. **Pure C# tests ที่มีอยู่ใน repo** (`ResearchFlow/Tests/*.csproj`) รันด้วย .NET 8 จาก Ubuntu
   (repo ตั้ง net10.0 ไว้ จึงรันผ่านสำเนา csproj ชั่วคราวที่เปลี่ยนเป็น net8.0)
2. **Python tools tests** (`Tools/test_*.py`) ด้วย pytest
3. **Compile-regression diff** ด้วย Roslyn: คอมไพล์ `Assets/VRMaintenanceResearch/**` กับ UnityEngine 2021.3 reference DLL จาก NuGet
   แล้วเทียบชุด error ก่อน/หลังแก้ ยืนยันว่าจับ error ที่ใส่ทดลองได้
   ขีดจำกัด: ไม่มี TMP/uGUI/Meta SDK/XRI จึงมี error พื้นฐาน ~610 (runtime) / ~1457 (editor) ที่ซ่อนบรรทัดที่ใช้ type เหล่านั้น
   **ไม่ใช่การคอมไพล์ของ Unity 6000.3.20f1 จริง** — ต้องเปิด Unity ตรวจ Console อีกครั้ง
4. **Render ภาพสอน Training แบบ offline**: คอมไพล์ไฟล์ `ResearchTutorialSequenceGraphic.cs` ตัวจริงกับ stub แล้ว rasterize mesh เป็น PNG
   ใช้ดูรูปทรงที่วาดเท่านั้น ไม่ใช่ภาพใน Canvas/ผ่านเลนส์

| ชุดตรวจ | ผล |
|---|---|
| ResearchFlowChecks (registry/flow/anchor/caption/video rules) | 112/112 ผ่าน (รอบแรก 85; เดิม project คอมไพล์ไม่ได้เพราะ Core มีไฟล์พึ่ง Unity — แก้ csproj ให้ exclude แล้ว) |
| FloorPolicy 138, FanKinematics 24, FanObservation 10, TaskACatalog 80, TaskAInput 22, TaskAKinematics 27, S1Task 280, TaskSnapshot 52, TaskTiming 73 | ผ่านทั้งหมด (ไม่ได้แก้โค้ดส่วนนี้) |
| BehaviorChecks / LoggerResumeChecks / S1BContextChecks | ไม่ได้รัน (ต้องการ argument โฟลเดอร์ fixture) |
| Tools pytest | 90 passed (เดิม 87 + test ใหม่ 3) |
| Compile-regression diff ทุก commit | ไม่มี error ใหม่ |
| Editor/PlayMode checks ใน Unity | **NOT RUN** |
| `Tools/tests/quest-profile-guard-tests.sh` | FAIL ใน Linux เพราะต้องรันจาก Git Bash บน Windows (เดิมเป็นแบบนี้ ไม่เกี่ยวกับงานรอบนี้) |

## 3. สิ่งที่แก้และรวมเข้า `main` แล้ว

Commit (push เข้า `main` แบบ fast-forward ไม่มี force-push):
- `d2256f6` — นโยบาย UI ตำแหน่งคงที่แบบมีเวอร์ชัน
- `9d0382d` — Training สั้นลง + แก้ Reader (feedback/วิดีโอ/caption)
- `91a68b8` — export ไป PC พกเวอร์ชันนโยบาย
- `02ca506` — รายงานฉบับแรก + ตัวเลือก ui_policy ใน ADB
- รอบที่ 2: `d29c4b5` station anchor, `2714e53` ลำดับ Training, `be6612a` Illustrated หน้าย่อย,
  `8331772` registry sidecar/attribution, และ commit ของ Reader rules + รายงานที่ปรับนี้

### 3.1 นโยบาย UI คงที่ `S1_FIXED_UI_DEMO_20261014` (C01, ADVISOR §1)
- ไฟล์ใหม่ `ResearchFlow/Core/ResearchUIPolicy.cs` ค่าเริ่มต้นคือ fixed
  แบบเดิม `S1_BASELINE_MOVABLE_PANELS` ใช้ได้เฉพาะ technical regression ผ่าน ADB (`ui_policy=legacy_movable`) กับ ID DEV/TEST/PILOT
  ID ศึกษาจริง (TH/JP) ถูกปฏิเสธถ้าขอ policy อื่น
- เริ่ม session ใหม่จะบันทึก `ui_policy`, `protocol_version`, `training_version`, `layout_version` และ event `SESSION_PROTOCOL_VERSIONS`
  (รายละเอียดการเก็บและการย้อนรุ่นอยู่ข้อ 3.6)
- ใน fixed policy: ปิด GameObject ของแถบลาก (ทั้งเส้น grip และพื้นที่กดล่องหน) และปุ่ม "กลับมาตรงหน้า" ของผู้เข้าร่วม
  คำสั่ง recenter ถูกปฏิเสธพร้อม log `RejectedByUIPolicy`; event ลากที่ค้างถูกกันใน `ResearchDragHandle`
  ยังมี Minimize/Close/Pause และเปิด Reader คืนได้; การกู้แผงผ่านหน้า Pause (`panel_recovery`) ยังอยู่และถูก log
- Reader เปิดซ้ำที่ตำแหน่งแรกของ Task นั้น (ตรวจ clearance ใหม่; ถ้าไม่ผ่านระบบวางใหม่และ log เป็น System)
- Training ใน fixed policy ตัดบท "ย้ายแผง" ออก (ส่วน Reader เหลือ เปิด → เปลี่ยนรูปแบบ → ปิด) จึงไม่ติดทางตัน
  Editor checks ที่ทดสอบบทลากแผงเดิมถูกห่อด้วย `LegacyRegressionScope()`
- ADB: `researcher-adb.sh start DEV001 Thai Controller AB_P [fixed|legacy_movable]` และ `state` แสดง `ui_policy`

**Station anchor (รอบที่ 2, `S1_STATION_ANCHORS_20261015`):** ดูข้อ 3.5

### 3.2 Training (C05, C06, ADVISOR §2) — ตัวนับ/feedback/ภาพสอน (ลำดับกิจกรรมดูข้อ 3.7)
- ตัวนับเปลี่ยนจาก "ผ่าน 8 / 24" เป็น "ส่วนที่ n จาก 4" (1 มอง/เคลื่อนที่/เลือก, 2 จับวัตถุ, 3 Reader, 4 แท่นฝึก+ตรวจผล)
  เครดิตยังนับรายทักษะจากเหตุการณ์จริงเหมือนเดิม ไม่เติมเครดิต
- ข้อความ "ผ่านแล้ว: …" แสดง 3.5 วินาทีแล้วหายเอง จึงไม่ค้างทับคำสั่งใหม่; ผลตัวอย่างตรวจผลของแท่นฝึกยังค้างจนถึงขั้นถัดไป
- ภาพสอนของแท่นฝึกวาดเฉพาะการกระทำ: ตรวจดู (ชี้โมดูล + เครื่องหมาย i ว่าเป็นข้อมูลเท่านั้น), ใส่โมดูล, ใส่ผิดด้าน (ติด ✕),
  ปิด/ปลดสลัก, หมุนแท่น, ขัน/คลายสกรู (ลูกศรตาม/ทวนเข็ม), เสียบ/ถอดปลั๊ก — เดิมใช้ภาพชี้เมนูซ้ำ
  ภาพ offline: ดูได้จากการรันตัว render (ไม่ได้ commit ภาพ) ยังไม่ได้ดูใน Canvas จริง
- ข้อความ "ส่วนที่ n จาก 4" ภาษาไทย/ญี่ปุ่นเป็นร่างของ AI ต้องให้คนตรวจภาษา

### 3.3 Reader (C02, C03, C04)
- C03: feedback ในโหมด Reader ย้ายไปช่องว่างใต้ปุ่มสี่รูปแบบ (sidebar) — คำนวณจาก geometry เดิมแล้วช่องเก่า (-284,-354, 512×52) ทับปุ่ม Play และเวลาของวิดีโอจริง
- C02: วิดีโอโหลดเกิน 10 วินาที จะแสดงข้อความและปุ่ม "ลองอีกครั้ง" ระหว่างที่ยังโหลดต่อ; log ทุกช่วง (`REFERENCE_VIDEO_LOAD_PHASE`, `..._LOAD_STALLED`, retry พร้อมช่วงที่ค้าง)
  ตรวจ SHA256 ของไฟล์วิดีโอครั้งเดียวต่อการเปิดแอป (เดิมอ่าน ~20 MB ใหม่ทุกครั้งที่สลับไปวิดีโอ)
  **ยังไม่รู้สาเหตุจริงของ C02 บน Android** — ต้องดู log phase จากรอบ Quest
- C04: ภาพประกอบเผื่อพื้นที่ caption ก่อนกำหนดขนาดภาพ (ขั้นต่ำ 55% ของความสูง แทน 75%) และรอบที่ 2 แบ่ง caption ยาวเป็นหน้าย่อย (ข้อ 3.8)

### 3.4 ข้อมูล/export
- `Tools/research_export.py` ใส่ `protocol_versions` ใน manifest, QC report และ task_summary ทุกแถว

### 3.5 Station anchor ตาม P/Q (รอบที่ 2)
- `Core/ResearchStationAnchors.cs` (pure): ตำแหน่งแผงงานและ Reader คำนวณจาก transform ของสถานีในฉาก
  (`ResearchFlow_Station_Training` (9.72, 0, 4.95), `_Computer` (0, 0, 5.35), `_Fan` (−9.7, 0, 4.85) ทุกสถานีหัน −Z — อ่านจากไฟล์ฉาก)
  บวกความสูงตาที่วัดได้ (จำกัด 1.0–1.95 m) แผงงานอยู่ขวา Reader อยู่ซ้าย ที่ 20° / 1.2 m / ต่ำกว่าตา 0.1 m
  (เท่ากับตำแหน่งแรกของแบบเดิม) ทางเลือกเมื่อชน: ยกขึ้น 0.3 m และ/หรือถอยออก 15% เท่านั้น ไม่สลับข้างและไม่เข้าใกล้หัว
- P/Q: ใน `ApplyLayout` P/Q แค่สลับตำแหน่งตัวเลือกในที่นั่งชุดเดิม จึงใช้ footprint และ anchor เดียวกัน
  แต่ anchor id ระบุ layout และสถานี Computer ต้องมี P/Q จริง ถ้าไม่มีจะ fallback แบบเดิมพร้อม log
- ลำดับวาง: ลอง anchor ก่อนโดยใช้การตรวจชน/บังเดิม ถ้าไม่ผ่านใช้ตำแหน่งตามศีรษะแบบเดิม และ log `AnchorFallbackRecentered` (System)
  หน้าสถานะ (พัก, checkpoint, ผล) และแถบ recovery ยังวางตามศีรษะเพื่ออ่านได้ทุกที่; การกู้ผ่าน Pause ยังใช้ได้
- ใน Task ไม่บังคับว่า anchor ต้องอยู่ในสายตาขณะนั้น (เป็นตำแหน่งคงที่) แต่ใน Training ยังบังคับ เพราะผู้ฝึกเดิน/teleport
- ความสูงตา: วัดเมื่อถึงสถานี (System) และผู้วิจัยวัดใหม่ได้ก่อนเริ่มจับเวลา: `researcher-adb.sh calibrate_anchor`
  (เฉพาะ Welcome / Task n Ready) → log `STATION_ANCHOR_CALIBRATED`; `state` แสดงค่า
- **ยังไม่ได้ตรวจ:** ระยะ มุม การบังโต๊ะ/เคส/สาย จากมุมผู้เล่นจริงทั้ง P/Q ยืน/นั่ง — ต้องดูใน Unity และบนแว่น (ข้อ 5.2)

### 3.6 Registry/version: การย้ายข้อมูลและการย้อนรุ่น (รอบที่ 2)
- เวอร์ชันย้ายไปเก็บในไฟล์ข้าง registry: `protocol-versions.json` (key = session_id, เขียนแบบ atomic ก่อน registry)
  `registry.json` คงรูปแบบเดียวกับ a6a6cf8 ทุกตัวอักษรของ schema — ทดสอบด้วยตัวอ่านแบบ strict ที่เลียนคลาสรุ่นเก่า
  จึง **ย้อนติดตั้ง Q4/Q5 แล้วยังเปิด registry ได้** (session ใหม่ที่ทำไว้จะดูเป็น session ธรรมดาในรุ่นเก่า)
- registry ที่ build ช่วงกลาง (d2256f6..be6612a) เขียนฟิลด์ไว้ใน registry.json ยังอ่านได้ และย้ายไปไฟล์ข้างโดยไม่เปลี่ยนค่า
  (ไม่มี build ช่วงนั้นที่ติดตั้งจริง แต่รองรับไว้)
- ไม่เหมาว่า session ที่ไม่มีค่าเป็นแบบลากได้:
  - ตอน resume ถ้าไม่มีค่า จะอ่านจาก event `SESSION_PROTOCOL_VERSIONS` ของ session นั้นเอง; ถ้าไม่มีอีก runtime คงแผงแบบลากได้
    เพื่อไม่ให้ UI เปลี่ยนกลาง session และ log ว่า `unrecorded_runtime_keeps_movable_panels_for_continuity` (ไม่ใช่การระบุที่มา)
  - export: `ui_policy_source` = recorded → session_event_log → observed_drag_events (มี PANEL_REPOSITION/DRAG/บทฝึกลาก) → `unknown`
- ไม่เขียนทับ RAW, ไม่แก้ registry เดิมย้อนหลัง, ไม่ลบข้อมูลเก่า; ไฟล์ข้างที่เสียหายหรือ policy ไม่รู้จัก → หยุดอ่าน (fail-closed)
- **ก่อนติดตั้งรุ่นใหม่บนแว่น** (ข้อ 5.3 เพิ่ม): export session ที่ยังค้างด้วยรุ่นเดิม, บันทึก `list Dev/Test/Pilot`, แล้วจึง `adb install -r`

### 3.7 ลำดับกิจกรรม Training ก่อน–หลัง (รอบที่ 2)
| ส่วน | เดิม (legacy/Q5) | ใหม่ (fixed, `S1_TRAINING_FIXED_UI_20261015`) |
|---|---|---|
| 1 มอง/เคลื่อนที่/เลือก | teleport → หมุนมุมมอง → ขยับศีรษะ → กดปุ่มฝึก | เหมือนเดิม (ทักษะต่างกันทุกข้อ) |
| 2 จับวัตถุ | จับ → ยก → หมุน → ปล่อย (วัตถุชิ้นเดียว ต่อเนื่อง) | เหมือนเดิม; ปล่อยก่อนไม่ล้างเครดิต |
| 3 Reader | เปิด → เปลี่ยนรูปแบบ → **ลากแผง** → ปิด | เปิด → เปลี่ยนรูปแบบ → ปิด |
| 4 แท่นฝึก | ตรวจดู → ใส่ผิดด้าน → ใส่ → ปิดสลัก → ปลดสลัก+ดึงออก → หมุนแท่น → ขัน → คลาย → เสียบ → ถอด → ตรวจผล(ไม่พร้อม) → **ใส่+ปิดสลัก+ขันซ้ำโดยไม่ได้เครดิต** → ตรวจผล(พร้อม) | ตรวจดู → ตรวจผล(ไม่พร้อม: ยังไม่ได้ใส่) → ใส่ผิดด้าน → ใส่ → ปิดสลัก → หมุนแท่น → ขัน → เสียบ → ตรวจผล(พร้อม) → ถอดปลั๊ก → คลาย → ปลดสลัก+ดึงออก |
| รวม | เครดิต 24, การกระทำทางกาย 16 ที่แท่นฝึก (ซ้ำ 3) | เครดิต 23, การกระทำทางกาย 13 ที่แท่นฝึก (ไม่ซ้ำ) |

ทักษะ S1 §4 ยังครบทุกข้อและได้เครดิตครั้งเดียวจาก input จริง (ไม่มี auto-pass); legacy ยังใช้ลำดับเดิม
จำนวนการกระทำมาจากการจำลองเงื่อนไขใน `ResearchTrainingStation.Update` (ไม่ใช่การจับเวลาจริง)
ข้อความใหม่ 2 ข้อ (`tutorial.try.inspect_fail.start`, `tutorial.try.inspect.trainer_ready`) เป็นร่าง AI รอคนตรวจ TH/JA
Editor checks ที่อาจผูกลำดับแท่นฝึกเดิม (เช่น TutorialAcceptance) ยังไม่ได้รัน

### 3.8 Illustrated: หน้าย่อยจาก caption ยาว (รอบที่ 2)
- caption ที่มี ≥4 ย่อหน้าแสดงเป็นหน้าย่อย: หัวข้อซ้ำทุกหน้า + 1 ย่อหน้าเดิมต่อหน้า (`S1_ILLUSTRATED_PARAGRAPH_PAGES_20261015`)
- ข้อความไม่ถูกแก้คำ/ลำดับ/ตัวเลข: ตรวจ `Join(Split(x)) == x` ทุก caption 21 ภาพ TH/JA; จำนวนหน้าย่อย TH = JA ทุกภาพ
  A/B 16 ภาพแบ่ง (ย่อหน้าไทยยาวสุด 234 ตัวอักษร จากเดิม 738 ต่อ caption); Training 5 ภาพสั้นไม่แบ่ง
- ตารางเทียบเดิม–ใหม่: `Documentation/S1/ILLUSTRATED_CAPTION_SPLIT_TH_JA.md` (สร้างจาก package โดย `Tools/s1_caption_split_table.py`, มี test ตรวจว่าไม่ล้าสมัย)
- log ยังใช้ `index` = เลขภาพเหมือนเดิม และเพิ่ม `caption_part`/`caption_parts`
- **ยังไม่ได้ทำ:** ภาพย่อยแบบครอปเฉพาะส่วน — ทุกหน้าย่อยใช้ภาพเต็ม; การเลือกกรอบภาพต้องดูภาพจริงและระวังไม่ชี้ชิ้นที่ถูก
- เนื้อหายังอยู่สถานะ `S1_ENGINEERING_DRAFT_PENDING_HUMAN_REVIEW`

### 3.9 Reader loading (รอบที่ 2)
- กฎของการโหลดแยกเป็น `Core/ResearchVideoLoadRules.cs` และมี pure tests: callback เก่า (generation ไม่ตรง) ถูกทิ้ง,
  ปิด/ซ่อนแล้วไม่รับ callback, แจ้งค้างครั้งเดียวหลัง 10 วินาที, Retry ได้เฉพาะหลัง error หรือหลังแจ้งค้าง และกดซ้ำทันทีไม่ได้
- **แก้ทางออกจาก Loading แล้ว แต่ root cause บน Android ยังเปิดอยู่**
- กรณีที่ local ต้องตรวจใน Play Mode และบน Quest (ดู log `REFERENCE_VIDEO_LOAD_PHASE` ทุกกรณี):

| กรณี | ทำอะไร | ผลที่ต้องเห็น |
|---|---|---|
| R1 โหลดปกติ | เปิดแท็บวิดีโอ A/B × TH/JA | phase reading→verified→preparing→prepared→first_frame; ครั้งที่ 2 ต้องเป็น verified_cached |
| R2 stale callback | เปิดวิดีโอ แล้วสลับไป PDF ภายใน 1–2 วินาที แล้วกลับมา | ไม่มี first_frame ของรอบเก่าหลังสลับ; รอบใหม่เริ่ม phase ใหม่; ไม่มีเสียงหลุด |
| R3 สลับสื่อระหว่างโหลด | สลับ วิดีโอ↔ภาพ↔ขั้นตอน เร็ว ๆ 5 ครั้ง | ไม่มีปุ่มวิดีโอค้างในสื่ออื่น; สุดท้ายวิดีโอโหลดจบหรือมี Retry |
| R4 ค้าง | (Quest) ถ้าค้าง >10 วินาที | ข้อความ “ยังโหลดไม่เสร็จ” + ปุ่มลองอีกครั้งไม่ทับเวลา/timeline; log `REFERENCE_VIDEO_LOAD_STALLED` พร้อม phase |
| R5 retry ซ้ำ | กดลองอีกครั้ง 2 ครั้งติด | ครั้งที่ 2 ไม่รับจนกว่าจะค้างอีก 10 วินาที; log `REFERENCE_VIEWER_RETRY` มี `retried_from` |
| R6 ปิดหน้า | ปิด Reader ระหว่างโหลด / ย่อ / กด Pause / ถอดแว่น (focus loss) | ไม่มี error หลังปิด; เปิดใหม่เริ่มโหลดใหม่ถูกต้อง; ไม่มี player ค้าง |

## 4. ตาราง requirement / ข้อบกพร่อง

สถานะ: SOURCE = แก้ในโค้ดแล้วแต่ยังไม่ได้รันใน Unity/Quest; OPEN = ยังไม่ทำ; BLOCKED = ต้องใช้ Unity/Quest/คน

| ID | เรื่อง | สถานะ | หลักฐาน / ขั้นตรวจที่ต้องทำ |
|---|---|---|---|
| C01 | ลากแผงอิสระ + บทฝึกลาก | SOURCE | pure tests + compile diff; ต้องดูใน Play Mode ว่าไม่มีแถบลาก/hit area และ Training ไปถึงจบได้ |
| Anchor | แผงผูกกับสถานี P/Q | SOURCE | pure geometry 9 checks; ระยะ/มุม/การบังยังต้องตรวจใน Unity (ข้อ 3.5) |
| C02 | วิดีโอค้าง Loading | SOURCE (ทางออก + rules tests) / OPEN (สาเหตุ) | กรณี R1–R6 ข้อ 3.9 บน Editor และ Quest |
| C03 | feedback ทับปุ่ม Play | SOURCE | ต้องจับภาพ Reader วิดีโอระหว่าง Training ทั้งสองภาษา |
| C04 | caption ภาพถูกตัด | SOURCE | หน้าย่อยจากย่อหน้าเดิม (ข้อ 3.8); ภาพยังไม่ครอป; ต้องดูใน Reader จริงทั้ง TH/JA |
| C05 | ภาพสอนไม่ตรงการกระทำ | SOURCE | ดูภาพใน Canvas จริงทุกขั้น ทั้ง controller และมือ |
| C06 | 24 ขั้น + feedback ค้าง + บทซ้ำ | SOURCE | ลำดับใหม่ข้อ 3.7; ต้องลอง Training ครบด้วย input จริงและจับเวลา |
| Registry | ย้อนรุ่น/ที่มาของ policy | SOURCE | ข้อ 3.6; 9 checks รวม strict old-reader; ยังไม่ได้ลองย้อนรุ่นบนแว่นจริง |
| C07 | แถบดำ | OPEN / BLOCKED | ต้องดู hierarchy/canvas ของ build จริง ไม่ลบอะไรจากภาพเดียว |
| C08 | หน้า Setup ภาษาอังกฤษเทคนิค | OPEN | หน้า DEV เท่านั้น ยังไม่แก้ |
| C09 | โต๊ะดูว่าง | BLOCKED | ต้องตรวจฉากและมุมมองจริง P/Q |
| C10 | system shell + Pause | BLOCKED | ทดสอบ focus loss/return บน Quest |
| C11 | ภาพไม่คม/shimmer | BLOCKED | Q5 1.10/MSAA4 ยังเป็น candidate; ต้องวัดภาระจริงและสวมดู |
| D06/D12 | ไม่มี driver input จำลองบน Quest; Training/Matrix ไม่ครบ | BLOCKED | ไม่ได้เขียน adapter ใหม่เพราะรันทดสอบไม่ได้เลย จะเป็นโค้ดที่ยืนยันไม่ได้ |
| สื่อ Video A/B | ยังเป็นภาพนิ่งมีคำบรรยาย (สุ่มดู A_th ยืนยัน) | BLOCKED | ตัวผลิตเป็น Editor fixture `ResearchS1EducationalMedia` ต้องใช้ Unity บันทึก motion จากโมเดลจริง |
| สื่อ A/B EN | ไม่มี | OPEN | ไม่ได้อยู่ในขอบเขต TH/JA |
| Operator Start/Resume/Abort/Export บน standalone | มีอยู่เดิม (ADB, technical IDs เท่านั้น) | ไม่ได้ทดสอบ | Finalize = `resume` ที่ PostTask2Checkpoint → complete → export; ID ศึกษาจริงบน standalone ยังไม่มีทาง (ต้องมีการอนุมัติ ไม่เปิดเอง) |
| Performance | ไม่ได้วัด | BLOCKED | ดูข้อ 5 |
| ภาษา TH/JA ของข้อความใหม่ | ร่างโดย AI | OPEN | ต้องให้คนตรวจ |
| Matrix TH/JA × AB/BA × P/Q | ไม่ได้รัน | BLOCKED | Editor หรือ Quest |

## 5. ขั้นตอนรับตรวจที่ทำต่อได้ทันที (บน PC ที่มี Unity + Quest)

### 5.1 เตรียม source
1. ตรวจว่าไม่มีงาน local ค้าง: `git status` (ถ้ามี ให้ commit หรือเก็บก่อน ห้ามทับ)
2. `git fetch origin main && git checkout main && git merge --ff-only origin/main`
3. `git lfs pull` แล้วตรวจ `git log --oneline -5` ว่ามี commit ในข้อ 3
4. เปิด Unity 6000.3.20f1 → รอ import → Console ต้องไม่มี compile error
   (ถ้ามี error ในไฟล์ที่รอบนี้แก้ ให้แจ้ง — compile diff ใน cloud ครอบคลุมได้ไม่ครบ)

### 5.2 ตรวจใน Editor ก่อน Build (ประมาณ 30–45 นาที)
1. เปิดฉาก `Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity`
2. เริ่ม session DEV ผ่าน `VR Research > Researcher Session Console` แล้วไล่ Training ด้วย XR simulator/ray:
   - ไม่เห็นแถบลากใต้แผงหลักและ Reader; ชี้ตรงตำแหน่งเดิมของแถบแล้วไม่มี hover/hit
   - ส่วน Reader มีแค่ เปิด → เปลี่ยนรูปแบบ → ปิด และไปต่อถึง Inspect ได้
   - ตัวนับแสดง "ส่วนที่ n จาก 4"; ข้อความ "ผ่านแล้ว" หายใน ~3.5 วินาที
   - ระหว่าง Training ที่เปิด Reader แท็บวิดีโอ ข้อความผ่านอยู่ใต้ปุ่มสี่รูปแบบ ไม่ทับปุ่มเล่น
   - ภาพสอนของแท่นฝึกแต่ละขั้นต่างกันและตรงกับคำสั่ง
3. ใน Task: เปิด/ปิด Reader แล้วเปิดซ้ำ → อยู่ตำแหน่งเดิม; หา Minimize/Pause ได้
   - ดู log `PANEL_PLACEMENT` ต้องเป็น `Anchored` ที่ทั้ง Computer (P และ Q) และ Fan; ถ้าเป็น `AnchorFallbackRecentered` ให้จด `anchor_rejection`
   - วัดด้วยสายตา/ภาพ Game View ว่าแผงไม่บังเคส สาย ถาด และเอื้อมชิ้นงานได้ ทั้งท่ายืนและนั่ง
   - Illustrated: เลื่อนหน้าย่อย 1/5→5/5 แล้วข้ามไปภาพถัดไป; ข้อความครบ ไม่ถูกตัด
   - Training แท่นฝึกตามลำดับใหม่ ข้อ 3.7 จบได้โดยไม่ต้องทำซ้ำ
4. รัน Editor checks ที่ห่อ legacy (`CheckTutorialReferenceEarlyClose`, `RunTutorialLayoutChecks`, `RunTutorialAffordances`, VisualClarity) ดูว่ายังผ่าน
5. Export รอบ DEV แล้วรัน `python Tools/research_export.py ingest ...` ตรวจว่า manifest มี `protocol_versions.ui_policy = S1_FIXED_UI_DEMO_20261014`

### 5.3 Build และติดตั้งบน Quest (รักษาข้อมูล)
1. `VR Research > Research Flow > Build Quest APK` (technical identity) แล้วบันทึก SHA256 ของ APK
2. ตรวจแว่น: แบตพอ ไม่ร้อน ไม่มีคนสวมระหว่างสั่งคำสั่งอัตโนมัติ
3. ติดตั้งแบบแทนที่: `adb install -r <apk>` (**ห้าม** uninstall หรือล้าง app data)
4. อ่าน hash กลับจากเครื่องตาม `Tools/quest_build_identity.py` แล้วบันทึกคู่กับ package/build
   (ก่อนข้อ 3: `researcher-adb.sh list Dev` / `Test` / `Pilot` เก็บไว้ และ `export` session ที่ยังค้างด้วยรุ่นเดิม)
5. `Tools/researcher-adb.sh state` → ต้องเห็น `ui_policy: S1_FIXED_UI_DEMO_20261014`, `panel_drag_allowed: false`

### 5.4 รอบสวมรับตรวจ (ต้องมีคนสวม)
1. `researcher-adb.sh create Dev` → `start DEVxxx Thai Controller AB_P`
2. Training ครบด้วยมือจริง จับเวลา และจดขั้นที่งง
3. Task A: เปิด Reader ทุกรูปแบบ, วิดีโอ เล่น/พัก/เลื่อน/seek, Inspect Fail → แก้ → Pass
4. พัก → `researcher-adb.sh resume` → Task B → Pass → `resume` (Finalize) → `export`
5. ถ้าวิดีโอค้าง ให้ดูว่ามีปุ่ม "ลองอีกครั้ง" ภายใน ~10 วินาที แล้วเก็บ log phase
6. ทำซ้ำ JA × BA_Q อย่างน้อยหนึ่งรอบ; แยกรอบ timeout/withdrawal ออกจากรอบ Pass ทั้งสองงาน
7. ตรวจความคม/ความสบายผ่านเลนส์ด้วยตัวเอง (cloud ทำแทนไม่ได้)

## 6. สิ่งที่ไม่ได้ทำและเหตุผล
- ไม่ได้ Build/ติดตั้ง/ทดสอบบน Quest, ไม่ได้วัด performance — ไม่มี Unity และ ADB
- ไม่ได้ผลิตวิดีโอสาธิต motion ใหม่ — ต้องใช้ Unity Editor; วิดีโอปัจจุบันยังไม่ผ่านเกณฑ์ "สาธิตการเคลื่อนไหว"
- ไม่ได้จัดโต๊ะ/ย้ายชิ้นงาน — ต้องดูฉากและมุมมองจริง ถ้าเดาจะเสี่ยงทำ reach/collision เสีย
- ไม่ได้เขียน driver input จำลองบน Quest — ทดสอบไม่ได้แม้แต่ครั้งเดียว
- ไม่ได้แตะข้อมูลวิจัย, registry, consent หรือไฟล์ส่วนตัว; ไม่ได้ลบหรือ cleanup ไฟล์ใดใน project
