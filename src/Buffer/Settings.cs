using System;
using System.IO;

namespace Buffer
{
    sealed class Settings
    {
        public Hotkey Hotkey { get; set; } = Hotkey.Default;
        public bool SaveEverything { get; set; }
        public bool UnlimitedHistory { get; set; }
        public bool RunAsAdmin { get; set; }

        // system, en или ru
        public string Language { get; set; } = "system";

        static string FilePath => Path.Combine(AppPaths.DataFolder, "settings.ini");

        public static bool Exists => File.Exists(FilePath);

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                    return settings;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;
                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    switch (key)
                    {
                        case "hotkey":
                            if (Hotkey.TryParse(value, out var hotkey) && hotkey.Validate() == null)
                                settings.Hotkey = hotkey;
                            break;
                        case "saveEverything":
                            settings.SaveEverything = value == "1";
                            break;
                        case "unlimitedHistory":
                            settings.UnlimitedHistory = value == "1";
                            break;
                        case "runAsAdmin":
                            settings.RunAsAdmin = value == "1";
                            break;
                        case "language":
                            if (value == "en" || value == "ru" || value == "system")
                                settings.Language = value;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataFolder);
                File.WriteAllLines(FilePath, new[]
                {
                    "hotkey=" + Hotkey.ToStorageString(),
                    "saveEverything=" + (SaveEverything ? "1" : "0"),
                    "unlimitedHistory=" + (UnlimitedHistory ? "1" : "0"),
                    "runAsAdmin=" + (RunAsAdmin ? "1" : "0"),
                    "language=" + Language,
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }
    }
}
