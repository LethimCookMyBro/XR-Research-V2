namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1FanObservations
    {
        public static string TargetForPart(string target)
        {
            switch (target)
            {
                case "B.blade": case "B.spinner": return "B.blade_mount";
                case "B.rear_guard": case "B.rear_guard_lock": return "B.rear_guard_mount";
                default: return target;
            }
        }

        public static string Get(S1TaskBState value, string target, string language)
        {
            bool jp = language == nameof(ParticipantLanguage.Japanese);
            bool en = language == nameof(ParticipantLanguage.English);
            string Text(string english, string thai, string japanese) => en ? english : jp ? japanese : thai;
            if (value == null || !value.systemIntegrityValid || !en && !jp && language != nameof(ParticipantLanguage.Thai))
                return ResearchStrings.Get("examine.info.unavailable", language);
            if (!value.powerIsolated || value.motorRunning) return Text("Wait until the fan stops before examining it.", "ยังส่องดูไม่ได้: พัดลมยังไม่หยุดนิ่ง", "扇風機が止まっていないため、まだ見られません。");
            // The station has one "Examine" chip on the whole fan; it reports every area visible right now, one line each (owner 29 Oct:
            // the Reader told participants to examine each area but the chip only returned the unavailable placeholder).
            if (target == "B.module")
            {
                // Short lines so the whole-fan chip keeps its tested 340 x 220 size; front-guard-on hides the blade line.
                string front = value.frontGuardPlacement == S1Placement.NotInstalled ? (Text("Front guard: removed", "ตะแกรงหน้า: ถอดอยู่", "前ガード：外れている"))
                    : value.frontGuardAligned && value.frontGuardClipped ? (Text("Front guard: marks aligned; clip closed", "ตะแกรงหน้า: ตรง คลิปปิด", "前ガード：マーク一致・クリップ閉"))
                    : (Text("Front guard: misaligned or clip open", "ตะแกรงหน้า: ไม่ตรง หรือคลิปเปิด", "前ガード：ずれ／クリップ開"));
                string blade = value.frontGuardPlacement != S1Placement.NotInstalled ? null
                    : value.bladePlacement == S1Placement.Seated && value.bladeKeyAligned && value.spinner == S1ThreadState.Tight
                        ? (Text("Blade: fully seated; does not move", "ใบพัด: ใส่สุด ไม่ขยับ", "羽根：奥まで入り動かない")) : (Text("Blade: not fully seated or moves", "ใบพัด: ใส่ไม่สุด หรือขยับ", "羽根：浮き／動く"));
                string rear = value.rearGuardPlacement == S1Placement.NotInstalled ? (Text("Rear guard: removed", "ตะแกรงหลัง: ถอดอยู่", "後ガード：外れている"))
                    : value.rearLock == S1ThreadState.Tight ? (Text("Rear guard: rim does not move", "ตะแกรงหลัง: ขอบไม่ขยับ", "後ガード：縁が動かない"))
                    : (Text("Rear guard: rim moves", "ตะแกรงหลัง: ขอบขยับ", "後ガード：縁が動く"));
                return blade == null ? front + "\n" + rear : front + "\n" + blade + "\n" + rear;
            }
            switch (target)
            {
                case "B.front_guard_clip":
                    return value.frontGuardClipped
                        ? Text("The clip is closed.", "คลิปปิดอยู่", "クリップは閉じています。")
                        : Text("The clip is open.", "คลิปเปิดอยู่", "クリップは開いています。");
                case "B.front_guard":
                    if (value.frontGuardPlacement == S1Placement.NotInstalled) return Text("The front guard is removed.", "ตะแกรงหน้าถอดออกอยู่", "前ガードは外れています。");
                    return value.frontGuardAligned && value.frontGuardClipped
                        ? Text("The marks align and the clip is closed.", "เครื่องหมายตรงกัน และคลิปปิด", "マークが合い、クリップが閉じています。")
                        : Text("The marks do not align or the clip is open.", "เครื่องหมายไม่ตรง หรือคลิปไม่ปิด", "マークがずれているか、クリップが閉じていません。");
                case "B.blade_mount":
                    if (value.frontGuardPlacement != S1Placement.NotInstalled) return Text("The front guard hides the blade.", "ตะแกรงหน้าบังใบพัดอยู่", "前ガードで羽根が見えません。");
                    return value.bladePlacement == S1Placement.Seated && value.bladeKeyAligned && value.spinner == S1ThreadState.Tight
                        ? Text("The blade is fully seated and does not move.", "ใบพัดใส่สุด ไม่ขยับ", "羽根は奥まで入り、動きません。")
                        : Text("The blade is not fully seated or moves.", "ใบพัดใส่ไม่สุด หรือขยับได้", "羽根が奥まで入っていないか、動きます。");
                case "B.rear_guard_mount":
                    if (value.rearGuardPlacement == S1Placement.NotInstalled) return Text("The rear guard is removed.", "ตะแกรงหลังถอดออกอยู่", "後ガードは外れています。");
                    string movement = value.rearLock == S1ThreadState.Tight
                        ? Text("The rear guard rim does not move relative to the head.", "ขอบตะแกรงหลังไม่ขยับเทียบหัวพัดลม", "後ガードの縁はヘッドに対して動きません。")
                        : Text("The rear guard rim moves relative to the head.", "ขอบตะแกรงหลังขยับเทียบหัวพัดลม", "後ガードの縁がヘッドに対して動きます。");
                    return movement + (value.bladePlacement == S1Placement.NotInstalled
                        ? Text(" The front of the locking ring is visible.", " · เห็นหน้าวงแหวนล็อก", " 固定リングの前面が見えます。")
                        : Text(" The blade hides the front of the locking ring.", " · ใบพัดบังหน้าวงแหวนล็อกอยู่", " 固定リングの前面は羽根に隠れています。"));
                default: return ResearchStrings.Get("examine.info.unavailable", language);
            }
        }
    }
}
