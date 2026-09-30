using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Buffer
{
    sealed class CapturedClip
    {
        public ClipKind Kind;
        public bool IsPrivate;
        public string Text;
        public byte[] Html;
        public byte[] Rtf;
        public byte[] Png;
        public byte[] Dib;
        public string[] Files;
    }

    static class ClipboardFormats
    {
        public static readonly uint Html = Register("HTML Format");
        public static readonly uint Rtf = Register("Rich Text Format");
        public static readonly uint Png = Register("PNG");
        public static readonly uint PreferredDropEffect = Register("Preferred DropEffect");

        // Этими форматами менеджеры паролей просят не сохранять содержимое в истории.
        public static readonly uint ExcludeFromMonitor = Register("ExcludeClipboardContentFromMonitorProcessing");
        public static readonly uint CanIncludeInHistory = Register("CanIncludeInClipboardHistory");
        public static readonly uint CanUploadToCloud = Register("CanUploadToCloudClipboard");
        public static readonly uint ViewerIgnore = Register("Clipboard Viewer Ignore");

        static uint Register(string name) => NativeMethods.RegisterClipboardFormat(name);
    }

    sealed class ClipboardMonitor : IDisposable
    {
        const int MaxRichFormatBytes = 8 * 1024 * 1024;
        const int MaxAttempts = 10;

        readonly IntPtr _hwnd;
        readonly DispatcherTimer _timer;
        uint _lastSequence;
        uint _ownSequence;
        int _attempts;
        bool _initial;

        public ClipboardMonitor(IntPtr hwnd)
        {
            _hwnd = hwnd;
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Tick += (s, e) => Poll();
            NativeMethods.AddClipboardFormatListener(hwnd);
        }

        // Второй параметр: содержимое прочитано при запуске программы, а не скопировано только что.
        public event Action<CapturedClip, bool> Captured;

        public Func<bool> CapturePrivate { get; set; }

        public void IgnoreOwnChange(uint sequence) => _ownSequence = sequence;

        // Программы часто кладут данные в несколько приёмов, поэтому читаем с небольшой задержкой.
        public void OnClipboardUpdate() => Schedule(80, resetAttempts: true);

        public void CaptureNow()
        {
            _initial = true;
            Schedule(1, resetAttempts: true);
        }

        void Schedule(int milliseconds, bool resetAttempts)
        {
            if (resetAttempts)
                _attempts = 0;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            _timer.Start();
        }

        void Poll()
        {
            _timer.Stop();
            uint sequence = NativeMethods.GetClipboardSequenceNumber();
            if (sequence == _lastSequence)
            {
                _initial = false;
                return;
            }
            if (sequence == _ownSequence)
            {
                _lastSequence = sequence;
                _initial = false;
                return;
            }

            if (!NativeMethods.OpenClipboard(_hwnd))
            {
                if (++_attempts < MaxAttempts)
                    Schedule(50, resetAttempts: false);
                else
                    _initial = false;
                return;
            }

            CapturedClip clip;
            try
            {
                clip = Read(CapturePrivate?.Invoke() == true);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                clip = null;
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }

            _lastSequence = sequence;
            bool initial = _initial;
            _initial = false;
            if (clip != null)
                Captured?.Invoke(clip, initial);
        }

        static CapturedClip Read(bool allowPrivate)
        {
            bool isPrivate = IsPrivate();
            if (isPrivate && !allowPrivate)
                return null;

            var clip = new CapturedClip { IsPrivate = isPrivate };

            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_HDROP))
            {
                clip.Files = ReadFiles();
                if (clip.Files != null && clip.Files.Length > 0)
                {
                    clip.Kind = ClipKind.Files;
                    return clip;
                }
            }

            if (NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
            {
                string text = ReadText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    clip.Kind = ClipKind.Text;
                    clip.Text = text;
                    clip.Html = ReadBytes(ClipboardFormats.Html, MaxRichFormatBytes);
                    clip.Rtf = ReadBytes(ClipboardFormats.Rtf, MaxRichFormatBytes);
                    return clip;
                }
            }

            clip.Png = ReadBytes(ClipboardFormats.Png, int.MaxValue);
            if (clip.Png == null)
                clip.Dib = ReadBytes(NativeMethods.CF_DIB, int.MaxValue);
            if (clip.Png == null && clip.Dib == null)
                return null;
            clip.Kind = ClipKind.Image;
            return clip;
        }

        static bool IsPrivate()
        {
            if (NativeMethods.IsClipboardFormatAvailable(ClipboardFormats.ExcludeFromMonitor) ||
                NativeMethods.IsClipboardFormatAvailable(ClipboardFormats.ViewerIgnore))
                return true;
            byte[] flag = ReadBytes(ClipboardFormats.CanIncludeInHistory, 64);
            return flag != null && flag.Length >= 4 && BitConverter.ToInt32(flag, 0) == 0;
        }

        static byte[] ReadBytes(uint format, int maxBytes)
        {
            if (format == 0 || !NativeMethods.IsClipboardFormatAvailable(format))
                return null;
            IntPtr handle = NativeMethods.GetClipboardData(format);
            if (handle == IntPtr.Zero)
                return null;
            ulong size = (ulong)NativeMethods.GlobalSize(handle);
            if (size == 0 || size > (ulong)maxBytes)
                return null;
            IntPtr pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
                return null;
            try
            {
                var data = new byte[size];
                Marshal.Copy(pointer, data, 0, (int)size);
                return data;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }

        static string ReadText()
        {
            IntPtr handle = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
                return null;
            ulong size = (ulong)NativeMethods.GlobalSize(handle);
            IntPtr pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
                return null;
            try
            {
                // Завершающего нуля может не быть, поэтому читаем не дальше размера блока.
                int length = (int)Math.Min(size / 2, int.MaxValue / 2);
                string text = Marshal.PtrToStringUni(pointer, length);
                int end = text.IndexOf('\0');
                return end >= 0 ? text.Substring(0, end) : text;
            }
            finally
            {
                NativeMethods.GlobalUnlock(handle);
            }
        }

        static string[] ReadFiles()
        {
            IntPtr drop = NativeMethods.GetClipboardData(NativeMethods.CF_HDROP);
            if (drop == IntPtr.Zero)
                return null;
            uint count = NativeMethods.DragQueryFile(drop, 0xFFFFFFFF, null, 0);
            var files = new List<string>();
            for (uint i = 0; i < count; i++)
            {
                uint length = NativeMethods.DragQueryFile(drop, i, null, 0);
                var buffer = new StringBuilder((int)length + 1);
                NativeMethods.DragQueryFile(drop, i, buffer, buffer.Capacity);
                files.Add(buffer.ToString());
            }
            return files.ToArray();
        }

        public void Dispose()
        {
            _timer.Stop();
            NativeMethods.RemoveClipboardFormatListener(_hwnd);
        }
    }
}
