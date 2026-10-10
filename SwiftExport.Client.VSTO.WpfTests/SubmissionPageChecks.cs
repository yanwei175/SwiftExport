using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Resources.Controls;
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
        /// <summary>用实际模板验证图片路径绑定及发票更新日期双向编辑；不下载远程图片。</summary>
        internal static void CheckSnapshotControls(Action<bool, string> check)
        {
            using (var f = new ImportTestFixture())
            {
                f.Session.TryCommit(f.Session.Capture().Version, "test-token", new MeDto { UserId = "writer", Permissions = new List<string> { PermissionKeys.SalesOrderMaintain } });
                var view = new SalesOrderImportView(f.Model);
                var window = new Window { Content = view, Width = 1500, Height = 950, ShowInTaskbar = false };
                try
                {
                    window.Show(); Pump();
                    var grid = (DataGrid)view.FindName("OrderLinesGrid");
                    var cnyColumns = grid.Columns.Where(c => Equals(c.Header, "CNY单价") || Equals(c.Header, "CNY金额")).ToArray();
                    f.Model.Header.Currency = "USD"; Pump();
                    check(cnyColumns.Length == 2 && cnyColumns.All(c => c.Visibility == Visibility.Collapsed), "USD 时人民币两列隐藏");
                    f.Model.Header.Currency = "CNY"; Pump();
                    check(cnyColumns.All(c => c.Visibility == Visibility.Visible), "CNY 时人民币两列显示");
                    check(grid.Columns.Where(c => Equals(c.Header, "PCS/CTN") || Equals(c.Header, "M3/CTN")).All(c => c.Visibility == Visibility.Visible), "更多字段关闭时每箱数量和体积仍显示");
                    var quantityRow = new SwiftExport.Client.VSTO.Features.Sales.Orders.Models.SalesOrderImportLine(false, null) { QtyText = "0" };
                    f.Model.Lines.Add(quantityRow); Pump();
                    var quantityColumn = grid.Columns.OfType<DataGridTextColumn>().Single(c => Equals(c.Header, "数量"));
                    var quantityText = (TextBlock)quantityColumn.GetCellContent(quantityRow);
                    check((quantityText.Foreground as SolidColorBrush)?.Color == Colors.Red, "零数量在真实网格显示红色");
                    quantityRow.QtyText = "1"; Pump();
                    check((quantityText.Foreground as SolidColorBrush)?.Color != Colors.Red, "修正为正数量后恢复原文字颜色");
                    var column = grid.Columns.OfType<DataGridTemplateColumn>().Single(c => Equals(c.Header, "图片"));
                    var image = (SwiftExport.Client.VSTO.Resources.Controls.HttpsImage)column.CellTemplate.LoadContent();
                    image.DataContext = new { MainImage = "https://example.invalid/product.png" };
                    Pump(); image.GetBindingExpression(SwiftExport.Client.VSTO.Resources.Controls.HttpsImage.SourcePathProperty).UpdateTarget();
                    check(column.IsReadOnly && image.SourcePath == "https://example.invalid/product.png", "图片列只读并绑定匹配后的 MainImage；当前路径=" + image.SourcePath);
                    check(image.Source == null && !image.IsLoaded, "未加载模板不下载图片，测试不访问网络");
                    var date = Descendants<DatePicker>(view).Single(p => BindingOperations.GetBinding(p, DatePicker.SelectedDateProperty)?.Path.Path == "Header.InvoiceUpdateDate");
                    date.SetCurrentValue(DatePicker.SelectedDateProperty, new DateTime(2026, 8, 25));
                    date.GetBindingExpression(DatePicker.SelectedDateProperty).UpdateSource(); Pump();
                    check(f.Model.Header.InvoiceUpdateDate == new DateTime(2026, 8, 25), "发票更新日期控件回写快照");
                    f.Model.Header.InvoiceUpdateDate = null; Pump(); check(date.SelectedDate == null, "空快照回显为空，不补默认日期");
                    var invoiceDates = Descendants<DatePicker>(view).Where(p => BindingOperations.GetBinding(p, DatePicker.SelectedDateProperty)?.Path.Path == "Header.OrderDate").ToArray();
                    check(invoiceDates.Length > 0 && invoiceDates.All(p => p.SelectedDate == f.Model.Header.OrderDate), "发票日期仍使用订单日期，更新快照不改订单日期");
                }
                finally { window.Close(); }
            }
        }

        // 所有检查在 STA 消息循环运行，按钮和行内 Popup 使用实际模板。
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
                    var entry = Descendants<ToggleButton>(grid).Single(b => b.Name == "MatchActionsEntry");
                    entry.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump();
                    var popup = Descendants<ImportHeaderPopup>(grid).Single(p => p.Name == "RowMatchPopup");
                    check(popup.IsOpen && ReferenceEquals(popup.DataContext, row) && ReferenceEquals(popup.Tag, f.Model), "行内弹层显式连接本行和页面命令上下文");
                    check(grid.RowDetailsTemplate == null && !Descendants<Button>(view).Any(b => Equals(b.Content, "匹配选中 SKU")), "移除行展开模板和工具栏选中匹配入口");
                    var chooser = Descendants<ComboBox>(popup.Child).Single(c => BindingOperations.GetBinding(c, ItemsControl.ItemsSourceProperty)?.Path.Path == "MatchCandidates");
                    chooser.SetCurrentValue(Selector.SelectedItemProperty, row.MatchCandidates[0]); Pump();
                    check(row.SelectedMatchCandidate != null, "弹层候选绑定回写本行待选项");
                    // 执行实际按钮绑定的命令及参数，避免模拟 RoutedEvent 绕开原生 OnClick。
                    var use = Descendants<Button>(popup.Child).Single(b => ReferenceEquals(b.Command, f.Model.ChooseMatchCommand)); use.Command.Execute(use.CommandParameter); Pump();
                    check(row.SelectedMatchId == candidate.CustomerProductId && !save.IsEnabled, "使用候选只提交关联选择，不跳过重新检验");
                    f.Model.CheckImportCommand.Execute(null); Pump();
                    var confirm = Descendants<CheckBox>(popup.Child).Single(c => BindingOperations.GetBinding(c, ToggleButton.IsCheckedProperty)?.Path.Path == "AcceptLinkSkuMismatch");
                    confirm.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump();
                    check(row.AcceptLinkSkuMismatch && !save.IsEnabled, "型号差异勾选回写且仍要求重新检验");
                    var match = Descendants<Button>(popup.Child).Single(b => ReferenceEquals(b.Command, f.Model.MatchLineSkuCommand));
                    check(ReferenceEquals(match.CommandParameter, row) && match.IsEnabled, "弹层匹配命令参数固定为当前行");
                    match.Command.Execute(match.CommandParameter); Pump();
                    check(row.IsCheckPassed && !save.IsEnabled, "行内匹配通过仍不能替代整单保存检验");
                    ApplicationCommands.Close.Execute(null, popup.Child); Pump();
                    check(!popup.IsOpen && entry.IsChecked == false && row.AcceptLinkSkuMismatch, "关闭弹层同步入口并保留差异确认输入");
                    entry.SetCurrentValue(ToggleButton.IsCheckedProperty, true); Pump();
                    f.Model.CheckImportCommand.Execute(null); Pump(); check(save.IsEnabled, "后端整单通过后真实保存按钮启用");
                    check(grid.Columns.OfType<DataGridTextColumn>().Any(c => (c.Binding as Binding)?.Path.Path == "LinkSku"), "原表型号之外独立展示系统中性型号");
                    var wait = new TaskCompletionSource<ServiceResult<SaveSalesOrderResponse>>(); f.Api.Save = r => wait.Task;
                    var saveTask = f.Model.SaveDraftAsync(); Pump();
                    check(!confirm.IsEnabled && !chooser.IsEnabled && grid.IsReadOnly && !save.IsEnabled && !match.IsEnabled, "保存期间弹层输入、行匹配、网格和重复保存均锁定");
                    wait.SetResult(ServiceResult<SaveSalesOrderResponse>.Ok(new SaveSalesOrderResponse { Document = new SalesOrderDetailDto {
                        Header = new SalesOrderEditHeaderDto { Uid = Guid.NewGuid(), CustomerId = f.Model.Header.CustomerId, OrderNo = "SO-STA" } } }));
                    var timeout = System.Diagnostics.Stopwatch.StartNew(); while (!saveTask.IsCompleted && timeout.ElapsedMilliseconds < 10000) Pump();
                    check(saveTask.IsCompleted && !saveTask.IsFaulted && f.Model.Header.OrderNo == "SO-STA", "异步回执在 STA 接纳并回显单号");
                    Pump(); check(!save.IsEnabled && f.Model.NewImportCommand.CanExecute(null), "成功后不能再新建相同输入，可开始新导入");
                    window.Content = null; Pump();
                    check(!popup.IsOpen && entry.IsChecked == false, "行模板卸载关闭浮动弹层并复位入口");
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
                    var full = Descendants<Button>(view).Single(b => ReferenceEquals(b.Command, f.Model.CheckImportCommand));
                    f.Model.Lines.Add(new SwiftExport.Client.VSTO.Features.Sales.Orders.Models.SalesOrderImportLine(false, null)); Pump();
                    var disabledRow = Descendants<Button>(grid).Single(b => ReferenceEquals(b.Command, f.Model.MatchLineSkuCommand));
                    check(!all.IsEnabled && !disabledRow.IsEnabled && !full.IsEnabled, "未选客户时全部、行内及整单匹配均禁用");
                    f.Model.SelectedCustomer = f.Cache.Customers[0]; f.Model.CustomerSelectedCommand.Execute(null);
                    f.Model.AddStandardLineCommand.Execute(null); var row = f.Model.Lines[0]; row.CustomerSku = "AQ1";
                    Pump();
                    grid.SelectedItem = null; Pump();
                    var selected = Descendants<Button>(grid).Single(b => ReferenceEquals(b.Command, f.Model.MatchLineSkuCommand));
                    check(all.IsEnabled && full.IsEnabled && selected.IsEnabled && grid.SelectedItem == null, "已选客户启用全部和行内匹配，行内命令不要求选择网格行；all=" + all.IsEnabled + ", full=" + full.IsEnabled + ", row=" + selected.IsEnabled + ", selected=" + grid.SelectedItem + ", parameter=" + ReferenceEquals(selected.CommandParameter, row) + ", can=" + f.Model.MatchLineSkuCommand.CanExecute(row) + ", loaded=" + selected.IsLoaded);
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
                    selected.Command.Execute(selected.CommandParameter); Pump();
                    check(row.Sku == "REAL" && StatusDot(grid, row).Fill is SolidColorBrush green && green.Color == success.Color
                        && !f.Model.CanSaveDraft, "行内匹配回填真实 SKU、圆点变绿，但仍需整单检验");
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
