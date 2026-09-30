using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Buffer
{
    sealed class ThemeManager
    {
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

        readonly Application _app;
        readonly bool? _forcedDark;

        public ThemeManager(Application app, bool? forcedDark)
        {
            _app = app;
            _forcedDark = forcedDark;
        }

        public bool IsDark { get; private set; }
        public bool IsTaskbarDark { get; private set; }

        public void Refresh()
        {
            IsDark = _forcedDark ?? ReadFlag("AppsUseLightTheme", 1) == 0;
            IsTaskbarDark = ReadFlag("SystemUsesLightTheme", 0) == 0;

            ResourceDictionary palette;
            if (SystemParameters.HighContrast)
            {
                palette = BuildHighContrast();
            }
            else
            {
                palette = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Buffer;component/Themes/" + (IsDark ? "Dark" : "Light") + ".xaml"),
                };
                AddAccent(palette);
            }
            _app.Resources.MergedDictionaries[0] = palette;
        }

        static int ReadFlag(string name, int fallback)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                    return key?.GetValue(name) is int value ? value : fallback;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        // Оттенки акцента как в WinUI: светлый второй для тёмной темы, тёмный первый для светлой.
        void AddAccent(ResourceDictionary palette)
        {
            Color accent = IsDark ? Color.FromRgb(0x4C, 0xC2, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AccentKey))
                {
                    if (key?.GetValue("AccentPalette") is byte[] colors && colors.Length >= 32)
                    {
                        int index = IsDark ? 1 : 4;
                        accent = Color.FromRgb(colors[index * 4], colors[index * 4 + 1], colors[index * 4 + 2]);
                    }
                }
            }
            catch (Exception)
            {
            }
            palette["AccentFillColorDefaultBrush"] = Brush(accent, 1.0);
            palette["AccentFillColorSecondaryBrush"] = Brush(accent, 0.9);
            palette["AccentFillColorTertiaryBrush"] = Brush(accent, 0.8);
        }

        static SolidColorBrush Brush(Color color, double opacity)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }

        static ResourceDictionary BuildHighContrast()
        {
            Brush window = SystemColors.WindowBrush;
            Brush text = SystemColors.WindowTextBrush;
            Brush highlight = SystemColors.HighlightBrush;
            Brush highlightText = SystemColors.HighlightTextBrush;
            Brush disabled = SystemColors.GrayTextBrush;
            Brush button = SystemColors.ControlBrush;
            Brush buttonText = SystemColors.ControlTextBrush;

            return new ResourceDictionary
            {
                ["TextFillColorPrimaryBrush"] = text,
                ["TextFillColorSecondaryBrush"] = text,
                ["TextFillColorDisabledBrush"] = disabled,
                ["TextOnAccentFillColorPrimaryBrush"] = highlightText,
                ["CardBackgroundFillColorDefaultBrush"] = window,
                ["CardBackgroundFillColorHoverBrush"] = window,
                ["CardBackgroundFillColorPressedBrush"] = window,
                ["CardStrokeColorDefaultBrush"] = text,
                ["ControlFillColorDefaultBrush"] = button,
                ["ControlFillColorSecondaryBrush"] = button,
                ["ControlFillColorTertiaryBrush"] = button,
                ["ControlFillColorDisabledBrush"] = button,
                ["ControlStrokeColorDefaultBrush"] = buttonText,
                ["ControlElevationBorderBrush"] = buttonText,
                ["SubtleFillColorSecondaryBrush"] = window,
                ["SubtleFillColorTertiaryBrush"] = window,
                ["FocusStrokeColorOuterBrush"] = highlight,
                ["FocusStrokeColorInnerBrush"] = window,
                ["DividerStrokeColorDefaultBrush"] = text,
                ["FlyoutBackgroundBrush"] = window,
                ["FlyoutStrokeBrush"] = text,
                ["WindowFallbackBrush"] = window,
                ["PanelBackgroundBrush"] = window,
                ["PanelBorderBrush"] = text,
                ["ScrollBarThumbBrush"] = text,
                ["LayerFillColorDefaultBrush"] = window,
                ["AccentFillColorDefaultBrush"] = highlight,
                ["AccentFillColorSecondaryBrush"] = highlight,
                ["AccentFillColorTertiaryBrush"] = highlight,
                ["SystemFillColorCautionBrush"] = text,
            };
        }
    }
}
