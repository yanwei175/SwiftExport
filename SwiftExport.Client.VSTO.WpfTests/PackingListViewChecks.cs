using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Customers.Components;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;
using SwiftExport.Client.VSTO.Resources.Controls;
using SwiftExport.Client.VSTO.Tests;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>独立 STA 验证装箱单侧栏真实绑定、选择、排序和预留导出工具栏。</summary>
    internal static class PackingListViewChecks
    {
        /// <summary>使用真实页面和查询模型，不加载 Office；宽度只检查合法，不固定用户可调整的尺寸。</summary>
        internal static void Run(Action<bool, string> check)
        {
            var config = TaskPaneRegistrar.CreateDefault().Get("packing-list");
            check(config.ViewType == typeof(PackingListView) && config.DefaultWidth > 0 && config.DockPosition == Dock.Right, "装箱单注册为合法宽度的右侧窗格");
            using (var fixture = new PackingListViewTests.Fixture())
            {
                var model = fixture.Model; var view = new PackingListView(model);
                var window = new Window { Style = null, Content = view, Width = 1400, Height = 850, ShowInTaskbar = false };
                window.Show();
                try
                {
                    Pump(); view.UpdateLayout(); var grid = (DataGrid)view.FindName("DocumentsGrid");
                    check(fixture.Api.Requests.Count == 1 && ReferenceEquals(grid.ItemsSource, model.Documents), "首次 Loaded 加载实际摘要集合");
                    check(grid.Columns.Count == 10 && grid.Columns.Skip(1).All(column => column.IsReadOnly), "勾选与九个只读字段齐全");
                    var bindings = grid.Columns.OfType<DataGridTextColumn>().Select(column => ((Binding)column.Binding).Path.Path).ToArray();
                    check(bindings.SequenceEqual(new[] { "Document.PackingListNo", "Document.CustomerNameSnapshot", "Document.InvoiceDate", "Document.InvoiceNosSummary",
                        "PostingStatusText", "Document.ContainerNo", "Document.SealNo", "Document.Etd", "Document.Eta" }), "复用摘要 DTO 的箱单、客户、发票、运输及日期字段");
                    var customer = (CustomerAutoSuggestComboBox)view.FindName("CustomerSelector");
                    ((LookupTextBox)customer.FindName("LookupEditor")).CommitExternalSelection(fixture.Base.Cache.Customers[0], LookupCommitReason.Mouse); Pump();
                    check(ReferenceEquals(customer.SelectedCustomer, model.SelectedCustomer), "客户选择提交实际缓存身份");
                    var status = (ComboBox)view.FindName("PostingStatusSelector");
                    check((int)status.SelectedValue == -1, "过账状态默认全部"); status.SelectedValue = 8; Pump();
                    model.SearchCommand.Execute(null); Pump();
                    check(fixture.Api.Requests.Last().CustomerId == model.SelectedCustomer.Uid && model.PostingStatus == 8, "客户和过账筛选提交共享请求");
                    check(((TextBlock)grid.Columns[5].GetCellContent(model.Documents[0])).Text == "未过账", "过账状态中文回显");
                    check(((TextBlock)grid.Columns[3].GetCellContent(model.Documents[0])).Text == "2026-10-11", "单据日期明确年月日格式");
                    grid.SelectedItem = model.Documents[0]; Pump();
                    check(ReferenceEquals(model.SelectedDocument, grid.SelectedItem) && model.CheckedCount == 0, "当前行与批量勾选独立");
                    var checkbox = (CheckBox)((DataGridTemplateColumn)grid.Columns[0]).CellTemplate.LoadContent();
                    checkbox.DataContext = model.Documents[0]; Pump(); checkbox.IsChecked = true; Pump();
                    check(model.CheckedCount == 1, "真实模板勾选通知数量");
                    var path = (Wpf.Ui.Controls.TextBox)view.FindName("OutputPathBox");
                    check(path.Text == Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "输出路径默认本机桌面");
                    path.Text = "D:\\Exports"; Pump(); check(model.OutputPath == path.Text, "输出路径双向绑定");
                    check(!((Wpf.Ui.Controls.Button)view.FindName("GenerateSelectedButton")).IsEnabled
                        && !((Wpf.Ui.Controls.Button)view.FindName("GenerateCheckedButton")).IsEnabled, "单张与批量导出保持待实现且禁用");
                    CollectionViewSource.GetDefaultView(model.Documents).SortDescriptions.Add(new SortDescription("Document.PackingListNo", ListSortDirection.Descending)); Pump();
                    check(grid.CanUserSortColumns && grid.Columns[5].SortMemberPath == "Document.PostingStatus", "编号和过账状态可按实际字段排序");
                    var messages = (ScrollViewer)view.FindName("StatusScroll");
                    check(messages.MaxHeight == 84 && grid.ActualHeight > 200 && ScrollViewer.GetHorizontalScrollBarVisibility(grid) == ScrollBarVisibility.Auto,
                        "提示限高，明细占剩余空间并保留横向滚动");
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); Pump();
                    check(fixture.Api.Requests.Count == 2 && model.CheckedCount == 1, "重复 Loaded 不查询或清空勾选");
                    fixture.Base.Session.TryEnd(fixture.Base.Session.Capture().Version); Pump();
                    check(!grid.IsEnabled && model.Documents.Count == 0 && model.SelectedCustomer == null, "退出账号清空并禁用旧列表");
                }
                finally { window.Close(); }
            }
        }
        // 驱动真实 Loaded 与 Binding，使用宿主同一 STA 消息循环。
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
