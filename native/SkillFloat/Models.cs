using System;
using System.Collections.Generic;

namespace SkillFloat
{
    internal sealed class SkillItem
    {
        public string Invocation { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string LocalizedDescription { get; set; } = "";
        public string Source { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public bool Favorite { get; set; }
        public string Category { get; set; } = "";
        public List<string> Tags { get; set; } = new List<string>();
        public long UsageCount { get; set; }
        public Dictionary<string, long> UsageSources { get; set; } = new Dictionary<string, long>();
        public string VisibleName => string.IsNullOrWhiteSpace(DisplayName) ? Name : DisplayName;
        public string VisibleDescription => string.IsNullOrWhiteSpace(LocalizedDescription) ? Description : LocalizedDescription;
    }

    internal sealed class AliasEntry
    {
        public string displayName { get; set; } = "";
        public string localizedDescription { get; set; } = "";
        public bool favorite { get; set; }
        public string category { get; set; } = "";
        public List<string> tags { get; set; } = new List<string>();
    }

    internal sealed class AliasStore
    {
        public Dictionary<string, AliasEntry> skills { get; set; } = new Dictionary<string, AliasEntry>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class TranslationSettings
    {
        public string endpoint { get; set; } = "https://api.openai.com/v1";
        public string model { get; set; } = "";
    }

    internal sealed class TranslationSuggestion
    {
        public string shortName { get; set; } = "";
        public string descriptionZh { get; set; } = "";
        public string category { get; set; } = "其他";
        public List<string> tags { get; set; } = new List<string>();
        public string engine { get; set; } = "ai";
        public string notice { get; set; } = "";
    }

    internal sealed class TranslationDraft
    {
        public string invocation { get; set; } = "";
        public TranslationSuggestion suggestion { get; set; } = new TranslationSuggestion();
        public long generatedAt { get; set; }
    }

    internal sealed class TranslationDraftStore
    {
        public Dictionary<string, TranslationDraft> drafts { get; set; } = new Dictionary<string, TranslationDraft>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class FileCursor
    {
        public string source { get; set; } = "";
        public string path { get; set; } = "";
        public long offset { get; set; }
        public long modified_ms { get; set; }
        public Dictionary<string, long> counts { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public string current_turn { get; set; } = "";
        public List<string> seen_in_turn { get; set; } = new List<string>();
    }

    internal sealed class UsageStore
    {
        public Dictionary<string, long> local_counts { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, FileCursor> files { get; set; } = new Dictionary<string, FileCursor>(StringComparer.OrdinalIgnoreCase);
        public long last_refreshed_at { get; set; }
    }

    internal sealed class UsageSourceSummary
    {
        public string Name { get; set; } = "";
        public bool Detected { get; set; }
        public int Files { get; set; }
        public long Count { get; set; }
    }

    internal sealed class UsageSummary
    {
        public long Total { get; set; }
        public int UsedSkills { get; set; }
        public List<UsageSourceSummary> Sources { get; set; } = new List<UsageSourceSummary>();
        public Dictionary<string, long> Counts { get; set; } = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, long>> SourceCounts { get; set; } = new Dictionary<string, Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
    }
}
