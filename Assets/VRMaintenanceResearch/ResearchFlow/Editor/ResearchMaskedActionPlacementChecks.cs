#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    public static class ResearchMaskedActionPlacementChecks
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent) rect.SetParent(parent, false);
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        static Button Button(Transform parent, string name, Vector2 size, Vector2 position)
        {
            var rect = Rect(parent, name, size, position); var image = rect.gameObject.AddComponent<Image>();
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; return button;
        }
        static string[] Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var s = SceneManager.GetSceneAt(i); return s.handle + ":" + s.path + ":" + s.isDirty;
        }).ToArray();
        public static string Run(string evidenceDirectory)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !ResearchFloorPolicy.EditorFixtureAllowed
                || !Path.IsPathRooted(evidenceDirectory) || Directory.Exists(evidenceDirectory))
                throw new InvalidOperationException("Stopped Editor, no XR and fresh absolute evidence directory required.");
            Directory.CreateDirectory(evidenceDirectory); var rows = new List<object>(); int failures = 0;
            void Check(bool pass, string name, object observed = null) { rows.Add(new { name, pass, observed }); if (!pass) failures++; }
            var before = Scenes(); var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var owner = new GameObject("MaskedPlacementOwner"); owner.SetActive(false);
                var view = owner.AddComponent<ResearchFlowView>(); view.enabled = false;
                var head = new GameObject("MaskedPlacementHead", typeof(Camera)).GetComponent<Camera>();
                head.enabled = false; head.fieldOfView = 60; head.aspect = 16f / 9; head.stereoTargetEye = StereoTargetEyeMask.None;
                head.transform.position = new Vector3(1000, 1.7f, 998); view.Head = head.transform;
                var panelRect = Rect(null, "MaskedReader", new Vector2(1180, 800), Vector2.zero);
                var canvas = panelRect.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                panelRect.position = new Vector3(1000, 1.7f, 1000); panelRect.localScale = Vector3.one * .001f;
                var panel = panelRect.gameObject.AddComponent<ResearchPanelMotion>(); panel.enabled = false; panel.view = view; panel.panelId = "reference";
                view.referencePanel = panel;
                typeof(ResearchPanelMotion).GetField("environment", Hidden).SetValue(panel, Array.Empty<Renderer>());
                var viewport = Rect(panelRect, "Viewport", new Vector2(810, 400), Vector2.zero);
                viewport.gameObject.AddComponent<Image>(); var mask = viewport.gameObject.AddComponent<RectMask2D>();
                var content = Rect(viewport, "Content", new Vector2(810, 38 * 700), Vector2.zero);
                content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero;
                var choices = new List<Button>();
                for (int i = 0; i < 38; i++)
                {
                    var card = Rect(content, "Step_" + (i + 1), new Vector2(770, 676), Vector2.zero);
                    card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, 1); card.anchoredPosition = new Vector2(0, -i * 700);
                    var button = Button(card, "ChooseStep", new Vector2(240, 64), Vector2.zero);
                    var rect = (RectTransform)button.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1); rect.anchoredPosition = new Vector2(0, -80);
                    choices.Add(button);
                }
                var toolbar = Button(panelRect, "UnmaskedToolbar", new Vector2(80, 64), new Vector2(500, -350));
                var floor = new GameObject("MaskedTest_Floor", typeof(BoxCollider)); floor.transform.position = new Vector3(1000, -.1f, 1000);
                floor.GetComponent<BoxCollider>().size = new Vector3(20, .2f, 20); Physics.SyncTransforms(); Canvas.ForceUpdateCanvases();
                var points = (List<Vector3>)typeof(ResearchPanelMotion).GetField("actionPoints", Hidden).GetValue(panel);
                void Collect() { Canvas.ForceUpdateCanvases(); typeof(ResearchPanelMotion).GetMethod("CollectActionPoints", Hidden).Invoke(panel, null); }
                for (int page = 0; page < 3; page++)
                {
                    int row = page == 0 ? 0 : page == 1 ? 18 : 37;
                    content.anchoredPosition = new Vector2(0, row * 700); Collect(); Physics.SyncTransforms();
                    Check(points.Count > 5 && points.All(p => p.x >= -590.1f && p.x <= 590.1f && p.y >= -400.1f && p.y <= 400.1f), "38 active cards: only masked visible bounds at scroll " + row,
                        new { count = points.Count, minY = points.Min(p => p.y), maxY = points.Max(p => p.y) });
                    Check(panel.PlacementClear(panelRect.position, panelRect.rotation), "38 cards remain above actual floor at scroll " + row, panel.LastRejection);
                    Check(panel.ReadableFromHeadForDrag(panelRect.position, panelRect.rotation), "visible actions fit camera at scroll " + row, panel.LastRejection);
                }
                content.anchoredPosition = Vector2.zero; mask.enabled = false; Collect();
                Check(points.Any(p => p.y < -20000), "disabled mask keeps offscreen action bounds");
                Check(!panel.PlacementClear(panelRect.position, panelRect.rotation), "unmasked long content still rejected by floor", panel.LastRejection);
                mask.enabled = true; choices[37].GetComponent<Image>().maskable = false; Collect();
                Check(points.Any(p => p.y < -20000), "nonmaskable control not discarded"); choices[37].GetComponent<Image>().maskable = true;
                void Single(RectTransform control) { points.Clear(); Canvas.ForceUpdateCanvases(); typeof(ResearchPanelMotion).GetMethod("AddActionPoints", Hidden).Invoke(panel, new object[] { control }); }
                mask.padding = new Vector4(10, 15, 20, 25);
                var partial = Button(viewport, "Partial", new Vector2(100, 100), new Vector2(0, -190)); Single((RectTransform)partial.transform);
                Check(points.Count > 0 && points.Min(p => p.y) >= -185.1f && points.Max(p => p.y) <= -139.9f, "partial button respects visible edge and mask padding", points.Select(p => p.ToString()).ToArray());
                var nested = Rect(viewport, "NestedMask", new Vector2(200, 100), Vector2.zero); nested.gameObject.AddComponent<Image>();
                var nestedMask = nested.gameObject.AddComponent<RectMask2D>(); nestedMask.padding = Vector4.one * 10;
                var large = Button(nested, "NestedChoice", new Vector2(500, 500), Vector2.zero); Single((RectTransform)large.transform);
                Check(points.Count > 0 && points.All(p => Mathf.Abs(p.x) <= 90.1f && Mathf.Abs(p.y) <= 40.1f), "nested masks intersect in canvas coordinates", points.Select(p => p.ToString()).ToArray());
                toolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(700, -350); Single((RectTransform)toolbar.transform);
                Check(points.Max(p => p.x) > 739.9f, "unmasked toolbar bounds remain protected");
                toolbar.GetComponent<RectTransform>().anchoredPosition = new Vector2(500, -350);
                floor.transform.position = panelRect.position; Physics.SyncTransforms();
                Check(!panel.PlacementClear(panelRect.position, panelRect.rotation), "actual panel-floor collision still rejected", panel.LastRejection);
                floor.transform.position = new Vector3(1000, -.1f, 1000); Physics.SyncTransforms();
                var peer = Rect(null, "PeerPanel", new Vector2(1000, 600), Vector2.zero); peer.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                peer.position = panelRect.position; peer.localScale = Vector3.one * .001f;
                var peerMotion = peer.gameObject.AddComponent<ResearchPanelMotion>(); peerMotion.enabled = false; view.mainPanel = peerMotion;
                Check(!panel.PlacementClear(panelRect.position, panelRect.rotation), "actual peer overlap still rejected", panel.LastRejection);
            }
            catch (Exception error) { Check(false, "fixture exception", error.ToString()); }
            finally { EditorSceneManager.CloseScene(scene, true); if (active.IsValid()) SceneManager.SetActiveScene(active); }
            Check(before.SequenceEqual(Scenes()), "owning scene state preserved");
            string status = failures == 0 ? "PASS" : "FAIL";
            File.WriteAllText(Path.Combine(evidenceDirectory, "masked-placement.json"), JsonConvert.SerializeObject(new { status, evidence = "STOPPED_EDITOR_ACTUAL_UGUI_38_CARD_PHYSICS_FIXTURE_NOT_QUEST", checks = rows.Count, failures, rows }, Formatting.Indented));
            return "MASKED_ACTION_PLACEMENT " + status + " checks=" + rows.Count + " failures=" + failures;
        }
    }
}
#endif
