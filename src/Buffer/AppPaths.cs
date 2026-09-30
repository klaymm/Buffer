using System;
using System.IO;
using System.Reflection;

namespace Buffer
{
    static class AppPaths
    {
        public static string DataFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buffer");

        public static string ExePath { get; } = Assembly.GetEntryAssembly()?.Location ?? string.Empty;
    }

    static class ErrorLog
    {
        const long MaxSize = 512 * 1024;
        static readonly object Lock = new object();

        public static void Write(Exception ex)
        {
            if (ex != null)
                Write(ex.ToString());
        }

        public static void Write(string message)
        {
            try
            {
                lock (Lock)
                {
                    Directory.CreateDirectory(AppPaths.DataFolder);
                    string path = Path.Combine(AppPaths.DataFolder, "errors.log");
                    if (File.Exists(path) && new FileInfo(path).Length > MaxSize)
                        File.Delete(path);
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
