using System;
using System.Windows.Interop;

namespace Buffer
{
    // Невидимое окно верхнего уровня. Окна HWND_MESSAGE не получают широковещательных сообщений
    // (смена темы, перезапуск панели задач), поэтому нужно именно такое.
    sealed class MessageWindow : IDisposable
    {
        const string Title = "Buffer.MessageWindow.6F1C3E2A";
        public const int WM_SHOW_FLYOUT = NativeMethods.WM_APP + 2;
        public const int WM_EXIT_APP = NativeMethods.WM_APP + 4;

        readonly HwndSource _source;

        public MessageWindow()
        {
            var parameters = new HwndSourceParameters(Title)
            {
                Width = 0,
                Height = 0,
                WindowStyle = NativeMethods.WS_POPUP,
                ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
            };
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        public IntPtr Handle => _source.Handle;

        public Func<int, IntPtr, IntPtr, bool> Handler { get; set; }

        public static bool SignalRunningInstance(int message)
        {
            IntPtr hwnd = NativeMethods.FindWindow(null, Title);
            if (hwnd == IntPtr.Zero)
                return false;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint processId);
            NativeMethods.AllowSetForegroundWindow(processId);
            NativeMethods.PostMessage(hwnd, message, IntPtr.Zero, IntPtr.Zero);
            return true;
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (Handler != null && Handler(msg, wParam, lParam))
                handled = true;
            return IntPtr.Zero;
        }

        public void Dispose() => _source.Dispose();
    }
}
