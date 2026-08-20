using System;
using System.Collections.Generic;
using System.Linq;

namespace SkillFloat
{
    internal static class SkillSearchRanker
    {
        public static IEnumerable<SkillItem> Rank(IEnumerable<SkillItem> skills, string query)
        {
            var terms = (query ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return skills.OrderByDescending(item => item.Favorite)
                    .ThenByDescending(item => Math.Min(item.UsageCount, 1000))
                    .ThenBy(item => item.VisibleName, StringComparer.CurrentCultureIgnoreCase);

            return skills.Select(item => new { Item = item, Score = Score(item, terms) })
                .Where(value => value.Score >= 0)
                .OrderByDescending(value => value.Score)
                .ThenByDescending(value => value.Item.Favorite)
                .ThenByDescending(value => Math.Min(value.Item.UsageCount, 1000))
                .ThenBy(value => value.Item.VisibleName, StringComparer.CurrentCultureIgnoreCase)
                .Select(value => value.Item);
        }

        internal static long Score(SkillItem item, IEnumerable<string> terms)
        {
            long total = 0;
            foreach (var raw in terms)
            {
                var term = raw.ToLowerInvariant();
                var score = TermScore(item, term);
                if (score < 0) return -1;
                total += score;
            }
            if (item.Favorite) total += 50;
            total += Math.Min(item.UsageCount, 1000) / 20;
            return total;
        }

        private static long TermScore(SkillItem item, string term)
        {
            var invocation = Lower(item.Invocation);
            var display = Lower(item.DisplayName);
            var visible = Lower(item.VisibleName);
            if (invocation == term) return 100000;
            if (display.Length > 0 && display == term) return 90000;
            if (invocation.StartsWith(term)) return 80000;
            if (visible.StartsWith(term)) return 70000;
            if (visible.Contains(term)) return 60000;
            if ((item.Tags ?? new List<string>()).Any(tag => Lower(tag).Contains(term))) return 50000;
            if (Lower(item.Category).Contains(term)) return 40000;
            if (Lower(item.Name).Contains(term)) return 30000;
            if (Lower(item.VisibleDescription).Contains(term)) return 20000;
            if (Lower(item.Source).Contains(term)) return 10000;
            return -1;
        }

        private static string Lower(string value) => (value ?? "").ToLowerInvariant();
    }
}
