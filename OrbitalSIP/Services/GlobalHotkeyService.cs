using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Threading;

namespace OrbitalSIP.Services
{
    /// <summary>
    /// Fires hotkey actions even when the application window is not focused.
    ///
    /// Two mechanisms, picked at <see cref="Start"/>:
    ///
    /// <b>RegisterHotKey</b> (opt-in, <see cref="SipSettings.UseHotkeyRegistration"/>) is
    /// the right primitive: Windows delivers only the combinations this app asked for, and
    /// consumes them so they never reach the focused application or the shell. Requires a
    /// thread with a message loop, because a hotkey registered with a null window handle
    /// is delivered to the registering thread's message queue.
    ///
    /// <b>WH_KEYBOARD_LL</b> is the fallback and, for now, the default. It works
    /// unconditionally, at the cost of routing every keystroke on the machine through this
    /// process — and of a callback Windows will silently drop the hook from if it ever
    /// takes longer than LowLevelHooksTimeout.
    ///
    /// The registration path falls back on its own if any combination cannot be
    /// registered (another application already owns it), and refuses outright if any
    /// binding has no modifier — RegisterHotKey consumes what it claims, so a bare letter
    /// would become untypeable in every application on the machine.
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
        private const int VK_SHIFT       = 0x10;
        private const int VK_LWIN        = 0x5B;
        private const int VK_RWIN        = 0x5C;

        private const uint WM_HOTKEY  = 0x0312;
        private const uint WM_QUIT    = 0x0012;
        /// <summary>WM_APP + 1. Tells the hotkey thread its bindings changed.</summary>
        private const uint WM_REREGISTER = 0x8001;

        private const uint MOD_ALT      = 0x0001;
        private const uint MOD_CONTROL  = 0x0002;
        /// <summary>Suppresses key-repeat, so holding the combination fires once.</summary>
        private const uint MOD_NOREPEAT = 0x4000;

        private const uint PM_NOREMOVE = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint   vkCode;
            public uint   scanCode;
            public uint   flags;
            public uint   time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr Hwnd;
            public uint   Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint   Time;
            public int    PointX;
            public int    PointY;
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // ── Hotkey binding ────────────────────────────────────────────
        private readonly struct HotkeyBinding
        {
            public readonly bool Ctrl;
            public readonly bool Alt;
            public readonly int  VkCode;
            public HotkeyBinding(bool ctrl, bool alt, int vk) { Ctrl = ctrl; Alt = alt; VkCode = vk; }

            public bool HasModifier => Ctrl || Alt;

            public uint Win32Modifiers =>
                (Ctrl ? MOD_CONTROL : 0) | (Alt ? MOD_ALT : 0) | MOD_NOREPEAT;
        }

        private const int IdMute = 1, IdHold = 2, IdHangup = 3, IdAnswer = 4;

        private readonly object _bindingLock = new();
        private HotkeyBinding? _bindMute;
        private HotkeyBinding? _bindHold;
        private HotkeyBinding? _bindHangup;
        private HotkeyBinding? _bindAnswer;

        // ── State ─────────────────────────────────────────────────────
        private IntPtr                _hookHandle = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;   // GC guard

        private Thread? _hotkeyThread;
        private uint    _hotkeyThreadId;
        private volatile bool _registrationActive;

        // ── Events ────────────────────────────────────────────────────
        public event EventHandler? MuteToggleRequested;
        public event EventHandler? HoldToggleRequested;
        public event EventHandler? HangupPressed;
        public event EventHandler? AnswerPressed;

        // ── Public API ────────────────────────────────────────────────
        public void ApplySettings(SipSettings s)
        {
            lock (_bindingLock)
            {
                _bindMute   = ParseHotkey(s.HotkeyMute);
                _bindHold   = ParseHotkey(s.HotkeyHold);
                _bindHangup = ParseHotkey(s.HotkeyHangup);
                _bindAnswer = ParseHotkey(s.HotkeyAnswer);
            }

            if (!_registrationActive) return;

            // Re-checked here, not only at Start: the operator can edit a hotkey down to a
            // bare key mid-session, and RegisterHotKey consumes what it claims. Registering
            // that would take the key away from every application on the machine.
            if (!AllBindingsRegistrable())
            {
                AppLogger.Log("Hotkeys",
                    "New bindings include a modifier-less key. Dropping RegisterHotKey and going back to the hook, " +
                    "which passes unmatched keys through.");
                StopRegistration();
                StartLowLevelHook();
                return;
            }

            // Settings can be saved long after Start; the registration thread owns the
            // registrations and has to redo them itself.
            if (_hotkeyThreadId != 0)
                PostThreadMessage(_hotkeyThreadId, WM_REREGISTER, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>True when every current binding is eligible for RegisterHotKey.</summary>
        private bool AllBindingsRegistrable()
        {
            lock (_bindingLock)
            {
                return IsSafeToRegister(_bindMute)
                    && IsSafeToRegister(_bindHold)
                    && IsSafeToRegister(_bindHangup)
                    && IsSafeToRegister(_bindAnswer);
            }
        }

        /// <summary>
        /// Starts delivering hotkeys. Tries RegisterHotKey when
        /// <see cref="SipSettings.UseHotkeyRegistration"/> is set, and falls back to the
        /// low-level hook whenever that cannot be made to work.
        /// </summary>
        public void Start(SipSettings? settings = null)
        {
            if (_registrationActive || _hookHandle != IntPtr.Zero) return;

            if (settings?.UseHotkeyRegistration == true && TryStartRegistration())
            {
                AppLogger.Log("Hotkeys", "Using RegisterHotKey delivery.");
                return;
            }

            StartLowLevelHook();
        }

        public void Stop()
        {
            StopRegistration();

            if (_hookHandle == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _proc = null;
        }

        public void Dispose() => Stop();

        // ── RegisterHotKey delivery ───────────────────────────────────

        /// <summary>
        /// Spins the hotkey thread and waits for it to report whether every binding
        /// registered. Returns false — leaving nothing behind — if the answer is no, so
        /// the caller can fall back.
        /// </summary>
        private bool TryStartRegistration()
        {
            if (!AllBindingsRegistrable())
            {
                AppLogger.Log("Hotkeys",
                    "Not using RegisterHotKey: at least one binding has no Ctrl/Alt modifier, and " +
                    "registering a bare key would swallow it system-wide. Falling back to the hook.");
                return false;
            }

            var ready = new TaskCompletionSourceLite();

            _hotkeyThread = new Thread(() => HotkeyThreadBody(ready))
            {
                IsBackground = true,
                Name = "OrbitalSIP-Hotkeys",
            };
            _hotkeyThread.Start();

            // Bounded: a thread that never reports is a thread that will never deliver a
            // hotkey either, and the operator gets the hook instead of nothing.
            if (!ready.Wait(TimeSpan.FromSeconds(2)) || !ready.Succeeded)
            {
                AppLogger.Log("Hotkeys", "RegisterHotKey setup did not succeed. Falling back to the hook.");
                StopRegistration();
                return false;
            }

            _registrationActive = true;
            return true;
        }

        private void HotkeyThreadBody(TaskCompletionSourceLite ready)
        {
            // Everything is inside the try so the finally always clears _hotkeyThreadId.
            // An early return that left it set meant StopRegistration would later post
            // WM_QUIT to a thread id that no longer exists — or, worse, to whatever thread
            // Windows had since given that id to.
            try
            {
                _hotkeyThreadId = GetCurrentThreadId();

                // Forces the thread's message queue into existence before anything can post
                // to it — PostThreadMessage silently fails against a thread that has none yet.
                PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);

                if (!RegisterAll())
                {
                    ready.SetResult(false);
                    return;
                }

                ready.SetResult(true);

                while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.Message == WM_HOTKEY)
                    {
                        DispatchHotkey((int)msg.WParam);
                    }
                    else if (msg.Message == WM_REREGISTER)
                    {
                        UnregisterAll();
                        if (!RegisterAll())
                        {
                            // A partially registered set is worse than none: the operator
                            // cannot tell which of their hotkeys still works. Hand the job
                            // back to the hook, which always works.
                            AppLogger.Log("Hotkeys", "Re-registration after a settings change failed. Dropping to the low-level hook.");
                            Dispatcher.UIThread.Post(FallBackToHook);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("Hotkeys", $"Hotkey message loop threw: {ex.GetType().Name}: {ex.Message}");
                ready.SetResult(false);
            }
            finally
            {
                UnregisterAll();
                _hotkeyThreadId = 0;
            }
        }

        /// <summary>
        /// Runs on the UI thread after the hotkey thread has given up. Not
        /// <see cref="StopRegistration"/>, which joins the thread — calling that from the
        /// thread itself would deadlock until the timeout.
        /// </summary>
        private void FallBackToHook()
        {
            _registrationActive = false;
            _hotkeyThread = null;

            if (_hookHandle == IntPtr.Zero) StartLowLevelHook();
        }

        /// <summary>All or nothing: a partially registered set is worse than none, because
        /// the operator cannot tell which of their four hotkeys is live.</summary>
        private bool RegisterAll()
        {
            HotkeyBinding? mute, hold, hangup, answer;
            lock (_bindingLock)
            {
                mute = _bindMute; hold = _bindHold; hangup = _bindHangup; answer = _bindAnswer;
            }

            return RegisterOne(IdMute, mute, nameof(MuteToggleRequested))
                && RegisterOne(IdHold, hold, nameof(HoldToggleRequested))
                && RegisterOne(IdHangup, hangup, nameof(HangupPressed))
                && RegisterOne(IdAnswer, answer, nameof(AnswerPressed));
        }

        private static bool RegisterOne(int id, HotkeyBinding? binding, string what)
        {
            if (binding == null) return true;   // unbound is not a failure

            // Defence in depth: both entry points check this before getting here, but this
            // is the call that would actually take a key away from the whole machine.
            if (!IsSafeToRegister(binding))
            {
                AppLogger.Log("Hotkeys", $"Refusing to register {what}: a modifier-less key would be consumed system-wide.");
                return false;
            }

            if (RegisterHotKey(IntPtr.Zero, id, binding.Value.Win32Modifiers, (uint)binding.Value.VkCode))
                return true;

            // ERROR_HOTKEY_ALREADY_REGISTERED (1409) is the usual one: another application
            // owns the combination. The hook never surfaced this at all — it simply fired
            // alongside whatever else was listening.
            AppLogger.Log("Hotkeys",
                $"RegisterHotKey failed for {what} (Win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        private static void UnregisterAll()
        {
            foreach (var id in new[] { IdMute, IdHold, IdHangup, IdAnswer })
                UnregisterHotKey(IntPtr.Zero, id);
        }

        private void DispatchHotkey(int id)
        {
            var handler = id switch
            {
                IdMute   => MuteToggleRequested,
                IdHold   => HoldToggleRequested,
                IdHangup => HangupPressed,
                IdAnswer => AnswerPressed,
                _        => null,
            };

            if (handler != null)
                Dispatcher.UIThread.InvokeAsync(() => handler.Invoke(this, EventArgs.Empty));
        }

        private void StopRegistration()
        {
            _registrationActive = false;

            var threadId = _hotkeyThreadId;
            if (threadId != 0)
                PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

            _hotkeyThread?.Join(TimeSpan.FromSeconds(2));
            _hotkeyThread = null;
        }

        /// <summary>
        /// A one-shot signal without dragging a TaskCompletionSource (and its continuation
        /// scheduling) into a raw Win32 thread's start-up path.
        /// </summary>
        private sealed class TaskCompletionSourceLite
        {
            private readonly ManualResetEventSlim _done = new(false);
            public bool Succeeded { get; private set; }

            public void SetResult(bool succeeded)
            {
                Succeeded = succeeded;
                _done.Set();
            }

            public bool Wait(TimeSpan timeout) => _done.Wait(timeout);
        }

        // ── Low-level hook delivery ───────────────────────────────────

        private void StartLowLevelHook()
        {
            _proc = HookCallback;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);

            if (_hookHandle == IntPtr.Zero)
                AppLogger.Log("Hotkeys", $"SetWindowsHookEx failed (Win32 error {Marshal.GetLastWin32Error()}). Global hotkeys are inactive.");
            else
                AppLogger.Log("Hotkeys", "Using low-level keyboard hook delivery.");
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                // vkCode is the first DWORD of KBDLLHOOKSTRUCT. Read it directly rather
                // than marshalling the whole struct, and check the cheap thing first:
                // this callback runs for every keystroke anywhere on the machine, on a
                // thread Windows drops the hook from if it ever takes longer than
                // LowLevelHooksTimeout.
                int vk = Marshal.ReadInt32(lParam);

                if (BindsVirtualKey(vk) && !IsExtraModifierDown())
                {
                    bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                    bool alt  = (GetAsyncKeyState(VK_MENU)    & 0x8000) != 0;

                    EventHandler? handler = null;
                    HotkeyBinding? matched = null;

                    if      (Matches(_bindMute,   ctrl, alt, vk)) { handler = MuteToggleRequested; matched = _bindMute;   }
                    else if (Matches(_bindHold,   ctrl, alt, vk)) { handler = HoldToggleRequested; matched = _bindHold;   }
                    else if (Matches(_bindHangup, ctrl, alt, vk)) { handler = HangupPressed;       matched = _bindHangup; }
                    else if (Matches(_bindAnswer, ctrl, alt, vk)) { handler = AnswerPressed;       matched = _bindAnswer; }

                    if (handler != null)
                    {
                        Dispatcher.UIThread.InvokeAsync(() => handler.Invoke(this, EventArgs.Empty));

                        // Swallow the combination instead of passing it on. The defaults
                        // are Alt+Escape and Alt+Enter, and the deployed config uses
                        // Alt+Space — every one of them a Windows shell shortcut, so
                        // hanging up a call also cycled windows or opened the system menu
                        // of whatever happened to be focused.
                        //
                        // Only a combination that carries a modifier is swallowed:
                        // ParseHotkey also accepts a bare letter, and eating that would
                        // make the letter untypeable in every application on the machine —
                        // far worse than the shell collision this is fixing.
                        if (matched.HasValue && matched.Value.HasModifier)
                            return (IntPtr)1;
                    }
                }
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        /// <summary>
        /// True when Shift or a Windows key is held.
        ///
        /// ParseHotkey never produces a binding with either, so a combination that
        /// includes one is NOT the operator's hotkey — Ctrl+Shift+M is not Ctrl+M. The old
        /// <see cref="Matches"/> ignored them, which was harmless while the hook passed
        /// every key through; now that a match is swallowed, it would have destroyed
        /// keystrokes the operator never bound.
        /// </summary>
        private static bool IsExtraModifierDown() =>
            (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_LWIN)  & 0x8000) != 0 ||
            (GetAsyncKeyState(VK_RWIN)  & 0x8000) != 0;

        /// <summary>Cheap pre-filter, so an unrelated keystroke costs one comparison.</summary>
        private bool BindsVirtualKey(int vk) =>
            _bindMute?.VkCode   == vk ||
            _bindHold?.VkCode   == vk ||
            _bindHangup?.VkCode == vk ||
            _bindAnswer?.VkCode == vk;

        private static bool Matches(HotkeyBinding? b, bool ctrl, bool alt, int vk) =>
            b.HasValue && b.Value.Ctrl == ctrl && b.Value.Alt == alt && b.Value.VkCode == vk;

        // ── Key string parser ─────────────────────────────────────────
        /// <summary>
        /// Returns true if the key combo string is parseable (e.g. "Ctrl+M", "Escape").
        /// </summary>
        public static bool IsValidHotkey(string? s) => ParseHotkey(s).HasValue;

        /// <summary>
        /// Whether this combination may be handed to RegisterHotKey.
        ///
        /// False only for a parsed combination with no Ctrl/Alt: RegisterHotKey consumes
        /// what it claims, so registering a bare letter would stop that letter being
        /// typeable in every application on the machine. Unset or unparseable is fine —
        /// nothing gets registered for it. One such binding disqualifies the whole
        /// registration path, because a partial set leaves the operator unable to tell
        /// which of their hotkeys is live.
        /// </summary>
        public static bool IsSafeToRegister(string? hotkey) => IsSafeToRegister(ParseHotkey(hotkey));

        private static bool IsSafeToRegister(HotkeyBinding? binding) =>
            binding == null || binding.Value.HasModifier;

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
    }
}
