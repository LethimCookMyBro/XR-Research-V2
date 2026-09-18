using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace VRMaintenanceResearch.TrainingV2
{
    // Presentation only. The V2 controller owns state, validation, actions and event logging.
    public sealed class TrainingPanel : MonoBehaviour
    {
        [Serializable] public sealed class Page { public string id; public GameObject root; }
        [SerializeField] List<Page> pages = new List<Page>();
        [SerializeField] TMP_Text status, inspection, passedInspection, abortedDetail, manualHeading, manualBody;
        [SerializeField] CanvasGroup transition;
        [SerializeField] string currentPage = "start";
        public string CurrentPage => currentPage;
        public string InspectionText => inspection != null ? inspection.text : "";
        public int ManualPage { get; private set; }
        public event Action<string> Command;
        public event Action<string, string> PageChanged;
        Coroutine fade;
        static readonly Color Ink = new Color32(242, 246, 251, 255);
        static readonly Color Muted = new Color32(181, 194, 211, 255);
        static readonly Color Card = new Color32(40, 51, 69, 255);
        static readonly Color Accent = new Color32(71, 132, 229, 255);

        public void Dispatch(string command) { Command?.Invoke(command); }
        public void SetTrialStatus(string value) { if (status != null) status.text = value; }
        public void SetCommandsEnabled(bool enabled)
        {
            foreach (var button in GetComponentsInChildren<UnityEngine.UI.Button>(true)) button.interactable = enabled;
        }

        public void ShowPage(string id, string detail = null)
        {
            if (!pages.Exists(p => p.id == id)) throw new ArgumentException("Unknown training page: " + id);
            string previous = currentPage;
            // A supplied Inspect result is a string value captured at that inspection, never a live validator.
            if (id == "inspect" && detail != null) inspection.text = detail;
            if (id == "passed" && detail != null) passedInspection.text = detail;
            if (id == "aborted" && detail != null) abortedDetail.text = detail;
            foreach (var page in pages) page.root.SetActive(page.id == id);
            currentPage = id;
            if (Application.isPlaying && isActiveAndEnabled)
            {
                if (fade != null) StopCoroutine(fade);
                fade = StartCoroutine(FadeIn());
            }
            if (previous != id) PageChanged?.Invoke(previous, id);
        }

        IEnumerator FadeIn()
        {
            transition.alpha = .92f;
            while (transition.alpha < 1f) { transition.alpha = Mathf.MoveTowards(transition.alpha, 1, Time.unscaledDeltaTime * 4); yield return null; }
            fade = null;
        }

        public void ShowManualPage(int index)
        {
            ManualPage = Mathf.Clamp(index, 0, 2);
            manualHeading.text = TrainingStrings.Get("manual_title_" + ManualPage);
            manualBody.text = TrainingStrings.Get("manual_body_" + ManualPage);
            foreach (var command in GetComponentsInChildren<TrainingPanelCommand>(true))
            {
                if (command.Id == "manual_previous") command.GetComponent<UnityEngine.UI.Button>().interactable = ManualPage > 0;
                if (command.Id == "manual_next") command.GetComponent<UnityEngine.UI.Button>().interactable = ManualPage < 2;
            }
        }

        public static TrainingPanel Build(Transform parent, TMP_FontAsset font, Camera eventCamera = null)
        {
            if (font == null) throw new ArgumentNullException(nameof(font));
            var go = new GameObject("TrainingPanel", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(CanvasGroup), typeof(TrainingPanel));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(1000, 760); rt.localScale = Vector3.one * .001f;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = eventCamera;
            go.GetComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 10;
            var raycaster = go.GetComponent<TrackedDeviceGraphicRaycaster>(); raycaster.checkFor3DOcclusion = true; raycaster.checkFor2DOcclusion = false; raycaster.ignoreReversedGraphics = true;
            var panel = go.GetComponent<TrainingPanel>(); panel.transition = go.GetComponent<CanvasGroup>();
            Surface(go.transform, "Shell", new Vector2(0, 0), new Vector2(1000, 760), new Color32(22, 30, 44, 255), 32);
            Text(go.transform, font, TrainingStrings.Get("header"), new Vector2(44, -28), new Vector2(912, 48), 26, Muted);
            panel.status = Text(go.transform, font, TrainingStrings.Get("ready"), new Vector2(44, -86), new Vector2(912, 52), 26, Ink);
            Surface(go.transform, "Divider", new Vector2(44, -146), new Vector2(912, 2), new Color32(69, 81, 101, 255), 0);
            foreach (string id in new[] { "start", "training", "manual", "examine", "inspect", "pause", "passed", "aborted" })
            {
                var page = Rect(go.transform, "Page_" + id, new Vector2(0, -160), new Vector2(1000, 560));
                panel.pages.Add(new Page { id = id, root = page.gameObject });
                string title = TrainingStrings.Get(id + "_title");
                Text(page, font, title, new Vector2(44, -12), new Vector2(912, 76), 40, Ink);
                var body = Text(page, font, TrainingStrings.Get(id + "_body"), new Vector2(44, -94), new Vector2(912, 284), 29, Muted);
                if (id == "start") { panel.Button(page, font, "01", "เริ่มบทฝึก", "start", 44, 424, 912, true); }
                if (id == "training")
                {
                    panel.Button(page, font, "?", "คู่มือ", "manual_open", 44, 310, 440);
                    panel.Button(page, font, "i", "ดูวัตถุ", "examine", 516, 310, 440);
                    panel.Button(page, font, ">", "ตรวจการวาง", "inspect", 44, 424, 600, true);
                    panel.Button(page, font, "II", "พัก", "pause", 676, 424, 280);
                    body.rectTransform.sizeDelta = new Vector2(912, 192);
                }
                if (id == "manual")
                {
                    // Replace generic page title/body with explicitly paginated, fixed-height content.
                    panel.manualHeading = page.GetChild(0).GetComponent<TMP_Text>(); panel.manualBody = body;
                    panel.Button(page, font, "<", "ก่อนหน้า", "manual_previous", 44, 424, 280);
                    panel.Button(page, font, ">", "หน้าถัดไป", "manual_next", 348, 424, 280);
                    panel.Button(page, font, "x", "ปิดคู่มือ", "manual_close", 652, 424, 304, true);
                }
                if (id == "examine") panel.Button(page, font, "<", "กลับไปฝึก", "back", 44, 424, 912, true);
                if (id == "inspect")
                {
                    panel.inspection = body;
                    panel.Button(page, font, "<", "กลับไปจัดวาง", "back", 44, 424, 592, true);
                    panel.Button(page, font, "?", "คู่มือ", "manual_open", 668, 424, 288);
                }
                if (id == "passed") panel.passedInspection = body;
                if (id == "aborted") panel.abortedDetail = body;
                if (id == "pause")
                {
                    panel.Button(page, font, ">", "กลับมาฝึก", "resume", 44, 424, 592, true);
                    panel.Button(page, font, "x", "ยุติรอบนี้", "abort", 668, 424, 288);
                }
                if (id == "passed" || id == "aborted") panel.Button(page, font, "+", "เริ่มรอบฝึกใหม่", "new_trial", 44, 424, 912, true);
            }
            Text(go.transform, font, TrainingStrings.Get("footer"), new Vector2(44, -704), new Vector2(912, 44), 22, Muted);
            panel.ShowManualPage(0); panel.ShowPage("start");
            return panel;
        }

        void Button(Transform parent, TMP_FontAsset font, string icon, string label, string id, float x, float y, float width, bool primary = false)
        {
            label = TrainingStrings.Get("button_" + id);
            var shape = Surface(parent, "Command_" + id, new Vector2(x, -y), new Vector2(width, 80), primary ? Accent : Card, 18);
            var button = shape.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = shape;
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1); colors.pressedColor = new Color(.7f, .8f, .96f, 1); colors.disabledColor = new Color(.43f, .46f, .5f, 1); colors.fadeDuration = .09f; button.colors = colors;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var command = shape.gameObject.AddComponent<TrainingPanelCommand>(); command.Configure(this, id);
            Text(shape.transform, font, icon, new Vector2(18, -13), new Vector2(50, 54), 29, Ink).alignment = TextAlignmentOptions.Center;
            Text(shape.transform, font, label, new Vector2(78, -13), new Vector2(width - 92, 54), 29, Ink).alignment = TextAlignmentOptions.MidlineLeft;
        }

        static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = position; rt.sizeDelta = size; return rt;
        }
        static TrainingRoundedGraphic Surface(Transform parent, string name, Vector2 position, Vector2 size, Color color, float radius)
        {
            var graphic = Rect(parent, name, position, size).gameObject.AddComponent<TrainingRoundedGraphic>(); graphic.color = color; graphic.Radius = radius; graphic.raycastTarget = true; return graphic;
        }
        static TMP_Text Text(Transform parent, TMP_FontAsset font, string text, Vector2 position, Vector2 size, float fontSize, Color color)
        {
            var label = Rect(parent, "Text", position, size).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font; label.text = text; label.fontSize = fontSize; label.color = color; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal; label.overflowMode = TextOverflowModes.Overflow;
            label.margin = new Vector4(0, 4, 0, 4); label.lineSpacing = 8; label.richText = false; return label;
        }
    }
}
