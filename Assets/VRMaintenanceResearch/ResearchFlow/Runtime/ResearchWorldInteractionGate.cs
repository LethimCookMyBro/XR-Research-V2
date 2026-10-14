using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;
using VRMaintenanceResearch.HandsLabQuest3.Runtime;

namespace VRMaintenanceResearch.ResearchFlow
{
    // The SDK still owns input, ray/poke and its active-state trackers. These parent gates
    // prevent its trackers from re-enabling physical interactors during a UI-only state.
    public sealed class ResearchWorldInteractionGate : MonoBehaviour
    {
        HandsLabInputGuard guard;
        readonly List<GameObject> roots = new List<GameObject>();
        bool requested, restored;
        public bool Allowed => restored;

        public void Configure(HandsLabInputGuard owner)
        {
            guard = owner;
            if (!guard || !guard.interactionsRoot) return;
            // Guard Awake keeps this subtree inactive; the helper runs before the SDK group's Start.
            guard.interactionsRoot.AddComponent<ResearchNearGrabPriority>();
            var controllerRay = guard.interactionsRoot.GetComponentsInChildren<RayInteractor>(true).Single(r => r.name == "ControllerRayInteractor");
            var wrapped = new HashSet<GameObject>();
            foreach (var component in guard.interactionsRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(component is IInteractor) || component is InteractorGroup ||
                    component is RayInteractor || component is PokeInteractor || !wrapped.Add(component.gameObject)) continue;
                if (component is HandGrabInteractor near && near.name == "ControllerHandGrabInteractor")
                {
                    var intent = near.gameObject.AddComponent<ResearchControllerGrabIntent>();
                    intent.guard = guard; intent.grabber = near; intent.ray = controllerRay;
                    // Installed SDK205 prefab uses this same HandRef for _hand and _activeState.
                    intent.source = near.GetComponent<HandRef>();
                    near.InjectOptionalActiveState(intent);
                }
                var gate = new GameObject("ResearchWorldGate_" + component.name);
                gate.SetActive(false);
                gate.transform.SetParent(component.transform.parent, false);
                gate.transform.SetSiblingIndex(component.transform.GetSiblingIndex());
                component.transform.SetParent(gate.transform, false);
                roots.Add(gate);
            }
        }

        public void SetAllowed(bool allowed)
        {
            requested = allowed;
            if (!allowed) SetRoots(false);
        }

        void Update()
        {
            if (!guard || !guard.InteractionsAllowed) { SetRoots(false); return; }
            // Releasing the pause/resume UI trigger cannot accidentally grab a world object.
            if (requested && !restored && guard.InputNeutral) SetRoots(true);
        }

        void SetRoots(bool active)
        {
            if (restored == active) return;
            restored = active;
            foreach (var root in roots) if (root) root.SetActive(active);
        }
        void OnDisable() { requested = false; SetRoots(false); }
    }

    public sealed class ResearchControllerGrabIntent : MonoBehaviour, IActiveState
    {
        public HandsLabInputGuard guard;
        public HandGrabInteractor grabber;
        public HandRef source;
        public RayInteractor ray;
        public bool Active
        {
            get
            {
                if (!source || !source.Active) return false;
                if (grabber.State == InteractorState.Select || guard.Mode != HandsLabInputGuard.InputMode.Controller) return true;
                var input = (guard.controllerReference as IController)?.ControllerInput;
                // Trigger selects UI; grip selects nearby objects. Preserve an existing hold.
                // The SDK evaluates this with its candidates, without a separate Update-order race.
                return !input.HasValue || input.Value.Trigger < guard.analogReleaseThreshold || input.Value.Grip >= guard.analogReleaseThreshold ||
                    !ray.Candidate || !ray.Candidate.GetComponentInParent<Canvas>();
            }
        }
    }

    [DefaultExecutionOrder(-100)]
    public sealed class ResearchNearGrabPriority : MonoBehaviour
    {
        void Start()
        {
            var group = GetComponent<BestHoverInteractorGroup>();
            // SDK Awake has populated Interactors, but group Start (default order 0) has not run.
            // Preserve membership and stable ordering within each class; never reorder an active selection.
            if (group && group.Interactors != null)
                group.InjectInteractors(group.Interactors.OrderBy(i => i is PokeInteractor ? 0 :
                    i is HandGrabInteractor || i is GrabInteractor ? 1 : i is RayInteractor ? 2 : 3).ToList());
        }
    }
}
