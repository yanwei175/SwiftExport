using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Models;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.Views;
using SwiftExport.Client.VSTO.Features.Customers.Components;
using SwiftExport.Client.VSTO.Resources.Controls;
using SwiftExport.Client.VSTO.Tests;
namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>真实资源和 XAML 的 STA 绑定验证，不加载 Office。</summary>
    internal static class PackingListImportChecks
    {
        /// <summary>验证常驻单头、行内匹配、多选和排序定位；保留实际保存门禁与候选选择断言。</summary>
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
                    ImportLayoutChecks.Run(view, grid, check, expectedPopupCount: 0);
                    check(ReferenceEquals(grid.ItemsSource, model.Lines), "装箱单绑定真实行集合");
                    check(grid.FontSize == 10 && Find<DataGridColumnHeader>(grid).Where(x => x.Content != null).All(x => x.FontSize == 10), "表格与真实列头统一字号 10");
                    // 图标继续使用控件模板自己的尺寸，只统一数据文字与按钮标签。
                    var cellText = Find<DataGridRow>(grid).SelectMany(Find<TextBlock>).Where(x => !string.IsNullOrEmpty(x.Text) && !IsIconText(x)).ToArray();
                    check(cellText.All(x => x.FontSize == 10), "单元格文本及模板按钮实际字号 10：" + string.Join("；", cellText.Where(x => x.FontSize != 10).Select(x => x.Text + "=" + x.FontSize)));
                    check(grid.IsEnabled && !grid.IsReadOnly && !grid.CanUserAddRows && grid.SelectionMode == DataGridSelectionMode.Extended, "原生表格允许编辑、多选且禁止自动空白行");
                    var referenceColumns = grid.Columns.OfType<DataGridTextColumn>().Where(x =>
                        (x.Binding as Binding)?.Path?.Path == "CustomerPoNo" || (x.Binding as Binding)?.Path?.Path == "InvoiceNo").ToArray();
                    check(referenceColumns.Length == 2 && referenceColumns.All(x => !x.IsReadOnly), "Q/R 参考列绑定原文字段并允许编辑，不依赖显示列名");
                    // 下拉菜单有独立可视树，实际打开后确认三个入口仍接纳页面命令。
                    var more = Find<Wpf.Ui.Controls.DropDownButton>(view).Single();
                    more.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                        { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent }); Pump();
                    var flyout = (ContextMenu)more.Flyout;
                    check(flyout.IsOpen && flyout.Items.OfType<MenuItem>().Select(x => x.Command)
                        .SequenceEqual(new ICommand[] { model.InitializeCommand, model.DesignCommand, model.NewCommand }), "更多下拉菜单打开后正确绑定配置加载、设计及新导入命令");
                    flyout.IsOpen = false; Pump();
                    var header = (StackPanel)view.FindName("HeaderStackPanel");
                    var invoices = Find<Wpf.Ui.Controls.TextBox>(header).Single(x => x.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path == "InvoiceNosSummary");
                    model.Lines[0].InvoiceNo = " INV-1 "; Pump();
                    check(invoices.IsReadOnly && invoices.Text == "INV-1", "相关发票号只读并随明细 InvoiceNo 编辑自动回显汇总");
                    check(view.FindName("OtherPopup") == null && !header.Children.OfType<ImportHeaderPopup>().Any(), "全部单头常驻，不再使用单头弹层");
                    var customer = Find<CustomerAutoSuggestComboBox>(header).Single();
                    ((LookupTextBox)customer.FindName("LookupEditor")).CommitExternalSelection(fixture.Base.Cache.Customers[0], LookupCommitReason.Mouse); Pump();
                    check(ReferenceEquals(customer.SelectedCustomer, model.SelectedCustomer) && model.MatchAllSkusCommand.CanExecute(null), "客户真实提交回显并启用匹配命令");
                    var dates = Find<DatePicker>(header).ToArray();
                    var eta = dates.Single(x => x.GetBindingExpression(DatePicker.SelectedDateProperty)?.ParentBinding.Path?.Path == "Header.Eta");
                    eta.SelectedDate = new DateTime(2026, 12, 3); check(model.Header.Eta == eta.SelectedDate, "常驻 ETA 双向绑定");
                    var required = Find<TextBlock>(header).Where(x => x.Text.Contains("*")).ToArray();
                    check(required.Length == 2 && required.All(x => Equals(x.Foreground, Brushes.Red)), "客户、单据日期红字星号");
                    var status = (ScrollViewer)view.FindName("StatusScroll");
                    check(status.MaxHeight == 84 && status.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "提示最多 84 高度，内部滚动");
                    var numberColumn = (DataGridTemplateColumn)grid.Columns.Single(x => (string)x.Header == "行号");
                    var processingColumn = (DataGridTemplateColumn)grid.Columns.Single(x => (string)x.Header == "校验 / 处理");
                    check(numberColumn.SortMemberPath == "LineNumber" && processingColumn.SortMemberPath == "IsCheckPassed" && grid.RowHeaderWidth == 0, "行号可还原顺序，校验列可集中处理错误，取消重复行头");
                    var unitColumn = (DataGridTemplateColumn)grid.Columns.Single(x => (string)x.Header == "单位");
                    grid.ScrollIntoView(model.Lines[0], unitColumn); Pump(); grid.UpdateLayout();
                    var unit = Find<ComboBox>(unitColumn.GetCellContent(model.Lines[0])).Single();
                    check(unitColumn.CellEditingTemplate == null && unit.IsEditable && !unit.IsEnabled, "单位常驻可输入 ComboBox，标准行只读");
                    model.Lines[0].IsNonStandard = true; Pump(); check(unit.IsEnabled, "非标单位允许输入"); model.Lines[0].IsNonStandard = false;
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump(); check(model.SaveCommand.CanExecute(null) && model.Lines[0].Sku == "REAL-SKU", "检查后真实 SKU 和保存资格回显");
                    grid.ScrollIntoView(model.Lines[0], numberColumn); Pump(); grid.UpdateLayout();
                    var circle = Find<Ellipse>(numberColumn.GetCellContent(model.Lines[0])).Single();
                    check(Equals(circle.Fill, view.FindResource("UserBusinessTextSuccessBrush")), "行号内圆点检查通过显示绿色");
                    var imageColumn = (DataGridTemplateColumn)grid.Columns.Single(x => (string)x.Header == "系统图片");
                    var image = Find<HttpsImage>(imageColumn.GetCellContent(model.Lines[0])).Single();
                    check(image.SourcePath == model.Lines[0].MainImage && !string.IsNullOrWhiteSpace(image.SourcePath), "系统图片绑定检查返回的 MainImage");
                    model.Lines[0].QtyText = "0"; Pump();
                    check(Equals(circle.Fill, view.FindResource("UserBusinessTextCriticalBrush")), "编辑撤销资格后圆点变红");
                    var quantityColumn = (DataGridTextColumn)grid.Columns.Single(x => (string)x.Header == "数量");
                    grid.ScrollIntoView(model.Lines[0], quantityColumn); Pump(); grid.UpdateLayout();
                    var quantity = (TextBlock)quantityColumn.GetCellContent(model.Lines[0]);
                    check(Equals(quantity.Foreground, Brushes.Red), "非正数量在真实显示单元格中变红");
                    model.Lines[0].QtyText = "bad"; Pump(); check(!model.SaveCommand.CanExecute(null) && model.Summary.Contains("不完整"), "非法数量保留文字并阻断保存");
                    fixture.Base.Confirm = false; check(!view.CanClose(), "关闭确认取消保留输入");
                    // 使用真实弹层验证候选重新反序列化后的 TwoWay 选择顺序，避免只测模型遗漏绑定回写。
                    model.Lines[0].QtyText = "3";
                    var choices = new[] { Guid.NewGuid(), Guid.NewGuid() };
                    fixture.Transport.Check = request => Task.FromResult(PackingListImportTests.CandidateResponse(request, choices));
                    grid.ScrollIntoView(model.Lines[0], processingColumn); Pump(); grid.UpdateLayout();
                    var popup = Find<ImportHeaderPopup>(processingColumn.GetCellContent(model.Lines[0])).Single();
                    popup.EntryButton.IsChecked = true; Pump();
                    check(popup.IsOpen && ReferenceEquals(popup.DataContext, model.Lines[0]) && ReferenceEquals(popup.Tag, model), "行内弹层使用当前行和页面命令，驻留且不占明细高度");
                    var chooser = Find<ComboBox>(popup.Child).Single();
                    var matchButton = Find<Button>(popup.Child).Single(x => Equals(x.Content, "匹配本行 SKU"));
                    check(ReferenceEquals(matchButton.Command, model.MatchLineSkuCommand) && ReferenceEquals(matchButton.CommandParameter, model.Lines[0]) && matchButton.IsEnabled, "弹层本行命令参数接线正确");
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump();
                    check(chooser.SelectedItem == null && !model.SaveCommand.CanExecute(null), "多候选不自动选择");
                    chooser.SelectedItem = model.Lines[0].Candidates[1]; Pump();
                    Wait(model.MatchLineSkuCommand.ExecuteAsync(model.Lines[0])); Pump();
                    check(ReferenceEquals(chooser.SelectedItem, model.Lines[0].Candidates[1]) && !model.SaveCommand.CanExecute(null), "本行候选重新反序列化保留关系，但不授予保存资格");
                    model.Lines[0].AcceptLinkSkuMismatch = true;
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump();
                    check(model.SaveCommand.CanExecute(null) && model.Lines[0].SelectedCandidate.CustomerProductId == choices[1], "确认型号差异后仍可检查通过");
                    model.Lines[0].QtyText = "4";
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump();
                    check(model.SaveCommand.CanExecute(null) && ReferenceEquals(chooser.SelectedItem, model.Lines[0].Candidates[1]), "改数量不丢失所选关系");
                    var close = Find<Button>(popup.Child).Single(x => ReferenceEquals(x.Command, ApplicationCommands.Close));
                    ApplicationCommands.Close.Execute(null, close); Pump();
                    check(!popup.IsOpen && popup.EntryButton.IsChecked == false, "行内关闭按钮同步入口");
                    popup.EntryButton.IsChecked = true; Pump(); window.Content = null; Pump();
                    check(!popup.IsOpen, "卸载页面关闭独立行内弹层");
                    window.Content = view; Pump();
                    check(model.Lines.Count == 1 && model.Lines[0].QtyText == "4", "重复 Loaded 不重置编辑");
                    VerifySelectionAndPosition(model, grid, check);
                    // 多行反馈只能在提示区滚动，不能挤掉 DataGrid；真实视图接纳后台返回的完整问题文本。
                    fixture.Transport.Check = request => {
                        var response = fixture.Transport.Response(request); response.CanSave = false;
                        response.Issues.AddRange(Enumerable.Range(1, 80).Select(i => new SwiftExport.Contracts.DTOs.ForExcel.PackingLists.PackingListImportIssueDto { Message = "问题 " + i }));
                        return Task.FromResult(response);
                    };
                    Wait(model.CheckCommand.ExecuteAsync(null)); Pump(); view.UpdateLayout();
                    check(status.ActualHeight <= 84 && grid.ActualHeight > view.ActualHeight * .35, "大量错误反馈不挤占明细区域");
                    check(Find<TextBlock>(status).Any(x => !string.IsNullOrWhiteSpace(x.Text) && Equals(x.Foreground, Brushes.Red)), "检验反馈使用红字");
                }
                finally { window.Close(); }
            }
            VerifyDesignPreview(check);
        }
        /// <summary>不创建真实页面服务即可显示设计数据；运行时 BAML 不自动注入设计模型。</summary>
        private static void VerifyDesignPreview(Action<bool, string> check)
        {
            using (var design = new PackingListImportDesignViewModel())
            {
                var view = new PackingListImportView();
                check(view.DataContext == null, "运行时忽略 d:DataContext，不自动实例化设计模型");
                view.DataContext = design;
                var window = new Window { Style = null, Content = view, Width = 1280, Height = 820, ShowInTaskbar = false };
                try
                {
                    window.Show(); Pump(); view.UpdateLayout();
                    var grid = (DataGrid)view.FindName("LinesGrid");
                    check(grid.Items.Count == 3 && ReferenceEquals(grid.ItemsSource, design.Lines), "无参视图可用设计模型显示三条示例，无需注入运行服务");
                    check(design.Lines.Select(x => x.LineNumber).SequenceEqual(new[] { 1, 2, 3 })
                        && design.Lines[0].IsCheckPassed && !design.Lines[1].IsCheckPassed && design.Lines[2].IsNonStandard,
                        "设计示例覆盖已匹配、未匹配及非标状态，并提供真实行号");
                    var header = (StackPanel)view.FindName("HeaderStackPanel");
                    var customer = Find<CustomerAutoSuggestComboBox>(header).Single();
                    check(ReferenceEquals(customer.SelectedCustomer, design.SelectedCustomer)
                        && Find<DatePicker>(header).Any(x => x.SelectedDate == design.Header.InvoiceDate), "设计单头的客户和日期实际回显");
                    var note = design.Header.Note;
                    design.InitializeCommand.Execute(null); design.ReadLinesCommand.Execute(null); design.SaveCommand.Execute(null);
                    Pump(); check(design.Lines.Count == 3 && design.Header.Note == note && design.InvoiceNosSummary == "260820", "设计按钮无业务副作用，发票汇总与示例明细一致");
                }
                finally { window.Close(); }
            }
        }
        /// <summary>排序只改变视图；新增仍按稳定行号定位，多选删除按行对象身份处理。</summary>
        private static void VerifySelectionAndPosition(SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels.PackingListImportViewModel model, DataGrid grid, Action<bool, string> check)
        {
            var rows = CollectionViewSource.GetDefaultView(model.Lines);
            rows.SortDescriptions.Add(new SortDescription("LineNumber", ListSortDirection.Descending));
            model.AddStandardLineCommand.Execute(null); Pump(); grid.UpdateLayout(); var standard = model.Lines.Last();
            check(ReferenceEquals(grid.SelectedItem, standard) && ReferenceEquals(grid.CurrentCell.Item, standard) && grid.ItemContainerGenerator.ContainerFromItem(standard) != null, "排序后添加标准行仍选中、定位并生成实际容器");
            model.AddNonStandardLineCommand.Execute(null); Pump(); grid.UpdateLayout(); var nonstandard = model.Lines.Last();
            check(ReferenceEquals(grid.SelectedItem, nonstandard) && nonstandard.IsNonStandard, "添加非标行立即选中定位");
            check(((PackingListImportLine)grid.Items[0]).LineNumber == 3 && model.Lines[0].LineNumber == 1, "显示排序不更改保存集合顺序");
            rows.SortDescriptions.Clear(); rows.SortDescriptions.Add(new SortDescription("IsCheckPassed", ListSortDirection.Ascending)); Pump();
            check(grid.Items.Cast<PackingListImportLine>().Select(x => x.IsCheckPassed).SequenceEqual(new[] { false, false, true }), "校验排序把未通过行集中在前面");
            rows.SortDescriptions.Clear(); rows.SortDescriptions.Add(new SortDescription("LineNumber", ListSortDirection.Ascending)); Pump();
            check(grid.Items.Cast<PackingListImportLine>().Select(x => x.LineNumber).SequenceEqual(new[] { 1, 2, 3 }), "行号排序还原导入顺序");
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(standard); grid.SelectedItems.Add(nonstandard); Pump();
            model.DeleteLineCommand.Execute(null); Pump();
            check(model.Lines.Count == 1 && model.Lines[0].LineNumber == 1, "视图桥接多选删除，不重编号剩余行");
        }
        /// <summary>在当前 STA 执行同步调度。</summary>
        private static Task Immediate(Action action) { action(); return Task.CompletedTask; }
        /// <summary>排除图标控件内部的字形文本，其字号由图标模板独立控制。</summary>
        private static bool IsIconText(TextBlock text)
        {
            for (DependencyObject node = text; node != null; node = VisualTreeHelper.GetParent(node))
                if (node is Wpf.Ui.Controls.IconElement) return true;
            return false;
        }
        /// <summary>等待异步任务时处理绑定消息，不阻塞 STA 调度。</summary>
        private static void Wait(Task task) { while (!task.IsCompleted) Pump(); task.GetAwaiter().GetResult(); }
        /// <summary>推进当前线程的绑定及布局消息。</summary>
        private static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); }
        /// <summary>弹层独立可视树从 Child 查找，表格从当前实际单元格查找。</summary>
        private static System.Collections.Generic.IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T value) yield return value; foreach (var nested in Find<T>(child)) yield return nested; } }
    }
}
