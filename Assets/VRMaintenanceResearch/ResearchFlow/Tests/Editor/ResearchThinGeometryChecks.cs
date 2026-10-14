#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    // Sub-pixel decorative lines stay hidden (headset wear-20260924_1516 showed them breaking into dashes).
    // Scene/source evidence only; distant clarity on the headset is not claimed.
    public static class ResearchThinGeometryChecks
    {
        // Decorative only, no colliders, not reachable by teleport (factory) or 9 mm floor seams.
        static readonly string[] Hidden = { "Factory_Static/Factory_Expansion_Joints", "Factory_Static/Factory_Floor_Zoning",
            "Factory_Static/Demo_Cell_Label", "Factory_Static/Factory_Cladding_Details", "Factory_Activity/Production_Demo_Marking" };

        [MenuItem("VR Research/Research Flow/Check Thin Geometry")]
        public static void Run()
        {
            int checks = 0;
            void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Thin geometry check failed: " + name); checks++; }
            var scene = SceneManager.GetActiveScene();
            Check(scene.path.EndsWith("VRResearch_ResearchFlow_V2.unity"), "ResearchFlow V2 scene loaded");
            var roots = scene.GetRootGameObjects();
            var env = roots.Single(g => g.name == "Environment_V2").transform;
            var seams = roots.Single(g => g.name == "ResearchFlow_EnvironmentSecondPass").GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "Floor construction seam").Select(t => t.gameObject).ToArray();
            var hidden = Hidden.Select(p => env.Find(p)?.gameObject).Concat(seams).ToArray();
            Check(hidden.Length == 17 && hidden.All(g => g), "17 decorative line objects found");
            Check(hidden.All(g => !g.activeSelf), "decorative line objects inactive");
            Check(hidden.All(g => g.GetComponentsInChildren<Collider>(true).Length == 0), "hidden objects carried no colliders");
            Check(!File.ReadAllText("Assets/VRMaintenanceResearch/ResearchFlow/Editor/ResearchEnvironmentSecondPass.cs").Contains("\"Floor construction seam\""),
                "generator no longer creates floor seams");

            // Guard: no active floor-level primitive line (< 25 mm wide, > 1 m long) anywhere in the scene.
            var lines = roots.SelectMany(r => r.GetComponentsInChildren<MeshRenderer>()).Where(r => r.enabled).Select(r => r.bounds)
                .Where(b => b.max.y < .05f && b.size.y < .01f && Mathf.Min(b.size.x, b.size.z) < .025f && Mathf.Max(b.size.x, b.size.z) > 1f).ToArray();
            Check(lines.Length == 0, "no active floor line primitives (" + string.Join(", ", lines.Select(b => b.center.ToString("F2"))) + ")");
            foreach (var floor in new[] { "Training/Training_Floor", "Computer/Computer_Floor", "Fan/Fan_Floor" })
                Check(env.Find(floor)?.GetComponent<Collider>()?.enabled == true, floor + " collider intact");
            // P9 (crop-ceiling-edge.png): wall tops bevel down to y 3.592 but ceilings started at 3.600, so the slot showed the
            // Suite_Ceiling_Grid wall caps (z-fighting V22_Building_Integration over the facades) as a bright dotted line.
            foreach (var ceiling in new[] { "Training/Training_Ceiling", "Computer/Computer_Ceiling", "Fan/Fan_Ceiling", "Corridor/Corridor_Ceiling" })
                Check(Mathf.Abs(env.Find(ceiling).GetComponent<Renderer>().bounds.min.y - 3.58f) < .001f, ceiling + " underside below wall-top bevel");
            // P9: props placed by later passes must not sink into Environment_V2 props (the spare board cut through
            // Computer_ComponentTray by 22 mm). Bounds only; room shells (> 2 m³) are skipped.
            var props = roots.Where(g => g.name == "ResearchFlow_EnvironmentSecondPass" || g.name == "ResearchFlow_Session")
                .SelectMany(g => g.GetComponentsInChildren<MeshRenderer>()).Where(r => r.enabled && !r.GetComponentInParent<Canvas>()).ToArray();
            var envProps = env.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.bounds.size.x * r.bounds.size.y * r.bounds.size.z <= 2f).ToArray();
            float Pen(Bounds a, Bounds b) => Enumerable.Range(0, 3).Min(i => Mathf.Min(a.max[i], b.max[i]) - Mathf.Max(a.min[i], b.min[i]));
            var clips = props.SelectMany(p => envProps.Where(e => Pen(p.bounds, e.bounds) >= .003f).Select(e => p.name + " x " + e.name)).ToArray();
            Check(clips.Length == 0, "no added prop penetrates an environment prop (" + string.Join(", ", clips) + ")");
            // P9: the corridor must not see under the facades, so closers + door thresholds cover the wall footprint z -0.2..0.
            var corridor = env.Find("Corridor/Corridor_Floor").GetComponent<Renderer>().bounds;
            var footprint = props.Where(r => r.name == "Facade base closer").Concat(env.GetComponentsInChildren<MeshRenderer>()
                .Where(r => r.name.EndsWith("_Door_Threshold"))).Select(r => r.bounds).OrderBy(c => c.min.x).ToArray();
            float reach = corridor.min.x;
            foreach (var c in footprint.Where(c => Mathf.Abs(c.max.y) < .001f && Mathf.Abs(c.min.z + .2f) < .001f && Mathf.Abs(c.max.z) < .001f))
                if (c.min.x <= reach + .001f) reach = Mathf.Max(reach, c.max.x);
            Check(reach >= corridor.max.x - .001f, "facade base closed along the corridor (open from x " + reach.ToString("F3") + ")");
            var perimeter = env.Find("Factory_Static/Factory_Perimeter").GetComponent<Renderer>().bounds;
            foreach (var side in new[] { "Corridor/Corridor_West_Landing", "Corridor/Corridor_East_Landing" })
            {
                var l = env.Find(side).GetComponent<Renderer>().bounds;
                float lo = l.center.x > 0 ? l.max.x : perimeter.min.x, hi = l.center.x > 0 ? perimeter.max.x : l.min.x;
                Check(props.Any(r => r.name == "Landing end closer" && r.bounds.min.x <= lo + .001f && r.bounds.max.x >= hi - .001f
                    && r.bounds.min.z <= l.min.z + .001f && r.bounds.max.z >= l.max.z - .001f && Mathf.Abs(r.bounds.max.y) < .001f), side + " closed to the perimeter wall");
            }
            Debug.Log("RESEARCH_THIN_GEOMETRY_PASS checks=" + checks + "; scene/source only, not headset clarity");
        }
    }
}
#endif
