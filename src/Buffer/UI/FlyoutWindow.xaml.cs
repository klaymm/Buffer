using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Buffer
{
    public static class CardState
    {
        public static readonly DependencyProperty IsPressedProperty =
            DependencyProperty.RegisterAttached("IsPressed", typeof(bool), typeof(CardState), new PropertyMetadata(false));

        public static bool GetIsPressed(DependencyObject element) => (bool)element.GetValue(IsPressedProperty);

        public static void SetIsPressed(DependencyObject element, bool value) => element.SetValue(IsPressedProperty, value);
    }

    public partial class FlyoutWindow : Window
    {
        public static readonly DependencyProperty ShowFocusRingProperty =
            DependencyProperty.Register(nameof(ShowFocusRing), typeof(bool), typeof(FlyoutWindow), new PropertyMetadata(true));

        // Прокрутку списка нельзя анимировать напрямую, поэтому анимируется это свойство.
        public static readonly DependencyProperty ScrollOffsetProperty =
            DependencyProperty.Register(nameof(ScrollOffset), typeof(double), typeof(FlyoutWindow),
                new PropertyMetadata(0.0, (d, e) => ((FlyoutWindow)d).Scroll?.ScrollToVerticalOffset((double)e.NewValue)));

        // Панель того же размера, что у Windows. Вокруг неё окно шире: там рисуется тень.
        const double PanelWidth = 360;
        const double PanelHeight = 400;
        static readonly Thickness ShadowMargin = new Thickness(32, 24, 32, 40);

        const double EnterOffset = 24;
        const double ActionsWidth = 81;
        const double WheelStep = 48;

        readonly ClipboardHistory _history;
        readonly HashSet<ClipItem> _leaving = new HashSet<ClipItem>();
        ListBoxItem _pressed;
        ClipItem _revealed;
        bool _closing;
        bool _allowClose;
        bool _clearing;
        bool _wasEmpty;
        bool _scrolling;
        double _scrollTarget;

        internal FlyoutWindow(ClipboardHistory history, bool dark)
        {
            InitializeComponent();
            _history = history;
            _wasEmpty = history.IsEmpty;
            DataContext = history;
            ApplyTheme(dark);
            history.PropertyChanged += OnHistoryPropertyChanged;
        }

        public bool ShowFocusRing
        {
            get => (bool)GetValue(ShowFocusRingProperty);
            set => SetValue(ShowFocusRingProperty, value);
        }

        public double ScrollOffset
        {
            get => (double)GetValue(ScrollOffsetProperty);
            set => SetValue(ScrollOffsetProperty, value);
        }

        internal IntPtr Target { get; private set; }
        internal bool OpenedFromTray { get; private set; }
        internal DateTime HiddenAt { get; private set; }
        internal bool IsOpen => IsVisible && !_closing;

        internal event Action<ClipItem> ItemChosen;

        IntPtr Handle => new WindowInteropHelper(this).Handle;
        ScrollViewer Scroll => List.Template?.FindName("Scroll", List) as ScrollViewer;

        internal void Prepare() => new WindowInteropHelper(this).EnsureHandle();

        internal void ApplyTheme(bool dark) => PanelShadow.Opacity = dark ? 0.45 : 0.22;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowStyling.HideFromTaskSwitcher(this);
        }

        internal void Open(IntPtr target, NativeMethods.RECT anchor, bool fromTray)
        {
            _closing = false;
            Target = target;
            OpenedFromTray = fromTray;
            ShowFocusRing = !fromTray;
            Collapse(false);
            PlaceNear(anchor, fromTray);
            Motion.Set(this, OpacityProperty, 0.0);
            Show();
            BringToFront();
            Motion.Double(this, OpacityProperty, 0, 1, 167, Motion.Linear);
            Motion.Double(SurfaceShift, TranslateTransform.YProperty, EnterOffset, 0, 333, Motion.Decelerate);
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(FocusFirst));
        }

        // Фокус возвращается сразу, а окно ещё мгновение гаснет, как в Windows.
        internal void HideFlyout(bool restoreFocus)
        {
            if (!IsVisible || _closing)
                return;
            _closing = true;
            Collapse(false);
            ReleasePressed();
            StopScrolling();
            HiddenAt = DateTime.UtcNow;
            if (restoreFocus && Target != IntPtr.Zero && NativeMethods.IsWindow(Target))
                NativeMethods.SetForegroundWindow(Target);
            Motion.Double(this, OpacityProperty, null, 0, 100, Motion.Linear, completed: () =>
            {
                if (!_closing)
                    return;
                _closing = false;
                Hide();
            });
        }

        internal void CloseForExit()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // Alt+F4 не должен уничтожать окно: оно создаётся один раз на всё время работы.
            if (!_allowClose)
            {
                e.Cancel = true;
                HideFlyout(true);
            }
            base.OnClosing(e);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            HideFlyout(false);
        }

        void PlaceNear(NativeMethods.RECT anchor, bool fromTray)
        {
            var point = new NativeMethods.POINT { X = anchor.Left, Y = anchor.Bottom };
            IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            NativeMethods.GetMonitorInfo(monitor, ref info);
            uint dpi = 96;
            try
            {
                NativeMethods.GetDpiForMonitor(monitor, 0, out dpi, out _);
            }
            catch (Exception)
            {
                dpi = 96;
            }

            double scale = dpi / 96.0;
            int width = Scale(PanelWidth, scale);
            int height = Scale(PanelHeight, scale);
            int gap = Scale(8, scale);
            var work = info.rcWork;

            // Смещение от каретки снято с панели Windows: на 9 px правее и на 4 px ниже.
            int caretOffsetX = Scale(9, scale);
            int caretGap = Scale(4, scale);

            int x, y;
            if (fromTray)
            {
                x = anchor.Left - width / 2;
                y = anchor.Top - gap - height;
            }
            else
            {
                x = anchor.Left + caretOffsetX;
                y = anchor.Bottom + caretGap;
                if (y + height > work.Bottom - gap)
                {
                    int above = anchor.Top - caretGap - height;
                    y = above >= work.Top + gap ? above : work.Bottom - gap - height;
                }
            }
            x = Clamp(x, work.Left + gap, work.Right - gap - width);
            y = Clamp(y, work.Top + gap, work.Bottom - gap - height);

            int left = Scale(ShadowMargin.Left, scale);
            int top = Scale(ShadowMargin.Top, scale);
            int right = Scale(ShadowMargin.Right, scale);
            int bottom = Scale(ShadowMargin.Bottom, scale);
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, x - left, y - top,
                width + left + right, height + top + bottom, NativeMethods.SWP_NOACTIVATE);
        }

        static int Scale(double value, double scale) => (int)Math.Round(value * scale);

        static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));

        void BringToFront()
        {
            IntPtr hwnd = Handle;
            Activate();
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == hwnd)
                return;

            // Запасной путь, если Windows не дала перехватить фокус обычным способом.
            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
            uint thisThread = NativeMethods.GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != thisThread &&
                            NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
            if (attached)
                NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
            Activate();
        }

        void FocusFirst()
        {
            StopScrolling();
            Scroll?.ScrollToTop();
            if (_history.Items.Count == 0)
            {
                Focus();
                return;
            }
            FocusIndex(0);
        }

        void FocusIndex(int index)
        {
            if (index < 0 || index >= _history.Items.Count)
                return;
            List.ScrollIntoView(_history.Items[index]);
            List.UpdateLayout();
            if (List.ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem container)
            {
                List.SelectedIndex = index;
                container.Focus();
            }
        }

        void FocusItem(ClipItem item) => ContainerFor(item)?.Focus();

        ListBoxItem ContainerFor(ClipItem item) => List.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;

        IEnumerable<ListBoxItem> RealizedContainers()
        {
            for (int i = 0; i < List.Items.Count; i++)
            {
                if (List.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container)
                    yield return container;
            }
        }

        ClipItem FocusedItem() => (Keyboard.FocusedElement as ListBoxItem)?.DataContext as ClipItem;

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            ShowFocusRing = false;
            base.OnPreviewMouseDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            ShowFocusRing = true;
            StopScrolling();

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ClipItem item = FocusedItem();
            switch (key)
            {
                case Key.Escape:
                    if (_revealed != null)
                    {
                        var revealed = _revealed;
                        Collapse(true);
                        FocusItem(revealed);
                    }
                    else
                    {
                        HideFlyout(true);
                    }
                    e.Handled = true;
                    return;

                case Key.Enter:
                case Key.Space:
                    if (item != null)
                    {
                        ItemChosen?.Invoke(item);
                        e.Handled = true;
                        return;
                    }
                    break;

                case Key.Delete:
                    // Фокус может стоять и на самой карточке, и на выехавшей кнопке удаления.
                    if ((Keyboard.FocusedElement as FrameworkElement)?.DataContext is ClipItem focused)
                    {
                        DeleteItem(focused);
                        e.Handled = true;
                        return;
                    }
                    break;

                case Key.Up:
                case Key.Down:
                case Key.Home:
                case Key.End:
                case Key.PageUp:
                case Key.PageDown:
                    if (_revealed != null)
                    {
                        var revealed = _revealed;
                        Collapse(true);
                        if (item == null)
                            FocusItem(revealed);
                    }
                    break;

                case Key.Apps:
                    if (item != null)
                    {
                        e.Handled = true;
                        return;
                    }
                    break;

                case Key.F10 when Keyboard.Modifiers == ModifierKeys.Shift:
                    if (item != null)
                    {
                        ToggleActions(item);
                        e.Handled = true;
                        return;
                    }
                    break;
            }
            base.OnPreviewKeyDown(e);
        }

        // Клавиша меню срабатывает при отпускании, как в Windows.
        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            if (e.Key == Key.Apps && FocusedItem() is ClipItem item)
            {
                ToggleActions(item);
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyUp(e);
        }

        void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var container = (ListBoxItem)sender;
            if (IsInsideButton(e.OriginalSource as DependencyObject, container))
                return;
            ReleasePressed();
            _pressed = container;
            CardState.SetIsPressed(container, true);
        }

        void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var container = (ListBoxItem)sender;
            bool wasPressed = _pressed == container;
            ReleasePressed();
            if (!wasPressed || !(container.DataContext is ClipItem item))
                return;
            if (_revealed == item)
                Collapse(true);
            else
                ItemChosen?.Invoke(item);
        }

        void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_pressed == sender)
                ReleasePressed();
        }

        void Card_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (((ListBoxItem)sender).DataContext is ClipItem item)
            {
                ToggleActions(item);
                e.Handled = true;
            }
        }

        void ReleasePressed()
        {
            if (_pressed != null)
                CardState.SetIsPressed(_pressed, false);
            _pressed = null;
        }

        void More_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is ClipItem item)
                ToggleActions(item);
        }

        void Pin_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is ClipItem item)
                _history.TogglePin(item);
        }

        void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is ClipItem item)
                DeleteItem(item);
        }

        // «...» сдвигает карточку влево и открывает справа кнопку удаления с фокусом, как в Windows.
        void ToggleActions(ClipItem item)
        {
            if (_revealed == item)
            {
                Collapse(true);
                FocusItem(item);
                return;
            }
            Collapse(true);
            var container = ContainerFor(item);
            if (container == null)
                return;
            _revealed = item;
            SetActionsVisible(container, true, animate: true);
            (container.Template.FindName("DeleteButton", container) as Button)?.Focus();
        }

        void Collapse(bool animate)
        {
            if (_revealed == null)
                return;
            var container = ContainerFor(_revealed);
            _revealed = null;
            if (container != null)
                SetActionsVisible(container, false, animate);
        }

        static void SetActionsVisible(ListBoxItem container, bool visible, bool animate)
        {
            var body = container.Template.FindName("Body", container) as FrameworkElement;
            var delete = container.Template.FindName("DeleteButton", container) as Button;
            if (body == null || delete == null)
                return;

            double duration = animate ? (visible ? 250 : 200) : 0;
            Motion.Thickness(body, MarginProperty, visible ? new Thickness(0, 0, ActionsWidth, 0) : new Thickness(0), duration, Motion.Decelerate);
            Motion.Double(delete, OpacityProperty, null, visible ? 1 : 0, animate ? (visible ? 200 : 120) : 0, Motion.Decelerate);
            Motion.Double(Motion.ShiftOf(delete), TranslateTransform.XProperty, visible ? 24 : (double?)null, visible ? 0 : 24, duration, Motion.Decelerate);
            delete.IsHitTestVisible = visible;
            delete.Focusable = visible;

            if (FindNamed<Button>(container, "MoreButton") is Button more)
            {
                if (visible)
                    more.SetResourceReference(BackgroundProperty, "SubtleFillColorSecondaryBrush");
                else
                    more.ClearValue(BackgroundProperty);
            }
        }

        void DeleteItem(ClipItem item)
        {
            if (_leaving.Contains(item))
                return;
            int index = _history.Items.IndexOf(item);
            if (index < 0)
                return;
            bool hadFocus = List.IsKeyboardFocusWithin;
            if (_revealed == item)
                _revealed = null;

            void Remove()
            {
                _leaving.Remove(item);
                AnimateLayoutChange(() => _history.Remove(item));
                if (_history.Items.Count == 0)
                    Focus();
                else if (hadFocus)
                    FocusIndex(Math.Min(index, _history.Items.Count - 1));
            }

            var container = ContainerFor(item);
            if (container == null || !Motion.Enabled)
            {
                Remove();
                return;
            }
            _leaving.Add(item);
            container.IsHitTestVisible = false;
            SlideOut(container, 0, Remove);
        }

        // Карточки уезжают вправо по очереди сверху вниз, закреплённые остаются.
        void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_clearing)
                return;
            Collapse(false);
            var leaving = RealizedContainers().Where(c => c.DataContext is ClipItem item && !item.IsPinned).ToList();
            if (leaving.Count == 0 || !Motion.Enabled)
            {
                FinishClear();
                return;
            }
            _clearing = true;
            for (int i = 0; i < leaving.Count; i++)
            {
                leaving[i].IsHitTestVisible = false;
                Action completed = null;
                if (i == leaving.Count - 1)
                {
                    completed = () =>
                    {
                        _clearing = false;
                        FinishClear();
                    };
                }
                SlideOut(leaving[i], i * 35, completed);
            }
        }

        void FinishClear()
        {
            AnimateLayoutChange(_history.ClearUnpinned);
            if (_history.Items.Count > 0)
                FocusIndex(0);
            else
                Focus();
        }

        static void SlideOut(ListBoxItem container, double delay, Action completed)
        {
            if (ShiftOf(container) is TranslateTransform shift)
                Motion.Double(shift, TranslateTransform.XProperty, 0, 60, 200, Motion.Accelerate, delay);
            Motion.Double(container, OpacityProperty, 1, 0, 200, Motion.Accelerate, delay, completed);
        }

        // Сдвиг вешается на корень шаблона карточки: у самого элемента списка он на экране не отображается.
        static TranslateTransform ShiftOf(ListBoxItem container) =>
            container.Template?.FindName("Root", container) is UIElement root ? Motion.ShiftOf(root) : null;

        // Оставшиеся карточки плавно переезжают на новые места: запоминаем, где они были,
        // меняем список и анимируем сдвиг от старого положения к новому.
        void AnimateLayoutChange(Action change)
        {
            var before = new Dictionary<ClipItem, double>();
            foreach (var container in RealizedContainers())
            {
                if (container.DataContext is ClipItem item)
                    before[item] = container.TranslatePoint(new Point(), List).Y + (ShiftOf(container)?.Y ?? 0);
            }

            change();
            if (!Motion.Enabled)
                return;
            List.UpdateLayout();

            foreach (var container in RealizedContainers())
            {
                if (!(container.DataContext is ClipItem item) || !before.TryGetValue(item, out double oldY))
                    continue;
                var shift = ShiftOf(container);
                if (shift == null)
                    continue;
                double from = oldY - container.TranslatePoint(new Point(), List).Y;
                if (Math.Abs(from) < 0.5)
                    continue;
                Motion.Double(shift, TranslateTransform.YProperty, from, 0, 300, Motion.Decelerate);
            }
        }

        void OnHistoryPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ClipboardHistory.IsEmpty))
                return;
            bool empty = _history.IsEmpty;
            if (empty && !_wasEmpty && IsVisible)
                Motion.Double(EmptyState, OpacityProperty, 0, 1, 250, Motion.Decelerate);
            _wasEmpty = empty;
        }

        void List_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scroll = Scroll;
            if (scroll == null || scroll.ScrollableHeight <= 0)
                return;
            e.Handled = true;
            double step = WheelStep * Math.Max(1, SystemParameters.WheelScrollLines) / 3.0;
            double from = _scrolling ? _scrollTarget : scroll.VerticalOffset;
            _scrollTarget = Math.Max(0, Math.Min(scroll.ScrollableHeight, from - e.Delta / 120.0 * step));
            _scrolling = true;
            Motion.Double(this, ScrollOffsetProperty, scroll.VerticalOffset, _scrollTarget, 250, Motion.Decelerate,
                completed: () => _scrolling = false);
        }

        void StopScrolling()
        {
            if (!_scrolling)
                return;
            _scrolling = false;
            BeginAnimation(ScrollOffsetProperty, null);
        }

        void List_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 && _revealed != null)
                Collapse(true);
        }

        static bool IsInsideButton(DependencyObject element, DependencyObject stop)
        {
            for (var current = element; current != null && current != stop; current = ParentOf(current))
            {
                if (current is ButtonBase)
                    return true;
            }
            return false;
        }

        static DependencyObject ParentOf(DependencyObject element) =>
            element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

        static T FindNamed<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match && match.Name == name)
                    return match;
                var nested = FindNamed<T>(child, name);
                if (nested != null)
                    return nested;
            }
            return null;
        }
    }
}
