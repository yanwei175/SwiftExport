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
        }
        private static Task Immediate(Action action) { action(); return Task.CompletedTask; }
        private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var child = VisualTreeHelper.GetChild(node, i); if (child is T match) yield return match; foreach (var value in Descendants<T>(child)) yield return value; } }
    }
}
