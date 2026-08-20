using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SkillFloat
{
    internal static class Program
    {
        private const string MutexName = "Local\\SkillFloat.Native.Singleton";
        internal const string ShutdownEventName = "Local\\SkillFloat.Native.Shutdown";
        internal const string ShowEventName = "Local\\SkillFloat.Native.Show";
        internal static bool QaMode { get; private set; }
        internal static bool StartHidden { get; private set; }

        [STAThread]
        private static int Main(string[] args)
        {
            QaMode = args.Any(value => value.Equals("--qa", StringComparison.OrdinalIgnoreCase));
            StartHidden = args.Any(value => value.Equals("--startup", StringComparison.OrdinalIgnoreCase));
            if (args.Any(value => value.Equals("--migrate-before-upgrade", StringComparison.OrdinalIgnoreCase)))
            {
                try { Storage.MigrateLegacyData(); return 0; }
                catch { return 1; }
            }
            if (args.Any(value => value.Equals("--shutdown", StringComparison.OrdinalIgnoreCase)))
            {
                using (var shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownEventName)) shutdownEvent.Set();
                NativeMethods.PostMessage(NativeMethods.HwndBroadcast, NativeMethods.WmShutdownSkillFloat, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }
            if (args.Any(value => value.Equals("--self-test", StringComparison.OrdinalIgnoreCase))) return SelfTestRunner.Run();

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    if (StartHidden) return 0;
                    var foreground = NativeMethods.GetForegroundWindow();
                    using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName)) showEvent.Set();
                    NativeMethods.PostMessage(NativeMethods.HwndBroadcast, NativeMethods.WmShowSkillFloat, foreground, IntPtr.Zero);
                    return 0;
                }
                Storage.MigrateLegacyData();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            return 0;
        }

    }
}
