using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// State machine that manages a sequence of research tasks.
    /// Assigns tasks, tracks completion, and logs task events.
    /// Note: Surveys (NASA-TLX, Sickness) are done on external tablet/paper,
    /// NOT in VR, per research protocol.
    /// </summary>
    public class VRResearchScenarioManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VRSessionManager _sessionManager;
        [SerializeField] private VRMovementTracker _tracker;
        [SerializeField] private VRInteractionEventsLogger _eventsLogger;
        [SerializeField] private VRResearchCoachingUI _coachingUI;

        [Header("Scenario Settings")]
        [SerializeField] private string _scenarioName = "DefaultScenario";
        [SerializeField] private List<ResearchTask> _tasks = new();
        [SerializeField] private bool _autoStartOnSession = true;
        [SerializeField] private bool _autoStopOnLastTask = true;

        // State
        private int _currentTaskIndex = -1;
        private float _taskStartTime;
        private bool _scenarioActive;
        private bool _taskCompleted;

        // Events
        public event Action<int, ResearchTask> OnTaskStarted;
        public event Action<int, ResearchTask, float> OnTaskCompleted;
        public event Action<int, ResearchTask> OnTaskFailed;
        public event Action OnScenarioCompleted;

        // Public state
        public bool ScenarioActive => _scenarioActive;
        public int CurrentTaskIndex => _currentTaskIndex;
        public ResearchTask? CurrentTask => _currentTaskIndex >= 0 && _currentTaskIndex < _tasks.Count
            ? _tasks[_currentTaskIndex] : null;
        public string ActiveTaskId => CurrentTask?.taskId ?? "";
        public int TotalTasks => _tasks.Count;

        private void Awake()
        {
            if (_sessionManager == null) _sessionManager = FindFirstObjectByType<VRSessionManager>();
            if (_tracker == null) _tracker = FindFirstObjectByType<VRMovementTracker>();
            if (_eventsLogger == null) _eventsLogger = FindFirstObjectByType<VRInteractionEventsLogger>();
            if (_coachingUI == null) _coachingUI = FindFirstObjectByType<VRResearchCoachingUI>();
        }

        private void OnEnable()
        {
            // Subscribe to zone events for task completion
        }

        private void Update()
        {
            if (!_scenarioActive || _currentTaskIndex < 0) return;

            var task = _tasks[_currentTaskIndex];
            float elapsed = Time.realtimeSinceStartup - _taskStartTime;

            // Check timeout
            if (task.timeoutSeconds > 0 && elapsed >= task.timeoutSeconds && !_taskCompleted)
            {
                FailCurrentTask("Timeout");
                return;
            }

            // Check completion conditions
            if (!_taskCompleted)
            {
                CheckTaskCompletion(task);
            }
        }

        /// <summary>
        /// Start the scenario. Called by VRSessionManager when recording begins.
        /// </summary>
        public void StartScenario()
        {
            if (_tasks.Count == 0)
            {
                Debug.LogWarning("[VRResearchScenario] No tasks defined!");
                return;
            }

            _scenarioActive = true;
            _currentTaskIndex = -1;
            Debug.Log($"[VRResearchScenario] Scenario '{_scenarioName}' started with {_tasks.Count} tasks.");
            AdvanceToNextTask();
        }

        /// <summary>
        /// Stop the scenario and log completion.
        /// </summary>
        public void StopScenario()
        {
            if (!_scenarioActive) return;

            _scenarioActive = false;
            LogTaskEvent("Scenario_Stopped", -1);
            Debug.Log($"[VRResearchScenario] Scenario '{_scenarioName}' stopped.");
        }

        /// <summary>
        /// Advance to the next task in the sequence.
        /// </summary>
        public void AdvanceToNextTask()
        {
            // Complete current task if any
            if (_currentTaskIndex >= 0 && !_taskCompleted)
            {
                CompleteCurrentTask();
            }

            _currentTaskIndex++;

            if (_currentTaskIndex >= _tasks.Count)
            {
                // All tasks completed
                CompleteScenario();
                return;
            }

            // Start new task
            _taskStartTime = Time.realtimeSinceStartup;
            _taskCompleted = false;

            var task = _tasks[_currentTaskIndex];
            LogTaskEvent("Task_Started", _currentTaskIndex);
            OnTaskStarted?.Invoke(_currentTaskIndex, task);

            // Update coaching UI
            if (_coachingUI != null)
            {
                _coachingUI.UpdateTask(_currentTaskIndex, _tasks.Count, task);
            }

            Debug.Log($"[VRResearchScenario] Task {_currentTaskIndex + 1}/{_tasks.Count}: {task.taskName}");
        }

        /// <summary>
        /// Force-complete the current task.
        /// </summary>
        public void CompleteCurrentTask()
        {
            if (_currentTaskIndex < 0 || _taskCompleted) return;

            float duration = Time.realtimeSinceStartup - _taskStartTime;
            var task = _tasks[_currentTaskIndex];

            _taskCompleted = true;
            LogTaskEvent("Task_Completed", _currentTaskIndex, duration);
            OnTaskCompleted?.Invoke(_currentTaskIndex, task, duration);

            Debug.Log($"[VRResearchScenario] Task {_currentTaskIndex + 1} completed in {duration:F1}s");

            // Auto-advance after brief delay
            if (_currentTaskIndex < _tasks.Count - 1)
            {
                Invoke(nameof(AdvanceToNextTask), 1.5f);
            }
            else
            {
                // Last task - complete scenario
                Invoke(nameof(CompleteScenario), 1.5f);
            }
        }

        private void FailCurrentTask(string reason)
        {
            if (_currentTaskIndex < 0 || _taskCompleted) return;

            _taskCompleted = true;
            var task = _tasks[_currentTaskIndex];

            LogTaskEvent("Task_Failed", _currentTaskIndex, 0, reason);
            OnTaskFailed?.Invoke(_currentTaskIndex, task);

            Debug.Log($"[VRResearchScenario] Task {_currentTaskIndex + 1} failed: {reason}");

            // Auto-advance to next task
            Invoke(nameof(AdvanceToNextTask), 2.0f);
        }

        private void CompleteScenario()
        {
            _scenarioActive = false;
            LogTaskEvent("Scenario_Completed", -1);
            OnScenarioCompleted?.Invoke();

            if (_coachingUI != null)
            {
                _coachingUI.ShowCompletion();
            }

            // Auto-stop recording when scenario completes
            // Surveys are done on external tablet (not in VR)
            if (_autoStopOnLastTask && _sessionManager != null && _sessionManager.IsRecording)
            {
                _sessionManager.StopSession();
                Debug.Log("[VRResearchScenario] Recording stopped. Please remove headset for survey.");
            }

            Debug.Log($"[VRResearchScenario] Scenario '{_scenarioName}' completed!");
        }

        private void CheckTaskCompletion(ResearchTask task)
        {
            switch (task.completionType)
            {
                case TaskCompletionType.EnterZone:
                    if (task.targetZone != null && task.targetZone.PlayerInside)
                    {
                        CompleteCurrentTask();
                    }
                    break;

                case TaskCompletionType.GrabObject:
                    // Checked via OnGrabDetected callback
                    break;

                case TaskCompletionType.InteractWithUI:
                    // Checked via OnUIInteraction callback
                    break;

                case TaskCompletionType.Manual:
                    // No auto-completion
                    break;
            }
        }

        /// <summary>
        /// Called by VRResearchZoneTrigger when player enters a zone.
        /// </summary>
        public void OnZoneEntered(string zoneName)
        {
            if (!_scenarioActive || _taskCompleted) return;

            var task = _tasks[_currentTaskIndex];
            if (task.completionType == TaskCompletionType.EnterZone &&
                task.targetZoneName == zoneName)
            {
                CompleteCurrentTask();
            }
        }

        /// <summary>
        /// Called by VRInteractionEventsLogger when a grab is detected.
        /// </summary>
        public void OnGrabDetected(string objectName)
        {
            if (!_scenarioActive || _taskCompleted) return;

            var task = _tasks[_currentTaskIndex];
            if (task.completionType == TaskCompletionType.GrabObject &&
                task.targetObjectName == objectName)
            {
                CompleteCurrentTask();
            }
        }

        /// <summary>
        /// Called when a UI button is pressed.
        /// </summary>
        public void OnUIInteraction()
        {
            if (!_scenarioActive || _taskCompleted) return;

            var task = _tasks[_currentTaskIndex];
            if (task.completionType == TaskCompletionType.InteractWithUI)
            {
                CompleteCurrentTask();
            }
        }

        private void LogTaskEvent(string eventType, int taskIndex, float duration = 0, string extra = "")
        {
            if (_eventsLogger == null) return;

            string taskId = taskIndex >= 0 && taskIndex < _tasks.Count
                ? _tasks[taskIndex].taskId : "None";

            string extraData = $"{{\"scenario\":\"{_scenarioName}\",\"taskId\":\"{taskId}\",\"taskIndex\":{taskIndex},\"duration\":{duration:F1},\"reason\":\"{extra}\"}}";

            _eventsLogger.LogLocomotionEvent(eventType, Vector3.zero, extraData);
        }
    }

    /// <summary>
    /// Defines a single research task.
    /// </summary>
    [Serializable]
    public struct ResearchTask
    {
        [Header("Task Identity")]
        public string taskId;
        public string taskName;

        [Header("Instructions")]
        [TextArea(2, 5)]
        public string instructionText;

        [Header("Completion Conditions")]
        public TaskCompletionType completionType;
        public string targetZoneName;
        public VRResearchZoneTrigger targetZone;
        public string targetObjectName;

        [Header("Settings")]
        [Tooltip("Maximum time allowed for this task (0 = unlimited)")]
        public float timeoutSeconds;

        [Header("Visual")]
        public Color zoneHighlightColor;
    }

    /// <summary>
    /// Types of task completion conditions.
    /// </summary>
    public enum TaskCompletionType
    {
        EnterZone,
        GrabObject,
        InteractWithUI,
        Manual
    }
}
