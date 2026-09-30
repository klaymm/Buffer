using System;
using System.ComponentModel;
using System.Globalization;

namespace Buffer
{
    public sealed class Loc : INotifyPropertyChanged
    {
        bool _russian;

        public static Loc Instance { get; } = new Loc();

        public event PropertyChangedEventHandler PropertyChanged;

        public void SetLanguage(string language)
        {
            _russian = language == "ru" ||
                (language == "system" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        string T(string english, string russian) => _russian ? russian : english;

        public string Clipboard => T("Clipboard", "Буфер обмена");
        public string ClearAll => T("Clear all", "Очистить все");
        public string NothingHere => T("Nothing here", "Здесь пусто");
        public string NothingHereHint => T("You'll see your clipboard history here once you've copied something.",
                                           "Здесь появится журнал буфера обмена, как только вы что-нибудь скопируете.");
        public string SeeMore => T("See more", "Подробнее");
        public string PinItem => T("Pin item", "Закрепить элемент");
        public string UnpinItem => T("Unpin item", "Открепить элемент");
        public string Delete => T("Delete", "Удалить");

        public string OpenHistory => T("Open clipboard history", "Открыть журнал буфера обмена");
        public string SaveEverything => T("Save everything, even after restart", "Сохранять всё, даже после перезагрузки");
        public string KeepAllItems => T("Keep all items (no 50-item limit)", "Хранить все записи (без лимита в 50)");
        public string ChangeShortcut => T("Change shortcut…", "Сменить сочетание клавиш…");
        public string StartWithWindows => T("Start with Windows", "Запускать вместе с Windows");
        public string RunAsAdmin => T("Run as administrator", "Запускать от имени администратора");
        public string Language => T("Language", "Язык");
        public string LanguageSystem => T("Same as Windows", "Как в Windows");
        public string Exit => T("Exit", "Выход");

        public string WelcomeTitle => T("Buffer is running", "Buffer запущен");
        public string SaveOnTitle => T("Saving everything", "Сохраняю всё");
        public string SaveOnText => T("Everything you copy, including passwords, is now kept even after a restart. It's encrypted for your Windows account.",
                                      "Всё, что вы копируете, включая пароли, теперь сохраняется и после перезагрузки. Данные зашифрованы ключом вашей учётной записи Windows.");
        public string AdminOnTitle => T("Running as administrator", "Запущено от имени администратора");
        public string AdminOnText => T("Buffer can now paste into windows opened as administrator.",
                                       "Теперь Buffer может вставлять и в окна, открытые от имени администратора.");
        public string AdminFailedText => T("Couldn't set up running as administrator.",
                                           "Не получилось настроить запуск от имени администратора.");

        public string ShortcutTitle => T("Activation shortcut", "Сочетание клавиш");
        public string ShortcutHint => T("Press the keys you want to use to open clipboard history.",
                                        "Нажмите клавиши, которыми будет открываться журнал буфера обмена.");
        public string ShortcutNeedsKey => T("Add a regular key to the combination.", "Добавьте к сочетанию обычную клавишу.");
        public string ShortcutNeedsModifier => T("Use Win, Ctrl or Alt together with another key.",
                                                 "Нужна клавиша Win, Ctrl или Alt вместе с другой клавишей.");
        public string ShortcutReservedClipboard => T("This shortcut is needed for copying and pasting.",
                                                     "Это сочетание нужно для копирования и вставки.");
        public string ShortcutReservedSystem => T("Windows uses this shortcut.", "Это сочетание занято Windows.");
        public string Reset => T("Reset", "Сбросить");
        public string Save => T("Save", "Сохранить");
        public string Cancel => T("Cancel", "Отмена");

        internal string WelcomeText(Hotkey hotkey) => string.Format(
            T("Press {0} to open clipboard history. Settings are in the tray icon menu.",
              "Нажмите {0}, чтобы открыть журнал буфера обмена. Настройки в меню значка в трее."), hotkey);

        internal string TrayTip(Hotkey hotkey) => string.Format(T("Buffer - clipboard history ({0})", "Buffer - журнал буфера обмена ({0})"), hotkey);

        public string ImageDescription(int width, int height) => string.Format(T("Image, {0} × {1}", "Изображение, {0} × {1}"), width, height);

        public string FilesDescription(int count) => _russian
            ? string.Format("{0} {1}", count, Plural(count, "файл", "файла", "файлов"))
            : string.Format(count == 1 ? "{0} file" : "{0} files", count);

        public string MoreFiles(int count) => string.Format(T("and {0} more", "и ещё {0}"), count);

        static string Plural(int count, string one, string few, string many)
        {
            int n = Math.Abs(count) % 100;
            int last = n % 10;
            if (n > 10 && n < 20)
                return many;
            if (last == 1)
                return one;
            if (last >= 2 && last <= 4)
                return few;
            return many;
        }
    }
}
