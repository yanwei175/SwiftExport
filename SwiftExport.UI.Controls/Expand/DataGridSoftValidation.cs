using SwiftExport.UI.Controls.Converters;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace SwiftExport.UI.Controls.Expand
{
    /// <summary>
    /// 自动为 DataGrid 的所有绑定列注入软校验（红框与ToolTip）
    /// 彻底消灭 XAML 里的重复代码！
    /// </summary>
    public static class DataGridSoftValidation
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DataGridSoftValidation), new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid && (bool)e.NewValue)
            {
                // 等待 DataGrid 加载完毕，所有的列都被解析出来后，开始注入魔法
                grid.Loaded -= Grid_Loaded;
                grid.Loaded += Grid_Loaded;
            }
        }

        private static void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            var grid = (DataGrid)sender;
            var converter = new ErrorToBoolConverter();
            // 定义极其优雅的浅粉色报错背景
            var errorBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));

            // 遍历所有带有绑定的列 (DataGridTextColumn, DataGridCheckBoxColumn 等)
            foreach (var col in grid.Columns.OfType<DataGridBoundColumn>())
            {
                if (col.Binding is Binding binding && !string.IsNullOrEmpty(binding.Path.Path))
                {
                    // 抓取出绑定的属性名，比如 "ColorCodeUnique"
                    var propName = binding.Path.Path;

                    // 获取它原有的 Style（防止覆盖掉用户自定义的其他样式），在此基础上创建一个新样式
                    var baseStyle = col.CellStyle;
                    var style = new Style(typeof(DataGridCell), baseStyle);

                    // ★ 核心魔法：用 C# 代码动态生成那一段又臭又长的 DataTrigger
                    var trigger = new DataTrigger
                    {
                        Binding = new Binding($"[{propName}]") { Converter = converter },
                        Value = true
                    };

                    trigger.Setters.Add(new Setter(Control.BackgroundProperty, errorBg));
                    trigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Red));
                    trigger.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                    trigger.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding($"[{propName}]")));

                    style.Triggers.Add(trigger);

                    // 将生成好的样式神不知鬼不觉地塞回给这一列
                    col.CellStyle = style;
                }
            }
        }
    }
}
