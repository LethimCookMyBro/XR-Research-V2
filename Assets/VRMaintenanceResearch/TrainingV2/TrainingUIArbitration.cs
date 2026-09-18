using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace VRTrainingV2
{
    // XRI NearFar suppresses UI when a near candidate exists. Resolve only this V2 panel's foreground ownership
    // before XRI's dynamic update; the normal XRI raycaster still delivers every UI event.
    [DefaultExecutionOrder(-200)]
    public sealed class TrainingUIArbitration : MonoBehaviour
    {
        public TrainingPrototype owner;
        public NearFarInteractor interactor;
        bool originalNear=true;
        XRPokeInteractor[] pokes; bool[] originalPokeUI;
        CurveVisualController visual; float originalWidthAnimation, originalStartWidth, originalEndWidth;
        public void Configure(TrainingPrototype p,NearFarInteractor near)
        {
            owner=p;interactor=near;originalNear=near.enableNearCasting;
            // This prototype deliberately uses controller ray UI, not poke UI. Keep the existing
            // Poke component/pose and restore its setting when the V2 training root is disabled.
            pokes=near.transform.parent.GetComponentsInChildren<XRPokeInteractor>(true);
            originalPokeUI=new bool[pokes.Length];
            for(int i=0;i<pokes.Length;i++){originalPokeUI[i]=pokes[i].enableUIInteraction;pokes[i].enableUIInteraction=false;}
            visual=near.GetComponentInChildren<CurveVisualController>(true);
            if(visual)
            {
                originalWidthAnimation=visual.linePropertyAnimationSpeed;
                originalStartWidth=visual.lineRenderer.startWidth;originalEndWidth=visual.lineRenderer.endWidth;
                // Use a thin initial ray and immediate width updates; page transitions retain their own fade.
                visual.linePropertyAnimationSpeed=0;
                visual.lineRenderer.startWidth=visual.lineRenderer.endWidth=.003f;
            }
            enabled=true;
        }
        public static bool PanelOwnsRay(NearFarInteractor near,RectTransform panel)
        {
            if(!panel || !panel.gameObject.activeInHierarchy || near.hasSelection)return false;
            // NearFar skips far evaluation while a near candidate exists; its stabilized anchor can be stale.
            // Use the live raw cast origin only to arbitrate priority, then XRI delivers actual UI raycasts.
            var origin=near.farInteractionCaster.castOrigin;
            var ray=new Ray(origin.position,origin.forward);
            if(Vector3.Dot(ray.direction,panel.forward)<=0)return false;
            if(!new Plane(panel.forward,panel.position).Raycast(ray,out var distance) || distance<.001f || distance>10)return false;
            var point=ray.GetPoint(distance);
            if(!panel.rect.Contains((Vector2)panel.InverseTransformPoint(point)))return false;
            // Existing walls/objects in front continue to occlude UI. Never change physics or tracked poses.
            return !Physics.Raycast(ray,out _,Mathf.Max(0,distance-.002f),~0,QueryTriggerInteraction.Ignore);
        }
        void Update()
        {
            if(!interactor)return;
            interactor.enableNearCasting=originalNear && !(owner && owner.isActiveAndEnabled && PanelOwnsRay(interactor,owner.panel.transform as RectTransform));
        }
        void OnDisable()
        {
            if(interactor)interactor.enableNearCasting=originalNear;
            if(visual){visual.linePropertyAnimationSpeed=originalWidthAnimation;visual.lineRenderer.startWidth=originalStartWidth;visual.lineRenderer.endWidth=originalEndWidth;}
            if(pokes!=null)for(int i=0;i<pokes.Length;i++)if(pokes[i])pokes[i].enableUIInteraction=originalPokeUI[i];
        }
    }
}
