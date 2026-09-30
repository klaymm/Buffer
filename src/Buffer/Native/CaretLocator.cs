using System;
using System.Runtime.InteropServices;
using Accessibility;

namespace Buffer
{
    static class CaretLocator
    {
        static readonly Guid IID_IAccessible = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

        // Прямоугольник текстового курсора в пикселях экрана или null, если его не удалось узнать.
        public static NativeMethods.RECT? Find(IntPtr window)
        {
            if (window == IntPtr.Zero)
                return null;

            uint thread = NativeMethods.GetWindowThreadProcessId(window, out _);
            var info = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO)) };
            if (!NativeMethods.GetGUIThreadInfo(thread, ref info))
                return null;

            if (info.hwndCaret != IntPtr.Zero && info.rcCaret.Bottom > info.rcCaret.Top)
            {
                var origin = new NativeMethods.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
                NativeMethods.ClientToScreen(info.hwndCaret, ref origin);
                int width = Math.Max(1, info.rcCaret.Right - info.rcCaret.Left);
                int height = info.rcCaret.Bottom - info.rcCaret.Top;
                return new NativeMethods.RECT(origin.X, origin.Y, origin.X + width, origin.Y + height);
            }

            // Браузеры и Electron не создают системную каретку, но сообщают её положение через MSAA.
            return FromAccessibility(info.hwndFocus != IntPtr.Zero ? info.hwndFocus : window);
        }

        static NativeMethods.RECT? FromAccessibility(IntPtr hwnd)
        {
            object instance = null;
            try
            {
                var iid = IID_IAccessible;
                if (NativeMethods.AccessibleObjectFromWindow(hwnd, NativeMethods.OBJID_CARET, ref iid, out instance) != 0)
                    return null;
                if (!(instance is IAccessible accessible))
                    return null;
                accessible.accLocation(out int left, out int top, out int width, out int height, 0);
                if (height <= 0 || (left == 0 && top == 0))
                    return null;
                return new NativeMethods.RECT(left, top, left + Math.Max(1, width), top + height);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (instance != null && Marshal.IsComObject(instance))
                    Marshal.ReleaseComObject(instance);
            }
        }
    }
}
