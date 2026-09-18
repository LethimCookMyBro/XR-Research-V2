using System.Collections.Generic;
namespace VRMaintenanceResearch.TrainingV2
{
    public static class TrainingStrings
    {
        // Japanese is deliberately not participant-enabled until translation and equivalence review.
        public const string ContentVersion = "training-ui-th-0.1";
        public const string JapaneseStatus = "DRAFT_NOT_TRANSLATED_NOT_EQUIVALENCE_VALIDATED";
        public static readonly IReadOnlyDictionary<string, string> Thai = new Dictionary<string, string>
        {
            ["header"] = "TRAINING  /  บทฝึกกลาง",
            ["ready"] = "พร้อมเริ่มฝึก",
            ["footer"] = "ชี้ด้วยจอย  •  กด Trigger เพื่อเลือก",
            ["start_title"] = "ลองจับ  หมุน  และจัดวาง",
            ["start_body"] = "ฝึกกับชิ้นงานสีฟ้าที่มีลูกศรหนึ่งชิ้น\n\nGrip: กำมือเพื่อจับใกล้ตัว ปล่อยเพื่อวาง\nTrigger: ชี้แผงแล้วกดเพื่อเลือก\nจอยซ้าย: เดิน  /  จอยขวา: หันเป็นจังหวะ",
            ["training_title"] = "จัดวาง แล้วตรวจด้วยตัวเอง",
            ["training_body"] = "วางชิ้นงานสีฟ้าบนจุดเป้าหมาย และหันเครื่องหมายให้ตรง\nปล่อยมือก่อนกดตรวจการวาง\nยังไม่ผ่าน? จัดใหม่ แล้วตรวจซ้ำได้ในรอบเดิม",
            ["manual_title"] = "คู่มือบทฝึก",
            ["manual_body"] = "",
            ["manual_title_0"] = "คู่มือ  1 / 3  ·  การควบคุม",
            ["manual_body_0"] = "นำมือเข้าใกล้ชิ้นงาน แล้วกด Grip ค้างเพื่อจับ\nหมุนจอยเพื่อเปลี่ยนทิศของชิ้นงาน\nปล่อย Grip เพื่อวาง\n\nชี้แผงด้วยมือที่ว่าง แล้วกด Trigger\nมืออีกข้างยังถือชิ้นงานได้",
            ["manual_title_1"] = "คู่มือ  2 / 3  ·  กติกาการวาง",
            ["manual_body_1"] = "วางชิ้นงานลงบนจุดวางให้ตรงตำแหน่ง\nหันเครื่องหมายทิศทางให้ตรงกับจุดวาง\nปล่อยชิ้นงานให้อยู่นิ่งก่อนตรวจ\n\nวางไม่ตรงหรือยังถืออยู่ จะยังไม่ผ่าน\nค่าคลาดเคลื่อนเป็นค่าของต้นแบบบทฝึก",
            ["manual_title_2"] = "คู่มือ  3 / 3  ·  ตรวจและฝึกซ้ำ",
            ["manual_body_2"] = "กดตรวจการวาง เมื่อต้องการตรวจสภาพขณะนั้น\nหากไม่ผ่าน ให้อ่านผล กลับไปแก้ แล้วตรวจใหม่\nหากผ่าน รอบฝึกนี้จะจบ\n\nเลือกเริ่มรอบฝึกใหม่เมื่อต้องการฝึกซ้ำ\nปุ่มพักใช้หยุดการทำงานชั่วคราวได้",
            ["examine_title"] = "ดูวัตถุ  ·  ชิ้นงานกลาง",
            ["examine_body"] = "ชิ้นงานฝึกมีเครื่องหมายบอกทิศทาง\nจุดวางมีเครื่องหมายสำหรับเทียบทิศ\n\nหน้านี้แสดงข้อมูลทั่วไปของชิ้นงานเท่านั้น\nไม่ได้ตรวจความถูกต้อง หรือเปลี่ยนการจัดวาง",
            ["inspect_title"] = "ผลตรวจครั้งก่อน",
            ["inspect_body"] = "ผลนี้เป็นสภาพเมื่อกดตรวจครั้งก่อน\nเมื่อแก้การจัดวางแล้ว ให้กลับไปกดตรวจใหม่",
            ["pause_title"] = "พักบทฝึก",
            ["pause_body"] = "การเปลี่ยนสถานะบทฝึกหยุดไว้ชั่วคราว\nคุณยังมองไปรอบตัวได้ตามปกติ\n\nเลือกกลับมาฝึกเพื่อทำรอบเดิมต่อ\nหรือยุติรอบนี้ โดยไม่ถือว่าเป็นผลตรวจไม่ผ่าน",
            ["passed_title"] = "ผ่านแล้ว  ·  จบรอบฝึกนี้",
            ["passed_body"] = "การจัดวางผ่านเกณฑ์ของบทฝึก\nรอบนี้จบแล้ว โดยไม่เริ่มงานจริงอัตโนมัติ\n\nเลือกเริ่มรอบฝึกใหม่เพื่อฝึกอีกครั้ง",
            ["aborted_title"] = "ยุติรอบฝึกแล้ว",
            ["aborted_body"] = "รอบนี้สิ้นสุดโดยการยุติ\nไม่ใช่ผลตรวจการวางไม่ผ่าน\n\nคุณเริ่มรอบฝึกใหม่ได้เมื่อพร้อม",
            ["button_start"] = "เริ่มบทฝึก", ["button_manual_open"] = "คู่มือ",
            ["button_examine"] = "ดูวัตถุ", ["button_inspect"] = "ตรวจการวาง",
            ["button_pause"] = "พัก", ["button_manual_previous"] = "ก่อนหน้า",
            ["button_manual_next"] = "หน้าถัดไป", ["button_manual_close"] = "ปิดคู่มือ",
            ["button_back"] = "กลับไปฝึก", ["button_resume"] = "กลับมาฝึก",
            ["button_abort"] = "ยุติรอบนี้", ["button_new_trial"] = "เริ่มรอบฝึกใหม่"
        };
        public static string Get(string key, string language = "th")
        {
            if (language != "th") return "[" + JapaneseStatus + "] " + key;
            return Thai.TryGetValue(key, out var value) ? value : key;
        }
    }
}
