#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UnityEngine.XR.Content.Interaction.Research.Editor
{
    /// <summary>
    /// Editor window for researcher to input participant metadata before VR session.
    /// Clean, clinical design matching PIC_REF NASA TLX style.
    /// Menu: Tools > VR Research > Pre-Session Setup
    /// </summary>
    public class VRPreSessionSetupWindow : EditorWindow
    {
        private string _participantId = "P001";
        private int _age = 25;
        private int _genderIndex = 0;
        private int _experienceIndex = 0;
        private int _orderIndex = 0;

        private static readonly string[] _genderOptions = { "Male", "Female", "Non-binary", "Not specified" };
        private static readonly string[] _experienceOptions = { "Beginner", "Intermediate", "Expert" };
        private static readonly string[] _orderOptions = { "A_then_B (Computer→Fan)", "B_then_A (Fan→Computer)" };

        // PIC_REF Design System Colors
        private readonly Color _headerBg = new Color(0.18f, 0.35f, 0.65f);
        private readonly Color _accentColor = new Color(0.25f, 0.55f, 0.85f);
        private readonly Color _panelBg = new Color(0.95f, 0.95f, 0.97f);
        private readonly Color _textColor = new Color(0.12f, 0.12f, 0.18f);

        [MenuItem("Tools/VR Research/Pre-Session Setup")]
        public static void ShowWindow()
        {
            var window = GetWindow<VRPreSessionSetupWindow>("Pre-Session Setup");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void OnGUI()
        {
            // Header
            DrawHeader();

            EditorGUILayout.Space(10);

            // Counterbalance suggestion
            DrawCounterbalanceSection();

            EditorGUILayout.Space(5);

            // Participant Info Section
            DrawSection("Participant Information", () =>
            {
                EditorGUILayout.LabelField("Participant ID", EditorStyles.boldLabel);
                _participantId = EditorGUILayout.TextField(_participantId);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Age", EditorStyles.boldLabel);
                _age = EditorGUILayout.IntSlider(_age, 10, 99);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Gender", EditorStyles.boldLabel);
                _genderIndex = EditorGUILayout.Popup(_genderIndex, _genderOptions);

                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("VR Experience Level", EditorStyles.boldLabel);
                _experienceIndex = EditorGUILayout.Popup(_experienceIndex, _experienceOptions);

                EditorGUILayout.Space(5);
                DrawExperienceDescription();
            });

            EditorGUILayout.Space(10);

            // Session Info
            DrawSection("Session Configuration", () =>
            {
                EditorGUILayout.LabelField("Scenario: First Research (3 Tasks)", EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("  Task 1: Locomotion → Station 1", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Task 2: Grab Object → Station 2", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Task 3: UI Interaction → Station 7", EditorStyles.miniLabel);

                EditorGUILayout.Space(5);
                string folderName = $"{_participantId}_{System.DateTime.Now:yyyyMMdd_HHmmss}";
                EditorGUILayout.LabelField("Output Folder:", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(folderName, EditorStyles.wordWrappedLabel, GUILayout.Height(18));
            });

            EditorGUILayout.Space(15);

            // Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply to Scene", GUILayout.Height(35)))
            {
                ApplyToScene();
            }
            if (GUILayout.Button("Start Session", GUILayout.Height(35), GUILayout.Width(150)))
            {
                ApplyToScene();
                StartSession();
            }
            GUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "1. Click 'Apply to Scene' to set participant info on VRParticipantMetadata component.\n" +
                "2. Click 'Start Session' to also begin recording.\n" +
                "3. Put on the Quest headset and follow the coaching UI instructions.",
                MessageType.Info);
        }

        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(position.width, 50);
            EditorGUI.DrawRect(rect, _headerBg);

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, "VR Research — Pre-Session Setup", style);
        }

        private void DrawCounterbalanceSection()
        {
            var counterbalancer = FindFirstObjectByType<VRResearchCounterbalancer>();
            if (counterbalancer == null)
            {
                EditorGUILayout.HelpBox("VRResearchCounterbalancer not found in scene.", MessageType.Warning);
                return;
            }

            var report = counterbalancer.ScanExistingSessions();
            string suggested = counterbalancer.SuggestNextOrder();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Counterbalance Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Total Sessions: {report.Total}");
            EditorGUILayout.LabelField($"A→B: {report.CountA_then_B}  |  B→A: {report.CountB_then_A}");
            EditorGUILayout.Space(3);

            string rec = suggested == "A_then_B" ? "A_then_B (Computer→Fan)" : "B_then_A (Fan→Computer)";
            EditorGUILayout.HelpBox($"Recommended: {rec}", MessageType.Info);

            _orderIndex = suggested == "A_then_B" ? 0 : 1;
            EditorGUILayout.EndVertical();
        }

        private void DrawSection(string title, System.Action drawContent)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.Space(3);
            drawContent();
            EditorGUILayout.EndVertical();
        }

        private void DrawExperienceDescription()
        {
            string desc = _experienceIndex switch
            {
                0 => "Beginner: Never used VR or used < 5 times",
                1 => "Intermediate: Used VR 5-20 times",
                2 => "Expert: Used VR > 20 times or develops VR content",
                _ => ""
            };
            EditorGUILayout.HelpBox(desc, MessageType.None);
        }

        private void ApplyToScene()
        {
            var metadata = FindFirstObjectByType<VRParticipantMetadata>();
            if (metadata == null)
            {
                EditorUtility.DisplayDialog("Error",
                    "VRParticipantMetadata not found in scene.\n" +
                    "Add it to a GameObject first.", "OK");
                return;
            }

            var so = new SerializedObject(metadata);
            so.FindProperty("_participantId").stringValue = _participantId;
            so.FindProperty("_age").intValue = _age;
            so.FindProperty("_gender").stringValue = _genderOptions[_genderIndex];
            so.FindProperty("_vrExperience").enumValueIndex = _experienceIndex;
            so.ApplyModifiedProperties();

            EditorUtility.DisplayDialog("Applied",
                $"Participant info set:\n\n" +
                $"ID: {_participantId}\n" +
                $"Age: {_age}\n" +
                $"Gender: {_genderOptions[_genderIndex]}\n" +
                $"Experience: {_experienceOptions[_experienceIndex]}", "OK");

            Debug.Log($"[VRResearch] Pre-session setup applied: {_participantId}");
        }

        private void StartSession()
        {
            var sessionManager = FindFirstObjectByType<VRSessionManager>();
            if (sessionManager != null && !sessionManager.IsRecording)
            {
                sessionManager.StartSession();
                Debug.Log("[VRResearch] Session started from Pre-Session Setup.");
            }
        }
    }
}
#endif
