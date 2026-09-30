using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace Buffer
{
    // Хук живёт в своём потоке с собственным циклом сообщений: если окно программы занято,
    // клавиатура в системе не подтормаживает, а Windows не снимает хук по таймауту.
    sealed class KeyboardHook : IDisposable
    {
        // Метка наших собственных нажатий: хук их пропускает, чтобы не реагировать сам на себя.
        public static readonly IntPtr InjectedMarker = new IntPtr(0x42554646);

        const int WM_REINSTALL = NativeMethods.WM_APP + 1;
        const int VK_TAB = 0x09;
        const int VK_RETURN = 0x0D;
        const int VK_SHIFT = 0x10;
        const int VK_CONTROL = 0x11;
        const int VK_MENU = 0x12;
        const int VK_ESCAPE = 0x1B;
        const int VK_SPACE = 0x20;
        const int VK_LWIN = 0x5B;
        const int VK_RWIN = 0x5C;

        // Неназначенная клавиша. Её нажатие, пока зажат Win, не даёт открыться меню «Пуск»,
        // а заодно разрешает процессу вывести своё окно на передний план.
        const ushort VK_MASK = 0xE8;

        readonly Dispatcher _dispatcher;
        readonly NativeMethods.LowLevelKeyboardProc _proc;
        readonly ManualResetEventSlim _ready = new ManualResetEventSlim();
        Thread _thread;
        uint _threadId;
        IntPtr _hook;
        volatile int _hotkey = Hotkey.Default.Pack();
        volatile bool _capturing;
        int _swallowedKey;
        bool _maskOnRelease;

        public KeyboardHook(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _proc = HookProc;
        }

        public event Action<IntPtr> HotkeyPressed;
        public event Action<Hotkey> Captured;

        // Зажатые модификаторы с учётом только что нажатой или отпущенной клавиши и признак нажатия.
        public event Action<HotkeyModifiers, bool> CaptureModifiersChanged;

        public Hotkey Hotkey
        {
            get => Hotkey.Unpack(_hotkey);
            set => _hotkey = value.Pack();
        }

        public bool Capturing
        {
            get => _capturing;
            set => _capturing = value;
        }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Keyboard hook" };
            _thread.Start();
            _ready.Wait(2000);
        }

        public void Reinstall()
        {
            if (_threadId != 0)
                NativeMethods.PostThreadMessage(_threadId, WM_REINSTALL, IntPtr.Zero, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (_threadId == 0)
                return;
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(1000);
            _threadId = 0;
        }

        public static HotkeyModifiers CurrentModifiers()
        {
            var m = HotkeyModifiers.None;
            if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) m |= HotkeyModifiers.Win;
            if (IsDown(VK_CONTROL)) m |= HotkeyModifiers.Ctrl;
            if (IsDown(VK_MENU)) m |= HotkeyModifiers.Alt;
            if (IsDown(VK_SHIFT)) m |= HotkeyModifiers.Shift;
            return m;
        }

        public static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

        public static void SendMaskKey() => SendKeys(new[] { (VK_MASK, false), (VK_MASK, true) });

        public static void SendKeys((ushort Key, bool Up)[] keys)
        {
            var inputs = new NativeMethods.INPUT[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                ushort vk = keys[i].Key;
                uint flags = keys[i].Up ? NativeMethods.KEYEVENTF_KEYUP : 0;
                if (IsExtendedKey(vk))
                    flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
                inputs[i].type = NativeMethods.INPUT_KEYBOARD;
                inputs[i].u.ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)NativeMethods.MapVirtualKey(vk, 0),
                    dwFlags = flags,
                    dwExtraInfo = InjectedMarker,
                };
            }
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        static bool IsWinOrAlt(int vk) => vk == VK_LWIN || vk == VK_RWIN || vk == 0xA4 || vk == 0xA5;

        static bool IsExtendedKey(ushort vk) =>
            vk == VK_LWIN || vk == VK_RWIN || vk == 0xA3 || vk == 0xA5 ||
            (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E;

        void Run()
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            Install();
            _ready.Set();
            while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_REINSTALL)
                {
                    Uninstall();
                    Install();
                    continue;
                }
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
            Uninstall();
        }

        void Install()
        {
            _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
                ErrorLog.Write("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
        }

        void Uninstall()
        {
            if (_hook == IntPtr.Zero)
                return;
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // Исключение здесь уронило бы процесс вместе с хуком, поэтому клавишу просто пропускаем дальше.
                try
                {
                    if (Handle((int)wParam, lParam))
                        return (IntPtr)1;
                }
                catch (Exception ex)
                {
                    ErrorLog.Write(ex);
                }
            }
            return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        bool Handle(int message, IntPtr data)
        {
            int vk = Marshal.ReadInt32(data);
            int flags = Marshal.ReadInt32(data, 8);
            if ((flags & NativeMethods.LLKHF_INJECTED) != 0 && Marshal.ReadIntPtr(data, 16) == InjectedMarker)
                return false;

            bool down = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;

            // Автоповтор и отпускание клавиши, нажатие которой уже перехвачено.
            if (_swallowedKey != 0 && vk == _swallowedKey)
            {
                if (!down)
                    _swallowedKey = 0;
                return true;
            }

            // Если нажатия пришли пачкой (макрос, переназначенные клавиши), маскирующая клавиша,
            // отправленная при срабатывании, встанет в очередь уже после отпускания Win, и откроется «Пуск».
            // Поэтому отпускание Win или Alt задерживаем и отправляем сами сразу после маскирующей клавиши.
            if (_maskOnRelease && !down && IsWinOrAlt(vk))
            {
                _maskOnRelease = false;
                SendKeys(new[] { (VK_MASK, false), (VK_MASK, true), ((ushort)vk, true) });
                if (_capturing)
                    PostModifiers(vk, down);
                return true;
            }

            if (Hotkey.IsModifierKey(vk))
            {
                if (_capturing)
                    PostModifiers(vk, down);
                return false;
            }

            if (!down)
                return false;

            var modifiers = CurrentModifiers();
            if (_capturing)
            {
                // Esc, Enter, Tab и пробел без модификаторов остаются окну настройки: отмена, сохранение, переход по кнопкам.
                if (modifiers == HotkeyModifiers.None && (vk == VK_ESCAPE || vk == VK_RETURN || vk == VK_TAB || vk == VK_SPACE))
                    return false;
                _swallowedKey = vk;
                if ((modifiers & (HotkeyModifiers.Win | HotkeyModifiers.Alt)) != 0)
                {
                    SendMaskKey();
                    _maskOnRelease = true;
                }
                var captured = new Hotkey(modifiers, vk);
                Post(() => Captured?.Invoke(captured));
                return true;
            }

            var hotkey = Hotkey.Unpack(_hotkey);
            if (vk != hotkey.Key || modifiers != hotkey.Modifiers)
                return false;

            _swallowedKey = vk;
            SendMaskKey();
            _maskOnRelease = (modifiers & (HotkeyModifiers.Win | HotkeyModifiers.Alt)) != 0;
            var foreground = NativeMethods.GetForegroundWindow();
            Post(() => HotkeyPressed?.Invoke(foreground));
            return true;
        }

        // GetAsyncKeyState ещё не знает о клавише, которую хук обрабатывает прямо сейчас, поэтому учитываем её сами.
        void PostModifiers(int vk, bool down)
        {
            var modifiers = CurrentModifiers();
            var flag = ModifierOf(vk);
            if (down)
                modifiers |= flag;
            else if (!IsDown(PairOf(vk)))
                modifiers &= ~flag;
            Post(() => CaptureModifiersChanged?.Invoke(modifiers, down));
        }

        static HotkeyModifiers ModifierOf(int vk)
        {
            switch (vk)
            {
                case VK_LWIN:
                case VK_RWIN:
                    return HotkeyModifiers.Win;
                case VK_CONTROL:
                case 0xA2:
                case 0xA3:
                    return HotkeyModifiers.Ctrl;
                case VK_MENU:
                case 0xA4:
                case 0xA5:
                    return HotkeyModifiers.Alt;
                case VK_SHIFT:
                case 0xA0:
                case 0xA1:
                    return HotkeyModifiers.Shift;
                default:
                    return HotkeyModifiers.None;
            }
        }

        // Та же клавиша с другой стороны клавиатуры: отпустили левый Shift, а правый ещё зажат.
        static int PairOf(int vk)
        {
            switch (vk)
            {
                case VK_LWIN: return VK_RWIN;
                case VK_RWIN: return VK_LWIN;
                case 0xA0: return 0xA1;
                case 0xA1: return 0xA0;
                case 0xA2: return 0xA3;
                case 0xA3: return 0xA2;
                case 0xA4: return 0xA5;
                case 0xA5: return 0xA4;
                default: return vk;
            }
        }

        void Post(Action action) => _dispatcher.BeginInvoke(DispatcherPriority.Send, action);
    }
}
