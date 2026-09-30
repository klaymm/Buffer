using System;
using Microsoft.Win32;

namespace Buffer
{
    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "Buffer";

        static string Command => "\"" + AppPaths.ExePath + "\"";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var run = Registry.CurrentUser.OpenSubKey(RunKey))
                    {
                        if (!(run?.GetValue(ValueName) is string))
                            return false;
                    }
                    // Запись о выключении из диспетчера задач: нечётный первый байт.
                    using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                        return !(approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        public static void Set(bool enabled)
        {
            try
            {
                using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled)
                        run.SetValue(ValueName, Command);
                    else
                        run.DeleteValue(ValueName, false);
                }
                // Иначе выключение, сделанное в диспетчере задач, перекрыло бы включение из меню.
                using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                    approved?.DeleteValue(ValueName, false);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }

        // Если exe перенесли в другую папку, автозапуск должен указывать на новое место.
        public static void UpdatePath()
        {
            try
            {
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (run?.GetValue(ValueName) is string current && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                        run.SetValue(ValueName, Command);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }
    }
}
