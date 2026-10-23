using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum ResearchButtonVariant { Primary, Secondary, Ghost, Destructive, Icon, Tab, SelectedTab }

    // One easing and three durations for every UI transition; animations restart from the rendered value.
    public static class ResearchMotion
    {
        public const float Micro = .12f, Panel = .2f, State = .24f;
        public static float Ease(float t) { t = Mathf.Clamp01(t); float u = 1 - t; return 1 - u * u * u; } // ease-out cubic
    }

    public static class ResearchUITheme
    {
        public const float SpaceXS = 4, SpaceS = 8, SpaceM = 12, SpaceL = 16, SpaceXL = 24, SpaceXXL = 32;
        // Layout tokens (canvas units ~ mm at 1 m). Meta hit targets: >= 48 dp, primary >= 60 dp, never touching.
        public const float PanelPadding = 44, RowHeight = 60, RowGap = 20, SectionGap = 32, TitleGap = 16, ActionGap = 36, TargetHeight = 80, TargetGap = 24;
        public const float RadiusSmall = 12, RadiusMedium = 18, RadiusPanel = 30;
        public const float Display = 46, Title = 38, Section = 32, Body = 28, Caption = 24, ButtonText = 28;
        public const float PanelElevation = 7, ModalElevation = 10, ContextElevation = 3;
        public static readonly Color Glass = new Color(.94f, .97f, 1f, 1f);
        public static readonly Color Content = new Color(1f, 1f, 1f, 1f);
        public static readonly Color Elevated = new Color(.86f, .92f, .98f, 1f);
        public static readonly Color Ink = new Color(.08f, .14f, .23f);
        public static readonly Color Muted = new Color(.30f, .39f, .49f);
        public static readonly Color Blue = new Color(.12f, .43f, .86f, 1);
        public static readonly Color Success = new Color(.08f, .40f, .28f);
        public static readonly Color Warning = new Color(.60f, .35f, .06f);
        public static readonly Color Failure = new Color(.72f, .16f, .22f);
        public static readonly Color Hover = new Color(.22f, .55f, .94f);
        public static readonly Color Pressed = new Color(.07f, .29f, .65f);
        public static readonly Color Disabled = new Color(.48f, .55f, .63f, .55f);

        public static ResearchGlassEffect Surface(Graphic graphic, bool panel = false, float radius = 24)
        {
            var effect = graphic.GetComponent<ResearchGlassEffect>();
            if (!effect) effect = graphic.gameObject.AddComponent<ResearchGlassEffect>();
            effect.panel = panel; effect.radius = radius; effect.edgeAlpha = panel ? .22f : .12f;
            graphic.SetVerticesDirty();
            return effect;
        }

        public static void Button(Selectable control, ResearchButtonVariant variant)
        {
            if (!control || !control.targetGraphic) return;
            bool primary = variant == ResearchButtonVariant.Primary || variant == ResearchButtonVariant.SelectedTab;
            bool ghost = variant == ResearchButtonVariant.Ghost;
            control.targetGraphic.color = primary ? Blue : variant == ResearchButtonVariant.Destructive ? new Color(1f, .88f, .90f, 1f) : ghost ? Glass : Elevated;
            Surface(control.targetGraphic, false, variant == ResearchButtonVariant.Icon ? 28 : 18);
            var colors = control.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.82f, .91f, 1f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.62f, .78f, .95f);
            colors.disabledColor = new Color(.52f, .62f, .72f, .55f);
            colors.fadeDuration = ResearchMotion.Micro;
            control.colors = colors;

            foreach (var text in control.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = primary ? Color.white : Ink;
                text.fontStyle = FontStyles.Normal;
                text.raycastTarget = false;
            }

            // In VR, controller trigger pull causes slight micro-movement (> 10px) which switches
            // pointer state to Dragging. If dragging bubbles to a parent ScrollRect or handle,
            // PointableCanvasModule cancels eligibleForClick and drops the click.
            // ResearchButtonDragAbsorber absorbs drag on the button itself so pointerPress == pointerDrag
            // and OnPointerClick fires reliably on trigger release.
            if ((control is UnityEngine.UI.Button || control is UnityEngine.UI.Toggle) && !control.GetComponent<ResearchButtonDragAbsorber>())
            {
                control.gameObject.AddComponent<ResearchButtonDragAbsorber>();
            }
        }
    }
}
