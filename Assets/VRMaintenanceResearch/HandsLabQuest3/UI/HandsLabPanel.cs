using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VRMaintenanceResearch.HandsLabQuest3
{
    // Presentation only: the lab controller owns input, actions, reset and evidence.
    public sealed class HandsLabPanel : MonoBehaviour
    {
        public const string ThaiFontPath = "Assets/VRMaintenanceResearch/TrainingV2/UI/Fonts/NotoSansThai SDF.asset";
        [SerializeField] UnityEngine.UI.Button testButton, resetButton, newRunButton;
        [SerializeField] TMP_Text modeText, feedbackText;
        [SerializeField] Canvas panelCanvas;
        public UnityEngine.UI.Button TestButton => testButton;
        public UnityEngine.UI.Button ResetButton => resetButton;
        public UnityEngine.UI.Button NewRunButton => newRunButton;
        public Canvas Canvas => panelCanvas;
        public string Feedback => feedbackText.text;
        public void SetMode(string value) { modeText.text = value ?? ""; }
        public void SetFeedback(string value) { feedbackText.text = value ?? ""; }
        public void SetInteractable(bool value)
        {
            testButton.interactable = value; resetButton.interactable = value; newRunButton.interactable = value;
        }

        public static HandsLabPanel Build(Transform parent, TMP_FontAsset font = null)
        {
#if UNITY_EDITOR
            if (font == null) font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ThaiFontPath);
#endif
            if (font == null) throw new ArgumentException("HandsLabPanel requires the existing Thai TMP font.", nameof(font));
            var root = new GameObject("HandsLabPanel", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(HandsLabPanel));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(800, 450); rect.localScale = Vector3.one * .00075f;
            var panel = root.GetComponent<HandsLabPanel>();
            panel.panelCanvas = root.GetComponent<Canvas>(); panel.panelCanvas.renderMode = RenderMode.WorldSpace;
            root.GetComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 10;
            // The caller wires Meta PointableCanvas + input module; this builder adds no EventSystem.
            var raycaster = root.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            raycaster.ignoreReversedGraphics = true;
            raycaster.blockingObjects = UnityEngine.UI.GraphicRaycaster.BlockingObjects.ThreeD;
            Surface(root.transform, "Shell", 0, 0, 800, 450, new Color32(22, 30, 44, 255), 28);
            Label(root.transform, font, "ลองใช้มือกับแผง", 32, 22, 480, 68, 34, new Color32(246, 248, 253, 255));
            panel.modeText = Label(root.transform, font, "พร้อม", 524, 32, 244, 52, 25, new Color32(174, 202, 248, 255));
            panel.modeText.alignment = TextAlignmentOptions.TopRight;
            Label(root.transform, font, "ชี้หรือแตะปุ่มเพื่อทดลอง\nจับชิ้นงาน หมุน แล้วปล่อยลงบนจุดวาง", 32, 106, 736, 96, 26, new Color32(199, 209, 222, 255));
            var feedback = Surface(root.transform, "FeedbackCard", 32, 218, 736, 98, new Color32(36, 48, 67, 255), 16);
            panel.feedbackText = Label(feedback.transform, font, "เลือกทดลองกด เมื่อพร้อม", 20, 4, 696, 90, 26, new Color32(231, 239, 250, 255));
            panel.feedbackText.alignment = TextAlignmentOptions.MidlineLeft;
            panel.testButton = MakeButton(root.transform, font, "Test", "ทดลองกด", 32, 228, new Color32(55, 117, 220, 255));
            panel.resetButton = MakeButton(root.transform, font, "Reset", "วางคืน", 286, 228, new Color32(46, 61, 83, 255));
            panel.newRunButton = MakeButton(root.transform, font, "NewRun", "เริ่มรอบใหม่", 540, 228, new Color32(46, 61, 83, 255));
            return panel;
        }

        // Runs against real TMP layout. Rendering and actual Meta ray/poke remain separate checks.
        public string CheckLayout()
        {
            UnityEngine.Canvas.ForceUpdateCanvases();
            var failures = new List<string>(); int labels = 0;
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
            {
                label.ForceMeshUpdate(); labels++;
                if (label.preferredHeight > label.rectTransform.rect.height + 1)
                    failures.Add("Text height " + label.preferredHeight + " > " + label.rectTransform.rect.height + ": " + label.text);
                foreach (char character in label.text)
                    if (!char.IsControl(character) && !label.font.HasCharacter(character)) failures.Add("Missing glyph U+" + ((int)character).ToString("X4"));
            }
            foreach (var surface in GetComponentsInChildren<HandsLabRoundedGraphic>(true))
                if (surface.GetComponent<CanvasRenderer>() == null) failures.Add("Missing CanvasRenderer: " + surface.name);
            if (GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length != 0) failures.Add("Unexpected EventSystem.");
            if (failures.Count > 0) throw new InvalidOperationException("HANDSLAB_UI_LAYOUT_FAIL\n" + string.Join("\n", failures));
            return "HANDSLAB_UI_LAYOUT_GLYPH_PASS labels=" + labels;
        }

        static UnityEngine.UI.Button MakeButton(Transform parent, TMP_FontAsset font, string name, string label, float x, float width, Color color)
        {
            var background = Surface(parent, name, x, 340, width, 76, color, 17);
            var button = background.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1);
            colors.pressedColor = new Color(.66f, .79f, 1, 1); colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.48f, .5f, .55f, 1); colors.fadeDuration = .08f; button.colors = colors;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var text = Label(background.transform, font, label, 12, 8, width - 24, 60, 27, new Color32(246, 248, 253, 255));
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }
        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); return rect;
        }
        static HandsLabRoundedGraphic Surface(Transform parent, string name, float x, float y, float width, float height, Color color, float radius)
        {
            var graphic = Rect(parent, name, x, y, width, height).gameObject.AddComponent<HandsLabRoundedGraphic>();
            graphic.color = color; graphic.Radius = radius; graphic.raycastTarget = true; return graphic;
        }
        static TMP_Text Label(Transform parent, TMP_FontAsset font, string value, float x, float y, float width, float height, float size, Color color)
        {
            var text = Rect(parent, "Label", x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color;
            text.raycastTarget = false; text.richText = false; text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow; text.margin = new Vector4(0, 3, 0, 3); text.lineSpacing = 5; return text;
        }
    }
}
