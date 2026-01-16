using ExcelDna.Integration.CustomUI;
using SwiftExport.ExcelClient.Views;
using System.Runtime.InteropServices;

namespace SwiftExport.ExcelClient.Managers;

public static class TaskPaneManager
{
    private static CustomTaskPane? _taskPane;

    public static void ShowTaskPane()
    {
        try
        {
            if (_taskPane == null)
            {
                // 1. 再次确认 UserControl 是 COM 可见的
                var viewType = typeof(ProductView);
                var isComVisible = viewType.GetCustomAttributes(typeof(ComVisibleAttribute), false).Length > 0;

                // 2. 创建侧边栏
                _taskPane = CustomTaskPaneFactory.CreateCustomTaskPane(viewType, "产品搜索");

                // 3. 设置布局
                _taskPane.DockPosition = MsoCTPDockPosition.msoCTPDockPositionRight;
                _taskPane.Width = 350;

                // 监听可见性变化
                _taskPane.VisibleStateChange += (ctp) =>
                {
                    if (!ctp.Visible)
                    {
                        // 可以在这里处理关闭逻辑，比如 _taskPane = null; 以便下次完全重建
                    }
                };
            }

            _taskPane.Visible = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"侧边栏创建失败！\n\n原因: {ex.Message}\n\n请检查 ProductView 类是否加了 [ComVisible(true)] 且是 public 的。");
        }
    }
}