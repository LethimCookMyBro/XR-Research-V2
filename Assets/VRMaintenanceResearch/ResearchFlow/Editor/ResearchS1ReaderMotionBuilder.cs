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
        // A caption that names small board details gets a close, neutral framing of every option it names (both long slots), never one choice.
        // The DIMM cards keep the wide board view: the routed 24-pin bundle covers the DIMM silkscreen from every tested direction.
        static readonly Dictionary<(string, int), (Vector3 from, string[] frame)> FactShots = new Dictionary<(string, int), (Vector3, string[])>
        {
            [("A.slots", 2)] = (new Vector3(0, .1f, 1), new[] { "PCIEX16_1", "PCIEX16_2" }),
        };

        // sections: render only these sections and merge them into an existing motion.json (null renders and rewrites everything).
        public static string Build(string outputRoot, string[] sections = null)
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
            string manifestPath = Path.Combine(outputRoot, "motion.json");
            var manifest = sections != null && File.Exists(manifestPath) ? JObject.Parse(File.ReadAllText(manifestPath)) : new JObject();
            try
            {
                foreach (var shot in ResearchS1ReaderMediaBuilder.Shots.Where(s => sections == null || sections.Contains(s.Key)))
                {
                    string section = shot.Key, dir = Path.Combine(outputRoot, section);
                    if (sections != null && Directory.Exists(dir)) foreach (var old in Directory.GetFiles(dir, "*.png")) File.Delete(old);
                    Directory.CreateDirectory(dir);
                    float distance = 0; Vector3 centre = default, direction = default;
                    void Aim((Vector3 from, string[] frame) framing, float minimum)
                    {
                        var bounds = ResearchS1ReaderMediaBuilder.Frame(targets, framing.frame);
                        float radius = Mathf.Max(bounds.extents.magnitude, .06f);
                        distance = Mathf.Clamp(radius / Mathf.Sin(camera.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f, minimum, 2.4f);
                        centre = bounds.center; direction = framing.from.normalized;
                        fill.intensity = Mathf.Clamp(1.6f * distance * distance / 1.4f, .25f, 1.6f);
                    }
                    Aim(shot.Value, .35f);
                    if (section == "A.ssd") cables.Single(c => c.assetId == "CABLE_SATA_DATA_S_500").transform.localRotation *= Quaternion.Euler(0, 0, 180);
                    var frames = new JArray();
                    int facts = Facts.TryGetValue(section, out int f) ? f : 1, perFact = Facts.ContainsKey(section) ? FramesPerFact : StaticFrames, n = 0;
                    for (int fact = 0; fact < facts; fact++)
                    {
                        Aim(FactShots.TryGetValue((section, fact), out var close) ? close : shot.Value, FactShots.ContainsKey((section, fact)) ? .12f : .35f);
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
                    }
                    manifest[section] = new JObject { ["fps"] = 12, ["frames"] = frames, ["facts"] = facts };
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGo);
                File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
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
        static void Sample(Transform root, AnimationClip clip, float time = 1)
        {
            if (!clip) throw new InvalidOperationException("Source latch animation is missing: " + root.name);
            root.GetLocalPositionAndRotation(out var position, out var rotation); var scale = root.localScale;
            try { clip.SampleAnimation(root.gameObject, clip.length * Mathf.Clamp01(time)); }
            finally { root.SetLocalPositionAndRotation(position, rotation); root.localScale = scale; }
        }
        static Transform Edge(QAPlug part) => part.mates.Single(m => m.name.EndsWith("_Edge", StringComparison.Ordinal));
        static void CableHead(ResearchS1TaskAMechanics a, int index, QAPlug part, S1Connector connector, float depth)
        {
            var end = a.wiring.ends[index]; var target = a.wiring.targets.Single(t => t.part == part && t.connector == connector);
            Place(end.plug.transform, end.plug.mates.Single(), target.socket.seat, depth);
        }

        internal static string MotionA(ResearchS1TaskAMechanics a, string section, int fact, double time)
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
                a.servicePivot.localRotation = pivotRest * Quaternion.AngleAxis(180 * Mathf.SmoothStep(0, 1, (float)Plan.Ramp(time, .08, .9)), Vector3.up);
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
        internal static void ClosedFan(ResearchS1TaskBMechanics b)
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
        internal static string MotionB(ResearchS1TaskBMechanics b, string section, int fact, double time)
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
                Sample(b.frontClip.clipAnimationRoot.transform, b.frontClip.clipOpen, (float)Plan.Ramp(time, 0, .1));
                Fan(b.frontGuard, 450 * (float)Plan.Ramp(time, .2, .4), b.frontGuard.insertTurnDegrees * (float)Plan.Ramp(time, .1, .2));
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

        // Training Reader T2 (TrainingTrainerV2 order) on the real Trainer_FX1 station, with the same seat math as play: the module and
        // the plug go through SocketRules.RootPose with the socket's own Seat/Block rules, the screw is placed as ResearchTrainingScrew.Place
        // does (ThreadDepthMm, so the head travels in and out instead of only spinning), the latch samples its open clip, the turntable yaws.
        // Edit Mode never runs ResearchTrainingStation.Awake, so the lead cable and the turntable grip cues are built here as Awake builds them.
        sealed class TrainingRest
        {
            public ResearchTrainingStation station; public Pose module; public Vector3 leadPosition; public Quaternion leadRotation; public float yaw;
            public ResearchS1Cable lead; public GameObject[] cues; public Transform[] grips;
        }
        static TrainingRest trainingRest;

        static TrainingRest Rest(ResearchTrainingStation t)
        {
            if (trainingRest != null && trainingRest.station == t) return trainingRest;
            var rest = new TrainingRest { station = t, module = new Pose(t.module.transform.position, t.module.transform.rotation), yaw = t.turntable.localEulerAngles.y,
                leadPosition = t.leadHead.transform.localPosition, leadRotation = t.leadHead.transform.localRotation };
            Screw(t, 0);
            // As ResearchTrainingStation.Awake: the lead is drawn like the Task A cables (blue tube over the bone path).
            var leadSkin = t.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(r => r.name == "VIS_TRAINER_FX1_LEAD_LOD0");
            Transform LeadEnd(string id) => leadSkin.bones.First(bone => bone.name == "B_TRAINER_FX1_LEAD_" + id);
            rest.lead = t.gameObject.AddComponent<ResearchS1Cable>();
            rest.lead.assetId = "TRAINER_FX1_LEAD"; rest.lead.skin = leadSkin; rest.lead.sideways = true;
            rest.lead.tubeMaterial = leadSkin.sharedMaterials.First(m => m && m.name == "M_PVC_Blue"); rest.lead.tubeRadius = .003f;
            rest.lead.spans = new[] { ResearchS1Cable.BindSpan(t.transform, leadSkin, LeadEnd("B1"), t.leadBones, LeadEnd("B2")) };
            // As ResearchTrainingStation.BuildGripZones: two rim grips at the front corners and three blue edge bars, shown only while turning.
            Physics.SyncTransforms();
            var layout = t.TurntableGripLayout();
            rest.grips = layout.zones.Skip(1).Select(bounds =>
            {
                var zone = new GameObject("TurntableRimGrip").transform; zone.SetParent(t.turnHandle.transform, false);
                zone.SetPositionAndRotation(t.turntable.TransformPoint(bounds.center), t.turntable.rotation); return zone;
            }).ToArray();
            rest.cues = layout.zones.Select(bounds =>
            {
                var cue = GameObject.CreatePrimitive(PrimitiveType.Cube); cue.name = "TurntableGripCue";
                UnityEngine.Object.DestroyImmediate(cue.GetComponent<Collider>());
                cue.transform.SetParent(t.turntable, false);
                cue.transform.localPosition = new Vector3(bounds.center.x, layout.surfaceY, bounds.center.z);
                cue.transform.localScale = new Vector3(bounds.size.x, .008f / t.turntable.lossyScale.y, .008f / t.turntable.lossyScale.z);
                cue.GetComponent<Renderer>().sharedMaterial = rest.lead.tubeMaterial; cue.SetActive(false); return cue;
            }).ToArray();
            return trainingRest = rest;
        }

        static Bounds PartBounds(Transform part)
        {
            var renderers = part.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(part.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        internal static Transform ModuleMate(ResearchTrainingStation t) =>
            t.module.mates.FirstOrDefault(m => m.name.EndsWith("_Slot", StringComparison.Ordinal)) ?? throw new InvalidOperationException("Training module has no _Slot mate.");
        /// <summary>The two turntable rim grips (built on the first MotionT call for this station).</summary>
        internal static Transform[] TrainingGrips(ResearchTrainingStation t) => Rest(t).grips;

        static void Screw(ResearchTrainingStation t, double fraction)
        {
            var screw = t.screw; double degrees = screw.fullDegrees * Math.Max(0, Math.Min(1, fraction));
            Place(screw.transform, screw.mate, screw.seat, (float)ResearchS1FanKinematics.ThreadDepthMm(degrees, screw.fullDegrees, screw.travelMm), -(float)degrees);
        }

        // away 0 = at the seat (depth, turn); 1 = resting on the table where the scene put it; between, an arc over the base edge.
        static void Module(ResearchTrainingStation t, TrainingRest rest, float depthMm, float turn, double away)
        {
            var mate = ModuleMate(t); var root = t.module.transform;
            if (away <= 0) { Place(root, mate, t.moduleSocket.seat, depthMm, turn); return; }
            var above = SocketRules.RootPose(t.moduleSocket.seat.localToWorldMatrix, depthMm, turn, root.worldToLocalMatrix * mate.localToWorldMatrix);
            float a = (float)away;
            root.SetPositionAndRotation(Vector3.Lerp(above.GetColumn(3), rest.module.position, a) + Vector3.up * .06f * Mathf.Sin(a * Mathf.PI),
                Quaternion.Slerp(above.rotation, rest.module.rotation, a));
        }

        static void LatchOpen(ResearchTrainingStation t, double open)
        {
            var clip = t.latchOpenClip; var root = t.latchAnimationRoot ? t.latchAnimationRoot.transform : null;
            if (!clip || !root) throw new InvalidOperationException("The training latch open clip is missing.");
            root.GetLocalPositionAndRotation(out var position, out var rotation); var scale = root.localScale;
            try { clip.SampleAnimation(root.gameObject, clip.length * Mathf.Clamp01((float)open)); }
            finally { root.SetLocalPositionAndRotation(position, rotation); root.localScale = scale; }
        }

        // away 0 = plug mate at depthMm above the open socket; 1 = the authored rest pose the station returns an unplugged head to.
        static void Plug(ResearchTrainingStation t, TrainingRest rest, float depthMm, double away)
        {
            var head = t.leadHead.transform; var parent = head.parent;
            var seated = SocketRules.RootPose(t.leadSocket.seat.localToWorldMatrix, depthMm, 0, head.worldToLocalMatrix * t.leadHead.mates[0].localToWorldMatrix);
            float a = (float)away;
            head.SetPositionAndRotation(Vector3.Lerp(seated.GetColumn(3), parent.TransformPoint(rest.leadPosition), a),
                Quaternion.Slerp(seated.rotation, parent.rotation * rest.leadRotation, a));
        }

        // Sections: T.start (as Training begins), T.module (0 fitting insert, 1 blocked try, 2 re-insert + latch, 3 unlatch + pull out),
        // T.turn (0 to 180 deg with the grip cues on), T.screw (0 tighten in with a pause to regrab, 1 loosen out, 2 static at progress = time),
        // T.cable (0 connect, 1 disconnect). Each call sets the whole station, so any frame can be rendered alone.
        internal static string MotionT(ResearchTrainingStation t, string section, int fact, double time)
        {
            var rest = Rest(t);
            var keyed = t.moduleSocket.rules.Single(r => r.outcome == PairOutcome.Seat);
            var blocked = t.moduleSocket.rules.Single(r => r.outcome == PairOutcome.Block);
            float approach = t.moduleSocket.spec.approachMm * 2, half = blocked.turnDeg - keyed.turnDeg;
            float yaw = 0, depth = 0, turn = keyed.turnDeg, plugDepth = 0;
            double away = 0, open = 0, screw = 0, plugAway = 1;
            bool cues = false; string state;
            double R(double start, double end) => Plan.Ramp(time, start, end);
            switch (section)
            {
                case "T.start": away = 1; open = 1; state = "start"; break;
                case "T.module":
                    if (fact == 0) { away = 1 - R(.05, .45); depth = approach * (float)(1 - R(.5, .8)); open = 1; state = depth > .3f ? "inserting" : "seated"; }
                    else if (fact == 1)
                    {
                        depth = approach * (float)R(0, .2) - (approach - blocked.reachMm) * (float)R(.45, .7);
                        turn += half * (float)R(.2, .42); open = 1; state = time >= .7 ? "blocked" : "reversing";
                    }
                    else if (fact == 2)
                    {
                        depth = blocked.reachMm + (approach - blocked.reachMm) * (float)R(0, .18) - approach * (float)R(.4, .62);
                        turn += half * (float)(1 + R(.18, .38)); open = 1 - R(.7, .85);
                        state = open <= 0 ? "latched" : depth > .3f ? "inserting" : "seated";
                    }
                    else if (fact == 3) { open = R(0, .15); depth = approach * (float)R(.25, .5); away = R(.55, .95); state = depth > .3f ? "removed" : "unlatched"; }
                    else if (fact == 4) { open = 1 - R(0, 1); state = open <= 0 ? "latched" : "seated"; }
                    else throw new ArgumentOutOfRangeException(nameof(fact));
                    break;
                case "T.turn": yaw = 120 * Mathf.SmoothStep(0, 1, (float)R(.08, .9)); cues = true; state = "yaw " + yaw.ToString("F0"); break;
                case "T.screw":
                    yaw = 120;
                    screw = fact == 0 ? .5 * R(.05, .42) + .5 * R(.6, .92) : fact == 1 ? 1 - R(.05, .9) : fact == 2 ? time : throw new ArgumentOutOfRangeException(nameof(fact));
                    state = screw >= 1 ? "tight" : screw <= 0 ? "loose" : "partial";
                    break;
                case "T.cable":
                    yaw = 120; screw = 1;
                    if (fact == 0) { plugAway = 1 - R(.1, .5); plugDepth = 25 * (float)(1 - R(.55, .85)); }
                    else if (fact == 1) { plugDepth = 25 * (float)R(.1, .35); plugAway = R(.4, .85); }
                    else throw new ArgumentOutOfRangeException(nameof(fact));
                    state = plugAway <= 0 && plugDepth <= .3f ? "connected" : "free";
                    break;
                default: throw new ArgumentException("Unknown training section " + section);
            }
            var euler = t.turntable.localEulerAngles; euler.y = rest.yaw + yaw; t.turntable.localEulerAngles = euler;
            Module(t, rest, depth, turn, away);
            LatchOpen(t, open);
            Screw(t, screw);
            Plug(t, rest, plugDepth, plugAway);
            foreach (var cue in rest.cues) cue.SetActive(cues);
            Physics.SyncTransforms();
            rest.lead.RefreshShape();
            return state;
        }
    }
}
#endif
