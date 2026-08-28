using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// Asynchronous data logger that buffers tracking frames and interaction events
    /// in RAM, then writes to CSV/JSON on session end to avoid frame drops.
    /// </summary>
    public class VRDataLogger : MonoBehaviour
    {
        [Header("Output Settings")]
        [SerializeField] private bool _writeCsv = true;
        [SerializeField] private bool _writeJson = true;
        [SerializeField] private string _sessionPrefix = "VRSession";

        [Header("Phase Splitting")]
        [Tooltip("Override file prefix for phase-split recording (e.g., 'task_1', 'task_2')")]
        [SerializeField] private string _filePrefix = "";

        // Thread-safe buffers
        private readonly ConcurrentQueue<TrackingFrame> _frameBuffer = new();
        private readonly ConcurrentQueue<InteractionLogEntry> _eventBuffer = new();
        private readonly List<string> _csvLines = new();
        private readonly object _csvLock = new();

        private string _outputDirectory;
        private string _sessionId;
        private bool _isWriting;
        private CancellationTokenSource _cts;

        public bool IsRecording { get; private set; }
        public int BufferedFrameCount => _frameBuffer.Count;
        public int BufferedEventCount => _eventBuffer.Count;

        /// <summary>
        /// Start recording with optional session ID and file prefix for phase splitting.
        /// </summary>
        public void StartRecording(string sessionId = null, string filePrefix = "")
        {
            if (!string.IsNullOrEmpty(filePrefix))
                _filePrefix = filePrefix;
            _sessionId = sessionId ?? $"{_sessionPrefix}_{DateTime.Now:yyyyMMdd_HHmmss}";
            _outputDirectory = Path.Combine(Application.persistentDataPath, "VRResearch", _sessionId);
            Directory.CreateDirectory(_outputDirectory);

            // Clear buffers
            while (_frameBuffer.TryDequeue(out _)) { }
            while (_eventBuffer.TryDequeue(out _)) { }
            lock (_csvLock) { _csvLines.Clear(); }

            IsRecording = true;
            _cts = new CancellationTokenSource();

            // Write CSV header immediately
            if (_writeCsv)
            {
                lock (_csvLock)
                {
                    _csvLines.Add(TrackingFrame.CsvHeader());
                }
            }

            Debug.Log($"[VRResearch] Recording started. Output: {_outputDirectory}");
        }

        public void StopRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;

            Debug.Log($"[VRResearch] Stopping recording. Frames: {_frameBuffer.Count}, Events: {_eventBuffer.Count}");

            // Write all data asynchronously
            _ = WriteDataAsync();
        }

        public void LogFrame(TrackingFrame frame)
        {
            if (!IsRecording) return;
            _frameBuffer.Enqueue(frame);

            // Also add to CSV buffer for incremental writing
            if (_writeCsv)
            {
                lock (_csvLock)
                {
                    _csvLines.Add(frame.ToCsvRow());
                }
            }
        }

        public void LogEvent(InteractionLogEntry entry)
        {
            if (!IsRecording) return;
            _eventBuffer.Enqueue(entry);
        }

        private async Task WriteDataAsync()
        {
            if (_isWriting) return;
            _isWriting = true;

            try
            {
                var tasks = new List<Task>();

                if (_writeCsv)
                    tasks.Add(WriteCsvAsync());

                if (_writeJson)
                    tasks.Add(WriteJsonAsync());

                await Task.WhenAll(tasks);

                Debug.Log($"[VRResearch] Data written successfully to {_outputDirectory}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VRResearch] Write error: {ex.Message}");
            }
            finally
            {
                _isWriting = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private string GetFileName(string baseName)
        {
            if (!string.IsNullOrEmpty(_filePrefix))
                return $"{_filePrefix}_{baseName}";
            return baseName;
        }

        private async Task WriteCsvAsync()
        {
            string filePath = Path.Combine(_outputDirectory, GetFileName("movement.csv"));

            List<string> lines;
            lock (_csvLock)
            {
                lines = new List<string>(_csvLines);
            }

            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                sb.AppendLine(line);
            }

            await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[VRResearch] CSV written: {filePath} ({lines.Count} rows)");
        }

        private async Task WriteJsonAsync()
        {
            string movementPath = Path.Combine(_outputDirectory, GetFileName("movement.json"));
            string eventsPath = Path.Combine(_outputDirectory, GetFileName("events.json"));

            // Write movement data
            var frames = new List<TrackingFrame>();
            while (_frameBuffer.TryDequeue(out var frame))
            {
                frames.Add(frame);
            }

            var movementJson = new StringBuilder();
            movementJson.AppendLine("[");
            for (int i = 0; i < frames.Count; i++)
            {
                movementJson.Append(JsonUtility.ToJson(frames[i]));
                if (i < frames.Count - 1) movementJson.AppendLine(",");
                else movementJson.AppendLine();
            }
            movementJson.AppendLine("]");

            await File.WriteAllTextAsync(movementPath, movementJson.ToString(), Encoding.UTF8);

            // Write interaction events
            var events = new List<InteractionLogEntry>();
            while (_eventBuffer.TryDequeue(out var evt))
            {
                events.Add(evt);
            }

            var eventsJson = new StringBuilder();
            eventsJson.AppendLine("[");
            for (int i = 0; i < events.Count; i++)
            {
                eventsJson.Append(JsonUtility.ToJson(events[i]));
                if (i < events.Count - 1) eventsJson.AppendLine(",");
                else eventsJson.AppendLine();
            }
            eventsJson.AppendLine("]");

            await File.WriteAllTextAsync(eventsPath, eventsJson.ToString(), Encoding.UTF8);

            Debug.Log($"[VRResearch] JSON written: {movementPath}, {eventsPath}");
        }

        /// <summary>
        /// Get the output directory for ADB pull.
        /// </summary>
        public string GetOutputPath() => _outputDirectory;

        /// <summary>
        /// Get ADB command to pull data from device.
        /// </summary>
        public string GetAdbPullCommand()
        {
            return $"adb pull \"{_outputDirectory}\" ./vr_research_data/";
        }

        private void OnDestroy()
        {
            if (IsRecording)
                StopRecording();
        }
    }

    /// <summary>
    /// Data structure for interaction events.
    /// </summary>
    [Serializable]
    public struct InteractionLogEntry
    {
        public float TimestampMs;
        public string EventType;     // "Grab", "Release", "Teleport", "LocomotionStart", "LocomotionEnd"
        public string ObjectName;    // Name of interacted object
        public string ObjectId;      // Instance ID or custom ID
        public string Hand;          // "Left", "Right", "None"
        public Vector3 Position;     // Position when event occurred
        public Quaternion Rotation;  // Rotation when event occurred
        public float Duration;       // Duration for time-based events (ms)
        public string ExtraData;     // Additional JSON data

        public static string CsvHeader()
        {
            return "TimestampMs,EventType,ObjectName,ObjectId,Hand," +
                   "PosX,PosY,PosZ,RotX,RotY,RotZ,RotW," +
                   "DurationMs,ExtraData";
        }

        public string ToCsvRow()
        {
            return $"{TimestampMs:F2},{EventType},{ObjectName},{ObjectId},{Hand}," +
                   $"{Position.x:F4},{Position.y:F4},{Position.z:F4}," +
                   $"{Rotation.x:F4},{Rotation.y:F4},{Rotation.z:F4},{Rotation.w:F4}," +
                   $"{Duration:F2},{ExtraData}";
        }
    }
}
