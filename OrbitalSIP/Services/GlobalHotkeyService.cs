using System;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Low-level keyboard hook that fires hotkey actions even when the
    /// application window is not focused.
    ///
    /// Default hotkeys (configurable via ApplySettings):
    ///   Ctrl+M  –  MuteToggleRequested
    ///   Ctrl+H  –  HoldToggleRequested
    ///   Escape  –  HangupPressed  (hangup / decline)
    ///   Enter   –  AnswerPressed  (answer incoming)
    ///
    /// Key string format accepted by ParseHotkey:
    ///   "Ctrl+M", "Ctrl+F5", "Escape", "Enter", "F5", "A" …
    /// </summary>
    public sealed class GlobalHotkeyService : IDisposable
    {
        // ── Win32 ─────────────────────────────────────────────────────
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN     = 0x0100;
        private const int WM_SYSKEYDOWN  = 0x0104;
        private const int VK_CONTROL     = 0x11;
        private const int VK_MENU        = 0x12;  // Alt

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint   vkCode;
            public uint   scanCode;
            public uint   flags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
                                                       IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        // ── Hotkey binding ────────────────────────────────────────────
        private readonly struct HotkeyBinding
        {
            public readonly bool Ctrl;
            public readonly bool Alt;
            public readonly int  VkCode;
            public HotkeyBinding(bool ctrl, bool alt, int vk) { Ctrl = ctrl; Alt = alt; VkCode = vk; }
        }

        private HotkeyBinding? _bindMute;
        private HotkeyBinding? _bindHold;
        private HotkeyBinding? _bindHangup;
        private HotkeyBinding? _bindAnswer;

        // ── State ─────────────────────────────────────────────────────
        private IntPtr                _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;   // GC guard

        // ── Events ────────────────────────────────────────────────────
        public event EventHandler? MuteToggleRequested;
        public event EventHandler? HoldToggleRequested;
        public event EventHandler? HangupPressed;
        public event EventHandler? AnswerPressed;

        // ── Public API ────────────────────────────────────────────────
        public void ApplySettings(SipSettings s)
        {
            _bindMute   = ParseHotkey(s.HotkeyMute);
            _bindHold   = ParseHotkey(s.HotkeyHold);
            _bindHangup = ParseHotkey(s.HotkeyHangup);
            _bindAnswer = ParseHotkey(s.HotkeyAnswer);
        }

        public void Start()
        {
            if (_hookHandle != IntPtr.Zero) return;
            _proc = HookCallback;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        }

        public void Stop()
        {
            if (_hookHandle == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        public void Dispose() => Stop();

        // ── Key string parser ─────────────────────────────────────────
        /// <summary>
        /// Returns true if the key combo string is parseable (e.g. "Ctrl+M", "Escape").
        /// </summary>
        public static bool IsValidHotkey(string? s) => ParseHotkey(s).HasValue;

        /// <summary>
        /// Parses a key combo string such as "Ctrl+M", "Escape", "F5".
        /// Returns null if the string is unrecognised.
        /// </summary>
        private static HotkeyBinding? ParseHotkey(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim();

            bool ctrl = false;
            bool alt  = false;
            if (s.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
                s = s[5..];
            }
            else if (s.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                s = s[4..];
            }

            int vk = s.ToUpperInvariant() switch
            {
                "ESCAPE" or "ESC"   => 0x1B,
                "ENTER" or "RETURN" => 0x0D,
                "SPACE"             => 0x20,
                "F1"  => 0x70, "F2"  => 0x71, "F3"  => 0x72, "F4"  => 0x73,
                "F5"  => 0x74, "F6"  => 0x75, "F7"  => 0x76, "F8"  => 0x77,
                "F9"  => 0x78, "F10" => 0x79, "F11" => 0x7A, "F12" => 0x7B,
                _ when s.Length == 1 && char.IsLetter(s[0]) => char.ToUpper(s[0]),
                _ => -1
            };

            return vk == -1 ? null : new HotkeyBinding(ctrl, alt, vk);
        }

        // ── Hook callback ─────────────────────────────────────────────
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                var kbd  = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool alt  = (GetAsyncKeyState(VK_MENU)    & 0x8000) != 0;
                int  vk   = (int)kbd.vkCode;

                EventHandler? handler = null;

                if      (Matches(_bindMute,   ctrl, alt, vk)) handler = MuteToggleRequested;
                else if (Matches(_bindHold,   ctrl, alt, vk)) handler = HoldToggleRequested;
                else if (Matches(_bindHangup, ctrl, alt, vk)) handler = HangupPressed;
                else if (Matches(_bindAnswer, ctrl, alt, vk)) handler = AnswerPressed;

                if (handler != null)
                    Dispatcher.UIThread.InvokeAsync(() => handler.Invoke(this, EventArgs.Empty));
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private static bool Matches(HotkeyBinding? b, bool ctrl, bool alt, int vk) =>
            b.HasValue && b.Value.Ctrl == ctrl && b.Value.Alt == alt && b.Value.VkCode == vk;
    }
}

