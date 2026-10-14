using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    /// <summary>
    /// Reference tab set. Each id becomes a tab, a localized label ("reference." + id) and the
    /// INFORMATION_OPEN/CLOSE target ("reference." + id), so the logs record first format opened,
    /// open duration per format, and switches/returns without extra fields.
    /// </summary>
    public static class ResearchReferenceScheme
    {
        // 24 Sep 2026 progress slides 3-4 ("Proposed design based on supervisor discussion, 22 Sep 2026"):
        // participants choose when to open information and which of four FORMATS to use.
        public static readonly string[] Formats = { "user_manual", "step_by_step", "illustrated", "video" };
        // 21-22 Sep implementation (ResearchFlowImplementationHandoff section 11): four content CATEGORIES.
        public static readonly string[] Categories = { "task_info", "component_guide", "manual", "troubleshooting" };

        // ponytail: one build-wide switch; bind a per-task list through ResearchTaskPort if tasks ever differ.
        public static string[] Active { get; set; } = Formats;
        public static string Name => Active == Formats ? "formats_20260924" : Active == Categories ? "categories_20260922" : "custom";
        public static bool Contains(string id) => Array.IndexOf(Active, id) >= 0;
    }
}
