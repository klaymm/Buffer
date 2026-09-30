using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Buffer
{
    // Запуск с правами администратора идёт через задачу планировщика: ключ Run не умеет поднимать права,
    // а задача с наивысшими правами стартует без окна UAC и при входе в Windows, и по запросу.
    static class AdminMode
    {
        static bool? _startsAtLogon;

        public static string TaskName { get; } = "Buffer_" + Environment.UserName;

        public static bool IsElevated
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        // Ответ планировщика запоминается: каждый запрос к нему запускает отдельный процесс.
        public static bool StartsAtLogon => _startsAtLogon ?? Query().AtLogon;

        public static TaskState Query()
        {
            bool exists = Schtasks("/Query /TN \"" + TaskName + "\" /XML ONE", out string xml, logErrors: false) == 0;
            var state = new TaskState(exists, exists ? xml : string.Empty);
            _startsAtLogon = state.AtLogon;
            return state;
        }

        public static bool RegisterTask(bool atLogon)
        {
            string path = Path.Combine(Path.GetTempPath(), "Buffer-task.xml");
            try
            {
                File.WriteAllText(path, TaskXml(atLogon), Encoding.Unicode);
                if (Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + path + "\" /F", out _) != 0)
                    return false;
                _startsAtLogon = atLogon;
                return true;
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                return false;
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }

        public static void DeleteTask()
        {
            Schtasks("/Delete /TN \"" + TaskName + "\" /F", out _);
            _startsAtLogon = false;
        }

        public static bool RunTask() => Schtasks("/Run /TN \"" + TaskName + "\"", out _) == 0;

        public static bool RelaunchElevated(string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppPaths.ExePath, arguments) { UseShellExecute = true, Verb = "runas" });
                return true;
            }
            catch (Win32Exception)
            {
                // В окне UAC отказались повышать права.
                return false;
            }
        }

        // Без ExecutionTimeLimit=PT0S планировщик завершит программу через 72 часа,
        // а без разрешения работы от батареи на ноутбуке она не запустится.
        static string TaskXml(bool atLogon)
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string command = SecurityElement.Escape(AppPaths.ExePath);
            var xml = new StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            xml.AppendLine("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            xml.AppendLine("  <RegistrationInfo><Description>Buffer</Description></RegistrationInfo>");
            if (atLogon)
                xml.AppendLine("  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>");
            xml.AppendLine("  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>");
            xml.AppendLine("  <Settings>");
            xml.AppendLine("    <AllowStartOnDemand>true</AllowStartOnDemand>");
            xml.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            xml.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            xml.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            xml.AppendLine("    <Enabled>true</Enabled>");
            xml.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            xml.AppendLine("    <Priority>5</Priority>");
            xml.AppendLine("  </Settings>");
            xml.AppendLine("  <Actions Context=\"Author\"><Exec><Command>" + command + "</Command></Exec></Actions>");
            xml.AppendLine("</Task>");
            return xml.ToString();
        }

        static int Schtasks(string arguments, out string output, bool logErrors = true)
        {
            output = string.Empty;
            try
            {
                var info = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                };
                using (var process = Process.Start(info))
                {
                    var error = process.StandardError.ReadToEndAsync();
                    output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0 && logErrors)
                        ErrorLog.Write("schtasks " + arguments + ": " + error.Result.Trim());
                    return process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                return -1;
            }
        }
    }

    sealed class TaskState
    {
        readonly string _xml;

        public TaskState(bool exists, string xml)
        {
            Exists = exists;
            _xml = xml;
        }

        public bool Exists { get; }
        public bool AtLogon => _xml.Contains("<LogonTrigger>");

        public bool PointsTo(string exePath) => _xml.Contains("<Command>" + SecurityElement.Escape(exePath) + "</Command>");
    }
}
