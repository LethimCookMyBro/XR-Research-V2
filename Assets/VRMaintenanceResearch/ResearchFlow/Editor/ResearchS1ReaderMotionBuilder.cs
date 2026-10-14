#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRMaintenanceResearch.AssetLibraryQA;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Plan = VRMaintenanceResearch.ResearchFlow.Editor.ResearchS1EducationalPlan;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Reader video motion: poses the real Task A/B models through the reviewed educational motion plan (same socket math as play)
    // and renders each section from the same framing as its Reader still. Edit Mode only; the scene is reloaded from disk afterwards.
    public static class ResearchS1ReaderMotionBuilder
    {
        const string Scene = "Assets/VRMaintenanceResearch/Scenes/V2/VRResearch_ResearchFlow_V2.unity";
        const int FramesPerFact = 48, StaticFrames = 72;
        static readonly Dictionary<string, int> Facts = new Dictionary<string, int>
        {
            ["A.slots"] = 4, ["A.ram"] = 3, ["A.ssd"] = 4, ["A.gpu"] = 4,
            ["B.symptom"] = 3, ["B.causes"] = 4, ["B.observations"] = 4, ["B.access"] = 4, ["B.correction"] = 4, ["B.reassembly"] = 1,
        };

        public static string Build(string outputRoot)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit Mode required.");
            var open = EditorSceneManager.GetActiveScene();
            if (open.isDirty) throw new InvalidOperationException("Active scene has unsaved changes; not touching it.");
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var a = UnityEngine.Object.FindObjectsByType<ResearchS1TaskAMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            var b = UnityEngine.Object.FindObjectsByType<ResearchS1TaskBMechanics>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            var cables = a.GetComponentsInChildren<ResearchS1Cable>(true);
            var targets = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
            var cameraGo = new GameObject("S1MotionCamera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraGo.AddComponent<Camera>(); camera.fieldOfView = 45; camera.nearClipPlane = .01f; camera.farClipPlane = 50;
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("UI")); camera.clearFlags = CameraClearFlags.Skybox;
            var fill = cameraGo.AddComponent<Light>(); fill.type = LightType.Spot; fill.spotAngle = 70; fill.range = 4; fill.shadows = LightShadows.None;
            var manifest = new JObject();
            try
            {
                foreach (var shot in ResearchS1ReaderMediaBuilder.Shots)
                {
                    string section = shot.Key, dir = Path.Combine(outputRoot, section);
                    Directory.CreateDirectory(dir);
                    var bounds = ResearchS1ReaderMediaBuilder.Frame(targets, shot.Value.frame);
                    float radius = Mathf.Max(bounds.extents.magnitude, .06f);
                    float distance = Mathf.Clamp(radius / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f, .35f, 2.4f);
                    Vector3 centre = bounds.center, direction = shot.Value.from.normalized;
                    fill.intensity = Mathf.Clamp(1.6f * distance * distance / 1.4f, .25f, 1.6f);
                    if (section == "A.ssd") cables.Single(c => c.assetId == "CABLE_SATA_DATA_S_500").transform.localRotation *= Quaternion.Euler(0, 0, 180);
                    var frames = new JArray();
                    int facts = Facts.TryGetValue(section, out int f) ? f : 1, perFact = Facts.ContainsKey(section) ? FramesPerFact : StaticFrames, n = 0;
                    for (int fact = 0; fact < facts; fact++)
                        for (int k = 0; k < perFact; k++)
                        {
                            double time = (double)k / (perFact - 1);
                            string label = section[0] == 'A' ? MotionA(a, section, fact, time) : MotionB(b, section, fact, time);
                            foreach (var cable in cables) cable.RefreshShape();
                            // Static sections orbit +-8 degrees, the plan's neutral overview motion; motion sections keep the still's framing.
                            float orbit = Facts.ContainsKey(section) ? 0 : 8 * Mathf.Sin((float)time * Mathf.PI * 2);
                            camera.transform.position = centre + Quaternion.AngleAxis(orbit, Vector3.up) * direction * distance;
                            camera.transform.LookAt(centre);
                            string file = (n++).ToString("0000") + ".png";
                            File.WriteAllBytes(Path.Combine(dir, file), ResearchS1ReaderMediaBuilder.Render(camera, null));
                            frames.Add(new JObject { ["file"] = file, ["fact"] = fact, ["time"] = Math.Round(time, 4), ["state"] = label });
                        }
                    manifest[section] = new JObject { ["fps"] = 12, ["frames"] = frames, ["facts"] = facts };
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGo);
                File.WriteAllText(Path.Combine(outputRoot, "motion.json"), manifest.ToString(Formatting.Indented));
                // Discard every pose change: reload the canonical scene from disk.
                scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            }
            return "sections=" + manifest.Count + " frames=" + manifest.Properties().Sum(p => ((JArray)p.Value["frames"]).Count) + " reloaded_dirty=" + scene.isDirty;
        }

        static void Place(Transform root, Transform mate, Transform seat, float depthMm, float turn = 0)
        {
            var pose = SocketRules.RootPose(seat.localToWorldMatrix, depthMm, turn, root.worldToLocalMatrix * mate.localToWorldMatrix);
            root.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        }
        static void Sample(Transform root, AnimationClip clip)
        {
            if (!clip) throw new InvalidOperationException("Source latch animation is missing: " + root.name);
            root.GetLocalPositionAndRotation(out var position, out var rotation); var scale = root.localScale;
            try { clip.SampleAnimation(root.gameObject, clip.length); }
            finally { root.SetLocalPositionAndRotation(position, rotation); root.localScale = scale; }
        }
        static Transform Edge(QAPlug part) => part.mates.Single(m => m.name.EndsWith("_Edge", StringComparison.Ordinal));
        static void CableHead(ResearchS1TaskAMechanics a, int index, QAPlug part, S1Connector connector, float depth)
        {
            var end = a.wiring.ends[index]; var target = a.wiring.targets.Single(t => t.part == part && t.connector == connector);
            Place(end.plug.transform, end.plug.mates.Single(), target.socket.seat, depth);
        }

        static string MotionA(ResearchS1TaskAMechanics a, string section, int fact, double time)
        {
            if (section == "A.ram")
            {
                var slot = a.ramSlots[fact == 1 ? 3 : 1]; var part = a.ram[fact == 1 ? 1 : 0];
                float depth = (float)Plan.RamDepth(fact, time);
                Place(part.transform, Edge(part), slot.seat, depth);
                var latch = slot.latches.Single(); bool closed = depth <= .3f && time < (fact == 1 ? .70 : .65);
                Sample(latch.animationRoot.transform, closed ? latch.closeClip : latch.openClip);
                return depth > .3f ? "partial" : closed ? "seated" : "released";
            }
            if (section == "A.ssd")
            {
                var motion = Plan.Ssd(fact, time); var sled = a.sleds[0]; Place(sled.transform, sled.mate, a.traySeats[0], (float)motion.DepthMm);
                float fastener = (float)motion.Fastener; var thumb = sled.thumb;
                Place(thumb.transform, thumb.mate, thumb.captiveSeat, thumb.travelMm * (1 - fastener), -thumb.FullDegrees * fastener);
                CableHead(a, 0, a.ssds[0], S1Connector.SsdData, (float)motion.DataDepthMm);
                CableHead(a, 1, a.ssds[0], S1Connector.SsdPower, (float)motion.PowerDepthMm);
                return null;
            }
            if (section == "A.gpu")
            {
                var motion = Plan.Gpu(fact, time); var part = a.gpus[1]; Place(part.transform, Edge(part), a.gpuSlots[0].seat, (float)motion.DepthMm);
                var screw = a.gpuScrews[0]; float progress = (float)motion.Fastener;
                Place(screw.transform, screw.mate, a.gpuScrewSeats[0], screw.travelMm * (1 - progress), -screw.FullDegrees * progress);
                CableHead(a, 2, part, S1Connector.GpuPcie8, (float)motion.PowerDepthMm);
                var latch = a.gpuSlots[0].latches.Single(); bool closed = motion.DepthMm <= .3 && !(fact == 2 && time >= .7);
                Sample(latch.animationRoot.transform, closed ? latch.closeClip : latch.openClip);
                return null;
            }
            if (section == "A.slots" && fact == 3)
            {
                if (time == 0) pivotRest = a.servicePivot.localRotation;
                a.servicePivot.localRotation = pivotRest * Quaternion.AngleAxis(30 * Mathf.Sin((float)time * Mathf.PI * 2), Vector3.up);
            }
            return null;
        }
        static Quaternion pivotRest;

        static void Fan(ResearchS1FanPart part, float depthMm = 0, float turn = 0) => Place(part.transform, part.mate, part.seat, depthMm, turn);
        static void Thread(ResearchS1FanPart part, double progress) =>
            Fan(part, (float)ResearchS1FanKinematics.ThreadDepthMm(progress, part.fullTurnDegrees, part.threadTravelMm), (part.rightHanded ? -1 : 1) * (float)progress);
        static void Clip(ResearchS1TaskBMechanics b, bool closed)
        {
            var clip = closed ? b.frontClip.clipClose : b.frontClip.clipOpen;
            if (!clip) throw new InvalidOperationException("The source guard clip animation is missing.");
            Sample(b.frontClip.clipAnimationRoot.transform, clip);
        }
        static void ClosedFan(ResearchS1TaskBMechanics b)
        {
            Fan(b.rearGuard); Thread(b.rearLock, b.rearLock.fullTurnDegrees);
            Fan(b.blade); Thread(b.spinner, b.spinner.fullTurnDegrees); Fan(b.frontGuard); Clip(b, true);
            b.motorShaft.localRotation = b.isolatedShaftRotation;
        }
        static void BladeAssembly(ResearchS1TaskBMechanics b, Plan.FanAssemblyMotion motion)
        {
            Fan(b.blade, (float)motion.BladeDepthMm, (float)motion.BladeTurn);
            if (motion.SpinnerClearanceMm > 0) Fan(b.spinner, b.spinner.threadTravelMm + (float)motion.SpinnerClearanceMm);
            else Thread(b.spinner, b.spinner.fullTurnDegrees * motion.SpinnerProgress);
        }

        // Mirrors ResearchS1EducationalMedia.MotionB on the real parts; the returned state labels normal/abnormal phases.
        static string MotionB(ResearchS1TaskBMechanics b, string section, int fact, double time)
        {
            ClosedFan(b);
            float insertion = (float)Plan.Insert(time);
            if (section == "B.causes" || section == "B.observations" || section == "B.symptom")
            {
                int area = fact < 3 && section != "B.symptom" ? fact : Math.Min(2, (int)(time * 3));
                double phase = section == "B.symptom" || fact >= 3 ? time * 3 - area : time;
                bool abnormal = phase >= .5; float wobble = abnormal ? .006f * Mathf.Sin((float)phase * Mathf.PI * 12) : 0;
                if (area == 0) { Fan(b.frontGuard, abnormal ? 4 : 0, abnormal ? b.frontGuard.insertTurnDegrees : 0); Clip(b, !abnormal); b.frontGuard.transform.position += b.frontGuard.seat.right * wobble; }
                if (area == 1) { Fan(b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(b, false); Fan(b.blade, abnormal ? 6 : 0); Thread(b.spinner, abnormal ? b.spinner.fullTurnDegrees * .5 : b.spinner.fullTurnDegrees); b.blade.transform.position += b.blade.seat.right * wobble; }
                if (area == 2) { Thread(b.rearLock, abnormal ? b.rearLock.fullTurnDegrees * .5 : b.rearLock.fullTurnDegrees); b.rearGuard.transform.position += b.rearGuard.seat.right * wobble; }
                return section == "B.symptom" ? null : abnormal ? "abnormal" : "normal";
            }
            if (section == "B.access")
            {
                Clip(b, false); Fan(b.frontGuard, 450 * (float)Plan.Ramp(time, .15, .4), b.frontGuard.insertTurnDegrees * (float)Plan.Ramp(time, 0, .15));
                if (fact >= 1) { Thread(b.spinner, b.spinner.fullTurnDegrees * (1 - Plan.Ramp(time, .4, .7))); if (time > .7) Fan(b.spinner, b.spinner.threadTravelMm + 200 * (float)Plan.Ramp(time, .7, .8)); }
                if (fact >= 2) Fan(b.blade, 250 * (float)Plan.Ramp(time, .8, 1));
                if (fact == 3) { Fan(b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Fan(b.spinner, 240); Fan(b.blade, 250); Thread(b.rearLock, b.rearLock.fullTurnDegrees * (1 - Plan.Ramp(time, .15, .8))); }
                return null;
            }
            if (section == "B.correction")
            {
                if (fact == 0) { Fan(b.frontGuard, 12 * insertion, b.frontGuard.insertTurnDegrees * insertion); Clip(b, time >= .4 && time < .7); }
                else if (fact == 1) { Fan(b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(b, false); BladeAssembly(b, Plan.BladeRepair(time)); }
                else if (fact == 2)
                {
                    Fan(b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(b, false); Fan(b.spinner, 240); Fan(b.blade, 250);
                    float aligned = (float)(1 - Plan.Ramp(time, .1, .4)); Fan(b.rearGuard, 20 * aligned, 20 * aligned);
                    if (time < .5) Fan(b.rearLock, b.rearLock.threadTravelMm + 60 * (float)(1 - Plan.Ramp(time, .4, .5)));
                    else Thread(b.rearLock, b.rearLock.fullTurnDegrees * Plan.Ramp(time, .5, .8));
                }
                else
                {
                    Fan(b.frontGuard, 450, b.frontGuard.insertTurnDegrees); Clip(b, false);
                    Thread(b.spinner, b.spinner.fullTurnDegrees * (1 - .5 * Math.Pow(Math.Sin(time * Math.PI), 2)));
                }
                return null;
            }
            if (section == "B.reassembly")
            {
                Clip(b, time >= .88); Fan(b.frontGuard, 450 * (float)(1 - Plan.Ramp(time, .6, .82)), b.frontGuard.insertTurnDegrees * (float)(1 - Plan.Ramp(time, .82, .88)));
                BladeAssembly(b, Plan.FanReassembly(time));
            }
            return null;
        }
    }
}
#endif
