using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Buffer
{
    static class WindowStyling
    {
        // Системная подложка Mica и Acrylic есть только в Windows 11 22H2 и новее.
        public static bool BackdropSupported => NativeMethods.WindowsBuild >= 22621;

        public static void Apply(Window window, bool dark, int backdrop)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            SetAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0);
            SetAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, NativeMethods.DWMWCP_ROUND);

            if (BackdropSupported && !SystemParameters.HighContrast)
            {
                HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;
                window.Background = Brushes.Transparent;
                var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
                SetAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, backdrop);
            }
            else
            {
                if (BackdropSupported)
                    SetAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, NativeMethods.DWMSBT_NONE);
                window.SetResourceReference(Window.BackgroundProperty, "WindowFallbackBrush");
            }
        }

        public static void HideFromTaskSwitcher(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
            RemoveSystemMenu(window);
        }

        public static void RemoveSystemMenu(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            style &= ~(NativeMethods.WS_SYSMENU | NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        }

        static void SetAttribute(IntPtr hwnd, int attribute, int value) =>
            NativeMethods.DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    }
}
