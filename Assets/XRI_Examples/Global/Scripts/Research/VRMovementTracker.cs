using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

#if XR_HANDS
using UnityEngine.XR.Hands;
#endif

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Captures HMD, controller, and hand joint movement data every frame.
    /// Attach to the XR Origin or a child of it.
    /// </summary>
    public class VRMovementTracker : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _headTransform;
        [SerializeField] private Transform _leftControllerTransform;
        [SerializeField] private Transform _rightControllerTransform;

        [Header("Tracking Settings")]
        [Tooltip("Sample rate in seconds. 0 = every frame (typically 72-120 Hz on Quest).")]
        [SerializeField] private float _sampleInterval = 0f;
        [SerializeField] private bool _trackHandJoints = true;

        [Header("Velocity Calculation")]
        [SerializeField] private bool _calculateVelocity = true;

        // Current tracking data (public read-only for other systems)
        public TrackingFrame CurrentFrame { get; private set; }
        public bool IsTracking { get; private set; }

        // Active task context (set by VRResearchScenarioManager)
        public string ActiveTaskId { get; set; } = "";

        // Events
        public event Action<TrackingFrame> OnFrameCaptured;

        private float _nextSampleTime;
        private Vector3 _prevHeadPos, _prevLeftPos, _prevRightPos;
        private bool _hasPrevData;
        private float _sessionStartTime;

        // Hand tracking data
#if XR_HANDS
        private XRHandSubsystem _handSubsystem;
        private bool _leftHandTracked;
        private bool _rightHandTracked;
#endif

        private void Awake()
        {
            // Auto-find transforms if not assigned
            if (_headTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) _headTransform = cam.transform;
            }

            if (_leftControllerTransform == null || _rightControllerTransform == null)
            {
                // Try to find XR controllers via XRNode
                var leftDevices = new List<InputDevice>();
                InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, leftDevices);
                if (leftDevices.Count > 0 && _leftControllerTransform == null)
                {
                    var go = new GameObject("TrackedLeftController");
                    go.transform.SetParent(transform);
                    _leftControllerTransform = go.transform;
                }

                var rightDevices = new List<InputDevice>();
                InputDevices.GetDevicesAtXRNode(XRNode.RightHand, rightDevices);
                if (rightDevices.Count > 0 && _rightControllerTransform == null)
                {
                    var go = new GameObject("TrackedRightController");
                    go.transform.SetParent(transform);
                    _rightControllerTransform = go.transform;
                }
            }
        }

        private void Start()
        {
#if XR_HANDS
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            if (subsystems.Count > 0)
                _handSubsystem = subsystems[0];
#endif
        }

        public void StartTracking()
        {
            IsTracking = true;
            _sessionStartTime = Time.realtimeSinceStartup;
            _nextSampleTime = 0f;
            _hasPrevData = false;
            Debug.Log("[VRResearch] Tracking started.");
        }

        public void StopTracking()
        {
            IsTracking = false;
            _hasPrevData = false;
            Debug.Log("[VRResearch] Tracking stopped.");
        }

        private void LateUpdate()
        {
            if (!IsTracking) return;
            if (Time.realtimeSinceStartup < _nextSampleTime) return;

            _nextSampleTime = Time.realtimeSinceStartup + _sampleInterval;

            var frame = CaptureFrame();
            CurrentFrame = frame;
            OnFrameCaptured?.Invoke(frame);

            _prevHeadPos = _headTransform != null ? _headTransform.position : Vector3.zero;
            _prevLeftPos = _leftControllerTransform != null ? _leftControllerTransform.position : Vector3.zero;
            _prevRightPos = _rightControllerTransform != null ? _rightControllerTransform.position : Vector3.zero;
            _hasPrevData = true;
        }

        private TrackingFrame CaptureFrame()
        {
            float timestamp = (Time.realtimeSinceStartup - _sessionStartTime) * 1000f; // ms
            var frame = new TrackingFrame
            {
                TimestampMs = timestamp,
                FrameCount = Time.frameCount,
                ActiveTaskId = ActiveTaskId,
            };

            // Head tracking
            if (_headTransform != null)
            {
                frame.HeadPos = _headTransform.position;
                frame.HeadRot = _headTransform.rotation;
                if (_calculateVelocity && _hasPrevData)
                    frame.HeadVel = (_headTransform.position - _prevHeadPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            }

            // Left controller
            if (_leftControllerTransform != null)
            {
                frame.LeftControllerPos = _leftControllerTransform.position;
                frame.LeftControllerRot = _leftControllerTransform.rotation;
                if (_calculateVelocity && _hasPrevData)
                    frame.LeftControllerVel = (_leftControllerTransform.position - _prevLeftPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            }

            // Right controller
            if (_rightControllerTransform != null)
            {
                frame.RightControllerPos = _rightControllerTransform.position;
                frame.RightControllerRot = _rightControllerTransform.rotation;
                if (_calculateVelocity && _hasPrevData)
                    frame.RightControllerVel = (_rightControllerTransform.position - _prevRightPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            }

            // Input mode detection
            frame.InputMode = DetectInputMode();

            // Hand tracking joints
#if XR_HANDS
            if (_trackHandJoints && _handSubsystem != null)
            {
                CaptureHandJoints(frame);
            }
#endif

            return frame;
        }

        private string DetectInputMode()
        {
            var leftDevices = new List<InputDevice>();
            InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, leftDevices);

            foreach (var device in leftDevices)
            {
                if (device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
                {
                    if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool grip) && grip)
                        return "Controller";
                }
            }

#if XR_HANDS
            if (_leftHandTracked || _rightHandTracked)
                return "HandTracking";
#endif

            return "None";
        }

#if XR_HANDS
        private void CaptureHandJoints(TrackingFrame frame)
        {
            var leftHand = _handSubsystem.leftHand;
            var rightHand = _handSubsystem.rightHand;

            _leftHandTracked = leftHand.isTracked;
            _rightHandTracked = rightHand.isTracked;

            if (_leftHandTracked)
            {
                if (leftHand.TryGetJoint(XRHandJointID.Wrist, out var wrist))
                {
                    if (wrist.TryGetPose(out Pose wristPose))
                    {
                        frame.LeftWristPos = wristPose.position;
                        frame.LeftWristRot = wristPose.rotation;
                    }
                }
                if (leftHand.TryGetJoint(XRHandJointID.IndexTip, out var indexTip))
                {
                    if (indexTip.TryGetPose(out Pose tipPose))
                        frame.LeftIndexTipPos = tipPose.position;
                }
                if (leftHand.TryGetJoint(XRHandJointID.ThumbTip, out var thumbTip))
                {
                    if (thumbTip.TryGetPose(out Pose thumbPose))
                        frame.LeftThumbTipPos = thumbPose.position;
                }
            }

            if (_rightHandTracked)
            {
                if (rightHand.TryGetJoint(XRHandJointID.Wrist, out var wrist))
                {
                    if (wrist.TryGetPose(out Pose wristPose))
                    {
                        frame.RightWristPos = wristPose.position;
                        frame.RightWristRot = wristPose.rotation;
                    }
                }
                if (rightHand.TryGetJoint(XRHandJointID.IndexTip, out var indexTip))
                {
                    if (indexTip.TryGetPose(out Pose tipPose))
                        frame.RightIndexTipPos = tipPose.position;
                }
                if (rightHand.TryGetJoint(XRHandJointID.ThumbTip, out var thumbTip))
                {
                    if (thumbTip.TryGetPose(out Pose thumbPose))
                        frame.RightThumbTipPos = thumbPose.position;
                }
            }
        }
#endif
    }

    /// <summary>
    /// Data structure for a single tracking frame.
    /// </summary>
    [Serializable]
    public struct TrackingFrame
    {
        public float TimestampMs;
        public int FrameCount;
        public string InputMode;
        public string ActiveTaskId;

        // Head
        public Vector3 HeadPos;
        public Quaternion HeadRot;
        public Vector3 HeadVel;

        // Left Controller
        public Vector3 LeftControllerPos;
        public Quaternion LeftControllerRot;
        public Vector3 LeftControllerVel;

        // Right Controller
        public Vector3 RightControllerPos;
        public Quaternion RightControllerRot;
        public Vector3 RightControllerVel;

        // Hand Tracking Joints
        public Vector3 LeftWristPos;
        public Quaternion LeftWristRot;
        public Vector3 LeftIndexTipPos;
        public Vector3 LeftThumbTipPos;

        public Vector3 RightWristPos;
        public Quaternion RightWristRot;
        public Vector3 RightIndexTipPos;
        public Vector3 RightThumbTipPos;

        /// <summary>
        /// Convert to CSV header line.
        /// </summary>
        public static string CsvHeader()
        {
            return "TimestampMs,FrameCount,InputMode,ActiveTaskId," +
                   "HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ,HeadRotW,HeadVelX,HeadVelY,HeadVelZ," +
                   "LeftCtrlPosX,LeftCtrlPosY,LeftCtrlPosZ,LeftCtrlRotX,LeftCtrlRotY,LeftCtrlRotZ,LeftCtrlRotW,LeftCtrlVelX,LeftCtrlVelY,LeftCtrlVelZ," +
                   "RightCtrlPosX,RightCtrlPosY,RightCtrlPosZ,RightCtrlRotX,RightCtrlRotY,RightCtrlRotZ,RightCtrlRotW,RightCtrlVelX,RightCtrlVelY,RightCtrlVelZ," +
                   "LeftWristPosX,LeftWristPosY,LeftWristPosZ,LeftWristRotX,LeftWristRotY,LeftWristRotZ,LeftWristRotW," +
                   "LeftIndexTipX,LeftIndexTipY,LeftIndexTipZ,LeftThumbTipX,LeftThumbTipY,LeftThumbTipZ," +
                   "RightWristPosX,RightWristPosY,RightWristPosZ,RightWristRotX,RightWristRotY,RightWristRotZ,RightWristRotW," +
                   "RightIndexTipX,RightIndexTipY,RightIndexTipZ,RightThumbTipX,RightThumbTipY,RightThumbTipZ";
        }

        /// <summary>
        /// Convert to CSV row.
        /// </summary>
        public string ToCsvRow()
        {
            return $"{TimestampMs:F2},{FrameCount},{InputMode},{ActiveTaskId}," +
                   $"{HeadPos.x:F4},{HeadPos.y:F4},{HeadPos.z:F4},{HeadRot.x:F4},{HeadRot.y:F4},{HeadRot.z:F4},{HeadRot.w:F4},{HeadVel.x:F4},{HeadVel.y:F4},{HeadVel.z:F4}," +
                   $"{LeftControllerPos.x:F4},{LeftControllerPos.y:F4},{LeftControllerPos.z:F4},{LeftControllerRot.x:F4},{LeftControllerRot.y:F4},{LeftControllerRot.z:F4},{LeftControllerRot.w:F4},{LeftControllerVel.x:F4},{LeftControllerVel.y:F4},{LeftControllerVel.z:F4}," +
                   $"{RightControllerPos.x:F4},{RightControllerPos.y:F4},{RightControllerPos.z:F4},{RightControllerRot.x:F4},{RightControllerRot.y:F4},{RightControllerRot.z:F4},{RightControllerRot.w:F4},{RightControllerVel.x:F4},{RightControllerVel.y:F4},{RightControllerVel.z:F4}," +
                   $"{LeftWristPos.x:F4},{LeftWristPos.y:F4},{LeftWristPos.z:F4},{LeftWristRot.x:F4},{LeftWristRot.y:F4},{LeftWristRot.z:F4},{LeftWristRot.w:F4}," +
                   $"{LeftIndexTipPos.x:F4},{LeftIndexTipPos.y:F4},{LeftIndexTipPos.z:F4},{LeftThumbTipPos.x:F4},{LeftThumbTipPos.y:F4},{LeftThumbTipPos.z:F4}," +
                   $"{RightWristPos.x:F4},{RightWristPos.y:F4},{RightWristPos.z:F4},{RightWristRot.x:F4},{RightWristRot.y:F4},{RightWristRot.z:F4},{RightWristRot.w:F4}," +
                   $"{RightIndexTipPos.x:F4},{RightIndexTipPos.y:F4},{RightIndexTipPos.z:F4},{RightThumbTipPos.x:F4},{RightThumbTipPos.y:F4},{RightThumbTipPos.z:F4}";
        }
    }
}
