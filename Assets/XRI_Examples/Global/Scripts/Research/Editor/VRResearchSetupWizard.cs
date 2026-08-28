#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;

namespace UnityEngine.XR.Content.Interaction.Research.Editor
{
    /// <summary>
    /// Editor wizard to automatically set up VR Research Data Collection system
    /// in the current scene. Found under menu: Tools > VR Research > Setup Wizard.
    /// </summary>
    public class VRResearchSetupWizard : EditorWindow
    {
        private XROrigin _xrOrigin;
        private Camera _headCamera;
        private Transform _leftController;
        private Transform _rightController;
        private InputActionAsset _inputActions;
        private TMPro.TextMeshPro _statusText;

        [MenuItem("Tools/VR Research/Setup Wizard")]
        public static void ShowWindow()
        {
            GetWindow<VRResearchSetupWizard>("VR Research Setup");
        }

        private void OnGUI()
        {
            GUILayout.Label("VR Research Data Collection Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Auto-detect references
            if (GUILayout.Button("Auto-Detect References"))
            {
                AutoDetectReferences();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);

            _xrOrigin = (XROrigin)EditorGUILayout.ObjectField("XR Origin", _xrOrigin, typeof(XROrigin), true);
            _headCamera = (Camera)EditorGUILayout.ObjectField("Head Camera", _headCamera, typeof(Camera), true);
            _leftController = (Transform)EditorGUILayout.ObjectField("Left Controller", _leftController, typeof(Transform), true);
            _rightController = (Transform)EditorGUILayout.ObjectField("Right Controller", _rightController, typeof(Transform), true);
            _inputActions = (InputActionAsset)EditorGUILayout.ObjectField("Input Actions", _inputActions, typeof(InputActionAsset), false);
            _statusText = (TMPro.TextMeshPro)EditorGUILayout.ObjectField("Status Text (Optional)", _statusText, typeof(TMPro.TextMeshPro), true);

            EditorGUILayout.Space();

            if (GUILayout.Button("Setup Research System", GUILayout.Height(40)))
            {
                SetupResearchSystem();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This wizard will create a 'Research_DataManager' GameObject under the XR Origin " +
                "with all 4 research scripts attached and configured.",
                MessageType.Info);
        }

        private void AutoDetectReferences()
        {
            // Find XR Origin
            _xrOrigin = FindFirstObjectByType<XROrigin>();

            // Find main camera
            if (_headCamera == null)
                _headCamera = Camera.main;

            // Find controllers by name
            if (_leftController == null || _rightController == null)
            {
                var leftHand = GameObject.Find("LeftHand Controller") ?? GameObject.Find("Left Controller");
                var rightHand = GameObject.Find("RightHand Controller") ?? GameObject.Find("Right Controller");

                if (leftHand != null) _leftController = leftHand.transform;
                if (rightHand != null) _rightController = rightHand.transform;
            }

            // Find Input Action Asset
            if (_inputActions == null)
            {
                // Try to find XRI Default Input Actions
                var guids = AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset");
                if (guids.Length > 0)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    _inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                }
            }

            Debug.Log("[VRResearch Setup] References auto-detected.");
        }

        private void SetupResearchSystem()
        {
            if (_xrOrigin == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign an XR Origin reference.", "OK");
                return;
            }

            // Create or find the Research_DataManager
            var existingManager = _xrOrigin.transform.Find("Research_DataManager");
            GameObject managerObj;

            if (existingManager != null)
            {
                managerObj = existingManager.gameObject;
                Debug.Log("[VRResearch Setup] Found existing Research_DataManager.");
            }
            else
            {
                managerObj = new GameObject("Research_DataManager");
                managerObj.transform.SetParent(_xrOrigin.transform);
                managerObj.transform.localPosition = Vector3.zero;
                managerObj.transform.localRotation = Quaternion.identity;
                Debug.Log("[VRResearch Setup] Created Research_DataManager.");
            }

            // Add components
            var tracker = managerObj.GetComponent<VRMovementTracker>();
            if (tracker == null) tracker = managerObj.AddComponent<VRMovementTracker>();

            var dataLogger = managerObj.GetComponent<VRDataLogger>();
            if (dataLogger == null) dataLogger = managerObj.AddComponent<VRDataLogger>();

            var sessionManager = managerObj.GetComponent<VRSessionManager>();
            if (sessionManager == null) sessionManager = managerObj.AddComponent<VRSessionManager>();

            var eventsLogger = managerObj.GetComponent<VRInteractionEventsLogger>();
            if (eventsLogger == null) eventsLogger = managerObj.AddComponent<VRInteractionEventsLogger>();

            // Set tracker references via SerializedObject
            var trackerSO = new SerializedObject(tracker);
            if (_headCamera != null)
                trackerSO.FindProperty("_headTransform").objectReferenceValue = _headCamera.transform;
            if (_leftController != null)
                trackerSO.FindProperty("_leftControllerTransform").objectReferenceValue = _leftController;
            if (_rightController != null)
                trackerSO.FindProperty("_rightControllerTransform").objectReferenceValue = _rightController;
            trackerSO.ApplyModifiedProperties();

            // Set session manager references
            var sessionSO = new SerializedObject(sessionManager);
            sessionSO.FindProperty("_tracker").objectReferenceValue = tracker;
            sessionSO.FindProperty("_logger").objectReferenceValue = dataLogger;
            sessionSO.FindProperty("_eventsLogger").objectReferenceValue = eventsLogger;

            // Try to find Menu button input action
            if (_inputActions != null)
            {
                var menuAction = _inputActions.FindAction("XRI LeftHand/Menu");
                if (menuAction != null)
                {
                    sessionSO.FindProperty("_menuButton").objectReferenceValue = null; // Will be set via InputActionReference
                }

                var primaryAction = _inputActions.FindAction("XRI RightHand/PrimaryButton");
                if (primaryAction != null)
                {
                    sessionSO.FindProperty("_primaryButton").objectReferenceValue = null;
                }
            }
            sessionSO.ApplyModifiedProperties();

            // Set events logger references
            var eventsSO = new SerializedObject(eventsLogger);
            eventsSO.FindProperty("_dataLogger").objectReferenceValue = dataLogger;
            eventsSO.FindProperty("_movementTracker").objectReferenceValue = tracker;
            eventsSO.ApplyModifiedProperties();

            // Create status text UI
            if (_statusText == null)
            {
                var statusObj = new GameObject("Research_StatusText");
                statusObj.transform.SetParent(managerObj.transform);
                statusObj.transform.localPosition = new Vector3(0, 1.5f, 1f);
                statusObj.transform.localRotation = Quaternion.identity;

                _statusText = statusObj.AddComponent<TMPro.TextMeshPro>();
                _statusText.text = "[VR Research]\nReady";
                _statusText.fontSize = 0.1f;
                _statusText.alignment = TMPro.TextAlignmentOptions.Center;
                _statusText.color = Color.green;

                sessionSO.FindProperty("_statusText").objectReferenceValue = _statusText;
                sessionSO.ApplyModifiedProperties();
            }

            // Mark scene dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Setup Complete",
                "VR Research Data Collection system has been set up!\n\n" +
                "Components added to: Research_DataManager\n" +
                "Under: " + _xrOrigin.gameObject.name + "\n\n" +
                "Press Play and use Left Menu Button to start/stop recording.",
                "OK");

            Debug.Log("[VRResearch Setup] Setup complete!");
        }
    }
}
#endif
