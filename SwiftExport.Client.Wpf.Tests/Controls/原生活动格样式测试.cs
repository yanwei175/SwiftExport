using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Behaviors;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>使用客户端真实资源验证活动行、活动格与验证提示，不依赖选择状态镜像。</summary>
[TestClass]
public sealed class NativeCurrentCellStyleTests
{
    /// <summary>未挂载、回收及重载的单元格不应因活动位置样式查找祖先而产生绑定警告。</summary>
    [TestMethod]
    public void CurrentCellStyle_ShouldNotTraceMissingAncestorDuringContainerLifecycle() =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            PresentationTraceSources.Refresh();
            using var output = new StringWriter();
            using var listener = new TextWriterTraceListener(output);
            var trace = PresentationTraceSources.DataBindingSource;
            var oldLevel = trace.Switch.Level;
            trace.Switch.Level = SourceLevels.Warning;
            trace.Listeners.Add(listener);
            var grid = new DataGrid
            {
                Style = (Style)Application.Current.FindResource("SwiftExportDataGrid"),
                ItemsSource = Enumerable.Range(0, 100).Select(i => new StyleRow($"R{i}")).ToArray(),
                SelectionUnit = DataGridSelectionUnit.Cell
            };
            grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(StyleRow.Name)) });
            var window = new Window { Content = grid, Width = 400, Height = 240, ShowInTaskbar = false };
            try
            {
                // 容器在进入树之前即可应用样式；这个状态也会发生在回收及卸载期间。
                var detached = new DataGridCell
                {
                    Style = (Style)Application.Current.FindResource("SwiftExportDataGridCell"),
                    DataContext = new StyleRow("detached")
                };
                detached.ApplyTemplate();
                await Drain();
                Assert.IsNull(DataGridPresentation.GetOwner(detached));
                window.Show();
                grid.UpdateLayout();
                foreach (var index in new[] { 0, 99, 0 })
                {
                    var item = grid.Items[index];
                    grid.ScrollIntoView(item, grid.Columns[0]);
                    grid.UpdateLayout();
                    grid.CurrentCell = new DataGridCellInfo(item, grid.Columns[0]);
                    await Drain();
                    var cell = FindCell(grid.Columns[0].GetCellContent(item));
                    Assert.AreSame(grid, DataGridPresentation.GetOwner(cell));
                    Assert.AreEqual(new Thickness(0.8), cell.BorderThickness);
                }
                window.Content = null;
                await Drain();
                window.Content = grid;
                grid.UpdateLayout();
                await Drain();
                window.Close();
                await Drain();
                trace.Flush();
                var warnings = output.ToString().Split(Environment.NewLine)
                    .Where(line => line.Contains("CurrentCell", StringComparison.Ordinal)).ToArray();
                Assert.IsEmpty(warnings, string.Join(Environment.NewLine, warnings));
                GC.KeepAlive(detached);
            }
            finally
            {
                window.Close();
                trace.Listeners.Remove(listener);
                trace.Switch.Level = oldLevel;
            }
        });

    /// <summary>原生 CurrentCell 驱动活动视觉；验证边框优先，离开活动格后恢复普通错误边框。</summary>
    [TestMethod]
    public void NativeCurrentCell_ShouldDriveActualStylesAndKeepValidationPriority() =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            var rows = new[] { new StyleRow("A"), new StyleRow("B") };
            var grid = new DataGrid
            {
                Style = (Style)Application.Current.FindResource("SwiftExportDataGrid"),
                ItemsSource = rows,
                SelectionUnit = DataGridSelectionUnit.Cell
            };
            grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(StyleRow.Name)) });
            GridValidation.SetIsEnabled(grid, true);
            var window = new Window { Content = grid, Width = 400, Height = 240, ShowInTaskbar = false };
            try
            {
                window.Show();
                grid.UpdateLayout();
                Assert.IsTrue(grid.EnableRowVirtualization);
                Assert.IsTrue(grid.EnableColumnVirtualization);
                Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(grid));
                Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(grid));
                Assert.IsTrue(ScrollViewer.GetCanContentScroll(grid));
                grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[0]);
                await Drain();
                var first = FindCell(grid.Columns[0].GetCellContent(rows[0]));
                var second = FindCell(grid.Columns[0].GetCellContent(rows[1]));
                var firstRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(rows[0]);
                Assert.AreEqual(new Thickness(0.8), first.BorderThickness);
                Assert.AreSame(Application.Current.FindResource("SwiftExportBrush.DataGridCurrentRowBackground"), firstRow.Background);
                Assert.AreEqual(new Thickness(0), second.BorderThickness);

                rows[0].SetError("测试错误");
                await Drain();
                Assert.AreEqual(new Thickness(2), first.BorderThickness);
                Assert.AreSame(Application.Current.FindResource("UserBusinessTextWarningBrush"), first.BorderBrush);
                Assert.AreEqual("测试错误", first.ToolTip);

                grid.CurrentCell = new DataGridCellInfo(rows[1], grid.Columns[0]);
                await Drain();
                Assert.AreEqual(new Thickness(0.8), second.BorderThickness);
                Assert.AreEqual((Thickness)Application.Current.FindResource("SwiftExportDataGridValidationBorderThickness"), first.BorderThickness);
                rows[0].SetError(string.Empty);
                await Drain();
                Assert.AreEqual(new Thickness(0), first.BorderThickness);

                // 重载同一视图仍直接读取原生状态，不靠协调器恢复视觉镜像。
                window.Content = null;
                await Drain();
                window.Content = grid;
                grid.UpdateLayout();
                grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[0]);
                await Drain();
                Assert.AreEqual(new Thickness(0.8), FindCell(grid.Columns[0].GetCellContent(rows[0])).BorderThickness);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>沿真实列内容向上取得容器，避免假定主题模板的子节点位置。</summary>
    private static DataGridCell FindCell(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is DataGridCell cell) return cell;
        throw new AssertFailedException("未找到原生单元格容器。");
    }

    private static Task Drain() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    /// <summary>通过模型通知驱动真实错误聚合与共享样式，不直接注入单元格呈现状态。</summary>
    private sealed class StyleRow(string name) : IDataErrorInfo, INotifyPropertyChanged
    {
        private string _fieldError = string.Empty;
        public string Name { get; } = name;
        public string Error => string.Empty;
        /// <summary>仅名称字段提供软错误。</summary>
        public string this[string columnName] => columnName == nameof(Name) ? _fieldError : string.Empty;
        /// <summary>通知适配重新读取模型错误。</summary>
        public event PropertyChangedEventHandler? PropertyChanged;
        /// <summary>模拟字段错误到达和清除，不创建 WPF 提交错误。</summary>
        public void SetError(string error)
        {
            _fieldError = error;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }
}
