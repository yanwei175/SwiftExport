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
        }
        private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var value in Descendants<T>(child)) yield return value; }
        }
    }
}
