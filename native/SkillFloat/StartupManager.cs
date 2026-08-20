using System;
using Microsoft.Win32;

namespace SkillFloat
{
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "SkillFloat";

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(ValueName, "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --startup", RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
