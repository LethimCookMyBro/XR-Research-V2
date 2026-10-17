using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>
    /// DEV-only Quest -> authenticated HTTPS relay. Reads immutable ResearchExportQueue snapshots;
    /// the research logger, PC receipts, and study-mode export permissions remain unchanged.
    /// Missing provisioning -> no requests. Provider receipt is NOT inferred from relay receipt.
    /// </summary>
    public sealed class ResearchQuestCloudUploader : MonoBehaviour
    {
        const int ChunkBytes = 256 * 1024;
        const float RetrySeconds = 45f;
        const string ConfigFile = "s1_cloud_relay.json";
        static readonly string[] Names = { "raw/events.jsonl", "raw/movement.csv", "raw/session_metadata.json", "raw/research-session.json" };
        ResearchSessionFlowController controller;
        string endpoint, bearer, root, deviceId;
        float nextCheck;
        bool busy, configured;
        string lastBundleAttempt;
        JObject answer;
        string requestError;
        public string DeliveryState { get; private set; } = "cloud_disabled";

        void Start()
        {
            // A participant-study build does not use this technical uploader without protocol and ethics review.
            if (Application.isEditor || !ResearchSessionFlowController.TechnicalBuild) { enabled = false; return; }
            controller = GetComponent<ResearchSessionFlowController>();
            root = string.IsNullOrWhiteSpace(controller.registryRoot)
                ? Path.Combine(Application.persistentDataPath, "ResearchSessionRegistry")
                : controller.registryRoot;
            try
            {
                string path = Path.Combine(Application.persistentDataPath, ConfigFile);
                if (!File.Exists(path)) { enabled = false; return; }
                var cfg = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                if ((string)cfg["schema"] != "s1.quest-relay-config.1"
                    || cfg.Value<bool?>("enabled") != true || cfg.Value<bool?>("technical_only") != true)
                { enabled = false; return; }
                var uri = new Uri((string)cfg["endpoint"]);
                if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                    throw new InvalidDataException("Relay must be a bare HTTPS endpoint");
                endpoint = uri.AbsoluteUri.TrimEnd('/');
                bearer = (string)cfg["device_token"];
                deviceId = (string)cfg["device_id"];
                if (!Safe(deviceId)) throw new InvalidDataException("Missing or invalid device enrollment ID");
                if (string.IsNullOrWhiteSpace(bearer) || bearer.Length < 32 || bearer.Length > 250)
                    throw new InvalidDataException("Device token is not provisioned");
                configured = true;
                DeliveryState = "cloud_waiting_for_network";
            }
            catch (Exception ex)
            {
                DeliveryState = "cloud_config_error";
                Debug.LogWarning("[S1Cloud] Disabled: " + ex.GetType().Name);
                enabled = false;
            }
        }

        void Update()
        {
            if (!configured || busy || Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + RetrySeconds;
            if (Application.internetReachability == NetworkReachability.NotReachable) { DeliveryState = "cloud_queued_offline"; return; }
            StartCoroutine(SendQueued());
        }

        IEnumerator SendQueued()
        {
            busy = true;
            try
            {
                var queue = Path.Combine(root, "ExportQueue");
                if (!Directory.Exists(queue)) yield break;
                var jobs = Directory.GetFiles(queue, "*.json").OrderBy(n => n, StringComparer.Ordinal).ToArray();
                // A pending Google confirmation must not starve the next bundle.
                var ordered = jobs.Where(j => string.CompareOrdinal(j, lastBundleAttempt) > 0)
                    .Concat(jobs.Where(j => string.CompareOrdinal(j, lastBundleAttempt) <= 0));
                foreach (string jobPath in ordered)
                {
                    JObject job, manifest, complete;
                    string id, folder;
                    try
                    {
                        job = JObject.Parse(File.ReadAllText(jobPath, Encoding.UTF8));
                        id = (string)job["bundle_id"];
                        if (!Safe(id) || (string)job["schema_version"] != "researchflow.export_queue.1") continue;
                        folder = Path.Combine(root, "Exports", id);
                        if (File.Exists(Path.Combine(root, "CloudReceipts", id + ".json"))) continue;
                        if (!File.Exists(Path.Combine(folder, "COMPLETE.json"))) continue;
                        manifest = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json"), Encoding.UTF8));
                        complete = JObject.Parse(File.ReadAllText(Path.Combine(folder, "COMPLETE.json"), Encoding.UTF8));
                        if ((string)manifest["bundle_id"] != id || (string)complete["bundle_id"] != id
                            || (string)manifest["schema_version"] != "researchflow.device-bundle.1"
                            || manifest["identity"]?.Value<bool?>("test_data") != true
                            || !Names.All(n => manifest["files"]?[n] != null)) continue;
                    }
                    catch (Exception ex) { Debug.LogWarning("[S1Cloud] Invalid local export: " + ex.GetType().Name); continue; }
                    string baseUrl = endpoint + "/v1/bundles";
                    var init = new JObject
                    {
                        ["manifest_b64"] = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder, "manifest.json"))),
                        ["complete_b64"] = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(folder, "COMPLETE.json")))
                    };
                    yield return Send("POST", baseUrl, Encoding.UTF8.GetBytes(init.ToString(Newtonsoft.Json.Formatting.None)));
                    if (requestError != null || answer == null) { DeliveryState = "cloud_queued_retry"; yield break; }
                    JObject offsets = answer["offsets"] as JObject;
                    if (offsets == null) { DeliveryState = "cloud_queued_retry"; yield break; }
                    foreach (var name in Names)
                    {
                        string path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
                        long expected = manifest["files"][name].Value<long>("bytes");
                        if (!File.Exists(path) || new FileInfo(path).Length != expected) { DeliveryState = "cloud_local_invalid"; yield break; }
                        long offset = offsets.Value<long?>(name) ?? 0;
                        if (offset < 0 || offset > expected) { DeliveryState = "cloud_queued_retry"; yield break; }
                        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            input.Position = offset;
                            while (offset < expected)
                            {
                                var block = new byte[(int)Math.Min(ChunkBytes, expected - offset)];
                                if (input.Read(block, 0, block.Length) != block.Length) { DeliveryState = "cloud_local_invalid"; yield break; }
                                string chunkSha = Sha(block);
                                yield return Send("PUT", baseUrl + "/" + id + "/chunk", block, name, offset, chunkSha);
                                if (requestError != null || answer == null) { DeliveryState = "cloud_queued_retry"; yield break; }
                                long next = answer.Value<long?>("next_offset") ?? -1;
                                if (next < offset || next > expected) { DeliveryState = "cloud_queued_retry"; yield break; }
                                offset = next;
                                input.Position = offset; // safe idempotent retry on an interrupted HTTP response
                            }
                        }
                    }
                    yield return Send("POST", baseUrl + "/" + id + "/finalize", Array.Empty<byte>());
                    if (requestError != null || answer == null) { DeliveryState = "cloud_queued_retry"; yield break; }
                    string state = answer.Value<string>("state") ?? "";
                    if (state == "drive_verified" && answer.Value<bool?>("provider_upload_confirmed") == true)
                    {
                        var receiptDir = Path.Combine(root, "CloudReceipts");
                        Directory.CreateDirectory(receiptDir);
                        var receipt = Path.Combine(receiptDir, id + ".json");
                        if (!File.Exists(receipt)) File.WriteAllText(receipt, answer.ToString(), Encoding.UTF8);
                        DeliveryState = "cloud_verified";
                    }
                    else DeliveryState = "cloud_relay_only_not_on_drive";
                    // One bundle per pass, round-robin across unconfirmed bundles to avoid starvation.
                    lastBundleAttempt = jobPath;
                    yield break;
                }
            }
            finally { busy = false; }
        }

        IEnumerator Send(string method, string url, byte[] body, string fileName = null, long offset = 0, string digest = null)
        {
            requestError = null; answer = null;
            using (var request = new UnityWebRequest(url, method))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 40;
                request.SetRequestHeader("Authorization", "Bearer " + bearer);
                request.SetRequestHeader("X-Device-Id", deviceId);
                request.SetRequestHeader("Content-Type", method == "PUT" ? "application/octet-stream" : "application/json");
                if (fileName != null)
                {
                    request.SetRequestHeader("X-File", fileName);
                    request.SetRequestHeader("X-Offset", offset.ToString(CultureInfo.InvariantCulture));
                    request.SetRequestHeader("X-Chunk-SHA256", digest);
                }
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                    requestError = "Network or remote response: " + (int)request.responseCode;
                else
                    try { answer = JObject.Parse(request.downloadHandler.text); }
                    catch (Exception) { requestError = "Invalid relay response"; }
            }
        }

        static bool Safe(string id) => !string.IsNullOrEmpty(id) && id.Length <= 160 && id.All(c =>
            c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-');
        static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
