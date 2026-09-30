using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Buffer
{
    static class ClipboardWriter
    {
        // Возвращает номер изменения буфера после записи, чтобы монитор не принял её за новое копирование.
        public static uint Write(ClipItem item, IntPtr owner)
        {
            if (!Open(owner))
                return 0;
            try
            {
                NativeMethods.EmptyClipboard();
                switch (item.Kind)
                {
                    case ClipKind.Text:
                        SetText(item.Text);
                        if (item.Html != null)
                            SetBytes(ClipboardFormats.Html, WithTerminator(item.Html));
                        if (item.Rtf != null)
                            SetBytes(ClipboardFormats.Rtf, WithTerminator(item.Rtf));
                        break;

                    case ClipKind.Image:
                        var image = ImageCodec.DecodePng(item.Png);
                        if (image != null)
                            SetBytes(NativeMethods.CF_DIB, ImageCodec.ToDib(image));
                        SetBytes(ClipboardFormats.Png, item.Png);
                        break;

                    case ClipKind.Files:
                        SetBytes(NativeMethods.CF_HDROP, BuildDropFiles(item.Files));
                        SetBytes(ClipboardFormats.PreferredDropEffect, BitConverter.GetBytes(1));
                        break;
                }

                if (item.IsPrivate)
                {
                    // Пароль возвращается с теми же пометками, чтобы его не подхватила история Windows.
                    SetBytes(ClipboardFormats.ExcludeFromMonitor, BitConverter.GetBytes(0));
                    SetBytes(ClipboardFormats.CanIncludeInHistory, BitConverter.GetBytes(0));
                    SetBytes(ClipboardFormats.CanUploadToCloud, BitConverter.GetBytes(0));
                }
            }
            finally
            {
                NativeMethods.CloseClipboard();
            }
            return NativeMethods.GetClipboardSequenceNumber();
        }

        static bool Open(IntPtr owner)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (NativeMethods.OpenClipboard(owner))
                    return true;
                Thread.Sleep(20);
            }
            return false;
        }

        static void SetText(string text)
        {
            var data = new byte[(text.Length + 1) * 2];
            Encoding.Unicode.GetBytes(text, 0, text.Length, data, 0);
            SetBytes(NativeMethods.CF_UNICODETEXT, data);
        }

        static void SetBytes(uint format, byte[] data)
        {
            if (format == 0 || data == null)
                return;
            IntPtr handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, new UIntPtr((uint)Math.Max(1, data.Length)));
            if (handle == IntPtr.Zero)
                return;
            IntPtr pointer = NativeMethods.GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                NativeMethods.GlobalFree(handle);
                return;
            }
            Marshal.Copy(data, 0, pointer, data.Length);
            NativeMethods.GlobalUnlock(handle);
            if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
                NativeMethods.GlobalFree(handle);
        }

        // Структура DROPFILES (20 байт), затем пути в UTF-16 через \0 и ещё один \0 в конце.
        static byte[] BuildDropFiles(string[] files)
        {
            byte[] paths = Encoding.Unicode.GetBytes(string.Join("\0", files) + "\0\0");
            var data = new byte[20 + paths.Length];
            BitConverter.GetBytes(20).CopyTo(data, 0);
            BitConverter.GetBytes(1).CopyTo(data, 16);
            paths.CopyTo(data, 20);
            return data;
        }

        static byte[] WithTerminator(byte[] data)
        {
            if (data.Length > 0 && data[data.Length - 1] == 0)
                return data;
            var result = new byte[data.Length + 1];
            data.CopyTo(result, 0);
            return result;
        }
    }
}
