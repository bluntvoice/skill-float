using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace SkillFloat
{
    internal static class Storage
    {
        private const string LegacyCredentialTarget = "translation-api-key.com.bluntvoice.skill-float";
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 120 };
        public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkillFloat", "UserData");
        public static readonly string LegacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.bluntvoice.skillfloat");
        public static string AliasesPath => Path.Combine(Root, "aliases.json");
        public static string SettingsPath => Path.Combine(Root, "translation-settings.json");
        public static string DraftsPath => Path.Combine(Root, "translation-drafts.json");
        public static string UsagePath => Path.Combine(Root, "usage-stats.json");
        private static string ApiKeyPath => Path.Combine(Root, "api-key.bin");

        public static void MigrateLegacyData()
        {
            Directory.CreateDirectory(Root);
            foreach (var file in new[] { "aliases.json", "translation-settings.json", "translation-drafts.json", "usage-stats.json" })
            {
                var source = Path.Combine(LegacyRoot, file);
                var destination = Path.Combine(Root, file);
                if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination, false);
            }
            if (!File.Exists(ApiKeyPath))
            {
                var legacyKey = CredentialStore.Read(LegacyCredentialTarget);
                if (!string.IsNullOrWhiteSpace(legacyKey)) SaveApiKey(legacyKey);
            }
        }

        public static T Read<T>(string path, Func<T> fallback) where T : class
        {
            try
            {
                if (!File.Exists(path)) return fallback();
                return Json.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8)) ?? fallback();
            }
            catch { return fallback(); }
        }

        public static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Root);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                var backup = path + ".bak";
                File.Replace(temporary, path, backup, true);
                if (File.Exists(backup)) File.Delete(backup);
            }
            else File.Move(temporary, path);
        }

        public static AliasStore LoadAliases()
        {
            var store = Read(AliasesPath, () => new AliasStore());
            if (store.skills == null) store.skills = new Dictionary<string, AliasEntry>(StringComparer.OrdinalIgnoreCase);
            return store;
        }

        public static void SaveAliases(AliasStore value) => Write(AliasesPath, value);
        public static TranslationSettings LoadSettings() => Read(SettingsPath, () => new TranslationSettings());
        public static void SaveSettings(TranslationSettings value) => Write(SettingsPath, value);
        public static TranslationDraftStore LoadDrafts() => Read(DraftsPath, () => new TranslationDraftStore());
        public static void SaveDrafts(TranslationDraftStore value) => Write(DraftsPath, value);
        public static UsageStore LoadUsage() => Read(UsagePath, () => new UsageStore());
        public static void SaveUsage(UsageStore value) => Write(UsagePath, value);

        public static string LoadApiKey()
        {
            try
            {
                if (!File.Exists(ApiKeyPath)) return "";
                var encrypted = File.ReadAllBytes(ApiKeyPath);
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));
            }
            catch { return ""; }
        }

        public static void SaveApiKey(string value)
        {
            Directory.CreateDirectory(Root);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (File.Exists(ApiKeyPath)) File.Delete(ApiKeyPath);
                return;
            }
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(ApiKeyPath, encrypted);
        }
    }

    internal static class CredentialStore
    {
        private const int CredTypeGeneric = 1;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr);
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        public static string Read(string target)
        {
            IntPtr pointer;
            if (!CredRead(target, CredTypeGeneric, 0, out pointer)) return "";
            try
            {
                var credential = (Credential)Marshal.PtrToStructure(pointer, typeof(Credential));
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize <= 0) return "";
                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            finally { CredFree(pointer); }
        }
    }
}
