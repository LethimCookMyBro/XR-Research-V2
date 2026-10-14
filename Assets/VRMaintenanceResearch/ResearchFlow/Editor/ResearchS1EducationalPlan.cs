#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace VRMaintenanceResearch.ResearchFlow.Editor
{
    internal static class ResearchS1EducationalPlan
    {
        internal const string ContentSha = "477301a25e4135560e94bd732200b08e221da4a43a871416779a6a5a9eed7808";
        internal static readonly string[] Sections = { "A.overview", "A.requirements", "A.parts", "A.slots", "A.ram", "A.ssd", "A.gpu", "A.inspect",
            "B.overview", "B.symptom", "B.causes", "B.observations", "B.access", "B.correction", "B.reassembly", "B.inspect" };
        internal sealed class FileIdentity { public string path { get; set; } public string sha256 { get; set; } public long bytes { get; set; } }
        internal sealed class Model { public string role { get; set; } public FileIdentity original { get; set; } public FileIdentity imported_model_prefab { get; set; } public FileIdentity owned_model_prefab { get; set; } }
        internal sealed class Fact { public string fact_id { get; set; } public string text { get; set; } }
        internal sealed class Caption { public string text { get; set; } public string sha256 { get; set; } public Fact[] facts { get; set; } }
        internal sealed class Chapter { public string task_id { get; set; } public string section_id { get; set; } public string capture_brief { get; set; } public int chapter_index { get; set; } public string[] fact_ids { get; set; } public Dictionary<string, Caption> captions { get; set; } }
        internal sealed class Contract { public string schema { get; set; } public string content_sha256 { get; set; } public int reviewed_fact_count { get; set; } public int required_images { get; set; } public int required_movies { get; set; } public int chapters_per_movie { get; set; } public Model[] source_models { get; set; } public Chapter[] chapters { get; set; } }
        internal sealed class Page { public Chapter Chapter; public string FactId, Text, Title; public int Part, Parts; }
        internal readonly struct AssemblyMotion
        {
            internal readonly double DepthMm, Fastener, DataDepthMm, PowerDepthMm;
            internal AssemblyMotion(double depth, double fastener, double data, double power) { DepthMm = depth; Fastener = fastener; DataDepthMm = data; PowerDepthMm = power; }
        }
        internal readonly struct FanAssemblyMotion
        {
            internal readonly double BladeDepthMm, BladeTurn, SpinnerClearanceMm, SpinnerProgress;
            internal FanAssemblyMotion(double bladeDepth, double bladeTurn, double spinnerClearance, double spinnerProgress)
            { BladeDepthMm = bladeDepth; BladeTurn = bladeTurn; SpinnerClearanceMm = spinnerClearance; SpinnerProgress = spinnerProgress; }
        }

        internal static string Sha(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        internal static string Sha(string text) => Sha(new UTF8Encoding(false, true).GetBytes(text));
        internal static string FileSha(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static void Verify(FileIdentity file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.path) || !Path.IsPathRooted(file.path) || !File.Exists(file.path) ||
                !IsSha(file.sha256) || new FileInfo(file.path).Length != file.bytes || FileSha(file.path) != file.sha256)
                throw new InvalidDataException("Source model identity mismatch: " + (file == null ? "null" : file.path));
        }
        internal static bool IsSha(string value) => value != null && value.Length == 64 && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        internal static Contract Load(string path, string expectedSha)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !IsSha(expectedSha)) throw new ArgumentException("Use an absolute table path and exact SHA256.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 512 * 1024 || Sha(bytes) != expectedSha) throw new InvalidDataException("Caption table hash or size mismatch.");
            var contract = JsonConvert.DeserializeObject<Contract>(new UTF8Encoding(false, true).GetString(bytes));
            Validate(contract);
            foreach (var model in contract.source_models)
                foreach (var file in new[] { model.original, model.imported_model_prefab, model.owned_model_prefab }.Where(f => f != null)) Verify(file);
            return contract;
        }
        internal static void Validate(Contract contract)
        {
            if (contract == null || contract.schema != "s1.runtime-media.acquisition-contract.1" || contract.content_sha256 != ContentSha ||
                contract.reviewed_fact_count != 63 || contract.required_images != 16 || contract.required_movies != 4 || contract.chapters_per_movie != 8 ||
                contract.chapters == null || contract.chapters.Length != 16 || contract.source_models == null || contract.source_models.Length != 19 ||
                contract.source_models.Any(m => m == null || string.IsNullOrWhiteSpace(m.role) || m.imported_model_prefab == null && m.owned_model_prefab == null))
                throw new InvalidDataException("Expected the frozen S1 acquisition contract with 63 facts, 16 sections and 19 model records.");
            var factIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < contract.chapters.Length; i++)
            {
                var chapter = contract.chapters[i];
                if (chapter == null || chapter.section_id != Sections[i] || chapter.task_id != Sections[i].Substring(0, 1) ||
                    chapter.chapter_index != i % 8 + 1 || chapter.fact_ids == null || chapter.fact_ids.Length == 0 ||
                    chapter.captions == null || !chapter.captions.Keys.OrderBy(k => k).SequenceEqual(new[] { "ja", "th" }))
                    throw new InvalidDataException("Chapter order, language or identity mismatch at " + Sections[i]);
                foreach (string id in chapter.fact_ids)
                    if (!id.StartsWith(chapter.section_id + ".", StringComparison.Ordinal) || !factIds.Add(id)) throw new InvalidDataException("Duplicate or foreign fact " + id);
                foreach (var language in new[] { "th", "ja" })
                {
                    var caption = chapter.captions[language];
                    if (caption == null || string.IsNullOrEmpty(caption.text) || !IsSha(caption.sha256) || Sha(caption.text) != caption.sha256 ||
                        caption.facts == null || caption.facts.Any(f => f == null || string.IsNullOrWhiteSpace(f.text)) ||
                        !caption.facts.Select(f => f.fact_id).SequenceEqual(chapter.fact_ids) ||
                        caption.text != caption.text.Split(new[] { "\n\n" }, StringSplitOptions.None)[0] + "\n\n" + string.Join("\n\n", caption.facts.Select(f => f.text)))
                        throw new InvalidDataException("Caption coverage or text hash mismatch at " + chapter.section_id + "/" + language);
                }
            }
            if (factIds.Count != 63) throw new InvalidDataException("Reviewed fact coverage mismatch.");
        }
        internal static List<Page> Pages(Contract contract, string task, string language, Func<string, bool> fits)
        {
            if ((task != "A" && task != "B") || (language != "th" && language != "ja") || fits == null) throw new ArgumentException("Use task A/B, language th/ja and an actual text measurement.");
            var pages = new List<Page>();
            foreach (var chapter in contract.chapters.Where(c => c.task_id == task))
                foreach (var fact in chapter.captions[language].facts)
                {
                    var parts = Split(fact.text, fits);
                    for (int i = 0; i < parts.Count; i++) pages.Add(new Page { Chapter = chapter, FactId = fact.fact_id, Text = parts[i],
                        Title = chapter.captions[language].text.Split('\n')[0], Part = i + 1, Parts = parts.Count });
                }
            return pages;
        }
        internal static List<string> Split(string text, Func<string, bool> fits)
        {
            if (string.IsNullOrWhiteSpace(text) || fits == null) throw new ArgumentException("Nonempty caption and measurement required.");
            var parts = new List<string>();
            for (int next = 0; next < text.Length;)
            {
                if (char.IsHighSurrogate(text[next]))
                {
                    if (next + 1 >= text.Length || !char.IsLowSurrogate(text[next + 1])) throw new InvalidDataException("Caption has an unpaired surrogate.");
                    next += 2;
                }
                else { if (char.IsLowSurrogate(text[next])) throw new InvalidDataException("Caption has an unpaired surrogate."); next++; }
            }
            var textElements = StringInfo.ParseCombiningCharacters(text).Skip(1).Concat(new[] { text.Length }).ToArray();
            for (int offset = 0; offset < text.Length;)
            {
                var boundaries = textElements.Where(next => next > offset).Select(next => next - offset).ToList();
                int low = 0, high = boundaries.Count;
                while (low < high)
                {
                    int middle = low + (high - low + 1) / 2;
                    if (fits(text.Substring(offset, boundaries[middle - 1]))) low = middle;
                    else high = middle - 1;
                }
                int length = low == 0 ? 0 : boundaries[low - 1];
                if (length == 0) throw new InvalidDataException("A caption glyph does not fit the existing Game View.");
                if (length < text.Length - offset)
                {
                    int space = text.LastIndexOf(' ', offset + length - 1, length);
                    if (space >= offset + length / 2 && boundaries.Contains(space - offset + 1)) length = space - offset + 1;
                }
                string part = text.Substring(offset, length);
                if (!fits(part)) throw new InvalidDataException("Caption slice exceeds its measured panel.");
                parts.Add(part); offset += length;
            }
            if (string.Concat(parts) != text) throw new InvalidDataException("Caption slicing lost text.");
            return parts;
        }
        internal static double Ramp(double value, double start, double end)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1 || start < 0 || end > 1 || start >= end)
                throw new ArgumentOutOfRangeException(nameof(value));
            return Math.Max(0, Math.Min(1, (value - start) / (end - start)));
        }
        internal static double Insert(double value)
        {
            return 1 - Ramp(value, .10, .40) + Ramp(value, .70, .95);
        }
        internal static double RamDepth(int fact, double time)
        {
            if (fact < 0 || fact > 2) throw new ArgumentOutOfRangeException(nameof(fact));
            return fact == 1 ? 35 - 20 * Ramp(time, .1, .25) - 15 * Ramp(time, .4, .5) + 35 * Ramp(time, .75, .95) : 35 * Insert(time);
        }
        internal static AssemblyMotion Ssd(int fact, double time)
        {
            if (fact < 0 || fact > 3) throw new ArgumentOutOfRangeException(nameof(fact));
            if (fact == 0) return new AssemblyMotion(40 * (1 - Ramp(time, .1, .35) + Ramp(time, .88, 1)), Ramp(time, .4, .55) - Ramp(time, .7, .85), 30, 30);
            if (fact == 3) return new AssemblyMotion(40 * Ramp(time, .7, .95), 1 - Ramp(time, .4, .65), 30 * Ramp(time, .05, .2), 30 * Ramp(time, .2, .35));
            return new AssemblyMotion(0, 1, 30 * (1 - Ramp(time, .15, .35) + Ramp(time, .7, .8)), 30 * (1 - Ramp(time, .45, .65) + Ramp(time, .8, .95)));
        }
        internal static AssemblyMotion Gpu(int fact, double time)
        {
            if (fact < 0 || fact > 3) throw new ArgumentOutOfRangeException(nameof(fact));
            if (fact == 0) return new AssemblyMotion(35 * (1 - Ramp(time, .1, .35) + Ramp(time, .9, 1)), Ramp(time, .4, .55) - Ramp(time, .7, .85), 0, 35);
            if (fact == 1) return new AssemblyMotion(0, 1, 0, 35 * (1 - Ramp(time, .15, .4) + Ramp(time, .7, .9)));
            if (fact == 2) return new AssemblyMotion(35 * Ramp(time, .75, .95), 1 - Ramp(time, .4, .65), 0, 35 * Ramp(time, .1, .3));
            return new AssemblyMotion(0, 1, 0, 0);
        }
        internal static FanAssemblyMotion BladeRepair(double time) => new FanAssemblyMotion(25 * (1 - Ramp(time, .2, .4)), 12 * (1 - Ramp(time, .05, .15)),
            200 * (1 - Ramp(time, .4, .5)), Ramp(time, .5, .8));
        internal static FanAssemblyMotion FanReassembly(double time) => new FanAssemblyMotion(100 * (1 - Ramp(time, .1, .3)), 0,
            200 * (1 - Ramp(time, .3, .35)), Ramp(time, .35, .6));
    }
}
#endif
