using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.UI.Controls.Expand
{
    public static class DataGridStyleInjector
    {
        public static readonly DependencyProperty AutoInjectProperty =
            DependencyProperty.RegisterAttached("AutoInject", typeof(bool), typeof(DataGridStyleInjector), new PropertyMetadata(false, OnAutoInjectChanged));

        public static bool GetAutoInject(DependencyObject obj) => (bool)obj.GetValue(AutoInjectProperty);
        public static void SetAutoInject(DependencyObject obj, bool value) => obj.SetValue(AutoInjectProperty, value);

        private static void OnAutoInjectChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid && (bool)e.NewValue)
            {
                // 确保在表格加载完毕、列都生成好之后进行批量注入
                grid.Loaded -= Grid_Loaded;
                grid.Loaded += Grid_Loaded;
            }
        }

        private static void Grid_Loaded(object sender, RoutedEventArgs e)
        {
            var grid = (DataGrid)sender;

            // 1. 从全局资源中捞出我们写好的“单行神装样式”
            var normalTextStyle = grid.TryFindResource("DataGridTextBlockStyle") as Style;
            var normalEditStyle = grid.TryFindResource("DataGridTextBoxStyle") as Style;

            if (normalTextStyle == null || normalEditStyle == null) return;

            // 2. 遍历所有的列
            foreach (var column in grid.Columns)
            {
                if (column is DataGridTextColumn textColumn)
                {
                    // ==========================================
                    // ★ 核心黑科技：探测开发者是否在 XAML 里“手动”写了样式
                    // ==========================================
                    var editStyleSource = DependencyPropertyHelper.GetValueSource(textColumn, DataGridTextColumn.EditingElementStyleProperty);
                    var textStyleSource = DependencyPropertyHelper.GetValueSource(textColumn, DataGridTextColumn.ElementStyleProperty);

                    // 只有当它是“系统默认（Default）”时，也就是你只写了 <DataGridTextColumn Binding="..."/> 时，我们才强行注入！
                    if (textStyleSource.BaseValueSource == BaseValueSource.Default)
                    {
                        textColumn.ElementStyle = normalTextStyle;
                    }
                    if (editStyleSource.BaseValueSource == BaseValueSource.Default)
                    {
                        textColumn.EditingElementStyle = normalEditStyle;
                    }

                    // (如果你在 XAML 里特意写了多行样式 MultiLineDataGridTextBoxStyle，
                    // 这里的 BaseValueSource 就会是 Local，这段代码就会智能跳过，绝不覆盖你的特殊设定！)
                }
            }
        }
    }
}