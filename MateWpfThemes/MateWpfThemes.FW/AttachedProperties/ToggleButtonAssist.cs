using System.Windows;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    public static class ToggleButtonAssist
    {
        // ==========================================
        // SwitchOnBackground: 开关打开(True)时的轨道高亮背景色
        // ==========================================
        public static readonly DependencyProperty SwitchOnBackgroundProperty =
            DependencyProperty.RegisterAttached(
                "SwitchOnBackground", typeof(Brush), typeof(ToggleButtonAssist), new PropertyMetadata(null));

        public static void SetSwitchOnBackground(DependencyObject element, Brush value) => element.SetValue(SwitchOnBackgroundProperty, value);
        public static Brush GetSwitchOnBackground(DependencyObject element) => (Brush)element.GetValue(SwitchOnBackgroundProperty);
    }
}