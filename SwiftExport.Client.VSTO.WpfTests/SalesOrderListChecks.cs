using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Views;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;
using SwiftExport.Client.VSTO.Tests;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>在独立 STA 验证真实列表 XAML、选择绑定和默认窗格配置，不加载 Office。</summary>
    internal static class SalesOrderListChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var config = TaskPaneRegistrar.CreateDefault().Get("sales-order-list");
            check(config.ViewType == typeof(SalesOrderListView) && config.DefaultWidth == 720 && config.DockPosition == Dock.Right, "销售列表注册为右侧720宽窗格");
            using (var fixture = new SalesOrderListTests.Fixture())
            {
                var model = fixture.Model; var view = new SalesOrderListView(model);
                var window = new Window { Style = null, Content = view, Width = 720, Height = 850, ShowInTaskbar = false };
                window.Show();
                try
                {
                    Pump(); view.UpdateLayout();
                    var grid = (DataGrid)view.FindName("OrdersGrid");
                    check(fixture.Api.Requests.Count == 1 && ReferenceEquals(grid.ItemsSource, model.Orders), "首次 Loaded 按需加载绑定行");
                    check(grid.Columns.Count == 9 && grid.Columns.Skip(1).All(x => x.IsReadOnly), "勾选列与八个只读数据列齐全");
                    var confirmation = (ComboBox)view.FindName("ConfirmationSelector");
                    var quick = (ComboBox)view.FindName("QuickViewSelector");
                    check((int)confirmation.SelectedValue == -1 && (SalesOrderManagerQuickView)quick.SelectedValue == SalesOrderManagerQuickView.All, "下拉默认全部回显");
                    confirmation.SelectedValue = 1; quick.SelectedValue = SalesOrderManagerQuickView.Overdue; Pump();
                    check(model.Confirmation == 1 && model.QuickView == SalesOrderManagerQuickView.Overdue, "下拉选择更新实际模型");
                    var path = (Wpf.Ui.Controls.TextBox)view.FindName("OutputPathBox");
                    check(path.Text == Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "默认桌面路径回显");
                    path.Text = "D:\\Exports"; Pump(); check(model.OutputPath == path.Text, "输出路径双向绑定");
                    grid.SelectedItem = model.Orders[0]; Pump();
                    check(ReferenceEquals(model.SelectedOrder, grid.SelectedItem) && model.CheckedCount == 0, "当前行不自动加入批量勾选");
                    var checkbox = (CheckBox)((DataGridTemplateColumn)grid.Columns[0]).CellTemplate.LoadContent();
                    checkbox.DataContext = model.Orders[0]; Pump(); checkbox.IsChecked = true; Pump();
                    check(model.CheckedCount == 1, "勾选通过真实模板通知模型");
                    var quantity = (TextBlock)grid.Columns[6].GetCellContent(model.Orders[0]);
                    check(quantity?.Text == "—", "空数量保留未知提示而非零");
                    check(!((Wpf.Ui.Controls.Button)view.FindName("GenerateSelectedButton")).IsEnabled
                        && !((Wpf.Ui.Controls.Button)view.FindName("GenerateCheckedButton")).IsEnabled, "两个生成入口仅预留且禁用");
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Pump();
                    check(fixture.Api.Requests.Count == 1 && model.CheckedCount == 1, "重复显示不重复请求或清空勾选");
                    fixture.Session.TryEnd(fixture.Session.Capture().Version); Pump();
                    check(!grid.IsEnabled && model.Orders.Count == 0, "退出账号清空并禁用旧列表");
                }
                finally { window.Close(); }
            }
        }
        // 驱动真实 WPF Binding 与 Loaded 消息，测试不创建另一个宿主线程。
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
