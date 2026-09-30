using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Buffer
{
    public partial class App : Application
    {
        const string MutexName = @"Local\Buffer.SingleInstance.6F1C3E2A";

        const int CommandOpen = 1;
        const int CommandSaveEverything = 2;
        const int CommandShortcut = 3;
        const int CommandAutostart = 4;
        const int CommandKeepAll = 5;
        const int CommandRunAsAdmin = 6;
        const int CommandLanguageSystem = 10;
        const int CommandLanguageEnglish = 11;
        const int CommandLanguageRussian = 12;
        const int CommandExit = 20;

        Mutex _mutex;
        bool _ownsMutex;
        Settings _settings;
        ThemeManager _theme;
        ClipboardHistory _history;
        HistoryStorage _storage;
        MessageWindow _messages;
        ClipboardMonitor _monitor;
        KeyboardHook _hook;
        TrayIcon _tray;
        FlyoutWindow _flyout;
        HotkeyWindow _hotkeyWindow;
        DispatcherTimer _saveTimer;
        Task _processing = Task.CompletedTask;
        ThumbnailSpec _thumbnails;
        bool _themeRefreshQueued;
        bool _saveAllOnExit;
        bool _historyRestored;
        bool _saveAfterRestore;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool exitRequested = HasArgument(e.Args, "--exit");
            bool adminHandover = HasArgument(e.Args, "--admin-on");
            _mutex = new Mutex(true, MutexName, out _ownsMutex);
            // С этим ключом программа перезапускает сама себя с правами администратора:
            // прежний экземпляр в это время закрывается, его надо дождаться.
            if (!_ownsMutex && adminHandover)
                _ownsMutex = WaitForMutex(_mutex);
            if (!_ownsMutex || exitRequested)
            {
                if (!_ownsMutex)
                    MessageWindow.SignalRunningInstance(exitRequested ? MessageWindow.WM_EXIT_APP : MessageWindow.WM_SHOW_FLYOUT);
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, args) =>
            {
                ErrorLog.Write(args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) => ErrorLog.Write(args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                ErrorLog.Write(args.Exception);
                args.SetObserved();
            };

            bool firstRun = !Settings.Exists;
            _settings = Settings.Load();
            if (_settings.RunAsAdmin && !adminHandover && !AdminMode.IsElevated && StartElevatedInstead())
                return;
            Loc.Instance.SetLanguage(_settings.Language);

            _theme = new ThemeManager(this, ParseThemeOverride(e.Args));
            _theme.Refresh();

            _thumbnails = new ThumbnailSpec(NativeMethods.GetDpiForSystem() / 96.0);
            _history = new ClipboardHistory { Unlimited = _settings.UnlimitedHistory };
            _storage = new HistoryStorage();
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (s, args) =>
            {
                _saveTimer.Stop();
                SaveHistory();
            };
            _history.Changed += () =>
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            };

            _flyout = new FlyoutWindow(_history, _theme.IsDark);
            _flyout.ItemChosen += OnItemChosen;
            _flyout.Prepare();

            _messages = new MessageWindow { Handler = OnMessage };
            NativeMethods.WTSRegisterSessionNotification(_messages.Handle, 0);

            _hook = new KeyboardHook(Dispatcher) { Hotkey = _settings.Hotkey };
            _hook.HotkeyPressed += OnHotkeyPressed;
            _hook.Start();

            _monitor = new ClipboardMonitor(_messages.Handle) { CapturePrivate = () => _settings.SaveEverything };
            _monitor.Captured += OnCaptured;

            _tray = new TrayIcon(_messages.Handle);
            _tray.Click += OnTrayClick;
            _tray.MenuRequested += ShowTrayMenu;
            _tray.Show(_theme.IsTaskbarDark, Loc.Instance.TrayTip(_settings.Hotkey));

            if (firstRun)
            {
                // История должна пережить перезагрузку, а для этого программа должна стартовать вместе с Windows.
                Autostart.Set(true);
                _settings.Save();
                _tray.ShowBalloon(Loc.Instance.WelcomeTitle, Loc.Instance.WelcomeText(_settings.Hotkey));
            }
            else if (!_settings.RunAsAdmin)
            {
                Autostart.UpdatePath();
            }

            if (adminHandover && AdminMode.IsElevated)
                ApplyAdminMode();
            else if (AdminMode.IsElevated)
                SyncAdminTask(_settings.RunAsAdmin);

            RestoreHistory();
        }

        static bool HasArgument(string[] args, string name) =>
            args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        static bool WaitForMutex(Mutex mutex)
        {
            try
            {
                return mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }

        // Режим администратора включён, а программу запустили без прав. Запуск передаётся задаче
        // планировщика (без окна UAC), а если её нет, программа перезапускается через UAC.
        // Мьютекс отпускается заранее, иначе новый экземпляр решит, что он второй, и закроется.
        bool StartElevatedInstead()
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
            _ownsMutex = false;
            if (AdminMode.RunTask() || AdminMode.RelaunchElevated(string.Empty))
            {
                Shutdown();
                return true;
            }
            _mutex = new Mutex(true, MutexName, out _ownsMutex);
            if (_ownsMutex)
                return false;
            Shutdown();
            return true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_ownsMutex)
            {
                _saveTimer?.Stop();
                if (_history != null)
                    SaveHistory(_saveAllOnExit);
                _storage?.Flush(TimeSpan.FromSeconds(5));
                _hook?.Dispose();
                _monitor?.Dispose();
                _tray?.Dispose();
                if (_messages != null)
                {
                    NativeMethods.WTSUnRegisterSessionNotification(_messages.Handle);
                    _messages.Dispose();
                }
                _flyout?.CloseForExit();
                _mutex.ReleaseMutex();
            }
            _mutex?.Dispose();
            base.OnExit(e);
        }

        // --theme=light и --theme=dark нужны, чтобы проверить оформление, не переключая тему Windows.
        static bool? ParseThemeOverride(string[] args)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--theme=light", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (string.Equals(arg, "--theme=dark", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return null;
        }

        async void RestoreHistory()
        {
            var storage = _storage;
            var spec = _thumbnails;
            List<ClipItem> restored = await Task.Run(() => storage.Load(spec));
            _history.AddRestored(restored);
            _historyRestored = true;
            if (_saveAfterRestore)
                SaveHistory();
            _monitor.CaptureNow();
        }

        // То, что лежало в буфере при запуске, не поднимается наверх: это может быть старая запись,
        // выбранная из истории перед перезапуском программы.
        void OnCaptured(CapturedClip clip, bool initial)
        {
            var spec = _thumbnails;
            _processing = _processing
                .ContinueWith(_ => ClipItemFactory.Create(clip, spec), TaskScheduler.Default)
                .ContinueWith(task =>
                {
                    if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                        _history.Add(task.Result, moveExistingToTop: !initial);
                    else if (task.Exception != null)
                        ErrorLog.Write(task.Exception);
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void OnHotkeyPressed(IntPtr foreground)
        {
            if (_flyout.IsOpen)
            {
                _flyout.HideFlyout(true);
                return;
            }
            var anchor = CaretLocator.Find(foreground) ?? CursorRect();
            _flyout.Open(foreground, anchor, fromTray: false);
        }

        void OnTrayClick(int x, int y)
        {
            if (_flyout.IsOpen)
            {
                _flyout.HideFlyout(false);
                return;
            }
            // Щелчок по значку сначала отнимает фокус у открытого окна, и оно прячется.
            // Без паузы тот же щелчок сразу открыл бы его снова.
            if ((DateTime.UtcNow - _flyout.HiddenAt).TotalMilliseconds < 400)
                return;
            _flyout.Open(IntPtr.Zero, new NativeMethods.RECT(x, y, x, y), fromTray: true);
        }

        static NativeMethods.RECT CursorRect()
        {
            NativeMethods.GetCursorPos(out var point);
            return new NativeMethods.RECT(point.X, point.Y, point.X, point.Y);
        }

        void OnItemChosen(ClipItem item)
        {
            IntPtr target = _flyout.Target;
            bool paste = !_flyout.OpenedFromTray && target != IntPtr.Zero;
            _flyout.HideFlyout(false);
            uint sequence = ClipboardWriter.Write(item, _messages.Handle);
            _monitor.IgnoreOwnChange(sequence);
            if (paste)
                _ = Paster.PasteInto(target);
        }

        void ShowTrayMenu(int x, int y)
        {
            _flyout.HideFlyout(false);
            var loc = Loc.Instance;
            int command;
            using (var menu = new NativeMenu())
            {
                var languages = new NativeMenu();
                languages.Add(CommandLanguageSystem, loc.LanguageSystem);
                languages.Add(CommandLanguageEnglish, "English");
                languages.Add(CommandLanguageRussian, "Русский");
                int currentLanguage = _settings.Language == "en" ? CommandLanguageEnglish
                                    : _settings.Language == "ru" ? CommandLanguageRussian
                                    : CommandLanguageSystem;
                languages.CheckRadio(CommandLanguageSystem, CommandLanguageRussian, currentLanguage);

                menu.Add(CommandOpen, loc.OpenHistory + "\t" + _settings.Hotkey);
                menu.SetDefault(CommandOpen);
                menu.AddSeparator();
                menu.Add(CommandSaveEverything, loc.SaveEverything, _settings.SaveEverything);
                menu.Add(CommandKeepAll, loc.KeepAllItems, _settings.UnlimitedHistory);
                menu.Add(CommandShortcut, loc.ChangeShortcut);
                menu.Add(CommandAutostart, loc.StartWithWindows, AutostartEnabled);
                menu.Add(CommandRunAsAdmin, loc.RunAsAdmin, _settings.RunAsAdmin);
                menu.AddSubmenu(languages, loc.Language);
                menu.AddSeparator();
                menu.Add(CommandExit, loc.Exit);
                command = menu.Show(_messages.Handle, x, y, _theme.IsTaskbarDark);
            }

            switch (command)
            {
                case CommandOpen:
                    _flyout.Open(IntPtr.Zero, new NativeMethods.RECT(x, y, x, y), fromTray: true);
                    break;
                case CommandSaveEverything:
                    ToggleSaveEverything();
                    break;
                case CommandKeepAll:
                    _settings.UnlimitedHistory = !_settings.UnlimitedHistory;
                    _settings.Save();
                    _history.Unlimited = _settings.UnlimitedHistory;
                    break;
                case CommandShortcut:
                    ShowHotkeyWindow();
                    break;
                case CommandAutostart:
                    SetAutostart(!AutostartEnabled);
                    break;
                case CommandRunAsAdmin:
                    if (_settings.RunAsAdmin)
                        DisableAdminMode();
                    else
                        EnableAdminMode();
                    break;
                case CommandLanguageSystem:
                    SetLanguage("system");
                    break;
                case CommandLanguageEnglish:
                    SetLanguage("en");
                    break;
                case CommandLanguageRussian:
                    SetLanguage("ru");
                    break;
                case CommandExit:
                    Shutdown();
                    break;
            }
        }

        void ToggleSaveEverything()
        {
            _settings.SaveEverything = !_settings.SaveEverything;
            _settings.Save();
            if (_settings.SaveEverything)
                _tray.ShowBalloon(Loc.Instance.SaveOnTitle, Loc.Instance.SaveOnText);
            else
                _history.RemovePrivateUnpinned();
            SaveHistory();
        }

        // Без галочки на диске остаются только закреплённые элементы, как в Windows.
        // Перед перезапуском с правами администратора сохраняется всё, чтобы новый экземпляр ничего не потерял.
        // Пока история не прочитана с диска, запись удалила бы ещё не прочитанные файлы, поэтому она ждёт конца загрузки.
        void SaveHistory(bool all = false)
        {
            if (!_historyRestored)
            {
                _saveAfterRestore = true;
                return;
            }
            var items = _history.Items.Where(i => all || _settings.SaveEverything || i.IsPinned).ToList();
            _storage.Save(items);
        }

        // В режиме администратора автозапуском управляет задача планировщика, иначе ключ Run.
        bool AutostartEnabled => _settings.RunAsAdmin ? AdminMode.StartsAtLogon : Autostart.IsEnabled;

        void SetAutostart(bool enabled)
        {
            if (!_settings.RunAsAdmin)
                Autostart.Set(enabled);
            else if (!AdminMode.RegisterTask(enabled))
                _tray.ShowBalloon("Buffer", Loc.Instance.AdminFailedText);
        }

        void EnableAdminMode()
        {
            if (AdminMode.IsElevated)
            {
                ApplyAdminMode();
                return;
            }
            // Поднять права можно только перезапуском. Новый экземпляр получит ключ --admin-on и сам всё настроит.
            if (!AdminMode.RelaunchElevated("--admin-on"))
                return;
            _saveAllOnExit = true;
            Shutdown();
        }

        void ApplyAdminMode()
        {
            if (!AdminMode.RegisterTask(Autostart.IsEnabled))
            {
                _tray.ShowBalloon("Buffer", Loc.Instance.AdminFailedText);
                return;
            }
            Autostart.Set(false);
            _settings.RunAsAdmin = true;
            _settings.Save();
            _tray.ShowBalloon(Loc.Instance.AdminOnTitle, Loc.Instance.AdminOnText);
        }

        void DisableAdminMode()
        {
            bool autostart = AdminMode.StartsAtLogon;
            AdminMode.DeleteTask();
            _settings.RunAsAdmin = false;
            _settings.Save();
            if (autostart)
                Autostart.Set(true);
        }

        // Задача должна указывать на текущий exe, а после выключения режима её не должно остаться.
        // Запросы к планировщику идут отдельными процессами, поэтому в фоне.
        static void SyncAdminTask(bool runAsAdmin)
        {
            Task.Run(() =>
            {
                var task = AdminMode.Query();
                if (runAsAdmin && (!task.Exists || !task.PointsTo(AppPaths.ExePath)))
                {
                    AdminMode.RegisterTask(!task.Exists || task.AtLogon);
                }
                else if (!runAsAdmin && task.Exists)
                {
                    AdminMode.DeleteTask();
                    if (task.AtLogon)
                        Autostart.Set(true);
                }
            });
        }

        void ShowHotkeyWindow()
        {
            if (_hotkeyWindow != null)
            {
                _hotkeyWindow.Activate();
                return;
            }
            _hotkeyWindow = new HotkeyWindow(_hook, _settings.Hotkey, _theme.IsDark, SetHotkey);
            _hotkeyWindow.Closed += (s, e) => _hotkeyWindow = null;
            _hotkeyWindow.Show();
            _hotkeyWindow.Activate();
        }

        void SetHotkey(Hotkey hotkey)
        {
            _settings.Hotkey = hotkey;
            _settings.Save();
            _hook.Hotkey = hotkey;
            _tray.Update(_theme.IsTaskbarDark, Loc.Instance.TrayTip(hotkey));
        }

        void SetLanguage(string language)
        {
            _settings.Language = language;
            _settings.Save();
            Loc.Instance.SetLanguage(language);
            _history.RefreshTexts();
            _tray.Update(_theme.IsTaskbarDark, Loc.Instance.TrayTip(_settings.Hotkey));
        }

        bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
        {
            if (_tray != null && _tray.HandleMessage(msg, wParam, lParam))
                return true;

            switch (msg)
            {
                case NativeMethods.WM_CLIPBOARDUPDATE:
                    _monitor?.OnClipboardUpdate();
                    return true;

                case MessageWindow.WM_SHOW_FLYOUT:
                    if (_flyout != null && !_flyout.IsOpen)
                        _flyout.Open(IntPtr.Zero, CursorRect(), fromTray: true);
                    return true;

                case MessageWindow.WM_EXIT_APP:
                    Shutdown();
                    return true;

                case NativeMethods.WM_SETTINGCHANGE:
                    if (wParam.ToInt64() == NativeMethods.SPI_SETHIGHCONTRAST ||
                        (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet"))
                        QueueThemeRefresh();
                    break;

                case NativeMethods.WM_SYSCOLORCHANGE:
                case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                    QueueThemeRefresh();
                    break;

                case NativeMethods.WM_WTSSESSION_CHANGE:
                    if (wParam.ToInt64() == NativeMethods.WTS_SESSION_UNLOCK)
                        _hook?.Reinstall();
                    break;

                case NativeMethods.WM_POWERBROADCAST:
                    if (wParam.ToInt64() == NativeMethods.PBT_APMRESUMEAUTOMATIC)
                        _hook?.Reinstall();
                    break;
            }
            return false;
        }

        void QueueThemeRefresh()
        {
            if (_themeRefreshQueued || _theme == null)
                return;
            _themeRefreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _themeRefreshQueued = false;
                _theme.Refresh();
                _flyout?.ApplyTheme(_theme.IsDark);
                _hotkeyWindow?.ApplyTheme(_theme.IsDark);
                _tray?.Update(_theme.IsTaskbarDark, Loc.Instance.TrayTip(_settings.Hotkey));
            }));
        }
    }
}
