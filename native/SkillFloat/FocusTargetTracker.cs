using System;
using System.Diagnostics;

namespace SkillFloat
{
    internal interface IWindowInspector
    {
        IntPtr ForegroundWindow { get; }
        bool IsWindow(IntPtr window);
        uint ProcessId(IntPtr window);
    }

    internal sealed class NativeWindowInspector : IWindowInspector
    {
        public IntPtr ForegroundWindow => NativeMethods.GetForegroundWindow();
        public bool IsWindow(IntPtr window) => NativeMethods.IsWindow(window);
        public uint ProcessId(IntPtr window) { uint processId; NativeMethods.GetWindowThreadProcessId(window, out processId); return processId; }
    }

    internal sealed class FocusTargetTracker
    {
        private readonly uint _ownProcessId;
        private readonly IWindowInspector _windows;
        private IntPtr _lastExternal = IntPtr.Zero;

        public FocusTargetTracker() : this(new NativeWindowInspector(), (uint)Process.GetCurrentProcess().Id) { }

        internal FocusTargetTracker(IWindowInspector windows, uint ownProcessId)
        {
            _windows = windows;
            _ownProcessId = ownProcessId;
        }

        public IntPtr LastExternal => IsValidExternal(_lastExternal) ? _lastExternal : IntPtr.Zero;

        public void ObserveForeground()
        {
            Remember(_windows.ForegroundWindow);
        }

        public void Remember(IntPtr window)
        {
            if (IsValidExternal(window)) _lastExternal = window;
        }

        public IntPtr CaptureImmediate()
        {
            var current = _windows.ForegroundWindow;
            Remember(current);
            return LastExternal;
        }

        public IntPtr Consume()
        {
            var value = LastExternal;
            _lastExternal = IntPtr.Zero;
            return value;
        }

        private bool IsValidExternal(IntPtr window)
        {
            if (window == IntPtr.Zero || !_windows.IsWindow(window)) return false;
            var processId = _windows.ProcessId(window);
            return processId != 0 && processId != _ownProcessId;
        }
    }
}
