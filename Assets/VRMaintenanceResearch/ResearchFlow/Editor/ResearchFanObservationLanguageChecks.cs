#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchFanObservationLanguageChecks
    {
        public static string Run(string evidencePath)
        {
            if (File.Exists(evidencePath)) throw new InvalidOperationException("Fresh evidence path required.");
            var task=UnityEngine.Object.FindFirstObjectByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include);
            if (!task) throw new InvalidOperationException("Authored Task B required.");
            string Snapshot()
            {
                var captured=task.Capture();
                captured.capturedAtSeconds=0;
                return JsonUtility.ToJson(captured);
            }
            string before=Snapshot();
            var state=JsonUtility.FromJson<S1TaskBState>(before);
            if (!state.systemIntegrityValid) throw new InvalidOperationException("Authored fan integrity required.");
            var translations=new[] {
                new KeyValuePair<string,string>("扇風機が止まっていないため、まだ見られません。", "Wait until the fan stops before examining it."),
                new KeyValuePair<string,string>("前ガード：外れている", "Front guard: removed"),
                new KeyValuePair<string,string>("前ガード：マーク一致・クリップ閉", "Front guard: marks aligned; clip closed"),
                new KeyValuePair<string,string>("前ガード：ずれ／クリップ開", "Front guard: misaligned or clip open"),
                new KeyValuePair<string,string>("羽根：奥まで入り動かない", "Blade: fully seated; does not move"),
                new KeyValuePair<string,string>("羽根：浮き／動く", "Blade: not fully seated or moves"),
                new KeyValuePair<string,string>("後ガード：外れている", "Rear guard: removed"),
                new KeyValuePair<string,string>("後ガード：縁が動かない", "Rear guard: rim does not move"),
                new KeyValuePair<string,string>("後ガード：縁が動く", "Rear guard: rim moves"),
                new KeyValuePair<string,string>("前ガードは外れています。", "The front guard is removed."),
                new KeyValuePair<string,string>("マークが合い、クリップが閉じています。", "The marks align and the clip is closed."),
                new KeyValuePair<string,string>("マークがずれているか、クリップが閉じていません。", "The marks do not align or the clip is open."),
                new KeyValuePair<string,string>("前ガードで羽根が見えません。", "The front guard hides the blade."),
                new KeyValuePair<string,string>("羽根は奥まで入り、動きません。", "The blade is fully seated and does not move."),
                new KeyValuePair<string,string>("羽根が奥まで入っていないか、動きます。", "The blade is not fully seated or moves."),
                new KeyValuePair<string,string>("後ガードは外れています。", "The rear guard is removed."),
                new KeyValuePair<string,string>("後ガードの縁はヘッドに対して動きません。", "The rear guard rim does not move relative to the head."),
                new KeyValuePair<string,string>("後ガードの縁がヘッドに対して動きます。", "The rear guard rim moves relative to the head."),
                new KeyValuePair<string,string>(" 固定リングの前面が見えます。", " The front of the locking ring is visible."),
                new KeyValuePair<string,string>(" 固定リングの前面は羽根に隠れています。", " The blade hides the front of the locking ring.")
            }.OrderByDescending(p=>p.Key.Length).ToArray();
            int checks=0;
            void Equal(string expected,string actual,string name)
            {
                checks++;
                if (expected!=actual) throw new InvalidOperationException(name+": expected "+expected+"; got "+actual);
            }
            string English(string japanese)
            {
                foreach(var pair in translations) japanese=japanese.Replace(pair.Key,pair.Value);
                return japanese;
            }
            var placements=new[]{S1Placement.NotInstalled,S1Placement.Partial,S1Placement.Seated};
            var threads=new[]{S1ThreadState.Absent,S1ThreadState.EngagedLoose,S1ThreadState.Tight};
            var bits=new[]{false,true};
            var targets=new[]{"B.module","B.front_guard","B.blade_mount","B.rear_guard_mount"};
            state.powerIsolated=true; state.motorRunning=false;
            foreach(var front in placements) foreach(var aligned in bits) foreach(var clipped in bits)
            foreach(var blade in placements) foreach(var keyed in bits) foreach(var spinner in threads)
            foreach(var rear in placements) foreach(var rearLock in threads)
            {
                state.frontGuardPlacement=front; state.frontGuardAligned=aligned; state.frontGuardClipped=clipped;
                state.bladePlacement=blade; state.bladeKeyAligned=keyed; state.spinner=spinner;
                state.rearGuardPlacement=rear; state.rearLock=rearLock;
                foreach(var target in targets)
                {
                    foreach(var language in new[]{"Thai","Japanese"})
                        Equal(ResearchFanObservationBaseline20261030.Get(state,target,language)
                            .Replace("นั่งไม่เต็ม","ใส่ไม่สุด").Replace("นั่งเต็ม","ใส่สุด").Replace("ใบพัด: ไม่เต็ม","ใบพัด: ใส่ไม่สุด"),
                            ResearchS1FanObservations.Get(state,target,language),target+"/"+language);
                    string jp=ResearchFanObservationBaseline20261030.Get(state,target,"Japanese");
                    Equal(English(jp),ResearchS1FanObservations.Get(state,target,"English"),target+"/English");
                }
            }
            foreach(var language in new[]{"Thai","Japanese","English"})
            {
                string unavailable=ResearchStrings.Get("examine.info.unavailable",language);
                Equal(unavailable,ResearchS1FanObservations.Get(null,"B.module",language),"null/"+language);
                state.systemIntegrityValid=false;
                Equal(unavailable,ResearchS1FanObservations.Get(state,"B.module",language),"integrity/"+language);
                state.systemIntegrityValid=true;
                Equal(unavailable,ResearchS1FanObservations.Get(state,"unknown",language),"unknown target/"+language);
                foreach(var power in bits) foreach(var running in bits)
                {
                    state.powerIsolated=power; state.motorRunning=running;
                    if(power&&!running)continue;
                    string expected=language=="English"?"Wait until the fan stops before examining it.":ResearchFanObservationBaseline20261030.Get(state,"B.module",language);
                    Equal(expected,ResearchS1FanObservations.Get(state,"B.module",language),"power gate/"+language);
                }
                state.powerIsolated=true; state.motorRunning=false;
            }
            Equal(ResearchStrings.Get("examine.info.unavailable","Unknown"),ResearchS1FanObservations.Get(state,"B.module","Unknown"),"unknown language");
            Equal(before,Snapshot(),"live fan unchanged");
            string result="FAN_OBSERVATION_LANGUAGES_PASS checks="+checks+" languages=3 live_fan_unchanged=true";
            File.WriteAllText(evidencePath,result);
            return result;
        }
    }
}
namespace VRMaintenanceResearch.ResearchFlow
{
    internal static class ResearchFanObservationBaseline20261030
    {
        public static string Get(S1TaskBState value, string target, string language)
        {
            bool jp = language == nameof(ParticipantLanguage.Japanese);
            if (value == null || !value.systemIntegrityValid || !jp && language != nameof(ParticipantLanguage.Thai))
                return ResearchStrings.Get("examine.info.unavailable", language);
            if (!value.powerIsolated || value.motorRunning) return jp ? "扇風機が止まっていないため、まだ見られません。" : "ยังส่องดูไม่ได้: พัดลมยังไม่หยุดนิ่ง";
            // The station has one "Examine" chip on the whole fan; it reports every area visible right now, one line each (owner 29 Oct:
            // the Reader told participants to examine each area but the chip only returned the unavailable placeholder).
            if (target == "B.module")
            {
                // Short lines so the whole-fan chip keeps its tested 340 x 220 size; front-guard-on hides the blade line.
                string front = value.frontGuardPlacement == S1Placement.NotInstalled ? (jp ? "前ガード：外れている" : "ตะแกรงหน้า: ถอดอยู่")
                    : value.frontGuardAligned && value.frontGuardClipped ? (jp ? "前ガード：マーク一致・クリップ閉" : "ตะแกรงหน้า: ตรง คลิปปิด")
                    : (jp ? "前ガード：ずれ／クリップ開" : "ตะแกรงหน้า: ไม่ตรง หรือคลิปเปิด");
                string blade = value.frontGuardPlacement != S1Placement.NotInstalled ? null
                    : value.bladePlacement == S1Placement.Seated && value.bladeKeyAligned && value.spinner == S1ThreadState.Tight
                        ? (jp ? "羽根：奥まで入り動かない" : "ใบพัด: นั่งเต็ม ไม่ขยับ") : (jp ? "羽根：浮き／動く" : "ใบพัด: ไม่เต็ม หรือขยับ");
                string rear = value.rearGuardPlacement == S1Placement.NotInstalled ? (jp ? "後ガード：外れている" : "ตะแกรงหลัง: ถอดอยู่")
                    : value.rearLock == S1ThreadState.Tight ? (jp ? "後ガード：縁が動かない" : "ตะแกรงหลัง: ขอบไม่ขยับ")
                    : (jp ? "後ガード：縁が動く" : "ตะแกรงหลัง: ขอบขยับ");
                return blade == null ? front + "\n" + rear : front + "\n" + blade + "\n" + rear;
            }
            switch (target)
            {
                case "B.front_guard":
                    if (value.frontGuardPlacement == S1Placement.NotInstalled) return jp ? "前ガードは外れています。" : "ตะแกรงหน้าถอดออกอยู่";
                    return value.frontGuardAligned && value.frontGuardClipped
                        ? jp ? "マークが合い、クリップが閉じています。" : "เครื่องหมายตรงกัน และคลิปปิด"
                        : jp ? "マークがずれているか、クリップが閉じていません。" : "เครื่องหมายไม่ตรง หรือคลิปไม่ปิด";
                case "B.blade_mount":
                    if (value.frontGuardPlacement != S1Placement.NotInstalled) return jp ? "前ガードで羽根が見えません。" : "ตะแกรงหน้าบังใบพัดอยู่";
                    return value.bladePlacement == S1Placement.Seated && value.bladeKeyAligned && value.spinner == S1ThreadState.Tight
                        ? jp ? "羽根は奥まで入り、動きません。" : "ใบพัดนั่งเต็ม ไม่ขยับ"
                        : jp ? "羽根が奥まで入っていないか、動きます。" : "ใบพัดนั่งไม่เต็ม หรือขยับได้";
                case "B.rear_guard_mount":
                    if (value.rearGuardPlacement == S1Placement.NotInstalled) return jp ? "後ガードは外れています。" : "ตะแกรงหลังถอดออกอยู่";
                    string movement = value.rearLock == S1ThreadState.Tight
                        ? jp ? "後ガードの縁はヘッドに対して動きません。" : "ขอบตะแกรงหลังไม่ขยับเทียบหัวพัดลม"
                        : jp ? "後ガードの縁がヘッドに対して動きます。" : "ขอบตะแกรงหลังขยับเทียบหัวพัดลม";
                    return movement + (value.bladePlacement == S1Placement.NotInstalled
                        ? jp ? " 固定リングの前面が見えます。" : " · เห็นหน้าวงแหวนล็อก"
                        : jp ? " 固定リングの前面は羽根に隠れています。" : " · ใบพัดบังหน้าวงแหวนล็อกอยู่");
                default: return ResearchStrings.Get("examine.info.unavailable", language);
            }
        }
    }
}

#endif
