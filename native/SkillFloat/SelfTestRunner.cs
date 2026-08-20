using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace SkillFloat
{
    internal static class SelfTestRunner
    {
        public static int Run()
        {
            var root = Path.Combine(Path.GetTempPath(), "SkillFloat-SelfTest-" + Guid.NewGuid().ToString("N"));
            try
            {
                var local = Path.Combine(root, "local");
                var plugins = Path.Combine(root, "plugins");
                WriteSkill(Path.Combine(local, "alpha", "SKILL.md"), "alpha", "Alpha description");
                WriteSkill(Path.Combine(local, "duplicate", "SKILL.md"), "alpha", "Deeper duplicate");
                WriteSkill(Path.Combine(local, ".system", "system-one", "SKILL.md"), "system-one", "System skill");
                WriteSkill(Path.Combine(local, "invalid", "SKILL.md"), "bad name", "Invalid invocation");
                WriteSkill(Path.Combine(plugins, "provider", "github", "1.0.0", "skills", "repo", "SKILL.md"), "repo", "Repository helper");
                WriteSkill(Path.Combine(plugins, "provider", "github", "1.1.0", "skills", "repo", "SKILL.md"), "repo", "New repository helper");
                var aliases = new AliasStore();
                aliases.skills["alpha"] = new AliasEntry { displayName = "阿尔法助手", favorite = true, category = "开发与代码", tags = new List<string> { "测试" } };
                var skills = SkillDiscovery.DiscoverFromRoots(aliases, local, plugins, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (skills.Count != 3) return 11;
                if (skills.Any(skill => !SkillDiscovery.IsValidInvocation(skill.Invocation))) return 12;
                if (!skills.Any(skill => skill.Invocation == "github:repo" && skill.Source.StartsWith("插件"))) return 13;
                if (!skills.First(skill => skill.Invocation == "github:repo").Description.StartsWith("New")) return 21;
                skills.Add(new SkillItem { Invocation = "popular", Name = "popular", DisplayName = "alpha 高频助手", UsageCount = 100000 });
                var ranked = SkillSearchRanker.Rank(skills, "alpha").FirstOrDefault();
                if (ranked == null || ranked.Invocation != "alpha") return 14;
                if (SkillSearchRanker.Rank(skills, "不存在").Any()) return 15;
                var defaults = new AppSettings();
                if (defaults.autoClassifyNewSkills || !defaults.scanCodex || defaults.globalShortcut != "Alt+S") return 16;
                using (var fallback = new GlobalHotkeyManager(new IntPtr(1), 73, new FakeHotkeyRegistrar(2)))
                {
                    var result = fallback.Register("Alt+S");
                    if (!result.Success || result.DisplayName != "Alt+Shift+S") return 17;
                }
                using (var unavailable = new GlobalHotkeyManager(new IntPtr(1), 73, new FakeHotkeyRegistrar(-1)))
                {
                    var result = unavailable.Register("Alt+S");
                    if (result.Success || !result.Error.Contains("托盘")) return 18;
                }
                var windows = new FakeWindowInspector();
                var tracker = new FocusTargetTracker(windows, 99);
                windows.Set(new IntPtr(101), 1);
                tracker.ObserveForeground();
                windows.Set(new IntPtr(202), 99);
                tracker.ObserveForeground();
                if (tracker.Consume() != new IntPtr(101) || tracker.Consume() != IntPtr.Zero) return 19;
                string directory, reason;
                int contained;
                if (SkillFileManager.TryGetDeletableDirectory(skills.First(item => item.Source.StartsWith("插件")), out directory, out contained, out reason)) return 20;
                var localSkill = skills.First(item => item.Invocation == "alpha");
                WriteSkill(Path.Combine(Path.GetDirectoryName(localSkill.SourcePath), "nested", "SKILL.md"), "nested", "Nested skill");
                if (!SkillFileManager.TryGetDeletableDirectory(localSkill, local, out directory, out contained, out reason) || contained != 2) return 24;
                var protectedSystem = skills.First(item => item.Source == "系统 Skill");
                if (SkillFileManager.TryGetDeletableDirectory(protectedSystem, local, out directory, out contained, out reason)) return 25;
                var parsed = AiService.ParseSuggestionContent("说明文字```json\n{\"short_name\":\"测试助手\",\"description_zh\":\"用于测试\",\"category\":\"开发与代码\",\"tags\":[\"测试\"]}\n```", skills[0], true);
                if (parsed.shortName != "测试助手" || parsed.category != "开发与代码") return 22;
                try
                {
                    AiService.ParseSuggestionContent("没有 JSON", skills[0], true);
                    return 23;
                }
                catch (InvalidOperationException) { }
                using (var list = new RedrawListBox { Size = new Size(360, 180), DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed, ItemHeight = 48 })
                {
                    list.Items.Add("resize-test");
                    var handle = list.Handle;
                    list.Width = 900;
                    list.RedrawNow();
                    if (handle == IntPtr.Zero || list.ClientSize.Width < 850) return 26;
                }
                return 0;
            }
            catch { return 10; }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }

        private sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
        {
            private readonly int _successAttempt;
            private int _attempt;
            public FakeHotkeyRegistrar(int successAttempt) { _successAttempt = successAttempt; }
            public int LastError => 1409;
            public bool Register(IntPtr window, int id, int modifiers, int key) => ++_attempt == _successAttempt;
            public void Unregister(IntPtr window, int id) { }
        }

        private sealed class FakeWindowInspector : IWindowInspector
        {
            private readonly Dictionary<IntPtr, uint> _processes = new Dictionary<IntPtr, uint>();
            public IntPtr ForegroundWindow { get; private set; }
            public bool IsWindow(IntPtr window) => _processes.ContainsKey(window);
            public uint ProcessId(IntPtr window) => _processes.ContainsKey(window) ? _processes[window] : 0;
            public void Set(IntPtr window, uint processId) { ForegroundWindow = window; _processes[window] = processId; }
        }

        private static void WriteSkill(string path, string name, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "---\nname: " + name + "\ndescription: " + description + "\n---\n", new UTF8Encoding(false));
        }
    }
}
