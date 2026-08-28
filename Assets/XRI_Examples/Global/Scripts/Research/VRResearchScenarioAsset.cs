using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// ScriptableObject that defines a complete research scenario.
    /// Create via: Assets > Create > VR Research > Scenario Asset.
    /// </summary>
    [CreateAssetMenu(fileName = "NewScenario", menuName = "VR Research/Scenario Asset")]
    public class VRResearchScenarioAsset : ScriptableObject
    {
        [Header("Scenario Info")]
        public string scenarioName = "New Scenario";
        [TextArea(2, 4)]
        public string description;

        [Header("Tasks")]
        public List<ResearchTask> tasks = new();

        [Header("Settings")]
        public bool autoStartOnSession = true;
        public bool autoStopOnLastTask = true;

        /// <summary>
        /// Get a task by index.
        /// </summary>
        public ResearchTask GetTask(int index)
        {
            if (index >= 0 && index < tasks.Count)
                return tasks[index];
            return default;
        }

        /// <summary>
        /// Get total number of tasks.
        /// </summary>
        public int TaskCount => tasks.Count;
    }
}
