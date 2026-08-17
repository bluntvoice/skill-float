using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkillFloat
{
    internal static class SkillDiscovery
    {
        public static List<SkillItem> Discover(AliasStore aliases)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localRoot = Path.Combine(home, ".codex", "skills");
            var pluginRoot = Path.Combine(home, ".codex", "plugins", "cache");
            var found = new Dictionary<string, Tuple<int, SkillItem>>(StringComparer.OrdinalIgnoreCase);
            var hidden = new HashSet<string>(Storage.LoadHiddenSkills().skills, StringComparer.OrdinalIgnoreCase);

            Collect(localRoot, path =>
            {
                string name, description;
                if (!TryReadFrontmatter(path, out name, out description)) return;
                var relative = SafeRelative(localRoot, path);
                var depth = relative.Split(Path.DirectorySeparatorChar).Length;
                var source = path.StartsWith(Path.Combine(localRoot, ".system"), StringComparison.OrdinalIgnoreCase) ? "系统 Skill" : "本地 Skill";
                Add(found, name, depth, new SkillItem { Invocation = name, Name = name, Description = description, Source = source, SourcePath = path });
            });

            Collect(pluginRoot, path =>
            {
                string name, description;
                if (!TryReadFrontmatter(path, out name, out description)) return;
                var plugin = PluginName(pluginRoot, path);
                if (string.IsNullOrWhiteSpace(plugin)) return;
                var invocation = name.Contains(":") ? name : plugin + ":" + name;
                Add(found, invocation, 99, new SkillItem { Invocation = invocation, Name = name, Description = description, Source = "插件 · " + plugin, SourcePath = path });
            });

            var result = found.Values.Select(value => value.Item2).ToList();
            foreach (var skill in result)
            {
                skill.Hidden = hidden.Contains(skill.Invocation);
                AliasEntry entry;
                if (!aliases.skills.TryGetValue(skill.Invocation, out entry) || entry == null) continue;
                skill.DisplayName = entry.displayName ?? "";
                skill.LocalizedDescription = entry.localizedDescription ?? "";
                skill.Favorite = entry.favorite;
                skill.Category = entry.category ?? "";
                skill.Tags = entry.tags ?? new List<string>();
            }
            return result.OrderByDescending(skill => skill.Favorite).ThenBy(skill => skill.VisibleName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        private static void Add(Dictionary<string, Tuple<int, SkillItem>> found, string invocation, int depth, SkillItem item)
        {
            Tuple<int, SkillItem> existing;
            if (!found.TryGetValue(invocation, out existing) || depth < existing.Item1) found[invocation] = Tuple.Create(depth, item);
        }

        private static void Collect(string root, Action<string> visitor)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
                {
                    try { visitor(path); } catch { }
                }
            }
            catch { }
        }

        private static bool TryReadFrontmatter(string path, out string name, out string description)
        {
            name = "";
            description = "";
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); } catch { return false; }
            if (lines.Length == 0 || lines[0].Trim() != "---") return false;
            for (var index = 1; index < lines.Length && lines[index].Trim() != "---"; index++)
            {
                var line = lines[index].Trim();
                if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase)) name = TrimYaml(line.Substring(5));
                else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line.Substring(12).Trim();
                    if (value == ">" || value == ">-" || value == "|" || value == "|-")
                    {
                        var chunks = new List<string>();
                        while (++index < lines.Length && lines[index].Trim() != "---" && (string.IsNullOrWhiteSpace(lines[index]) || char.IsWhiteSpace(lines[index][0])))
                        {
                            var part = lines[index].Trim();
                            if (part.Length > 0) chunks.Add(part);
                        }
                        index--;
                        description = string.Join(value.StartsWith("|") ? Environment.NewLine : " ", chunks);
                    }
                    else description = TrimYaml(value);
                }
            }
            name = name.Trim();
            return name.Length > 0;
        }

        private static string TrimYaml(string value)
        {
            var text = value.Trim();
            if (text.Length >= 2 && ((text[0] == '"' && text[text.Length - 1] == '"') || (text[0] == '\'' && text[text.Length - 1] == '\'')))
                return text.Substring(1, text.Length - 2).Trim();
            return text;
        }

        private static string PluginName(string root, string path)
        {
            var parts = SafeRelative(root, path).Split(Path.DirectorySeparatorChar);
            if (parts.Length < 5 || !parts[3].Equals("skills", StringComparison.OrdinalIgnoreCase)) return "";
            return parts[1];
        }

        private static string SafeRelative(string root, string path)
        {
            var rootUri = new Uri(AppendSeparator(Path.GetFullPath(root)));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendSeparator(string value) => value.EndsWith(Path.DirectorySeparatorChar.ToString()) ? value : value + Path.DirectorySeparatorChar;
    }
}
