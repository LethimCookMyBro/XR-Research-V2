using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Stores participant metadata, NASA-TLX scores, and exports Master CSV for SPSS/R.
    /// </summary>
    [Serializable]
    public class VRParticipantMetadata : MonoBehaviour
    {
        [Header("Participant Info")]
        [SerializeField] private string _participantId = "P001";
        [SerializeField] private int _age = 25;
        [SerializeField] private string _gender = "Not specified";
        [SerializeField] private VRExperienceLevel _vrExperience = VRExperienceLevel.Beginner;

        [Header("References")]
        [SerializeField] private VRSessionManager _sessionManager;
        [SerializeField] private VRResearchScenarioManager _scenarioManager;
        [SerializeField] private VRMovementTracker _tracker;

        // NASA-TLX scores (0-10 each)
        private int _mentalDemand, _physicalDemand, _temporalDemand;
        private int _performance, _effort, _frustration;
        private int _presenceRating, _sicknessRating;

        // Public properties
        public string ParticipantId => _participantId;
        public int Age => _age;
        public string Gender => _gender;
        public VRExperienceLevel VrExperience => _vrExperience;
        public int MentalDemand => _mentalDemand;
        public int PhysicalDemand => _physicalDemand;
        public int TemporalDemand => _temporalDemand;
        public int PerformanceScore => _performance;
        public int EffortScale => _effort;
        public int FrustrationLevel => _frustration;
        public int PresenceScore => _presenceRating;
        public int SicknessScore => _sicknessRating;

        public void SetParticipantInfo(string id, int age, string gender, VRExperienceLevel experience)
        {
            _participantId = id;
            _age = age;
            _gender = gender;
            _vrExperience = experience;
        }

        public void SetNASA_TLXScores(int md, int pd, int td, int perf, int eff, int frust)
        {
            _mentalDemand = Mathf.Clamp(md, 0, 10);
            _physicalDemand = Mathf.Clamp(pd, 0, 10);
            _temporalDemand = Mathf.Clamp(td, 0, 10);
            _performance = Mathf.Clamp(perf, 0, 10);
            _effort = Mathf.Clamp(eff, 0, 10);
            _frustration = Mathf.Clamp(frust, 0, 10);
        }

        public void SetPresenceRating(int rating) => _presenceRating = Mathf.Clamp(rating, 0, 10);
        public void SetSicknessRating(int rating) => _sicknessRating = Mathf.Clamp(rating, 0, 10);

        /// <summary>
        /// Export session_summary.json
        /// </summary>
        public string ExportSessionSummary(string sessionPath)
        {
            var summary = new SessionSummary
            {
                participantId = _participantId,
                age = _age,
                gender = _gender,
                vrExperience = _vrExperience.ToString(),
                exportTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mentalDemand = _mentalDemand,
                physicalDemand = _physicalDemand,
                temporalDemand = _temporalDemand,
                performanceScore = _performance,
                effortScale = _effort,
                frustrationLevel = _frustration,
                presenceScore = _presenceRating,
                sicknessScore = _sicknessRating
            };

            string json = JsonUtility.ToJson(summary, true);
            string filePath = Path.Combine(sessionPath, "session_summary.json");

            try
            {
                File.WriteAllText(filePath, json, Encoding.UTF8);
                Debug.Log($"[VRResearch] Session summary saved: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRResearch] Failed to save summary: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Append one row to master_research_database.csv (Wide Format for SPSS/R).
        /// Creates header on first run, appends thereafter.
        /// </summary>
        public void AppendMasterCSV(string sessionPath)
        {
            string masterDir = Path.Combine(Application.persistentDataPath, "VRResearch");
            Directory.CreateDirectory(masterDir);
            string masterPath = Path.Combine(masterDir, "master_research_database.csv");

            bool fileExists = File.Exists(masterPath);

            var sb = new StringBuilder();

            // Write header if new file
            if (!fileExists)
            {
                sb.AppendLine(MasterCSVHeader());
            }

            // Build data row
            sb.AppendLine(MasterCSVRow(sessionPath));

            try
            {
                // Append to master file
                File.AppendAllText(masterPath, sb.ToString(), Encoding.UTF8);
                Debug.Log($"[VRResearch] Master CSV updated: {masterPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRResearch] Failed to write master CSV: {ex.Message}");
            }
        }

        private string MasterCSVHeader()
        {
            return "ParticipantID,Age,Gender,VRExperience," +
                   "TotalDuration_Sec,Task1_Duration,Task2_Duration,Task3_Duration," +
                   "TotalDistance_Traveled,AvgSpeed,GrabCount,ReleaseCount,TeleportCount," +
                   "MentalDemand,PhysicalDemand,TemporalDemand,PerformanceScore,EffortScale,FrustrationLevel," +
                   "PresenceScore,SicknessScore_FMS," +
                   "ExportTimestamp,SessionFolder";
        }

        private string MasterCSVRow(string sessionPath)
        {
            // Get scenario metrics if available
            float totalDuration = 0;
            float task1Dur = 0, task2Dur = 0, task3Dur = 0;
            float totalDistance = 0;
            int grabCount = 0, releaseCount = 0, teleportCount = 0;

            // These would be populated by scenario manager in a real run
            // For now, use defaults (will be enhanced when scenario manager provides data)

            string sessionFolder = Path.GetFileName(sessionPath);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            return $"{_participantId},{_age},{_gender},{_vrExperience}," +
                   $"{totalDuration:F2},{task1Dur:F2},{task2Dur:F2},{task3Dur:F2}," +
                   $"{totalDistance:F2},0.00,{grabCount},{releaseCount},{teleportCount}," +
                   $"{_mentalDemand},{_physicalDemand},{_temporalDemand},{_performance},{_effort},{_frustration}," +
                   $"{_presenceRating},{_sicknessRating}," +
                   $"{timestamp},{sessionFolder}";
        }

        public string GetSessionFolderName()
        {
            return $"{_participantId}_{DateTime.Now:yyyyMMdd_HHmmss}";
        }
    }

    [Serializable]
    public class SessionSummary
    {
        public string participantId;
        public int age;
        public string gender;
        public string vrExperience;
        public string exportTimestamp;
        public int mentalDemand;
        public int physicalDemand;
        public int temporalDemand;
        public int performanceScore;
        public int effortScale;
        public int frustrationLevel;
        public int presenceScore;
        public int sicknessScore;
    }

    public enum VRExperienceLevel
    {
        Beginner,
        Intermediate,
        Expert
    }
}
