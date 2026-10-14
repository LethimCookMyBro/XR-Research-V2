using System;
using System.Collections.Generic;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>Fictitious technical-demo content, never an approved study manual.</summary>
    public static class ResearchDemoManual
    {
        public const string ContentVersion = "fictitious-demo-maintenance-2";
        private static readonly string[][] Rows =
        {
            new[] { "A.task_info", "Workstation module\n• The task concerns replacement of the designated module.\n• Module and station labels specify compatibility.\n• Inspect verifies the completed state.", "โมดูลสถานีฝึก\n• งานนี้เป็นการเปลี่ยนโมดูลที่กำหนด\n• ฉลากโมดูลและสถานีระบุความเข้ากันได้\n• “ตรวจผล” ใช้ยืนยันสถานะที่เสร็จสมบูรณ์", "作業ステーションのモジュール\n• 課題は指定モジュールの交換です。\n• モジュールとステーションのラベルが互換性を示します。\n• 「検査」で完了状態を検査します。" },
            new[] { "A.component_guide", "Module features\n• Model label and orientation mark\n• Connector and mounting latch\n• C-10 and C-20 are different models.\n• Matching shape alone does not establish compatibility.", "ลักษณะของโมดูล\n• ฉลากรุ่นและเครื่องหมายทิศทาง\n• ขั้วต่อและสลักยึด\n• C-10 และ C-20 เป็นคนละรุ่น\n• รูปร่างเหมือนกันไม่ได้ยืนยันว่าใช้แทนกันได้", "モジュールの構成\n• 型式ラベルと向きの印\n• コネクターと固定ラッチ\n• C-10とC-20は異なる型式です。\n• 形が同じだけでは互換性は判断できません。" },
            new[] { "A.manual", "Compatibility and mounting\n• The station label defines the compatible model.\n• The module faces the socket's orientation mark.\n• The module sits fully in its mount with the connector attached.\n• An open latch permits removal; a closed latch secures the seated module.", "ความเข้ากันได้และการยึด\n• ฉลากสถานีระบุรุ่นที่ใช้ร่วมกันได้\n• โมดูลหันตามเครื่องหมายทิศทางของช่อง\n• โมดูลเข้าฐานจนสุดและต่อขั้วต่ออยู่\n• สลักที่ปลดอยู่ทำให้ถอดได้ สลักที่ยึดอยู่ตรึงโมดูลเข้าที่", "互換性と固定\n• ステーションのラベルが対応型式を示します。\n• モジュールの向きはソケットの印に合います。\n• モジュールは奥まで収まり、コネクターが接続されています。\n• ラッチを外すと取り外せます。固定したラッチは収まったモジュールを保持します。" },
            new[] { "A.troubleshooting", "General conditions\n• Incomplete seating or an open latch\n• A detached connector\n• An incompatible model or reversed orientation\n• These can prevent completion; they are not a diagnosis of the current workstation.", "เงื่อนไขทั่วไป\n• การใส่ไม่สุดหรือสลักไม่ยึด\n• ขั้วต่อหลุด\n• รุ่นไม่ตรงหรือทิศทางกลับด้าน\n• เงื่อนไขเหล่านี้อาจทำให้งานไม่สมบูรณ์ ไม่ใช่การวินิจฉัยสถานีปัจจุบัน", "一般的な条件\n• 差し込み不足やラッチの未固定\n• コネクターの外れ\n• 非対応の型式や逆向きの取り付け\n• これらは完了を妨げる場合があります。現在のステーションの診断ではありません。" },
            new[] { "B.task_info", "Fan station\n• Abnormal operation has been reported.\n• The available information covers several possible causes.\n• Inspect verifies the completed state.", "สถานีพัดลม\n• พบความผิดปกติในการทำงาน\n• ข้อมูลครอบคลุมสาเหตุที่เป็นไปได้หลายอย่าง\n• “ตรวจผล” ใช้ยืนยันสถานะที่เสร็จสมบูรณ์", "ファンステーション\n• 動作の異常が報告されています。\n• 資料には複数の原因候補が含まれます。\n• 「検査」で完了状態を検査します。" },
            new[] { "B.component_guide", "Fan components\n• The power connector supplies power.\n• Rotor clearance allows rotation.\n• The module has an orientation mark and mounting latch.\n• The module label identifies its type and condition.", "ส่วนประกอบพัดลม\n• ขั้วต่อไฟทำหน้าที่จ่ายไฟ\n• พื้นที่ว่างรอบใบพัดช่วยให้หมุนได้\n• โมดูลมีเครื่องหมายทิศทางและสลักยึด\n• ฉลากโมดูลระบุชนิดและสภาพ", "ファンの構成\n• 電源コネクターが電力を供給します。\n• 回転部の空間が回転を可能にします。\n• モジュールには向きの印と固定ラッチがあります。\n• ラベルはモジュールの種類と状態を示します。" },
            new[] { "B.manual", "Normal operation\n• Power is connected.\n• Rotor space is clear.\n• Mounting orientation follows the mark.\n• A serviceable compatible module is secured in its mount.\n• Connection, clearance, orientation, and module condition are separate requirements.", "เงื่อนไขการทำงานปกติ\n• ไฟเชื่อมต่ออยู่\n• พื้นที่ใบพัดว่าง\n• ติดตั้งตามเครื่องหมายทิศทาง\n• โมดูลสภาพพร้อมใช้งานที่เข้ากันได้ยึดแน่นในฐาน\n• การเชื่อมต่อ พื้นที่ว่าง ทิศทาง และสภาพโมดูลเป็นเงื่อนไขแยกกัน", "正常動作の条件\n• 電源が接続されています。\n• 回転部の空間が確保されています。\n• 取り付け方向が印に合っています。\n• 正常で互換性のあるモジュールが固定されています。\n• 接続、空間、向き、モジュールの状態は別々の条件です。" },
            new[] { "B.troubleshooting", "Symptoms and possible causes\n• No response: power connection or module condition\n• Restricted rotation: obstruction or mounting\n• Incorrect output: orientation or module condition\n• One symptom may have more than one cause.", "อาการและสาเหตุที่เป็นไปได้\n• ไม่ตอบสนอง: การต่อไฟหรือสภาพโมดูล\n• หมุนติดขัด: สิ่งกีดขวางหรือการติดตั้ง\n• ผลการทำงานผิดปกติ: ทิศทางหรือสภาพโมดูล\n• อาการเดียวอาจมีหลายสาเหตุ", "症状と考えられる原因\n• 反応なし：電源接続やモジュールの状態\n• 回転の制限：障害物や取り付け\n• 出力の異常：向きやモジュールの状態\n• 一つの症状に複数の原因が考えられます。" },
            // 24 Sep format scheme. User Manual reuses the rows above; the demo only has text, so the
            // illustrated and video formats are explicit technical placeholders, never task content.
            new[] { "A.step_by_step", "Technical demo steps (fictitious module)\n1. Read the station label for the compatible model.\n2. Open the latch and remove the installed module.\n3. Seat the compatible module along the orientation mark.\n4. Attach the connector and close the latch.\n5. Select Inspect.", "ขั้นตอนสาธิต (โมดูลสมมติ)\n1. อ่านฉลากสถานีเพื่อดูรุ่นที่ใช้ได้\n2. ปลดสลักแล้วถอดโมดูลเดิมออก\n3. ใส่โมดูลรุ่นที่ใช้ได้ตามเครื่องหมายทิศทาง\n4. ต่อขั้วต่อแล้วล็อกสลัก\n5. เลือก “ตรวจผล”", "技術デモの手順（架空のモジュール）\n1. ステーションのラベルで対応型式を確認します。\n2. ラッチを外し、取り付け済みのモジュールを取り外します。\n3. 対応するモジュールを向きの印に合わせて差し込みます。\n4. コネクターを接続し、ラッチを固定します。\n5. 「検査」を選びます。" },
            new[] { "B.step_by_step", "Technical demo steps (fictitious fan)\n1. Check that power is connected.\n2. Check that the rotor space is clear.\n3. Compare the mounting orientation with the mark.\n4. Check the module label and condition.\n5. Correct any condition that is not met, then select Inspect.", "ขั้นตอนสาธิต (พัดลมสมมติ)\n1. ตรวจว่าต่อไฟแล้ว\n2. ตรวจว่าพื้นที่ใบพัดว่าง\n3. เทียบทิศทางการติดตั้งกับเครื่องหมาย\n4. ตรวจฉลากและสภาพโมดูล\n5. แก้ไขเงื่อนไขที่ยังไม่ครบ แล้วเลือก “ตรวจผล”", "技術デモの手順（架空のファン）\n1. 電源が接続されているか確認します。\n2. 回転部の空間が確保されているか確認します。\n3. 取り付け方向を印と照合します。\n4. モジュールのラベルと状態を確認します。\n5. 満たしていない条件を直し、「検査」を選びます。" },
            new[] { "any.illustrated", "Technical placeholder\nThe illustrated version is not included in this technical build.", "ตัวอย่างทางเทคนิค\nยังไม่มีฉบับภาพประกอบในเวอร์ชันทดสอบนี้", "技術用の仮表示\nこの技術テスト版には図解版が含まれていません。" },
            new[] { "any.video", "Technical placeholder\nThe video version is not included in this technical build.", "ตัวอย่างทางเทคนิค\nยังไม่มีฉบับวิดีโอในเวอร์ชันทดสอบนี้", "技術用の仮表示\nこの技術テスト版には動画版が含まれていません。" }
        };

        public static IReadOnlyDictionary<string, string> All(string language)
        {
            var index = string.Equals(language, "Thai", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "TH", StringComparison.OrdinalIgnoreCase) ? 2 :
                string.Equals(language, "Japanese", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "JP", StringComparison.OrdinalIgnoreCase) || string.Equals(language, "JA", StringComparison.OrdinalIgnoreCase) ? 3 : 1;
            var content = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in Rows) content.Add(row[0], row[index]);
            return content;
        }

        public static string Get(string taskId, string section, string language)
        {
            var all = All(language);
            // User Manual (slide 4: parts, functions, common problems) = the existing parts/manual/symptom rows.
            if (section == "user_manual")
            {
                var parts = new List<string>();
                foreach (var key in new[] { "component_guide", "manual", "troubleshooting" })
                    if (all.TryGetValue(taskId + "." + key, out var part)) parts.Add(part);
                return string.Join("\n\n", parts);
            }
            return all.TryGetValue(taskId + "." + section, out var text) || all.TryGetValue("any." + section, out text) ? text : string.Empty;
        }
    }
}
