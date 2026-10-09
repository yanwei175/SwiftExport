using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views;
using SwiftExport.Client.VSTO.Tests;
namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>真实资源和 XAML 的 STA 绑定验证，不加载 Office。</summary>
    internal static class PackingListImportChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            using (var fixture = new PackingListImportTests.Fixture())
            {
                var model = fixture.Model; var view = new PackingListImportView(model);
                var window = new Window { Style = null, Content = view, Width = 1280, Height = 820, ShowInTaskbar = false };
                fixture.Base.Dispatch = action => window.Dispatcher.CheckAccess() ? Immediate(action) : window.Dispatcher.InvokeAsync(action).Task;
                Wait(fixture.Start()); window.Show();
                try
                {
                    Pump(); view.UpdateLayout(); var grid = (DataGrid)view.FindName("LinesGrid");
                    ImportLayoutChecks.Run(view, grid, check);
                    check(ReferenceEquals(grid.ItemsSource, model.Lines), "装箱单绑定真实行集合");
                    check(grid.IsEnabled && !grid.IsReadOnly && !grid.CanUserAddRows, "原生表格允许编辑并禁止自动空白行");
                    check(grid.Columns.Any(x => (string)x.Header == "PO 原文") && grid.Columns.Any(x => (string)x.Header == "Invoice 原文"), "Q/R 参考列可编辑");
                    var transportPopup = (Popup)view.FindName("OtherPopup");
                    ((ToggleButton)view.FindName("OtherToggleButton")).IsChecked = true; Pump();
                    var dates = Find<DatePicker>(transportPopup.Child).ToArray();
                    var eta = dates.Single(x => x.GetBindingExpression(DatePicker.SelectedDateProperty)?.ParentBinding.Path?.Path == "Header.Eta");
                    eta.SelectedDate = new DateTime(2026, 12, 3); check(model.Header.Eta == eta.SelectedDate, "ETA 双向绑定");
                    transportPopup.SetCurrentValue(Popup.IsOpenProperty, false); Pump();
                    var unitColumn = (DataGridTemplateColumn)grid.Columns.Single(x => (string)x.Header == "单位");
                    var editor = (Wpf.Ui.Controls.TextBox)unitColumn.CellTemplate.LoadContent(); editor.DataContext = model.Lines[0]; Pump();
                    check(!editor.IsEnabled, "标准单位只读"); model.Lines[0].IsNonStandard = true; Pump(); check(editor.IsEnabled, "非标单位允许输入"); model.Lines[0].IsNonStandard = false;
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump(); check(model.SaveCommand.CanExecute(null) && model.Lines[0].Sku == "REAL-SKU", "检查后真实 SKU 和保存资格回显");
                    model.Lines[0].QtyText = "bad"; Pump(); check(!model.SaveCommand.CanExecute(null) && model.Summary.Contains("不完整"), "非法数量保留文字并阻断保存");
                    fixture.Base.Confirm = false; check(!view.CanClose(), "关闭确认取消保留输入");
                    // 使用真实单元格模板验证替换候选时的 TwoWay 选择事件，普通模型测试不能证明该绑定顺序。
                    model.Lines[0].QtyText = "3";
                    var choices = new[] { Guid.NewGuid(), Guid.NewGuid() };
                    fixture.Transport.Check = request => Task.FromResult(PackingListImportTests.CandidateResponse(request, choices));
                    var choiceColumn = (DataGridTemplateColumn)grid.Columns.Single(x => (string)x.Header == "多候选选择");
                    var chooser = (ComboBox)choiceColumn.CellTemplate.LoadContent(); chooser.DataContext = model.Lines[0];
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump();
                    check(chooser.SelectedItem == null && !model.SaveCommand.CanExecute(null), "多候选不自动选择");
                    chooser.SelectedItem = model.Lines[0].Candidates[1]; Pump();
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump();
                    check(ReferenceEquals(chooser.SelectedItem, model.Lines[0].Candidates[1]), "候选重新反序列化后保留同一关系");
                    model.Lines[0].AcceptLinkSkuMismatch = true;
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump();
                    check(model.SaveCommand.CanExecute(null) && model.Lines[0].SelectedCandidate.CustomerProductId == choices[1], "确认型号差异后仍可检查通过");
                    model.Lines[0].QtyText = "4";
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump();
                    check(model.SaveCommand.CanExecute(null) && ReferenceEquals(chooser.SelectedItem, model.Lines[0].Candidates[1]), "改数量不丢失所选关系");
                }
                finally { window.Close(); }
            }
        }
        private static Task Immediate(Action action) { action(); return Task.CompletedTask; }
        private static void Wait(Task task) { while (!task.IsCompleted) Pump(); task.GetAwaiter().GetResult(); }
        private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value; foreach (var nested in Find<T>(child)) yield return nested; } }
    }
}
