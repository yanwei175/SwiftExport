using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Services;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.ViewModels;
using SwiftExport.Client.VSTO.Features.Settings.ExcelReadProfiles.Views;
using SwiftExport.Client.VSTO.Resources.Controls;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Contracts.DTOs.Auth;
namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>实际 STA 验证配置页的四列明细、地址获取、顶部统一名称和结束方式绑定。</summary>
    internal static class ReadProfilePageChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            using (var f = new ImportTestFixture())
            {
                f.Session.TryCommit(f.Session.Capture().Version, "token", new MeDto { UserId = "admin", UserName = "admin", Permissions = new List<string> { PermissionKeys.SystemExcelFieldMappingManage } });
                using (var model = new ExcelReadProfileViewModel(new ExcelReadProfileService(f.ProfileApi), f.AddressReader, ExcelImportRegistry.CreateDefault(), f.Session, f, new ApiCallRunner(new AddInLog()), f))
                {
                    var view = new ExcelReadProfileView(model);
                    var window = new Window { Content = view, Width = 1100, Height = 850 };
                    f.Dispatch = action => window.Dispatcher.CheckAccess() ? Immediate(action) : window.Dispatcher.InvokeAsync(action).Task;
                    try
                    {
                        window.Show(); Pump();
                        check(ReferenceEquals(view.DataContext, model), "配置页 DataContext 正确");
                        var headers = (ItemsControl)view.FindName("HeaderFieldsControl");
                        var first = (FrameworkElement)headers.ItemContainerGenerator.ContainerFromIndex(0);
                        var second = (FrameworkElement)headers.ItemContainerGenerator.ContainerFromIndex(1);
                        var firstPosition = first.TransformToAncestor(headers).Transform(new Point());
                        var secondPosition = second.TransformToAncestor(headers).Transform(new Point());
                        check(Math.Abs(firstPosition.Y - secondPosition.Y) < 1 && secondPosition.X > firstPosition.X,
                            "宽配置页的单头地址在同一行排列");
                        window.Width = 360; Pump(); view.UpdateLayout();
                        secondPosition = second.TransformToAncestor(headers).Transform(new Point());
                        check(secondPosition.Y > firstPosition.Y && ((ScrollViewer)view.Content).ScrollableWidth == 0,
                            "窄配置页的单头地址自动换行且无需横向滚动");
                        window.Width = 1100; Pump(); view.UpdateLayout();
                        var grid = Descendants<DataGrid>(view).Single();
                        check(grid.Columns.Count == 4, "明细采用字段、地址、方向、操作四列");
                        check(model.LineFields.All(r => r.IsDownward), "明细字段默认向下读取");
                        check(model.HeaderFields.All(r => !r.IsDownward), "单头固定单次读取");
                        check(Descendants<TextBox>(view).Count(t => BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path.Path == "WorksheetName") == 1, "参考工作表名称只在顶部设置一次");
                        var box = Descendants<ExcelCellAddressBox>(view).Single(b => b.Address == "M12");
                        var row = model.HeaderFields.Single(r => r.FieldKey == "Header.InvoiceNo");
                        var input = Descendants<TextBox>(box).Single(); input.Text = "M13"; input.GetBindingExpression(TextBox.TextProperty).UpdateSource(); Pump();
                        check(row.CellAddress == "M13", "地址控件双向更新字段模型");
                        f.AddressReader.Selection = new ExcelCellSelection { Address = "N13", WorksheetName = "Sheet2", Preview = "260820" };
                        var pick = Descendants<Button>(box).Single(b => Equals(b.Content, "获取")); pick.Command.Execute(pick.CommandParameter); Pump();
                        check(row.CellAddress == "N13" && row.Preview == "260820", "获取按钮只接纳地址和临时预览");
                        check(model.WorksheetName == "Sheet2", "首次选址补齐统一参考名称");
                        var end = Descendants<ComboBox>(view).Single(c => BindingOperations.GetBinding(c, ComboBox.SelectedValueProperty)?.Path.Path == "EndMode");
                        end.SelectedIndex = 1; Pump(); check(model.EndMode == 1, "结束方式采用 int 协议而非显示文字");
                        model.LastRowColumn = "c"; var request = model.BuildRequest();
                        check(request.LastRowColumn == "C" && request.SummaryCellAddress == null, "仅提交当前结束方式的边界");
                        model.SaveCommand.Execute(null); Pump();
                        check(f.ProfileApi.Saves == 1 && !model.HasChanges, "配置保存完整快照并接纳版本");
                        check(model.HeaderFields.Single(r => r.FieldKey == "Header.InvoiceNo").Preview == "", "服务器快照不包含临时业务值");
                        var pending = new TaskCompletionSource<SwiftExport.Contracts.Wrappers.ServiceResult<SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileDto>>();
                        f.ProfileApi.GetOverride = () => pending.Task;
                        var reload = model.ReloadCommand.ExecuteAsync(null);
                        check(!model.CanChangeFields, "请求进行中禁止修改配置快照");
                        Task.Run(() => pending.SetResult(SwiftExport.Contracts.Wrappers.ServiceResult<SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles.ExcelReadProfileDto>.Ok(f.ProfileApi.Profile)));
                        var timeout = System.Diagnostics.Stopwatch.StartNew();
                        while (!reload.IsCompleted && timeout.ElapsedMilliseconds < 10000) Pump();
                        check(reload.IsCompleted && !reload.IsFaulted && model.HeaderFields.Count > 0 && model.CanChangeFields, "后台详情响应显式投递到 STA");
                        f.Login("next"); Pump(); check(!model.CanEdit && model.HeaderFields.Count == 0, "换号清空并禁用配置页");
                    }
                    finally { window.Close(); }
                }
            }
            CheckSidePane(check);
        }
        // 使用真实 ElementHost 模拟窄侧栏；隐藏重开不得丢失配置，实际 Office 显隐仍需实机验收。
        private static void CheckSidePane(Action<bool, string> check)
        {
            var config = SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneRegistrar.CreateDefault().Get("excel-read-profile");
            check(config.ViewType == typeof(ExcelReadProfileView) && config.DefaultWidth == 860 && config.DockPosition == Dock.Right,
                "读取配置注册为当前 860 宽右侧窗格");
            using (var f = new ImportTestFixture())
            using (var form = new System.Windows.Forms.Form { Width = 560, Height = 820, ShowInTaskbar = false })
            {
                f.Session.TryCommit(f.Session.Capture().Version, "token", new MeDto { UserId = "admin", Permissions = new List<string> { PermissionKeys.SystemExcelFieldMappingManage } });
                using (var model = new ExcelReadProfileViewModel(f.ProfileService, f.AddressReader, ExcelImportRegistry.CreateDefault(), f.Session, f, new ApiCallRunner(new AddInLog()), f))
                {
                    var view = new ExcelReadProfileView(model);
                    var salesView = new SwiftExport.Client.VSTO.Features.Sales.Orders.Views.SalesOrderImportView(f.Model);
                    var requests = 0; salesView.SetConfigurationOpener(uid => requests++);
                    f.Model.DesignReadProfileCommand.Execute(null);
                    check(requests == 1, "销售设计入口通过组合层打开侧栏，不再创建大型配置窗体");
                    var host = new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneHostControl(view) { Dock = System.Windows.Forms.DockStyle.Fill };
                    form.Controls.Add(host); form.Show(); Pump();
                    var scroll = (ScrollViewer)view.Content; var layout = (Grid)scroll.Content;
                    check(layout.MinWidth == 0 && layout.ActualWidth <= form.ClientSize.Width, "配置页不再强制 700 宽，适应窄侧栏");
                    check(scroll.ScrollableWidth == 0 && scroll.ScrollableHeight > 0, "窄侧栏纵向滚动且不需横向访问整个页面");
                    var row = model.HeaderFields.Single(r => r.FieldKey == "Header.InvoiceNo"); row.CellAddress = "M99";
                    form.Hide(); Pump(); form.Show(); Pump();
                    check(row.CellAddress == "M99" && model.HasChanges, "隐藏后再次显示保留未保存地址");
                    view.SetInitialProfile(Guid.NewGuid()); Pump();
                    check(ReferenceEquals(row, model.HeaderFields.Single(r => r.FieldKey == "Header.InvoiceNo")), "再次打开不重定位或替换已编辑配置");
                    var context = new SwiftExport.Client.VSTO.Hosting.TaskPanes.TaskPaneContext { Handle = form.Handle };
                    var prompt = new SwiftExport.Client.VSTO.Features.Sales.Orders.Views.SalesOrderImportPrompt(new SwiftExport.Client.VSTO.Hosting.Windows.ModelessWindowContext(), context);
                    context.Dispose(); check(!prompt.ConfirmDiscard("已释放不应弹窗"), "侧栏上下文释放后禁止使用旧 Owner 确认");
                    host.Dispose(); host.Dispose(); Pump();
                    check(PresentationSource.FromVisual(view) == null, "侧栏宿主幂等释放并断开配置可视树");
                }
            }
        }
        private static Task Immediate(Action action) { action(); return Task.CompletedTask; }
        private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = VisualTreeHelper.GetChild(node, i); if (child is T match) yield return match; foreach (var value in Descendants<T>(child)) yield return value; } }
    }
}
