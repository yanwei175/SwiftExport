using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Views;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Components;
using SwiftExport.Client.VSTO.Features.Customers.Components;
using SwiftExport.Client.VSTO.Resources.Controls;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>加载实际导入页并验证 WPF 绑定、选择器和 DataGrid 编辑模板，不连接 Excel。</summary>
    internal static class ImportPageChecks
    {
        /// <summary>验证真实销售导入页的布局、选择、编辑绑定及宿主释放，不加载 Office。</summary>
        internal static void Run(Action<bool, string> check)
        {
            using (var f = new ImportTestFixture())
            {
                var view = new SalesOrderImportView(f.Model);
                var window = new Window { Content = view, Width = 1500, Height = 900, ShowInTaskbar = false };
                window.Show(); Pump();
                try
                {
                    check(f.Model.IsEditable && f.Cache.Loads == 1, "实际 Loaded 首次加载基础资料");
                    check(ReferenceEquals(view.DataContext, f.Model), "页面接入注入的 DataContext");
                    var customer = Descendants<CustomerAutoSuggestComboBox>(view).Single();
                    var customerEditor = (LookupTextBox)customer.FindName("LookupEditor");
                    customerEditor.CommitExternalSelection(f.Cache.Customers[0], LookupCommitReason.Mouse); Pump();
                    check(f.Model.Header.CustomerId == f.Cache.Customers[0].Uid && f.Model.Header.ShipToName == "甲", "真实客户提交回填独立单头");
                    var currency = Descendants<ComboBox>(view).Single(c => BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path.Path == "Header.Currency");
                    check((string)currency.SelectedValue == "USD", "币种 SelectedValue 回显");
                    f.Cache.CurrencyItems.Add(new SysDicCurrencyDto { CurrencyCode = "CNY", IsEnabled = true }); Pump();
                    currency.SetCurrentValue(Selector.SelectedValueProperty, "CNY"); Pump();
                    check(f.Model.Header.Currency == "CNY", "币种选择双向回写编码");
                    f.Cache.CurrencyItems.Clear(); Pump();
                    check(f.Model.Header.Currency == "CNY" && (string)currency.SelectedValue == "CNY", "缓存清空保留当前币种与界面回显");
                    var tradePopup = (Popup)view.FindName("TradePopup");
                    ((ToggleButton)view.FindName("TradeToggleButton")).IsChecked = true; Pump();
                    var payment = Descendants<ComboBox>(tradePopup.Child).Single(c => BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path.Path == "Header.PaymentMethod");
                    f.Cache.DictionaryItems.Clear(); Pump();
                    check(f.Model.Header.PaymentMethod == "TT" && (string)payment.SelectedValue == "TT", "字典刷新不清掉当前付款方式");
                    tradePopup.SetCurrentValue(Popup.IsOpenProperty, false); Pump();
                    f.Model.AddStandardLineCommand.Execute(null); Pump();
                    var standard = f.Model.Lines[0];
                    var secondCustomer = new CustomerDto { Uid = Guid.NewGuid(), CustomerCode = "C2", CustomerName = "乙" };
                    f.Cache.CustomerItems.Add(secondCustomer); Pump(); f.Confirm = false;
                    customerEditor.CommitExternalSelection(secondCustomer, LookupCommitReason.Mouse); Pump();
                    check(customer.SelectedCustomer == f.Cache.Customers[0] && f.Model.Lines.Count == 1, "取消更换客户恢复控件显示并保留行");
                    var grid = (DataGrid)view.FindName("OrderLinesGrid");
                    ImportLayoutChecks.Run(view, grid, check);
                    check(view.Content is Grid && grid.ActualHeight > 0
                        && grid.TransformToAncestor(view).Transform(new Point(0, grid.ActualHeight)).Y <= view.ActualHeight,
                        "销售明细在有限窗体高度中显示，不依赖顶层滚动");
                    var previousHeight = grid.ActualHeight;
                    window.Height += 120; Pump(); view.UpdateLayout();
                    check(grid.ActualHeight > previousHeight + 30, "销售窗体增高时明细区域随之伸展");
                    window.Height -= 120; Pump(); view.UpdateLayout();
                    grid.SelectedItem = standard; grid.CurrentCell = new DataGridCellInfo(standard, grid.Columns[1]); grid.BeginEdit(); Pump();
                    var product = Descendants<SalesProductLookupTextBox>(grid).Single();
                    var candidate = new SalesOrderProductLookupDto { ProductId = Guid.NewGuid(), Sku = "SKU1", UsdPrice = 5m };
                    standard.ProductCandidates.Add(candidate); Pump();
                    ((LookupTextBox)product.FindName("LookupEditor")).CommitExternalSelection(candidate, LookupCommitReason.Mouse); Pump();
                    check(standard.Sku == "SKU1" && standard.ProductId == candidate.ProductId, "标准行产品选择通过实际编辑器回填");
                    grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                    f.Model.AddNonStandardLineCommand.Execute(null); Pump();
                    var nonStandard = f.Model.Lines[1];
                    grid.SelectedItem = nonStandard; grid.CurrentCell = new DataGridCellInfo(nonStandard, grid.Columns.Single(c => (string)c.Header == "单位")); grid.BeginEdit(); Pump();
                    check(Descendants<ComboBox>(grid).Any(c => c.IsEnabled && BindingOperations.GetBinding(c, Selector.SelectedValueProperty)?.Path.Path == "UnitSnapshot"), "非标单位使用可编辑下拉");
                    grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                    grid.CurrentCell = new DataGridCellInfo(nonStandard, grid.Columns.Single(c => (string)c.Header == "预计交期")); grid.BeginEdit(); Pump();
                    var date = Descendants<DatePicker>(grid).Single(); var expected = new DateTime(2026, 11, 2);
                    date.SetCurrentValue(DatePicker.SelectedDateProperty, expected); Pump();
                    check(nonStandard.ExpectedDeliveryDate == expected, "行交期编辑器双向绑定");
                    grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                    grid.SelectAll(); Pump(); f.Model.DeleteSelectedLineCommand.Execute(null); Pump();
                    check(f.Model.Lines.Count == 0, "DataGrid 多选桥接删除所有选中行");
                    ((ToggleButton)view.FindName("TradeToggleButton")).IsChecked = true; Pump();
                    var dates = Descendants<DatePicker>(tradePopup.Child).Where(d => BindingOperations.GetBinding(d, DatePicker.SelectedDateProperty)?.Path.Path == "Header.Etd").ToArray();
                    check(dates.Length == 1, "ETD 已绑定而不是空白占位");
                    tradePopup.SetCurrentValue(Popup.IsOpenProperty, false); Pump();
                    check(!Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.SaveDraftCommand)).IsEnabled, "未获维护权限且未检验时服务器保存入口不可执行");
                    var headerButton = Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.ReadHeaderCommand));
                    var linesButton = Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.ReadLinesCommand));
                    check(headerButton.IsEnabled && linesButton.IsEnabled, "两个地址读取按钮绑定命令且当前会话可执行");
                    f.Confirm = true; f.Model.AddStandardLineCommand.Execute(null);
                    f.Dispatch = action => window.Dispatcher.CheckAccess() ? Immediate(action) : window.Dispatcher.InvokeAsync(action).Task;
                    var parse = f.Model.ReadHeaderCommand.ExecuteAsync(null);
                    var timeout = System.Diagnostics.Stopwatch.StartNew();
                    while (!parse.IsCompleted && timeout.ElapsedMilliseconds < 10000) Pump();
                    check(parse.IsCompleted && !parse.IsFaulted, "STA 消息循环接纳地址读取转换结果：" + (parse.Exception?.GetBaseException().Message ?? f.Model.ErrorMessage)); Pump();
                    check(f.Model.Header.InvoiceNo == "260820" && f.Model.Lines.Count == 1, "替换 Header 后实际绑定回显且明细保留");
                    check(Descendants<TextBox>(view).Any(t => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path.Path == "Header.InvoiceNo" && t.Text == "260820"), "发票输入框绑定新单头实例");
                    check(grid.Columns.OfType<DataGridTextColumn>().Any(c => (c.Binding as Binding)?.Path.Path == "OriginalLinkSku"), "原表中性型号独立展示");
                    check(grid.Columns.OfType<DataGridTextColumn>().Any(c => (c.Binding as Binding)?.Path.Path == "PcsOfCartonText"), "包装数据具有可修正的编辑列");
                    f.Login("another"); Pump(); check(!f.Model.IsEditable && f.Model.Header.CustomerId == Guid.Empty, "换号清空并禁用实际页面");
                }
                finally { window.Close(); }
            }
            CheckPaneHostLifetime(check);
            CheckPaneRegistration(check);
        }
        // 使用真实 ElementHost 验证侧栏隐藏保留内容、移除宿主断开 WPF；不伪装成 Office 集成测试。
        private static void CheckPaneHostLifetime(Action<bool, string> check)
        {
            using (var f = new ImportTestFixture())
            using (var form = new System.Windows.Forms.Form { Width = 760, Height = 800, ShowInTaskbar = false })
            {
                var view = new SalesOrderImportView(f.Model);
                var host = new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneHostControl(view)
                    { Dock = System.Windows.Forms.DockStyle.Fill };
                form.Controls.Add(host); form.Show(); Pump();
                check(f.Model.IsEditable && f.Cache.Loads == 1, "ElementHost 加载实际页面及缓存");
                f.Model.Header.Remark = "保留草稿";
                form.Hide(); Pump(); form.Show(); Pump();
                check(f.Model.Header.Remark == "保留草稿" && f.Cache.Loads == 1, "隐藏重新显示不重置草稿或重复加载");
                var layout = (Grid)view.Content;
                var grid = (DataGrid)view.FindName("OrderLinesGrid");
                check(layout.ActualWidth <= form.ClientSize.Width && grid.ActualHeight > 0,
                    "有限 ElementHost 中页面不撑宽且明细保留可用高度：宽=" + layout.ActualWidth + "/" + form.ClientSize.Width + "，高=" + view.ActualHeight + "，行=" + string.Join("/", layout.RowDefinitions.Select(row => row.ActualHeight)));
                var popup = (Popup)view.FindName("ShippingPopup");
                ((ToggleButton)view.FindName("ShippingToggleButton")).IsChecked = true; Pump();
                check(popup.IsOpen, "ElementHost 中能够打开单头 Popup");
                host.Dispose(); host.Dispose(); f.Model.Dispose(); Pump();
                check(!popup.IsOpen && ((ToggleButton)view.FindName("ShippingToggleButton")).IsChecked == false,
                    "释放 ElementHost 同时关闭独立弹层并复位入口");
                check(host.IsDisposed, "Office 与会话先后释放同一宿主不抛异常");
                var childFirst = new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneHostControl(new UserControl());
                childFirst.Controls[0].Dispose();
                childFirst.Dispose(); childFirst.Dispose();
                check(childFirst.IsDisposed, "ElementHost 先释放后仍可安全释放宿主");
                check(System.Windows.PresentationSource.FromVisual(view) == null && f.Model.IsDisposed, "宿主释放断开 WPF 且页面可释放");
                using (var context = new SwiftExport.Client.VSTO.Hosting.Windows.ModelessWindowContext())
                {
                    var prompt = new SalesOrderImportPrompt(context);
                    check(!prompt.ConfirmDiscard("不应弹窗"), "无所属窗体时不丢弃数据");
                    context.Handle = form.Handle; context.Dispose();
                    check(context.Handle == IntPtr.Zero, "作用域释放清除销售提示框 Owner");
                }
            }
        }
        // 注册不构造需要服务的业务页面，且每个配置独立保存尺寸。
        private static void CheckPaneRegistration(Action<bool, string> check)
        {
            var registry = SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneRegistrar.CreateDefault();
            var salesNotInPanes = false;
            var purchaseNotInPanes = false;
            try { registry.Get("sales-order-import"); } catch (KeyNotFoundException) { salesNotInPanes = true; }
            try { registry.Get("purchase-order-import-preview"); } catch (KeyNotFoundException) { purchaseNotInPanes = true; }
            check(salesNotInPanes && purchaseNotInPanes, "销售采购导入均改用非模态窗体，不再注册侧边栏");
            registry.Register(new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneConfig("narrow", "窄窗格", typeof(UserControl), 420, Dock.Left));
            check(registry.Get("narrow").DefaultWidth == 420 && registry.Get("narrow").DockPosition == Dock.Left, "未来侧栏页面保留独立宽度与停靠配置");
            var duplicateRejected = false;
            var narrow = registry.Get("narrow");
            try { registry.Register(narrow); } catch (ArgumentException) { duplicateRejected = true; }
            check(duplicateRejected && ReferenceEquals(registry.Get(narrow.Id), narrow), "重复注册不覆盖原配置");
            var invalidRejected = false;
            try { new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneConfig("bad", "非法", typeof(UserControl), 0); }
            catch (ArgumentOutOfRangeException) { invalidRejected = true; }
            check(invalidRejected, "非法宽度在注册阶段被拒绝");
            using (var first = new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneContext { Handle = new IntPtr(1) })
            using (var second = new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneContext { Handle = new IntPtr(2) })
            {
                first.Dispose();
                check(second.Handle == new IntPtr(2), "释放一个窗格上下文不影响其他窗格");
            }
        }
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var descendant in Descendants<T>(child)) yield return descendant;
            }
        }
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        // STA 夹具短操作直接完成，后台结果通过真实 WPF Dispatcher 接纳。
        private static System.Threading.Tasks.Task Immediate(Action action) { action(); return System.Threading.Tasks.Task.CompletedTask; }
    }
}


