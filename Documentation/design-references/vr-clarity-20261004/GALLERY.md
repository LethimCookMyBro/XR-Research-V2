# ภาพอ้างอิงและ UI ที่ปรับใน Unity

ภาพ Unity ด้านล่างเป็น synthetic mono preview ใน Editor เพื่อดู layout และข้อความ ไม่ใช่ภาพจากผู้สวม Quest หรือผลยืนยันความสบายใน VR

## UI ที่ใช้งานจริงใน project

หน้าฝึกจับ: ภาพมือ/วัตถุจาก asset ที่นำเข้า คำสั่งของขั้นปัจจุบัน และปุ่มพัก

![หน้าฝึกจับ](D:/VR_TMU/XR-Research-V2/Logs/QuestFinalIntegration/visual-distill-20261004/release-verified/synthetic-mono-preview/Thai_coach_grab.png)

หน้าฝึกปล่อย: ใช้ภาพปล่อยบนโต๊ะ พร้อม feedback ของขั้นที่เพิ่งทำสำเร็จ

![หน้าฝึกปล่อย](D:/VR_TMU/XR-Research-V2/Logs/QuestFinalIntegration/visual-distill-20261004/release-verified/synthetic-mono-preview/Thai_coach_release_instruction.png)

เอกสารใน Tutorial: คำสั่งปัจจุบันอยู่ด้านบน แท็บมี icon กับชื่อ และคำกำกับสัญลักษณ์เป็นข้อมูลรอง

![หน้าฝึกย้ายเอกสาร](D:/VR_TMU/XR-Research-V2/Logs/QuestFinalIntegration/visual-distill-20261004/release-verified/synthetic-mono-preview/Thai_reader_reposition.png)

บทฝึก Inspect: คงคำอธิบายความต่างกับ Examine และมี action หลักเดียว

![บทฝึก Inspect](D:/VR_TMU/XR-Research-V2/Logs/QuestFinalIntegration/visual-distill-20261004/release-verified/synthetic-mono-preview/Japanese_coach_inspect.png)

## Reference ที่ generate 10 ภาพ

ภาพเหล่านี้เป็น concept จาก AI ไม่ใช่หน้าจอ Unity ใช้เลือกองค์ประกอบเฉพาะส่วน รายละเอียดสิ่งที่ใช้/ตัดออกอยู่ใน SELECTION.md

1. Grab — ใช้หลักภาพใหญ่กับคำสั่งเดียว

![01 Grab](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/01-grab.png)

2. Regrab — ใช้หัวข้อจับอีกครั้งและสถานะเก็บความคืบหน้า

![02 Regrab](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/02-regrab.png)

3. Select — ใช้ความสัมพันธ์ ray กับเป้าหมาย แต่ตัดภาพที่เน้นปุ่ม controller ผิดออก

![03 Select](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/03-select.png)

4. Rotate — ใช้ภาพมือ/วัตถุ แต่คงคำสอน 30 องศาเดิม

![04 Rotate](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/04-rotate.png)

5. Format switch — ใช้ icon คู่กับชื่อรูปแบบ ไม่ใช้เนื้อหาซ่อมที่ภาพแต่งขึ้น

![05 Reference switch](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/05-reference-switch.png)

6. Move reference — เน้น handle จริง ตัด Grip ที่ผิดออก เพราะของจริงใช้ Trigger/pinch

![06 Move reference](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/06-reference-move.png)

7. Reading — ใช้แนว spacing เท่านั้น ไม่แทนที่เอกสารวิจัยด้วยภาพชิ้นส่วนที่ AI แต่งขึ้น

![07 Reading](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/07-reference-reading.png)

8. Pause — ใช้ hierarchy ของสถานะกับ action ที่ชัด คงสิทธิ์ resume ตาม protocol เดิม

![08 Pause](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/08-pause.png)

9. Active task — ตัด card และปุ่มขนาดใหญ่ที่แข่งกับชิ้นงานออก

![09 Task](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/09-neutral-task.png)

10. Minimized reference — ตัด composition นี้ออกตาม feedback ของผู้ใช้: มีปุ่มและแผงหลายชุดจนสับสน

![10 Rejected](D:/VR_TMU/XR-Research-V2/Documentation/design-references/vr-clarity-20261004/10-minimized-reference.png)
