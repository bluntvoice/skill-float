using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SkillFloat
{
    internal interface IHotkeyRegistrar
    {
        bool Register(IntPtr window, int id, int modifiers, int key);
        void Unregister(IntPtr window, int id);
        int LastError { get; }
    }

    internal sealed class NativeHotkeyRegistrar : IHotkeyRegistrar
    {
        public int LastError { get; private set; }
        public bool Register(IntPtr window, int id, int modifiers, int key)
        {
            var success = NativeMethods.RegisterHotKey(window, id, modifiers, key);
            LastError = success ? 0 : Marshal.GetLastWin32Error();
            return success;
        }
        public void Unregister(IntPtr window, int id) => NativeMethods.UnregisterHotKey(window, id);
    }

    internal sealed class HotkeyChoice
    {
        public string Name { get; private set; }
        public int Modifiers { get; private set; }
        public Keys Key { get; private set; }

        private HotkeyChoice(string name, int modifiers, Keys key)
        {
            Name = name;
            Modifiers = modifiers;
            Key = key;
        }

        public static readonly HotkeyChoice[] Supported =
        {
            new HotkeyChoice("Alt+S", NativeMethods.ModAlt, Keys.S),
            new HotkeyChoice("Alt+Shift+S", NativeMethods.ModAlt | NativeMethods.ModShift, Keys.S),
            new HotkeyChoice("Ctrl+Alt+S", NativeMethods.ModControl | NativeMethods.ModAlt, Keys.S),
            new HotkeyChoice("Ctrl+Shift+S", NativeMethods.ModControl | NativeMethods.ModShift, Keys.S)
        };

        public static HotkeyChoice Find(string name)
        {
            return Supported.FirstOrDefault(item => item.Name.Equals(name ?? "", StringComparison.OrdinalIgnoreCase)) ?? Supported[0];
        }

        public override string ToString() => Name;
    }

    internal sealed class GlobalHotkeyManager : IDisposable
    {
        private readonly IntPtr _window;
        private readonly int _id;
        private readonly IHotkeyRegistrar _registrar;
        private bool _registered;

        public GlobalHotkeyManager(IntPtr window, int id, IHotkeyRegistrar registrar = null)
        {
            _window = window;
            _id = id;
            _registrar = registrar ?? new NativeHotkeyRegistrar();
        }

        public HotkeyRegistration Register(string preferred)
        {
            Unregister();
            var requested = HotkeyChoice.Find(preferred);
            var candidates = new List<HotkeyChoice> { requested };
            candidates.AddRange(HotkeyChoice.Supported.Where(item => !item.Name.Equals(requested.Name, StringComparison.OrdinalIgnoreCase)));
            var errors = new List<string>();
            foreach (var candidate in candidates)
            {
                if (_registrar.Register(_window, _id, candidate.Modifiers, (int)candidate.Key))
                {
                    _registered = true;
                    return new HotkeyRegistration { Success = true, DisplayName = candidate.Name };
                }
                errors.Add(candidate.Name + "（错误 " + _registrar.LastError + "）");
            }
            return new HotkeyRegistration
            {
                Success = false,
                Error = "全局快捷键注册失败：" + string.Join("、", errors) + "。仍可从托盘打开。"
            };
        }

        public void Unregister()
        {
            if (!_registered) return;
            _registrar.Unregister(_window, _id);
            _registered = false;
        }

        public void Dispose() => Unregister();
    }
}
