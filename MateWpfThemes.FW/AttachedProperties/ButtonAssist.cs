using System.Windows;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    public static class ButtonAssist
    {
        // 1. Icon 属性（改为 object，通吃 Geometry 和 Emoji 字符串）
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.RegisterAttached(
                "Icon", typeof(object), typeof(ButtonAssist), new PropertyMetadata(null));

        public static void SetIcon(DependencyObject element, object value) => element.SetValue(IconProperty, value);
        public static object GetIcon(DependencyObject element) => element.GetValue(IconProperty);

        // 2. IconSize 属性
        public static readonly DependencyProperty IconSizeProperty =
            DependencyProperty.RegisterAttached(
                "IconSize", typeof(double), typeof(ButtonAssist), new PropertyMetadata(16.0));

        public static void SetIconSize(DependencyObject element, double value) => element.SetValue(IconSizeProperty, value);
        public static double GetIconSize(DependencyObject element) => (double)element.GetValue(IconSizeProperty);

        // 3. IconMargin 属性 (保留它，控制图标和文字的距离)
        public static readonly DependencyProperty IconMarginProperty =
            DependencyProperty.RegisterAttached(
                "IconMargin", typeof(Thickness), typeof(ButtonAssist), new PropertyMetadata(new Thickness(0, 0, 6, 0)));

        public static void SetIconMargin(DependencyObject element, Thickness value) => element.SetValue(IconMarginProperty, value);
        public static Thickness GetIconMargin(DependencyObject element) => (Thickness)element.GetValue(IconMarginProperty);

        // 4. IconForeground 属性（独立控制图标颜色）
        public static readonly DependencyProperty IconForegroundProperty =
            DependencyProperty.RegisterAttached(
                "IconForeground", typeof(Brush), typeof(ButtonAssist), new PropertyMetadata(null));

        public static void SetIconForeground(DependencyObject element, Brush value) => element.SetValue(IconForegroundProperty, value);
        public static Brush GetIconForeground(DependencyObject element) => (Brush)element.GetValue(IconForegroundProperty);
    }

}
