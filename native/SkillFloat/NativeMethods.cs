using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SkillFloat
{
    internal static class NativeMethods
    {
        public const int WmHotkey = 0x0312;
        public const int WmClose = 0x0010;
        public const int WmShowSkillFloat = 0x8000 + 41;
        public const int WmShutdownSkillFloat = 0x8000 + 42;
        public const int ModAlt = 0x0001;
        public const int ModControl = 0x0002;
        public const int ModShift = 0x0004;
        public const uint KeyEventKeyUp = 0x0002;
        public const byte VkControl = 0x11;
        public const byte VkV = 0x56;
        public const int GwlExStyle = -20;
        public const int WsExToolWindow = 0x00000080;
        public static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int virtualKey);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")] public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
        [DllImport("user32.dll", SetLastError = true)] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll", SetLastError = true)] public static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

        public static void SendPaste()
        {
            keybd_event(VkControl, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, 0, UIntPtr.Zero);
            keybd_event(VkV, 0, KeyEventKeyUp, UIntPtr.Zero);
            keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
        }

        public static void BeginWindowDrag(Form form)
        {
            ReleaseCapture();
            SendMessage(form.Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }
    }
}
