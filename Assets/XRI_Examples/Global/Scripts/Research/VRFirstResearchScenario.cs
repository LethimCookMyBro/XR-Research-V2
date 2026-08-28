using UnityEngine;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Pre-configured first research scenario with 3 tasks:
    /// 1. Locomotion (go to Station 1)
    /// 2. Grab (pick up object at Station 2)
    /// 3. Interface (interact with UI at Station 7)
    /// </summary>
    [CreateAssetMenu(fileName = "FirstResearchScenario", menuName = "VR Research/First Research Scenario")]
    public class VRFirstResearchScenario : ScriptableObject
    {
        [Header("Scenario Configuration")]
        public string scenarioName = "First Research - Task-Driven";
        public string description = "Basic VR research scenario with locomotion, grab, and UI tasks.";

        [Header("Task 1: Locomotion")]
        public string task1ZoneName = "Station_1_Locomotion";
        public float task1Timeout = 60f;

        [Header("Task 2: Grab")]
        public string task2ObjectName = "Pyramid_Simple";
        public float task2Timeout = 90f;

        [Header("Task 3: Interface")]
        public string task3ZoneName = "Station_7_UI";
        public float task3Timeout = 60f;

        /// <summary>
        /// Get the research tasks as a list.
        /// </summary>
        public System.Collections.Generic.List<ResearchTask> GetTasks()
        {
            return new System.Collections.Generic.List<ResearchTask>
            {
                new ResearchTask
                {
                    taskId = "Task_01",
                    taskName = "Locomotion Test",
                    instructionText = "Please walk or teleport to Station 1.\nStand inside the blue zone to complete this task.",
                    completionType = TaskCompletionType.EnterZone,
                    targetZoneName = task1ZoneName,
                    timeoutSeconds = task1Timeout,
                    zoneHighlightColor = new Color(0.2f, 0.6f, 1f, 0.5f)
                },
                new ResearchTask
                {
                    taskId = "Task_02",
                    taskName = "Grab Interactable",
                    instructionText = "Go to Station 2 and grab the pyramid object.\nHold it for 2 seconds to complete this task.",
                    completionType = TaskCompletionType.GrabObject,
                    targetObjectName = task2ObjectName,
                    timeoutSeconds = task2Timeout,
                    zoneHighlightColor = new Color(0.2f, 0.9f, 0.3f, 0.5f)
                },
                new ResearchTask
                {
                    taskId = "Task_03",
                    taskName = "UI Interaction",
                    instructionText = "Go to Station 7 and interact with the UI panel.\nPress any button to complete this task.",
                    completionType = TaskCompletionType.InteractWithUI,
                    targetZoneName = task3ZoneName,
                    timeoutSeconds = task3Timeout,
                    zoneHighlightColor = new Color(0.9f, 0.6f, 0.2f, 0.5f)
                }
            };
        }
    }
}
