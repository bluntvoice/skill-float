using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace SkillFloat
{
    internal static class UsageScanner
    {
        private static readonly object Gate = new object();
        private static readonly Regex DollarPattern = new Regex(@"\$([\p{L}\p{N}_:.\-]+)", RegexOptions.Compiled);
        private static readonly Regex TurnPattern = new Regex("\\\"turn_id\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.Compiled);
        private static readonly Regex ClaudeTurnPattern = new Regex("\\\"(?:promptId|uuid)\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.Compiled);
        private static readonly Regex ClaudeSkillPattern = new Regex("\\\"skill\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.Compiled);

        private sealed class HistoryFile
        {
            public string Key;
            public string Source;
            public string Path;
        }

        private sealed class Catalog
        {
            public readonly HashSet<string> Invocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<Tuple<string, string>> Paths = new List<Tuple<string, string>>();

            public Catalog(IEnumerable<SkillItem> skills)
            {
                foreach (var skill in skills)
                {
                    Invocations.Add(skill.Invocation);
                    if (!Names.ContainsKey(skill.Name) || skill.Invocation.IndexOf(':') < 0) Names[skill.Name] = skill.Invocation;
                    Paths.Add(Tuple.Create(NormalizePath(skill.SourcePath), skill.Invocation));
                }
            }

            public string MatchName(string value)
            {
                var cleaned = (value ?? "").Trim().Trim('$', '"', '\'', '`', ',', ';', '(', ')');
                if (Invocations.Contains(cleaned)) return Invocations.First(item => item.Equals(cleaned, StringComparison.OrdinalIgnoreCase));
                var suffix = cleaned.Contains(":") ? cleaned.Substring(cleaned.LastIndexOf(':') + 1) : cleaned;
                string invocation;
                return Names.TryGetValue(suffix, out invocation) ? invocation : "";
            }

            public IEnumerable<string> MatchPaths(string text)
            {
                var normalized = NormalizePath(text);
                foreach (var pair in Paths)
                    if (pair.Item1.Length > 8 && normalized.Contains(pair.Item1)) yield return pair.Item2;
                if (!normalized.Contains("skill.md")) yield break;
                var parts = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                for (var index = 0; index + 1 < parts.Length; index++)
                    if (parts[index + 1] == "skill.md")
                    {
                        var invocation = MatchName(parts[index]);
                        if (invocation.Length > 0) yield return invocation;
                    }
            }
        }

        public static UsageSummary Refresh(IList<SkillItem> skills, Action<int, int, string> progress, CancellationToken token)
        {
            lock (Gate)
            {
                var store = Normalize(Storage.LoadUsage());
                var catalog = new Catalog(skills);
                var files = DiscoverHistoryFiles();
                var active = new HashSet<string>(files.Select(file => file.Key), StringComparer.OrdinalIgnoreCase);
                foreach (var key in store.files.Keys.Where(key => !active.Contains(key)).ToList()) store.files.Remove(key);

                for (var index = 0; index < files.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var file = files[index];
                    FileCursor cursor;
                    if (!store.files.TryGetValue(file.Key, out cursor))
                    {
                        cursor = new FileCursor { source = file.Source, path = file.Path };
                        store.files[file.Key] = cursor;
                    }
                    ScanFile(file, cursor, catalog);
                    progress?.Invoke(index + 1, files.Count, file.Source);
                }

                store.last_refreshed_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Storage.SaveUsage(store);
                return Summarize(store, files);
            }
        }

        public static UsageSummary Current(IList<SkillItem> skills)
        {
            lock (Gate)
            {
                var store = Normalize(Storage.LoadUsage());
                return Summarize(store, DiscoverHistoryFiles());
            }
        }

        public static long RecordLocal(string invocation)
        {
            lock (Gate)
            {
                var store = Normalize(Storage.LoadUsage());
                long count;
                store.local_counts.TryGetValue(invocation, out count);
                store.local_counts[invocation] = count + 1;
                Storage.SaveUsage(store);
                return store.local_counts[invocation];
            }
        }

        private static UsageStore Normalize(UsageStore store)
        {
            if (store.local_counts == null) store.local_counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            else store.local_counts = new Dictionary<string, long>(store.local_counts, StringComparer.OrdinalIgnoreCase);
            if (store.files == null) store.files = new Dictionary<string, FileCursor>(StringComparer.OrdinalIgnoreCase);
            else store.files = new Dictionary<string, FileCursor>(store.files, StringComparer.OrdinalIgnoreCase);
            foreach (var cursor in store.files.Values)
            {
                cursor.counts = cursor.counts == null ? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, long>(cursor.counts, StringComparer.OrdinalIgnoreCase);
                if (cursor.seen_in_turn == null) cursor.seen_in_turn = new List<string>();
            }
            return store;
        }

        private static List<HistoryFile> DiscoverHistoryFiles()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var result = new Dictionary<string, HistoryFile>(StringComparer.OrdinalIgnoreCase);
            AddFiles(result, "Codex", new[] { Path.Combine(home, ".codex", "sessions"), Path.Combine(home, ".codex", "archived_sessions") }, false);
            AddFiles(result, "Claude Code", new[] { Path.Combine(home, ".claude", "projects"), Path.Combine(home, ".claude", "sessions") }, false);
            AddFiles(result, "OpenClaw", new[] { Path.Combine(home, ".openclaw"), Path.Combine(home, ".config", "openclaw") }, true);
            return result.Values.ToList();
        }

        private static void AddFiles(Dictionary<string, HistoryFile> result, string source, IEnumerable<string> roots, bool fullPathKey)
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var path in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                    {
                        var key = source + ":" + (fullPathKey ? NormalizePath(path) : Path.GetFileName(path));
                        result[key] = new HistoryFile { Key = key, Source = source, Path = path };
                    }
                }
                catch { }
            }
        }

        private static void ScanFile(HistoryFile file, FileCursor cursor, Catalog catalog)
        {
            FileInfo info;
            try { info = new FileInfo(file.Path); } catch { return; }
            var modified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
            if (info.Length < cursor.offset)
            {
                cursor.offset = 0;
                cursor.counts.Clear();
                cursor.current_turn = "";
                cursor.seen_in_turn.Clear();
            }
            if (info.Length == cursor.offset && modified == cursor.modified_ms) return;
            try
            {
                using (var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    stream.Seek(cursor.offset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true, 65536, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null) DetectLine(file.Source, line, cursor, catalog);
                    }
                    cursor.offset = stream.Length;
                }
                cursor.source = file.Source;
                cursor.path = file.Path;
                cursor.modified_ms = modified;
            }
            catch { }
        }

        private static void DetectLine(string source, string line, FileCursor cursor, Catalog catalog)
        {
            if (source == "Codex") DetectCodex(line, cursor, catalog);
            else if (source == "Claude Code") DetectClaude(line, cursor, catalog);
            else DetectOpenClaw(line, cursor, catalog);
        }

        private static void DetectCodex(string line, FileCursor cursor, Catalog catalog)
        {
            if (line.Contains("\"type\":\"turn_context\"")) SetTurn(cursor, Match(TurnPattern, line));
            if (line.Contains("user_message")) Register(cursor, DollarMatches(line, catalog));
            if (line.Contains("SKILL.md")) Register(cursor, catalog.MatchPaths(line));
        }

        private static void DetectClaude(string line, FileCursor cursor, Catalog catalog)
        {
            if (line.Contains("\"type\":\"user\""))
            {
                SetTurn(cursor, Match(ClaudeTurnPattern, line));
                Register(cursor, DollarMatches(line, catalog));
            }
            if (line.Contains("\"name\":\"Skill\""))
            {
                var invocation = catalog.MatchName(Match(ClaudeSkillPattern, line));
                if (invocation.Length > 0) Register(cursor, new[] { invocation });
            }
            if (line.Contains("SKILL.md")) Register(cursor, catalog.MatchPaths(line));
        }

        private static void DetectOpenClaw(string line, FileCursor cursor, Catalog catalog)
        {
            if (!line.Contains("$") && line.IndexOf("skill", StringComparison.OrdinalIgnoreCase) < 0) return;
            Register(cursor, DollarMatches(line, catalog).Concat(catalog.MatchPaths(line)));
        }

        private static IEnumerable<string> DollarMatches(string line, Catalog catalog)
        {
            foreach (Match match in DollarPattern.Matches(line))
            {
                var invocation = catalog.MatchName(match.Groups[1].Value);
                if (invocation.Length > 0) yield return invocation;
            }
        }

        private static string Match(Regex regex, string value)
        {
            var match = regex.Match(value);
            return match.Success ? match.Groups[1].Value : "";
        }

        private static void SetTurn(FileCursor cursor, string turn)
        {
            if (turn.Length == 0 || turn == cursor.current_turn) return;
            cursor.current_turn = turn;
            cursor.seen_in_turn.Clear();
        }

        private static void Register(FileCursor cursor, IEnumerable<string> invocations)
        {
            var seen = new HashSet<string>(cursor.seen_in_turn, StringComparer.OrdinalIgnoreCase);
            foreach (var invocation in invocations.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!seen.Add(invocation)) continue;
                long count;
                cursor.counts.TryGetValue(invocation, out count);
                cursor.counts[invocation] = count + 1;
            }
            cursor.seen_in_turn = seen.ToList();
        }

        private static UsageSummary Summarize(UsageStore store, IList<HistoryFile> currentFiles)
        {
            var summary = new UsageSummary();
            Action<string, string, long> add = (invocation, source, count) =>
            {
                long current;
                summary.Counts.TryGetValue(invocation, out current);
                summary.Counts[invocation] = current + count;
                Dictionary<string, long> sources;
                if (!summary.SourceCounts.TryGetValue(invocation, out sources)) summary.SourceCounts[invocation] = sources = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                sources.TryGetValue(source, out current);
                sources[source] = current + count;
            };
            foreach (var pair in store.local_counts) add(pair.Key, "Skill Float", pair.Value);
            foreach (var cursor in store.files.Values) foreach (var pair in cursor.counts) add(pair.Key, cursor.source, pair.Value);
            summary.Total = summary.Counts.Values.Sum();
            summary.UsedSkills = summary.Counts.Count(pair => pair.Value > 0);
            foreach (var name in new[] { "Skill Float", "Codex", "Claude Code", "OpenClaw" })
            {
                var files = name == "Skill Float" ? 0 : currentFiles.Count(file => file.Source == name);
                var count = summary.SourceCounts.Values.Sum(map => { long value; return map.TryGetValue(name, out value) ? value : 0; });
                summary.Sources.Add(new UsageSourceSummary { Name = name, Detected = name == "Skill Float" || files > 0, Files = files, Count = count });
            }
            return summary;
        }

        private static string NormalizePath(string value)
        {
            var normalized = (value ?? "").Replace('/', '\\').ToLowerInvariant();
            while (normalized.Contains("\\\\")) normalized = normalized.Replace("\\\\", "\\");
            return normalized;
        }
    }
}
