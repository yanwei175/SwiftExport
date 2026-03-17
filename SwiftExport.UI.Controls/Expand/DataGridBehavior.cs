using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.UI.Controls.Expand
{
    public static class DataGridBehavior
    {
        // ==========================================
        // 1. 自动行号附加属性 (ShowAutoRowNumber)
        // ==========================================
        public static readonly DependencyProperty ShowAutoRowNumberProperty =
            DependencyProperty.RegisterAttached(
                "ShowAutoRowNumber",
                typeof(bool),
                typeof(DataGridBehavior),
                new PropertyMetadata(false, OnShowAutoRowNumberChanged));

        public static bool GetShowAutoRowNumber(DependencyObject obj) => (bool)obj.GetValue(ShowAutoRowNumberProperty);
        public static void SetShowAutoRowNumber(DependencyObject obj, bool value) => obj.SetValue(ShowAutoRowNumberProperty, value);

        private static void OnShowAutoRowNumberChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid grid)
            {
                if ((bool)e.NewValue)
                {
                    // 挂载事件
                    grid.LoadingRow += Grid_LoadingRow;
                    grid.UnloadingRow += Grid_UnloadingRow;
                }
                else
                {
                    // 卸载事件，防止内存泄漏
                    grid.LoadingRow -= Grid_LoadingRow;
                    grid.UnloadingRow -= Grid_UnloadingRow;
                }
            }
        }

        private static void Grid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            // 加载时：设置当前行的行号 (索引从 0 开始，所以 +1)
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        private static void Grid_UnloadingRow(object sender, DataGridRowEventArgs e)
        {
            // 删除时：延迟刷新剩下的所有行号，防止断号 (比如删了 2，3 不会自动变成 2)
            var grid = (DataGrid)sender;
            grid.Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in grid.Items)
                {
                    if (grid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                    {
                        row.Header = (row.GetIndex() + 1).ToString();
                    }
                }
            });
        }
    }
}