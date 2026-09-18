using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace VRMaintenanceResearch.TrainingV2
{
    /// <summary>Original articulated glove geometry, driven by existing controller actions, never hand tracking.</summary>
    [DisallowMultipleComponent]
    public sealed class ControllerDrivenTrainingHand : MonoBehaviour
    {
        [SerializeField] bool isLeft;
        [SerializeField] InputActionProperty grip;
        [SerializeField] InputActionProperty trigger;
        [SerializeField] XRBaseInteractor nearInteractor;
        [SerializeField] Transform[] joints;
        [SerializeField] Quaternion[] restRotations;
        [Tooltip("Visual-only calibration in controller-local metres. Does not modify grip/aim/tracked poses.")]
        public Vector3 visualPositionOffset = new Vector3(0f, -0.015f, -0.04f);
        [Tooltip("Visual-only calibration in controller-local degrees; calibrate again on physical controllers.")]
        public Vector3 visualEulerOffset;
        [Min(0f)] public float poseResponse = 18f;
        float displayedGrip;
        float displayedTrigger;

        public bool IsLeft => isLeft;
        public float GripCurl => displayedGrip;
        public float IndexCurl => displayedTrigger;
        public int JointCount => joints == null ? 0 : joints.Length;

        /// <summary>Build once in the Editor under the existing tracked controller, then save the scene/prefab.
        /// Caller owns the persistent material. This method never enables, disables, or rebinds input actions.</summary>
        public static ControllerDrivenTrainingHand Create(Transform controllerParent, bool isLeft,
            Material material, InputActionReference grip, InputActionReference trigger, XRBaseInteractor nearInteractor)
        {
            if (controllerParent == null) throw new ArgumentNullException(nameof(controllerParent));
            if (material == null) throw new ArgumentNullException(nameof(material));
            var root = new GameObject(isLeft ? "Training Left Controller Hand" : "Training Right Controller Hand");
            root.transform.SetParent(controllerParent, false);
            var hand = root.AddComponent<ControllerDrivenTrainingHand>();
            hand.isLeft = isLeft;
            hand.grip = new InputActionProperty(grip);
            hand.trigger = new InputActionProperty(trigger);
            hand.nearInteractor = nearInteractor;
            hand.BuildGeometry(material);
            hand.ApplyPose(0f, 0f, false);
            return hand;
        }

        void LateUpdate()
        {
            var held = nearInteractor != null && nearInteractor.hasSelection;
            float blend = 1f - Mathf.Exp(-poseResponse * Time.unscaledDeltaTime);
            displayedGrip = Mathf.Lerp(displayedGrip, Read(grip), blend);
            displayedTrigger = Mathf.Lerp(displayedTrigger, Read(trigger), blend);
            ApplyPose(displayedGrip, displayedTrigger, held);
        }

        static float Read(InputActionProperty property)
        {
            var action = property.action;
            return action != null && action.enabled ? Mathf.Clamp01(action.ReadValue<float>()) : 0f;
        }

        /// <summary>Index follows trigger, other fingers follow grip. A held object enforces a grasp floor.</summary>
        public static float FingerCurl(int finger, float gripValue, float triggerValue, bool held)
        {
            float value = Mathf.Clamp01(finger == 0 ? triggerValue : gripValue);
            return held ? Mathf.Max(value, finger == 0 ? 0.65f : 0.8f) : value;
        }

        void ApplyPose(float gripValue, float triggerValue, bool held)
        {
            transform.localPosition = visualPositionOffset;
            transform.localRotation = Quaternion.Euler(visualEulerOffset);
            if (joints == null || restRotations == null || joints.Length != 14 || restRotations.Length != 14) return;
            for (int i = 0; i < joints.Length; i++)
            {
                int finger = i < 12 ? i / 3 : 4;
                float curl = FingerCurl(finger, gripValue, triggerValue, held);
                float angle = i < 12 ? (i % 3 == 0 ? 64f : 78f) : 52f;
                joints[i].localRotation = restRotations[i] * Quaternion.Euler(Mathf.Lerp(7f, angle, curl), 0f, 0f);
            }
        }

        void BuildGeometry(Material material)
        {
            // ponytail: 17 rounded primitive renderers per hand; replace with one skinned mesh if device profiling requires it.
            RoundedPart("Palm", transform, new Vector3(0f, 0f, 0f), new Vector3(0.076f, 0.033f, 0.096f), material);
            RoundedPart("Wrist", transform, new Vector3(0f, 0f, -0.052f), new Vector3(0.055f, 0.031f, 0.039f), material);
            joints = new Transform[14];
            restRotations = new Quaternion[14];
            float side = isLeft ? 1f : -1f;
            float[] lateral = { 0.026f, 0.009f, -0.009f, -0.026f };
            float[] lengths = { 0.073f, 0.081f, 0.075f, 0.059f };
            string[] names = { "Index", "Middle", "Ring", "Little" };
            for (int finger = 0; finger < 4; finger++)
            {
                Transform parent = transform;
                float previousLength = 0f;
                for (int segment = 0; segment < 3; segment++)
                {
                    var joint = new GameObject(names[finger] + " Joint " + segment).transform;
                    joint.SetParent(parent, false);
                    joint.localPosition = segment == 0
                        ? new Vector3(side * lateral[finger], 0f, finger == 3 ? 0.034f : 0.044f)
                        : new Vector3(0f, 0f, previousLength);
                    float length = lengths[finger] * (segment == 0 ? 0.44f : segment == 1 ? 0.32f : 0.24f);
                    float width = (finger == 3 ? 0.0135f : 0.016f) * (1f - segment * 0.08f);
                    RoundedPart(names[finger] + " Segment " + segment, joint, new Vector3(0f, 0f, length / 2f),
                        new Vector3(width, width, length + width * 0.65f), material);
                    joints[finger * 3 + segment] = joint;
                    restRotations[finger * 3 + segment] = joint.localRotation;
                    parent = joint;
                    previousLength = length;
                }
            }
            Transform thumbParent = transform;
            for (int segment = 0; segment < 2; segment++)
            {
                var joint = new GameObject("Thumb Joint " + segment).transform;
                joint.SetParent(thumbParent, false);
                joint.localPosition = segment == 0 ? new Vector3(side * 0.031f, -0.006f, -0.009f) : new Vector3(0f, 0f, 0.033f);
                joint.localRotation = segment == 0 ? Quaternion.Euler(0f, side * 56f, side * -22f) : Quaternion.identity;
                RoundedPart("Thumb Segment " + segment, joint, new Vector3(0f, 0f, 0.0165f), new Vector3(0.021f, 0.020f, 0.046f), material);
                joints[12 + segment] = joint;
                restRotations[12 + segment] = joint.localRotation;
                thumbParent = joint;
            }
            RoundedPart("Thumb Base", transform, new Vector3(side * 0.027f, -0.008f, -0.020f), new Vector3(0.037f, 0.033f, 0.048f), material);
        }

        static void RoundedPart(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            var renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

#if UNITY_EDITOR
        /// <summary>Logic/geometry check only. NOT simulator interaction or physical controller validation.</summary>
        public static string RunSelfCheck()
        {
            var parent = new GameObject("Training Hand Self Check");
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            try
            {
                parent.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f));
                Vector3 parentPosition = parent.transform.position;
                Quaternion parentRotation = parent.transform.rotation;
                var left = Create(parent.transform, true, material, null, null, null);
                var right = Create(parent.transform, false, material, null, null, null);
                Check(left.JointCount == 14 && right.JointCount == 14, "14 joints per hand");
                Check(left.GetComponentsInChildren<Collider>().Length == 0, "visuals have no physics collider");
                Check(left.joints[12].localPosition.x > 0f && right.joints[12].localPosition.x < 0f, "thumbs are mirrored");
                Check(FingerCurl(0, 1f, 0f, false) == 0f, "grip does not curl pointing index");
                Check(FingerCurl(0, 0f, 1f, false) == 1f && FingerCurl(1, 0f, 1f, false) == 0f, "trigger only drives index");
                Check(FingerCurl(1, 1f, 0f, false) == 1f, "grip curls fingers");
                left.ApplyPose(0f, 0f, false);
                Quaternion resting = left.joints[3].localRotation;
                left.ApplyPose(0f, 0f, true);
                Check(Quaternion.Angle(resting, left.joints[3].localRotation) > 30f, "held pose curls joints");
                left.ApplyPose(0f, 0f, false);
                Check(Quaternion.Angle(resting, left.joints[3].localRotation) < 0.01f, "release restores rest pose");
                left.visualPositionOffset = new Vector3(0.02f, 0.03f, 0.04f);
                left.visualEulerOffset = new Vector3(1f, 2f, 3f);
                left.ApplyPose(1f, 1f, false);
                Check(parent.transform.position == parentPosition && parent.transform.rotation == parentRotation, "tracked parent unchanged");
                Check(FingerCurl(1, -1f, 0f, false) == 0f && FingerCurl(0, 0f, 2f, false) == 1f, "input clamped");
                return "HAND_LOGIC_CHECK_PASS checks=10 joints_per_hand=14 renderers_per_hand=17 tracked_parent=unchanged";
            }
            finally
            {
                DestroyImmediate(parent);
                DestroyImmediate(material);
            }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("HAND_LOGIC_CHECK_FAIL " + label);
        }
#endif
    }
}
