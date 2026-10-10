using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Views;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>使用真实销售导入 XAML 验证检查反馈、候选提交和保存门禁；不访问 Office 或服务器。</summary>
    internal static class SubmissionPageChecks
    {
        // 所有检查在 STA 消息循环运行，按钮和 RowDetails 使用实际模板。
        internal static void Run(Action<bool, string> check)
        {
            using (var f = new ImportTestFixture())
            {
                f.Session.TryCommit(f.Session.Capture().Version, "test-token", new MeDto { UserId = "writer", Permissions = new List<string> { PermissionKeys.SalesOrderMaintain } });
                var view = new SalesOrderImportView(f.Model);
                var window = new Window { Content = view, Width = 1500, Height = 950, ShowInTaskbar = false };
                window.Show(); Pump();
                try
                {
                    f.Model.SelectedCustomer = f.Cache.Customers[0]; f.Model.CustomerSelectedCommand.Execute(null); f.Model.AddStandardLineCommand.Execute(null);
                    var row = f.Model.Lines[0]; row.CustomerSku = "AQ1"; row.OriginalLinkSku = "ORIGINAL";
                    var candidate = new SalesOrderImportProductCandidateDto { CustomerProductId = Guid.NewGuid(), ProductId = Guid.NewGuid(), CustomerSku = "AQ1", Sku = "REAL", LinkSku = "SYSTEM", Unit = "PCS", IsUsable = true };
                    f.CheckApi.Check = (r, t) => {
                        var selected = r.Lines[0].SelectedCustomerProductId.HasValue; var accepted = r.Lines[0].AcceptLinkSkuMismatch;
                        return Task.FromResult(ServiceResult<CheckSalesOrderImportResponse>.Ok(new CheckSalesOrderImportResponse {
                            Customer = new SalesOrderImportCustomerDto { Uid = f.Model.Header.CustomerId, CustomerCode = r.CustomerCode, IsActive = true },
                            CanSave = selected && accepted, Lines = new List<SalesOrderImportLineCheckDto> {
                                new SalesOrderImportLineCheckDto { ClientLineId = row.Uid, MatchStatus = selected ? "Matched" : "Ambiguous", Product = selected ? candidate : null,
                                    Candidates = new List<SalesOrderImportProductCandidateDto> { candidate }, Issues = new List<SalesOrderImportIssueDto> {
                                        new SalesOrderImportIssueDto { Code = "LinkSkuMismatch", Message = "请核对型号", RequiresConfirmation = true, Acknowledged = accepted } } } } })); };
                    var grid = (DataGrid)view.FindName("OrderLinesGrid"); grid.SelectedItem = row; Pump();
                    var save = Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.SaveDraftCommand));
                    check(!Descendants<TextBox>(view).Any(box => BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == "Header.ExchangeRateText"), "销售导入页无汇率编辑入口");
                    f.Model.Header.ExchangeRateText = "旧非法文本";
                    check(!save.IsEnabled, "未检验时真实保存按钮禁用");
                    f.Model.CheckImportCommand.Execute(null); Pump();
                    var chooser = Descendants<ComboBox>(grid).Single(c => BindingOperations.GetBinding(c, ItemsControl.ItemsSourceProperty)?.Path.Path == "MatchCandidates");
                    chooser.SetCurrentValue(Selector.SelectedItemProperty, row.MatchCandidates[0]); Pump();
                    check(row.SelectedMatchCandidate != null, "RowDetails 候选绑定回写待选项");
                    // 执行实际按钮绑定的命令及参数，避免模拟 RoutedEvent 绕开原生 OnClick。
                    var use = Descendants<Button>(grid).Single(b => ReferenceEquals(b.Command, f.Model.ChooseMatchCommand)); use.Command.Execute(use.CommandParameter); Pump();
                    check(row.SelectedMatchId == candidate.CustomerProductId && !save.IsEnabled, "使用候选只提交关联选择，不跳过重新检验");
                    f.Model.CheckImportCommand.Execute(null); Pump();
                    var confirm = Descendants<CheckBox>(grid).Single(c => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path.Path == "AcceptLinkSkuMismatch");
                    confirm.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump();
                    check(row.AcceptLinkSkuMismatch && !save.IsEnabled, "型号差异勾选回写且仍要求重新检验");
                    f.Model.CheckImportCommand.Execute(null); Pump(); check(save.IsEnabled, "后端整单通过后真实保存按钮启用");
                    check(grid.Columns.OfType<DataGridTextColumn>().Any(c => (c.Binding as Binding)?.Path.Path == "LinkSku"), "原表型号之外独立展示系统中性型号");
                    var wait = new TaskCompletionSource<ServiceResult<SaveSalesOrderResponse>>(); f.Api.Save = r => wait.Task;
                    var saveTask = f.Model.SaveDraftAsync(); Pump();
                    check(!confirm.IsEnabled && !chooser.IsEnabled && grid.IsReadOnly && !save.IsEnabled, "保存期间行详情输入、网格和重复保存均锁定");
                    wait.SetResult(ServiceResult<SaveSalesOrderResponse>.Ok(new SaveSalesOrderResponse { Document = new SalesOrderDetailDto {
                        Header = new SalesOrderEditHeaderDto { Uid = Guid.NewGuid(), CustomerId = f.Model.Header.CustomerId, OrderNo = "SO-STA" } } }));
                    var timeout = System.Diagnostics.Stopwatch.StartNew(); while (!saveTask.IsCompleted && timeout.ElapsedMilliseconds < 10000) Pump();
                    check(saveTask.IsCompleted && !saveTask.IsFaulted && f.Model.Header.OrderNo == "SO-STA", "异步回执在 STA 接纳并回显单号");
                    Pump(); check(!save.IsEnabled && f.Model.NewImportCommand.CanExecute(null), "成功后不能再新建相同输入，可开始新导入");
                }
                finally { window.Close(); }
            }
            CheckMatchingControls(check);
        }
        // 用实际模板和原生点击路径验证非标勾选、红绿状态及客户门禁，不把颜色存在当作业务通过。
        private static void CheckMatchingControls(Action<bool, string> check)
        {
            using (var f = new ImportTestFixture())
            {
                f.Session.TryCommit(f.Session.Capture().Version, "test-token", new MeDto { UserId = "writer", Permissions = new List<string> { PermissionKeys.SalesOrderMaintain } });
                var view = new SalesOrderImportView(f.Model);
                var window = new Window { Content = view, Width = 1500, Height = 900, ShowInTaskbar = false };
                window.Show(); Pump();
                try
                {
                    var grid = (DataGrid)view.FindName("OrderLinesGrid");
                    var all = Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.MatchAllSkusCommand));
                    var selected = Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.MatchSelectedSkusCommand));
                    var full = Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.CheckImportCommand));
                    check(!all.IsEnabled && !selected.IsEnabled && !full.IsEnabled, "未选客户时三个匹配按钮均禁用");
                    f.Model.SelectedCustomer = f.Cache.Customers[0]; f.Model.CustomerSelectedCommand.Execute(null);
                    f.Model.AddStandardLineCommand.Execute(null); var row = f.Model.Lines[0]; row.CustomerSku = "AQ1";
                    Pump(); check(all.IsEnabled && full.IsEnabled && !selected.IsEnabled, "已选客户启用全部匹配，选中匹配仍需选行");
                    grid.SelectedItem = row; Pump();
                    check(selected.IsEnabled && grid.Columns.Count(c => (string)c.Header == "非标件") == 1
                        && !grid.Columns.Any(c => new[] { "类型", "行类型处理", "匹配状态" }.Contains((string)c.Header)),
                        "单一非标模板列替代类型及处理列，匹配改由行头显示");
                    var critical = (SolidColorBrush)view.FindResource("UserBusinessTextCriticalBrush");
                    var success = (SolidColorBrush)view.FindResource("UserBusinessTextSuccessBrush");
                    check(StatusDot(grid, row).Fill is SolidColorBrush red && red.Color == critical.Color, "未校验行头显示红色圆点");
                    f.CheckApi.Check = (r, t) => Task.FromResult(ServiceResult<CheckSalesOrderImportResponse>.Ok(new CheckSalesOrderImportResponse {
                        Customer = new SalesOrderImportCustomerDto { Uid = f.Model.Header.CustomerId, CustomerCode = r.CustomerCode, IsActive = true }, CanSave = true,
                        Lines = r.Lines.Select(l => new SalesOrderImportLineCheckDto { ClientLineId = l.ClientLineId, MatchStatus = l.IsNonStandard ? "NonStandard" : "Matched",
                            Product = l.IsNonStandard ? null : new SalesOrderImportProductCandidateDto { ProductId = Guid.NewGuid(), CustomerProductId = Guid.NewGuid(), Sku = "REAL", Unit = "PCS", IsUsable = true } }).ToList() }));
                    f.Model.MatchSelectedSkusCommand.Execute(null); Pump();
                    check(row.Sku == "REAL" && StatusDot(grid, row).Fill is SolidColorBrush green && green.Color == success.Color
                        && !f.Model.CanSaveDraft, "选中匹配回填真实 SKU、圆点变绿，但仍需整单检验");
                    var box = Descendants<CheckBox>(grid).Single(c => ReferenceEquals(c.Command, f.Model.ToggleNonStandardCommand));
                    ClickCheckBox(box); Pump();
                    check(row.IsNonStandard && box.IsChecked == true && row.ProductId == null && !row.IsCheckPassed,
                        "实际勾选转非标、清除产品身份并使校验失效");
                    row.DescriptionEn = "手工件"; f.Model.MatchAllSkusCommand.Execute(null); Pump();
                    check(row.IsCheckPassed && StatusDot(grid, row).Fill is SolidColorBrush nonStandardGreen && nonStandardGreen.Color == success.Color,
                        "非标行通过检查后也显示绿色");
                    ClickCheckBox(box); Pump(); check(!row.IsNonStandard && box.IsChecked == false && !row.IsCheckPassed, "取消非标勾选回到待匹配标准行");
                    var message = Descendants<TextBlock>(view).Single(b => BindingOperations.GetBinding(b, TextBlock.TextProperty)?.Path.Path == "SubmissionMessage");
                    check(message.Foreground is SolidColorBrush text && text.Color == critical.Color, "顶部检验反馈采用红色文字");
                    f.Model.CustomerClearedCommand.Execute(null); Pump();
                    check(!all.IsEnabled && !selected.IsEnabled && !full.IsEnabled, "清空客户后所有匹配入口即时禁用");
                }
                finally { window.Close(); }
            }
        }
        // Toggle 自动化接口只改变 IsChecked，不执行绑定命令；调用原生 OnClick 验证鼠标/键盘的完整切换路径。
        private static void ClickCheckBox(CheckBox box) => typeof(CheckBox)
            .GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(box, null);
        // 定位目标行真正的行头模板，确保验证的是用户看见的状态圆点。
        private static System.Windows.Shapes.Ellipse StatusDot(DataGrid grid, object row)
        {
            var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(row);
            return Descendants<System.Windows.Shapes.Ellipse>(Descendants<DataGridRowHeader>(container).Single()).Single();
        }
        private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var value in Descendants<T>(child)) yield return value; }
        }
    }
}
