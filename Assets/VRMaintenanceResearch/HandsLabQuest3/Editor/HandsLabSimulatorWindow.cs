#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace VRMaintenanceResearch.HandsLabQuest3.Editor
{
    public static partial class HandsLabEditor
    {
        public sealed class HandsLabSimulatorWindow : EditorWindow
        {
            string error;

            [MenuItem("Tools/Quest 3 HandsLab/Controller Input Simulator")]
            static void OpenWindow()
            {
                var window = GetWindow<HandsLabSimulatorWindow>("HandsLab Input");
                window.minSize = new Vector2(660, 450);
                window.Show();
            }

            void OnInspectorUpdate() { Repaint(); }

            void OnGUI()
            {
                EditorGUILayout.HelpBox("Synthetic controller input; no hand-tracking or Quest verification", MessageType.Info);
                EditorGUILayout.LabelField("Existing InputSystem queue at 60 Hz; position units: world metres.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || simRunning))
                    {
                        if (GUILayout.Button("Start", GUILayout.Height(28)))
                        {
                            try { error = null; SimStart(); }
                            catch (Exception exception)
                            {
                                // SimStart may have registered only part of the owned synthetic device set.
                                SimStop(); error = exception.Message; Debug.LogException(exception);
                            }
                        }
                    }
                    using (new EditorGUI.DisabledScope(!simRunning))
                        if (GUILayout.Button("Stop", GUILayout.Height(28))) { SimStop(); error = null; }
                    EditorGUILayout.LabelField(simRunning ? "Running" : "Stopped", GUILayout.Width(80));
                }
                if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox("Enter Play Mode in the HandsLab scene to start.", MessageType.None);
                if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);

                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || !simRunning))
                {
                    EditorGUILayout.Space(8);
                    // The existing simulator intentionally uses one tracking flag for both controllers.
                    simTracking = EditorGUILayout.Toggle("Tracking valid (both)", simTracking);
                    leftThumbTouch=EditorGUILayout.Toggle("Left primary capacitive touch",leftThumbTouch);rightThumbTouch=EditorGUILayout.Toggle("Right primary capacitive touch",rightThumbTouch);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawController("Left controller", ref leftPosition, ref leftRotation, ref leftGrip, ref leftTrigger);
                        DrawController("Right controller", ref rightPosition, ref rightRotation, ref rightGrip, ref rightTrigger);
                    }
                    if (GUILayout.Button("Reset both to neutral", GUILayout.Height(26)))
                    {
                        leftPosition = new Vector3(-.20f, 1.15f, .45f);
                        rightPosition = new Vector3(.20f, 1.15f, .45f);
                        leftRotation = rightRotation = Quaternion.identity;
                        leftGrip = rightGrip = leftTrigger = rightTrigger = 0f;leftThumbTouch=rightThumbTouch=false;
                        simTracking = true;
                    }
                }
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField("Use Stop before closing. Closing this window does not stop the simulator.", EditorStyles.wordWrappedMiniLabel);
            }

            static void DrawController(string title, ref Vector3 position, ref Quaternion rotation, ref float grip, ref float trigger)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    var newPosition = EditorGUILayout.Vector3Field("World position (m)", position);
                    if (EditorGUI.EndChangeCheck() && IsFinite(newPosition)) position = newPosition;
                    EditorGUI.BeginChangeCheck();
                    var euler = EditorGUILayout.Vector3Field("World Euler rotation (degrees)", rotation.eulerAngles);
                    if (EditorGUI.EndChangeCheck() && IsFinite(euler)) rotation = Quaternion.Euler(euler);
                    grip = EditorGUILayout.Slider("Grip", grip, 0f, 1f);
                    trigger = EditorGUILayout.Slider("Trigger", trigger, 0f, 1f);
                }
            }

            static bool IsFinite(Vector3 value)
            {
                return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                    && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                    && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
            }
        }
    }
}
#endif
