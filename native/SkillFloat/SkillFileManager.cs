using System;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace SkillFloat
{
    internal static class SkillFileManager
    {
        public static bool TryGetDeletableDirectory(SkillItem skill, out string directory, out int containedSkills, out string reason)
        {
            var localRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "skills");
            return TryGetDeletableDirectory(skill, localRoot, out directory, out containedSkills, out reason);
        }

        internal static bool TryGetDeletableDirectory(SkillItem skill, string allowedLocalRoot, out string directory, out int containedSkills, out string reason)
        {
            directory = "";
            containedSkills = 0;
            reason = "";
            if (skill == null || !skill.Source.Equals("本地 Skill", StringComparison.OrdinalIgnoreCase))
            {
                reason = "仅用户本地 Skill 可移入回收站。";
                return false;
            }
            try
            {
                var localRoot = Path.GetFullPath(allowedLocalRoot);
                var systemRoot = Path.GetFullPath(Path.Combine(localRoot, ".system"));
                var skillFile = Path.GetFullPath(skill.SourcePath ?? "");
                if (!Path.GetFileName(skillFile).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Skill 来源文件不是 SKILL.md，已拒绝删除。";
                    return false;
                }
                directory = Path.GetDirectoryName(skillFile) ?? "";
                var localPrefix = AppendSeparator(localRoot);
                var systemPrefix = AppendSeparator(systemRoot);
                if (directory.Length == 0
                    || directory.Equals(localRoot, StringComparison.OrdinalIgnoreCase)
                    || !AppendSeparator(directory).StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase)
                    || directory.Equals(systemRoot, StringComparison.OrdinalIgnoreCase)
                    || AppendSeparator(directory).StartsWith(systemPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "目标不在允许的本地 Skill 目录内，已拒绝删除。";
                    directory = "";
                    return false;
                }
                if (!File.Exists(skillFile) || !Directory.Exists(directory))
                {
                    reason = "Skill 目录已不存在，请刷新后重试。";
                    directory = "";
                    return false;
                }
                if (ContainsReparsePoint(localRoot, directory) || ContainsReparsePointInTree(directory))
                {
                    reason = "Skill 目录包含符号链接或目录联接。为避免越界删除，已拒绝操作。";
                    directory = "";
                    return false;
                }
                containedSkills = Directory.GetFiles(directory, "SKILL.md", System.IO.SearchOption.AllDirectories).Length;
                return true;
            }
            catch (Exception error)
            {
                reason = "无法验证 Skill 目录：" + error.Message;
                directory = "";
                return false;
            }
        }

        public static void MoveToRecycleBin(string directory)
        {
            FileSystem.DeleteDirectory(directory, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        }

        private static bool ContainsReparsePoint(string root, string directory)
        {
            var current = new DirectoryInfo(directory);
            var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            while (current != null && current.FullName.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                if (current.FullName.Equals(rootPath, StringComparison.OrdinalIgnoreCase)) break;
                current = current.Parent;
            }
            return false;
        }

        private static bool ContainsReparsePointInTree(string directory)
        {
            var pending = new System.Collections.Generic.Stack<DirectoryInfo>();
            pending.Push(new DirectoryInfo(directory));
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var item in current.EnumerateFileSystemInfos())
                {
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return true;
                    var child = item as DirectoryInfo;
                    if (child != null) pending.Push(child);
                }
            }
            return false;
        }

        private static string AppendSeparator(string value)
        {
            return value.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? value
                : value + Path.DirectorySeparatorChar;
        }
    }
}
