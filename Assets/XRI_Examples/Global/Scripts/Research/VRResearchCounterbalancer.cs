using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Scans existing session folders to count A→B vs B→A assignments,
    /// then suggests the next order to maintain balance.
    /// </summary>
    public class VRResearchCounterbalancer : MonoBehaviour
    {
        [Header("Task Definitions")]
        [SerializeField] private string _taskAName = "ComputerRepair";
        [SerializeField] private string _taskBName = "FanRepair";

        private string _dataRoot;

        private void Awake()
        {
            _dataRoot = Path.Combine(Application.persistentDataPath, "VRResearch");
        }

        /// <summary>
        /// Scan existing sessions and return balance counts.
        /// </summary>
        public BalanceReport ScanExistingSessions()
        {
            var report = new BalanceReport();
            if (string.IsNullOrEmpty(_dataRoot))
                _dataRoot = Path.Combine(Application.persistentDataPath, "VRResearch");
            if (!Directory.Exists(_dataRoot))
                return report;

            foreach (var dir in Directory.GetDirectories(_dataRoot))
            {
                // Try task_order.json first (saved by VRSessionManager)
                string orderPath = Path.Combine(dir, "task_order.json");
                if (File.Exists(orderPath))
                {
                    string json = File.ReadAllText(orderPath);
                    if (json.Contains("A_then_B"))
                        report.CountA_then_B++;
                    else if (json.Contains("B_then_A"))
                        report.CountB_then_A++;
                    continue;
                }
                string metaPath = Path.Combine(dir, "session_summary.json");
                if (!File.Exists(metaPath)) continue;

                string json2 = File.ReadAllText(metaPath);
                // Simple parse for Task1_Condition
                if (json2.Contains("\"Task1_Condition\""))
                {
                    // Extract condition from JSON
                    int idx = json2.IndexOf("\"Task1_Condition\"");
                    if (idx > 0)
                    {
                        string sub = json2.Substring(idx, 100);
                        if (sub.Contains("A"))
                            report.CountA_then_B++;
                        else if (sub.Contains("B"))
                            report.CountB_then_A++;
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// Suggest the next order to maintain counterbalance.
        /// Returns "A_then_B" or "B_then_A".
        /// </summary>
        public string SuggestNextOrder()
        {
            var report = ScanExistingSessions();
            // Give priority to the group with fewer participants
            return report.CountA_then_B <= report.CountB_then_A ? "A_then_B" : "B_then_A";
        }

        public string SuggestNextOrder(string participantId) => SuggestNextOrder();

        /// <summary>
        /// Get suggested task names for phase 1 and phase 2.
        /// </summary>
        public (string task1Type, string task2Type) GetSuggestedTasks()
        {
            string order = SuggestNextOrder();
            if (order == "A_then_B")
                return (_taskAName, _taskBName);
            else
                return (_taskBName, _taskAName);
        }

        /// <summary>
        /// Save order assignment to session metadata.
        /// </summary>
        public void SaveOrderToSession(string sessionPath, string order)
        {
            string orderFile = Path.Combine(sessionPath, "task_order.json");
            var orderData = new TaskOrderData
            {
                taskOrder = order,
                task1Type = order == "A_then_B" ? _taskAName : _taskBName,
                task2Type = order == "A_then_B" ? _taskBName : _taskAName,
                assignedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            File.WriteAllText(orderFile, JsonUtility.ToJson(orderData, true));
        }
    }

    [Serializable]
    public class BalanceReport
    {
        public int CountA_then_B;
        public int CountB_then_A;
        public int Total => CountA_then_B + CountB_then_A;
        public bool IsBalanced => CountA_then_B == CountB_then_A;
        public string Status => IsBalanced ? "Balanced" : $"Imbalanced (A→B:{CountA_then_B}, B→A:{CountB_then_A})";
    }

    [Serializable]
    public class TaskOrderData
    {
        public string taskOrder;
        public string task1Type;
        public string task2Type;
        public string assignedAt;
    }
}
