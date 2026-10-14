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
| ResearchFlowChecks (registry/flow) | 85/85 ผ่าน (เดิม project คอมไพล์ไม่ได้เพราะ Core มีไฟล์พึ่ง Unity — แก้ csproj ให้ exclude แล้ว) |
| FloorPolicy 138, FanKinematics 24, FanObservation 10, TaskACatalog 80, TaskAInput 22, TaskAKinematics 27, S1Task 280, TaskSnapshot 52, TaskTiming 73 | ผ่านทั้งหมด (ไม่ได้แก้โค้ดส่วนนี้) |
| BehaviorChecks / LoggerResumeChecks / S1BContextChecks | ไม่ได้รัน (ต้องการ argument โฟลเดอร์ fixture) |
| Tools pytest | 88 passed (เดิม 87 + test ใหม่ 1) |
| Compile-regression diff ทุก commit | ไม่มี error ใหม่ |
| Editor/PlayMode checks ใน Unity | **NOT RUN** |
| `Tools/tests/quest-profile-guard-tests.sh` | FAIL ใน Linux เพราะต้องรันจาก Git Bash บน Windows (เดิมเป็นแบบนี้ ไม่เกี่ยวกับงานรอบนี้) |

## 3. สิ่งที่แก้และรวมเข้า `main` แล้ว

Commit (push เข้า `main` แบบ fast-forward ไม่มี force-push):
- `d2256f6` — นโยบาย UI ตำแหน่งคงที่แบบมีเวอร์ชัน
- `9d0382d` — Training สั้นลง + แก้ Reader (feedback/วิดีโอ/caption)
- `91a68b8` — export ไป PC พกเวอร์ชันนโยบาย
- commit ของเอกสารนี้

### 3.1 นโยบาย UI คงที่ `S1_FIXED_UI_DEMO_20261014` (C01, ADVISOR §1)
- ไฟล์ใหม่ `ResearchFlow/Core/ResearchUIPolicy.cs` ค่าเริ่มต้นคือ fixed
  แบบเดิม `S1_BASELINE_MOVABLE_PANELS` ใช้ได้เฉพาะ technical regression ผ่าน ADB (`ui_policy=legacy_movable`) กับ ID DEV/TEST/PILOT
  ID ศึกษาจริง (TH/JP) ถูกปฏิเสธถ้าขอ policy อื่น
- เริ่ม session ใหม่จะบันทึก `ui_policy`, `protocol_version`, `training_version`, `layout_version` ใน registry และ event `SESSION_PROTOCOL_VERSIONS`
  session เก่าที่ไม่มีฟิลด์นี้จะถูกระบุว่าเป็น UI แบบลากได้ ไม่ปนกัน
  **ข้อควรรู้:** APK รุ่นเก่า (Q4/Q5) จะอ่าน registry ที่มีฟิลด์ใหม่ไม่ได้ (fail-closed ไม่ลบข้อมูล) — อย่าย้อนติดตั้งรุ่นเก่าทับหลังใช้รุ่นใหม่
- ใน fixed policy: ปิด GameObject ของแถบลาก (ทั้งเส้น grip และพื้นที่กดล่องหน) และปุ่ม "กลับมาตรงหน้า" ของผู้เข้าร่วม
  คำสั่ง recenter ถูกปฏิเสธพร้อม log `RejectedByUIPolicy`; event ลากที่ค้างถูกกันใน `ResearchDragHandle`
  ยังมี Minimize/Close/Pause และเปิด Reader คืนได้; การกู้แผงผ่านหน้า Pause (`panel_recovery`) ยังอยู่และถูก log
- Reader เปิดซ้ำที่ตำแหน่งแรกของ Task นั้น (ตรวจ clearance ใหม่; ถ้าไม่ผ่านระบบวางใหม่และ log เป็น System)
- Training ใน fixed policy ตัดบท "ย้ายแผง" ออก (ส่วน Reader เหลือ เปิด → เปลี่ยนรูปแบบ → ปิด) จึงไม่ติดทางตัน
  Editor checks ที่ทดสอบบทลากแผงเดิมถูกห่อด้วย `LegacyRegressionScope()`
- ADB: `researcher-adb.sh start DEV001 Thai Controller AB_P [fixed|legacy_movable]` และ `state` แสดง `ui_policy`

**ยังไม่ได้ทำในข้อนี้:** anchor จาก "station pose ที่ออกแบบไว้ในฉาก" — ตอนนี้แผงงานวางครั้งแรกจากตำแหน่งศีรษะตอนเริ่ม Task
(หลัง teleport ไปสถานี) แล้วคงที่ตลอด Task; ต้องตรวจในฉากจริงว่าพอดีทั้ง P/Q ก่อนตัดสินว่าต้องย้ายไป anchor ในฉาก

### 3.2 Training (C05, C06, ADVISOR §2)
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
- C04: ภาพประกอบเผื่อพื้นที่ caption ก่อนกำหนดขนาดภาพ (ขั้นต่ำ 55% ของความสูง แทน 75%)
  **ข้อค้าง:** caption ของ A/B ยาว 250–740 ตัวอักษร ยังต้องเลื่อนอ่าน ซึ่งขัด design_rule §7 ("ภาพใหญ่ caption สั้น") — เป็นงานเนื้อหาที่ต้องอนุมัติ ไม่ได้แก้เอง

### 3.4 ข้อมูล/export
- `Tools/research_export.py` ใส่ `protocol_versions` ใน manifest, QC report และ task_summary ทุกแถว

## 4. ตาราง requirement / ข้อบกพร่อง

สถานะ: SOURCE = แก้ในโค้ดแล้วแต่ยังไม่ได้รันใน Unity/Quest; OPEN = ยังไม่ทำ; BLOCKED = ต้องใช้ Unity/Quest/คน

| ID | เรื่อง | สถานะ | หลักฐาน / ขั้นตรวจที่ต้องทำ |
|---|---|---|---|
| C01 | ลากแผงอิสระ + บทฝึกลาก | SOURCE | pure tests + compile diff; ต้องดูใน Play Mode ว่าไม่มีแถบลาก/hit area และ Training ไปถึงจบได้ |
| C02 | วิดีโอค้าง Loading | SOURCE (ทางออก) / OPEN (สาเหตุ) | ต้องเปิดวิดีโอ A/B×TH/JA บน Quest แล้วอ่าน phase log |
| C03 | feedback ทับปุ่ม Play | SOURCE | ต้องจับภาพ Reader วิดีโอระหว่าง Training ทั้งสองภาษา |
| C04 | caption ภาพถูกตัด | SOURCE บางส่วน | caption A/B ยังยาว ต้องตัดสินใจด้านเนื้อหา |
| C05 | ภาพสอนไม่ตรงการกระทำ | SOURCE | ดูภาพใน Canvas จริงทุกขั้น ทั้ง controller และมือ |
| C06 | 24 ขั้น + feedback ค้าง | SOURCE | ต้องลอง Training ครบด้วย input จริงและจับเวลา |
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
4. รัน Editor checks ที่ห่อ legacy (`CheckTutorialReferenceEarlyClose`, `RunTutorialLayoutChecks`, `RunTutorialAffordances`, VisualClarity) ดูว่ายังผ่าน
5. Export รอบ DEV แล้วรัน `python Tools/research_export.py ingest ...` ตรวจว่า manifest มี `protocol_versions.ui_policy = S1_FIXED_UI_DEMO_20261014`

### 5.3 Build และติดตั้งบน Quest (รักษาข้อมูล)
1. `VR Research > Research Flow > Build Quest APK` (technical identity) แล้วบันทึก SHA256 ของ APK
2. ตรวจแว่น: แบตพอ ไม่ร้อน ไม่มีคนสวมระหว่างสั่งคำสั่งอัตโนมัติ
3. ติดตั้งแบบแทนที่: `adb install -r <apk>` (**ห้าม** uninstall หรือล้าง app data)
4. อ่าน hash กลับจากเครื่องตาม `Tools/quest_build_identity.py` แล้วบันทึกคู่กับ package/build
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
