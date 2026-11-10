#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchIntentionalExamineV7Checks
    {
        public static string Run(string output)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !Path.IsPathRooted(output) || File.Exists(output))
                throw new InvalidOperationException("Stopped Editor and fresh absolute output required.");
            var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
            var failures = new List<string>(); int checks = 0;
            void Check(bool pass, string name) { checks++; if (!pass) failures.Add(name); }
            const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
            var find = typeof(ResearchExamineTarget).GetMethod("FindPhysicalPart", hidden);
            var blocked = typeof(ResearchExamineTarget).GetMethod("PhysicalSelectionObstructed", hidden);
            var root = new GameObject("IntentionalExamineV7Fixture") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                GameObject Part(string id, float z)
                {
                    var go = new GameObject(id, typeof(BoxCollider)); go.transform.SetParent(root.transform);
                    go.transform.position = new Vector3(8000, 8000, z); var input = go.AddComponent<ResearchS1TaskAInput>(); input.stableId = id; return go;
                }
                var near = Part("M01", 3); var far = Part("S01", 5);
                var nearInput = near.GetComponent<ResearchS1TaskAInput>(); var farInput = far.GetComponent<ResearchS1TaskAInput>();
                var nearColliders = new Collider[] { near.GetComponent<BoxCollider>() };
                var farColliders = new Collider[] { far.GetComponent<BoxCollider>() };
                var parts = new[] { (farInput.stableId, far.transform, farColliders), (nearInput.stableId, near.transform, nearColliders) };
                var ray = new Ray(new Vector3(8000, 8000, 0), Vector3.forward);
                Physics.SyncTransforms();
                object[] args = { parts, ray, 10f, null, null };
                Check((string)find.Invoke(null, args) == nearInput.stableId, "physical ray picks nearest actual part, not catalog order");
                Check(ReferenceEquals(args[3], nearColliders), "physical hit retains selected collider set");
                Check(ReferenceEquals(args[4], near.transform), "physical hit returns the selected part anchor");
                Check(!((bool)blocked.Invoke(null, new object[] { ray, args[2], nearColliders })), "selected part does not occlude itself");
                near.GetComponent<BoxCollider>().enabled = false; Physics.SyncTransforms(); args = new object[] { parts, ray, 10f, null, null };
                Check((string)find.Invoke(null, args) == farInput.stableId, "disabled collider is not selectable");
                far.SetActive(false); args = new object[] { parts, ray, 10f, null, null };
                Check(find.Invoke(null, args) == null, "inactive part is not selectable");
                far.SetActive(true); near.GetComponent<BoxCollider>().enabled = true;
                var wall = new GameObject("Occluder", typeof(BoxCollider)); wall.transform.SetParent(root.transform); wall.transform.position = new Vector3(8000,8000,1);
                Physics.SyncTransforms();
                Check((bool)blocked.Invoke(null,new object[]{ray,2.5f,nearColliders}), "closer unrelated geometry blocks part selection");
                wall.GetComponent<BoxCollider>().isTrigger = true; Physics.SyncTransforms();
                Check(!(bool)blocked.Invoke(null,new object[]{ray,2.5f,nearColliders}), "non-solid trigger does not block physical selection");
                root.SetActive(false);
                var target = near.AddComponent<ResearchExamineTarget>();
                var presses = (System.Collections.IDictionary)typeof(ResearchExamineTarget).GetField("presses", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);
                presses.Add(1,("old-context","SimulatedController"));
                typeof(ResearchExamineTarget).GetField("observedInformation",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,"observed");
                typeof(ResearchExamineTarget).GetMethod("SetVisible",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,new object[]{false});
                Check(presses.Count==0,"hidden or gated chip cancels acquired presses");
                Check(typeof(ResearchExamineTarget).GetField("observedInformation",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target)==null,"hidden or gated chip clears old observation");
                var owner = root.AddComponent<ResearchSessionFlowController>();
                target.controller = owner;
                var targets = (List<ResearchExamineTarget>)typeof(ResearchExamineTarget).GetField("Targets", hidden).GetValue(null);
                targets.Add(target);
                try
                {
                    presses.Add(7, ("before-reader", "SimulatedController"));
                    ResearchExamineTarget.CancelPresses(owner);
                    Check(presses.Count == 0, "Reader transition boundary cancels acquisition before another frame");
                    var released = typeof(ResearchExamineTarget).GetField("releasedContext", BindingFlags.NonPublic | BindingFlags.Instance);
                    var pointer = typeof(ResearchExamineTarget).GetField("pressedPointer", BindingFlags.NonPublic | BindingFlags.Instance);
                    released.SetValue(target, "released-before-transition"); pointer.SetValue(target, 7);
                    ResearchExamineTarget.CancelPresses(owner);
                    Check(released.GetValue(target) == null && (int)pointer.GetValue(target) == int.MinValue, "transition between pointer up and click cancels released press");
                    presses.Add(7, ("Thai", "SimulatedController"));
                    owner.SetLanguage(ParticipantLanguage.Japanese); owner.SetLanguage(ParticipantLanguage.Thai);
                    Check(presses.Count == 0, "language roundtrip cancels old press without a frame");
                    var record = new ParticipantRecord { training_version = ResearchUIPolicy.TrainingTrainerV7 };
                    typeof(ResearchSessionFlowController).GetProperty("Record").SetValue(owner, record);
                    typeof(ResearchSessionFlowController).GetProperty("Flow").SetValue(owner,
                        new ResearchSessionFlow(TaskOrder.AB, ResearchFlowState.TutorialReferenceInspect, (int)TutorialStep.Inspect));
                    owner.trainingStation = root.AddComponent<ResearchTrainingStation>();
                    Check(owner.TutorialFact == "examine" && owner.TutorialInstructionKey == "tutorial.try.examine.physical", "V7 neutral training teaches the physical chip route");
                    foreach (var language in new[] { "Thai", "Japanese", "English" })
                        Check(ResearchStrings.Get(owner.TutorialInstructionKey, language) != owner.TutorialInstructionKey, "physical instruction localized for " + language);
                    var flowView = root.AddComponent<ResearchFlowView>(); flowView.controller = owner;
                    Check(!(bool)typeof(ResearchFlowView).GetProperty("CanExamine", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flowView), "V7 training has no main-panel Examine route");
                    int credits = owner.TutorialCompletedFactCount;
                    Check(!ResearchExamineTarget.TryExamineFromMain(owner, "training.module", "SimulatedController", out _)
                        && owner.TutorialCompletedFactCount == credits, "remote training Examine neither succeeds nor awards credit");
                    record.training_version = ResearchUIPolicy.TrainingTrainerV3;
                    Check(owner.TutorialFact == "examine" && owner.TutorialInstructionKey == "tutorial.try.examine", "legacy training retains its main-panel instruction");
                }
                finally { targets.Remove(target); }
                // Bound synthetic component fixture: tests event meaning, not a real participant session.
                var port = root.AddComponent<ResearchTaskPort>(); port.taskId = "B"; port.demoMode = true;
                var state = new S1TaskBState { systemIntegrityValid=true, powerIsolated=true, frontGuardPlacement=S1Placement.Seated };
                port.BindSnapshot(()=>state,_=>false,_=>{});
                port.BindExamineDetail(area=>ResearchS1TaskBMechanics.ExamineDetail(state,area,"Thai",ResearchUIPolicy.IntentionalExamineVersion));
                var events = new List<string>(); object lastAfter = null;
                port.Behavior += (name,area,before,after)=> { events.Add(name); lastAfter=after; };
                port.SetInteractionAllowed(true);
                Check(!port.Examine("B.blade_mount","SimulatedController") && events[events.Count-1]=="EXAMINE_REJECTED", "covered blade records rejection, not Examine");
                var detail = (Dictionary<string,object>)((Dictionary<string,object>)lastAfter)["examine"];
                Check(Equals(detail["examine_version"],ResearchUIPolicy.IntentionalExamineVersion),"event records V7 capability independently of DEV");
                state.frontGuardPlacement=S1Placement.NotInstalled;
                Check(port.Examine("B.blade_mount","SimulatedController") && events[events.Count-1]=="EXAMINE", "exposed blade records Examine");
                var attempt=port.BeginAttempt("B.front_guard","SimulatedController");
                Check(events[events.Count-1]=="GRAB" && port.CurrentInteraction.HasValue,"GRAB stays a mechanical attempt");
                port.SetInteractionAllowed(false);
                Check(!port.IsAttemptActive(attempt) && !port.CurrentInteraction.HasValue,"interaction gate cancels active attempts");
                bool rejected=false; try { port.Examine("B.front_guard","SimulatedController"); } catch(InvalidOperationException) { rejected=true; }
                Check(rejected,"paused interaction cannot record Examine");
                port.SetInteractionAllowed(true);
                Check(port.Examine("B.front_guard","SimulatedController"),"resumed interaction accepts explicit Examine");
                Check(ResearchTrainingStation.SequenceFor(ResearchUIPolicy.TrainingTrainerV7)[0]=="examine","V7 neutral trainer starts with explicit Examine");
                Check(ResearchTrainingStation.SequenceFor(ResearchUIPolicy.TrainingTrainerV6)[0]=="inspect_fail","V6 trainer sequence is unchanged");
                Check(ResearchUIPolicy.TrainingFor(ResearchUIPolicy.OwnerMovable)==ResearchUIPolicy.TrainingReaderPauseV9
                    && new[]{"A7_th","A7_ja","A8_th","A8_ja","B7_th","B7_ja"}.All(p=>Resources.Load<TextAsset>("ResearchReaderS1/"+p+"/package")!=null)
                    && ResearchUIPolicy.ReaderTaskFor(ResearchUIPolicy.TrainingReaderPauseV9,"A",false,ResearchUIPolicy.LayoutVersion)=="A8"
                    && ResearchUIPolicy.ReaderTaskFor(ResearchUIPolicy.TrainingReaderPauseV9,"A",false,ResearchUIPolicy.CableTidyLayoutVersion)=="A7"
                    && ResearchUIPolicy.ReaderTaskFor(ResearchUIPolicy.TrainingReaderPauseV9,"B",false,ResearchUIPolicy.LayoutVersion)=="B7","V9 default ships with installed A7/A8/B7 TH/JA packages; the cable-tray layout reads A8, older layouts A7");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Check(scene.isDirty==dirty,"scene dirty state preserved");
            string result = "V7_COMPONENT_"+(failures.Count==0?"PASS":"FAIL")+" checks="+checks+" failures="+failures.Count;
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonConvert.SerializeObject(new { result, checks, failures, scope="isolated Editor collider/context tests, not SDK input or Quest" },Formatting.Indented));
            return result;
        }
    }
}
#endif
