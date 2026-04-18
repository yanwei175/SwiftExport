using System.Windows;
using System.Windows.Media.Effects;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// 阴影层级 (Z轴高度) 附加属性，用于快速实现卡片、弹窗的立体感
    /// </summary>
    public static class ShadowAssist
    {
        // 定义通俗易懂的阴影级别
        public enum ShadowLevel
        {
            None,       // 扁平 (无阴影)
            Low,        // 浅阴影 (适合卡片、普通面板)
            Medium,     // 中阴影 (适合下拉菜单、悬浮按钮)
            High        // 深阴影 (适合居中弹窗、重要提示)
        }

        public static readonly DependencyProperty LevelProperty =
            DependencyProperty.RegisterAttached(
                "Level", typeof(ShadowLevel), typeof(ShadowAssist),
                new PropertyMetadata(ShadowLevel.None, OnLevelChanged));

        public static void SetLevel(DependencyObject element, ShadowLevel value) => element.SetValue(LevelProperty, value);
        public static ShadowLevel GetLevel(DependencyObject element) => (ShadowLevel)element.GetValue(LevelProperty);

        // 当 XAML 中设置了属性时，自动计算并附加 DropShadowEffect
        private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                var level = (ShadowLevel)e.NewValue;
                switch (level)
                {
                    case ShadowLevel.None:
                        element.Effect = null;
                        break;
                    case ShadowLevel.Low:
                        element.Effect = new DropShadowEffect { BlurRadius = 10, Opacity = 0.1, ShadowDepth = 1, Direction = 270 };
                        break;
                    case ShadowLevel.Medium:
                        element.Effect = new DropShadowEffect { BlurRadius = 15, Opacity = 0.15, ShadowDepth = 3, Direction = 270 };
                        break;
                    case ShadowLevel.High:
                        element.Effect = new DropShadowEffect { BlurRadius = 25, Opacity = 0.2, ShadowDepth = 6, Direction = 270 };
                        break;
                }
            }
        }
    }
}