using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Buffer
{
    sealed class TrayIcon : IDisposable
    {
        public const int CallbackMessage = NativeMethods.WM_APP + 3;
        const char Glyph = '';

        static readonly int TaskbarCreated = (int)NativeMethods.RegisterWindowMessage("TaskbarCreated");

        readonly IntPtr _hwnd;
        IntPtr _icon;
        string _tip = "Buffer";

        public TrayIcon(IntPtr hwnd)
        {
            _hwnd = hwnd;
        }

        public event Action<int, int> Click;
        public event Action<int, int> MenuRequested;

        public void Show(bool darkTaskbar, string tip)
        {
            _tip = tip;
            _icon = CreateGlyphIcon(darkTaskbar ? Colors.White : Colors.Black);
            Add();
        }

        public void Update(bool darkTaskbar, string tip)
        {
            _tip = tip;
            IntPtr old = _icon;
            _icon = CreateGlyphIcon(darkTaskbar ? Colors.White : Colors.Black);
            var data = Data(NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
            if (old != IntPtr.Zero)
                NativeMethods.DestroyIcon(old);
        }

        public void ShowBalloon(string title, string text)
        {
            var data = Data(NativeMethods.NIF_INFO);
            data.szInfoTitle = title;
            data.szInfo = text;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        }

        public bool HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == TaskbarCreated)
            {
                Add();
                return true;
            }
            if (msg != CallbackMessage)
                return false;

            int evt = (int)((long)lParam & 0xFFFF);
            long anchor = (long)wParam;
            int x = (short)(anchor & 0xFFFF);
            int y = (short)((anchor >> 16) & 0xFFFF);
            if (evt == NativeMethods.NIN_SELECT || evt == NativeMethods.NIN_KEYSELECT)
                Click?.Invoke(x, y);
            else if (evt == NativeMethods.WM_CONTEXTMENU)
                MenuRequested?.Invoke(x, y);
            return true;
        }

        public void Dispose()
        {
            var data = Data(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            if (_icon != IntPtr.Zero)
                NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        void Add()
        {
            var data = Data(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
            data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
        }

        NativeMethods.NOTIFYICONDATA Data(uint flags) => new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA)),
            hWnd = _hwnd,
            uID = 1,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = _tip,
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };

        // Значок рисуется из системного шрифта значков: он чёткий при любом масштабе
        // и перекрашивается под светлую или тёмную панель задач.
        static IntPtr CreateGlyphIcon(Color color)
        {
            uint dpi = NativeMethods.GetDpiForSystem();
            int size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, dpi);
            if (size <= 0)
                size = 16;

            var typeface = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var text = new FormattedText(Glyph.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, size, new SolidColorBrush(color), null, TextFormattingMode.Display, 1.0);
            Rect bounds = text.BuildGeometry(new Point()).Bounds;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                double x = Math.Round((size - bounds.Width) / 2 - bounds.X);
                double y = Math.Round((size - bounds.Height) / 2 - bounds.Y);
                dc.DrawText(text, new Point(x, y));
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            int stride = size * 4;
            var pixels = new byte[stride * size];
            bitmap.CopyPixels(pixels, stride, 0);

            // Значки Windows ждут непредумноженную альфу, а WPF отдаёт предумноженную.
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int a = pixels[i + 3];
                if (a == 0 || a == 255)
                    continue;
                pixels[i] = (byte)Math.Min(255, pixels[i] * 255 / a);
                pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] * 255 / a);
                pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] * 255 / a);
            }

            IntPtr colorBitmap = NativeMethods.CreateBitmap(size, size, 1, 32, pixels);
            IntPtr maskBitmap = NativeMethods.CreateBitmap(size, size, 1, 1, new byte[(size + 15) / 16 * 2 * size]);
            var info = new NativeMethods.ICONINFO { fIcon = true, hbmColor = colorBitmap, hbmMask = maskBitmap };
            IntPtr icon = NativeMethods.CreateIconIndirect(ref info);
            NativeMethods.DeleteObject(colorBitmap);
            NativeMethods.DeleteObject(maskBitmap);
            return icon;
        }
    }

    sealed class NativeMenu : IDisposable
    {
        public NativeMenu()
        {
            Handle = NativeMethods.CreatePopupMenu();
        }

        public IntPtr Handle { get; }

        public void Add(int id, string text, bool isChecked = false) =>
            NativeMethods.AppendMenu(Handle, NativeMethods.MF_STRING | (isChecked ? NativeMethods.MF_CHECKED : 0), new UIntPtr((uint)id), text);

        public void AddSeparator() => NativeMethods.AppendMenu(Handle, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);

        public void AddSubmenu(NativeMenu submenu, string text) =>
            NativeMethods.AppendMenu(Handle, NativeMethods.MF_POPUP, new UIntPtr((ulong)submenu.Handle.ToInt64()), text);

        public void SetDefault(int id) => NativeMethods.SetMenuDefaultItem(Handle, (uint)id, 0);

        public void CheckRadio(int first, int last, int id) =>
            NativeMethods.CheckMenuRadioItem(Handle, (uint)first, (uint)last, (uint)id, NativeMethods.MF_BYCOMMAND);

        public int Show(IntPtr owner, int x, int y, bool dark)
        {
            ApplyTheme(dark);
            // Без этого меню не закрывается по щелчку мимо него.
            NativeMethods.SetForegroundWindow(owner);
            uint align = NativeMethods.GetSystemMetrics(NativeMethods.SM_MENUDROPALIGNMENT) != 0 ? NativeMethods.TPM_RIGHTALIGN : NativeMethods.TPM_LEFTALIGN;
            int command = NativeMethods.TrackPopupMenuEx(Handle,
                align | NativeMethods.TPM_BOTTOMALIGN | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                x, y, owner, IntPtr.Zero);
            NativeMethods.PostMessage(owner, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return command;
        }

        static void ApplyTheme(bool dark)
        {
            if (NativeMethods.WindowsBuild < 18362)
                return;
            try
            {
                const int ForceDark = 2, ForceLight = 3;
                NativeMethods.SetPreferredAppMode(dark ? ForceDark : ForceLight);
                NativeMethods.FlushMenuThemes();
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        public void Dispose() => NativeMethods.DestroyMenu(Handle);
    }
}
