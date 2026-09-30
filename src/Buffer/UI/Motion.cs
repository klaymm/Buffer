using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Buffer
{
    // Анимации в духе Windows 11: резкий старт и мягкое торможение. Если в Windows выключены
    // «Эффекты анимации», значения применяются сразу, без движения.
    static class Motion
    {
        public static readonly KeySpline Decelerate = Frozen(new KeySpline(0.1, 0.9, 0.2, 1.0));
        public static readonly KeySpline Accelerate = Frozen(new KeySpline(0.7, 0.0, 1.0, 0.5));
        public static readonly KeySpline Linear = Frozen(new KeySpline(0.0, 0.0, 1.0, 1.0));

        public static bool Enabled => SystemParameters.ClientAreaAnimation;

        public static void Set(IAnimatable target, DependencyProperty property, object value)
        {
            target.BeginAnimation(property, null);
            ((DependencyObject)target).SetValue(property, value);
        }

        public static void Double(IAnimatable target, DependencyProperty property, double? from, double to,
            double milliseconds, KeySpline spline, double delay = 0, Action completed = null)
        {
            if (!Enabled || milliseconds <= 0)
            {
                Set(target, property, to);
                completed?.Invoke();
                return;
            }
            var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delay) };
            if (from.HasValue)
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from.Value, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), spline));
            if (completed != null)
                animation.Completed += (s, e) => completed();
            target.BeginAnimation(property, animation);
        }

        public static void Thickness(IAnimatable target, DependencyProperty property, Thickness to, double milliseconds, KeySpline spline)
        {
            if (!Enabled || milliseconds <= 0)
            {
                Set(target, property, to);
                return;
            }
            var animation = new ThicknessAnimationUsingKeyFrames();
            animation.KeyFrames.Add(new SplineThicknessKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)), spline));
            target.BeginAnimation(property, animation);
        }

        public static TranslateTransform ShiftOf(UIElement element)
        {
            if (element.RenderTransform is TranslateTransform shift && !shift.IsFrozen)
                return shift;
            shift = new TranslateTransform();
            element.RenderTransform = shift;
            return shift;
        }

        static KeySpline Frozen(KeySpline spline)
        {
            spline.Freeze();
            return spline;
        }
    }
}
