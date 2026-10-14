using System;
using System.Linq;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>
    /// Illustrated-format presentation (capture C04, design_rule §7 "large image, short caption"): a long reviewed caption is shown as
    /// sub-pages under the same image, one existing paragraph per sub-page with the caption's heading repeated. Text is never reworded,
    /// reordered or dropped: Join(Split(x)) == x, so facts, numbers and TH/JA equivalence stay those of the reviewed content.
    /// The split depends only on paragraph structure (blank lines), which is identical in TH and JA, so both languages get the same
    /// number of sub-pages. Captions with fewer than MinParagraphs paragraphs are shown whole.
    /// </summary>
    public static class ResearchCaptionParts
    {
        public const string Version = "S1_ILLUSTRATED_PARAGRAPH_PAGES_20261015";
        public const int MinParagraphs = 4;
        const string Break = "\n\n";

        public static (string heading, string[] parts) Split(string caption)
        {
            if (string.IsNullOrEmpty(caption)) return (null, new[] { caption ?? "" });
            var paragraphs = caption.Split(new[] { Break }, StringSplitOptions.None);
            if (paragraphs.Length < MinParagraphs || paragraphs.Any(p => p.Trim().Length == 0)) return (null, new[] { caption });
            return (paragraphs[0], paragraphs.Skip(1).ToArray());
        }

        public static int Count(string caption) => Split(caption).parts.Length;

        /// <summary>Inverse of Split; used by checks to prove the presentation keeps the exact reviewed text.</summary>
        public static string Join(string heading, string[] parts) =>
            heading == null ? string.Join(Break, parts) : heading + Break + string.Join(Break, parts);
    }
}
