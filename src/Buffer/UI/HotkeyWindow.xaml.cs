using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Buffer
{
    public partial class HotkeyWindow : Window
    {
        readonly KeyboardHook _hook;
        readonly Action<Hotkey> _save;
        Hotkey _hotkey;
        bool _dark;
        bool _showingHeldModifiers;

        internal HotkeyWindow(KeyboardHook hook, Hotkey current, bool dark, Action<Hotkey> save)
        {
            InitializeComponent();
            _hook = hook;
            _save = save;
            _dark = dark;
            _hook.Captured += OnCaptured;
            _hook.CaptureModifiersChanged += OnModifiersChanged;
            ShowHotkey(current);
        }

        internal void ApplyTheme(bool dark)
        {
            _dark = dark;
            WindowStyling.Apply(this, dark, NativeMethods.DWMSBT_MAINWINDOW);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowStyling.RemoveSystemMenu(this);
            WindowStyling.Apply(this, _dark, NativeMethods.DWMSBT_MAINWINDOW);
        }

        // Клавиши перехватываются только пока окно активно, иначе они пропадали бы во всей системе.
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _hook.Capturing = true;
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            _hook.Capturing = false;
            _showingHeldModifiers = false;
            ShowHotkey(_hotkey);
        }

        protected override void OnClosed(EventArgs e)
        {
            _hook.Capturing = false;
            _hook.Captured -= OnCaptured;
            _hook.CaptureModifiersChanged -= OnModifiersChanged;
            base.OnClosed(e);
        }

        void OnCaptured(Hotkey hotkey)
        {
            if (!IsActive)
                return;
            _showingHeldModifiers = false;
            ShowHotkey(hotkey);
        }

        // Пока зажаты только модификаторы, показываем их. После ввода полного сочетания
        // отпускание клавиш уже ничего не меняет.
        void OnModifiersChanged(HotkeyModifiers held, bool pressed)
        {
            if (!IsActive || (!pressed && !_showingHeldModifiers))
                return;
            if (held == HotkeyModifiers.None)
            {
                _showingHeldModifiers = false;
                ShowHotkey(_hotkey);
                return;
            }
            _showingHeldModifiers = true;
            Keys.ItemsSource = Hotkey.ModifierNames(held).ToList();
            SaveButton.IsEnabled = false;
        }

        void ShowHotkey(Hotkey hotkey)
        {
            _hotkey = hotkey;
            Keys.ItemsSource = hotkey.Parts().ToList();
            string error = hotkey.Validate();
            ErrorText.Text = error ?? string.Empty;
            ErrorPanel.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
            SaveButton.IsEnabled = error == null;
        }

        void Reset_Click(object sender, RoutedEventArgs e) => ShowHotkey(Hotkey.Default);

        void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_hotkey.Validate() != null)
                return;
            _save(_hotkey);
            Close();
        }

        void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
