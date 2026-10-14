namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1FanObservations
    {
        public static string Get(S1TaskBState value, string target, string language)
        {
            bool jp = language == nameof(ParticipantLanguage.Japanese);
            if (value == null || !value.systemIntegrityValid || !jp && language != nameof(ParticipantLanguage.Thai))
                return ResearchStrings.Get("examine.info.unavailable", language);
            if (!value.powerIsolated || value.motorRunning) return jp ? "停止状態を確認できないため観察できません。" : "ตรวจการสังเกตไม่ได้ขณะยืนยันสถานะหยุดไม่ได้";
            switch (target)
            {
                case "B.front_guard":
                    if (value.frontGuardPlacement == S1Placement.NotInstalled) return jp ? "前ガードは本体から離れています。" : "ตะแกรงหน้าแยกจากตัวเครื่อง";
                    return value.frontGuardAligned && value.frontGuardClipped
                        ? jp ? "合わせ位置が一致し、クリップは閉じています。" : "ตำแหน่งประกบตรงกันและคลิปปิด"
                        : jp ? "合わせ位置またはクリップの状態が閉じた位置と異なります。" : "ตำแหน่งประกบหรือคลิปต่างจากตำแหน่งปิด";
                case "B.blade_mount":
                    if (value.frontGuardPlacement != S1Placement.NotInstalled) return jp ? "前ガードが観察箇所を覆っています。" : "ตะแกรงหน้าบังบริเวณสังเกต";
                    return value.bladePlacement == S1Placement.Seated && value.bladeKeyAligned && value.spinner == S1ThreadState.Tight
                        ? jp ? "模擬観察では羽根の取り付け部に相対的な動きは見られません。" : "ใน routine จำลอง ไม่พบการขยับสัมพันธ์ของชุดยึดใบพัด"
                        : jp ? "羽根の取り付け位置または保持状態に変化が見られます。" : "พบความต่างของตำแหน่งนั่งหรือการรั้งชุดใบพัด";
                case "B.rear_guard_mount":
                    if (value.rearGuardPlacement == S1Placement.NotInstalled) return jp ? "後ガードは本体から離れています。" : "ตะแกรงหลังแยกจากตัวเครื่อง";
                    string movement = value.rearLock == S1ThreadState.Tight
                        ? jp ? "模擬観察では後ガードとヘッドの間に相対的な動きは見られません。" : "ใน routine จำลอง ไม่พบตะแกรงขยับเทียบกับหัวเครื่อง"
                        : jp ? "模擬観察では後ガードがヘッドに対して動きます。" : "ใน routine จำลอง ตะแกรงขยับเทียบกับหัวเครื่อง";
                    return movement + (value.bladePlacement == S1Placement.NotInstalled
                        ? jp ? " 前側の合わせ面を観察できます。" : " มองเห็นผิวประกบด้านหน้า"
                        : jp ? " 前側の合わせ面は羽根に隠れています。" : " ใบพัดบังผิวประกบด้านหน้า");
                default: return ResearchStrings.Get("examine.info.unavailable", language);
            }
        }
    }
}
