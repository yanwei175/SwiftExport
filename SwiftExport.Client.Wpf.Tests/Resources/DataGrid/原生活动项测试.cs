using SwiftExportClient.Resources.Behaviors;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Resources.GridRegression;

/// <summary>验证框架启停不改变原生活动行绑定及选区。</summary>
[TestClass]
public sealed class NativeCurrentItemTests
{
    /// <summary>活动行直接绑定 CurrentItem，保留当前列和多选集合，框架启停不接管选择。</summary>
    [TestMethod]
    public void CurrentItemBinding_ShouldSynchronizeWithoutChangingSelectedCells()
    {
        StaTest.Run(() =>
        {
            var rows = CreateRows();
            var grid = CreateGrid(rows, DataGridSelectionUnit.CellOrRowHeader);
            var probe = new NativeCurrentItemProbe();
            grid.SetBinding(DataGrid.CurrentItemProperty, new Binding(nameof(probe.CurrentItem))
            {
                Source = probe, Mode = BindingMode.TwoWay
            });
            using var host = Show(grid);
            grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[0]);
            grid.SelectedCells.Add(new DataGridCellInfo(rows[0], grid.Columns[0]));
            grid.SelectedCells.Add(new DataGridCellInfo(rows[2], grid.Columns[0]));
            Drain();
            Assert.AreSame(rows[0], probe.CurrentItem);
            probe.CurrentItem = rows[1];
            Drain();
            Assert.AreSame(rows[1], grid.CurrentItem);
            Assert.AreSame(grid.Columns[0], grid.CurrentColumn);
            Assert.AreEqual(2, grid.SelectedCells.Count);
            Assert.IsNull(grid.SelectedItem);

            grid.CurrentCell = new DataGridCellInfo(rows[2], grid.Columns[0]);
            Drain();
            Assert.AreSame(rows[2], probe.CurrentItem);
            // 原生 CurrentItem 不承诺在集合 Reset 后清空；重载用例由 VM 明确释放活动项。
            // 业务 BaseListModel.Clear/Remove 已负责其 SelectedItem，这里验证原生双向绑定接受清空。
            probe.CurrentItem = null;
            rows.Clear();
            Drain();
            Assert.IsNull(probe.CurrentItem);
            Assert.IsNull(grid.CurrentItem);
        });
    }

    /// <summary>不加载主题、筛选或列头适配，证明原生失焦会清空 CurrentItem，而整行选择仍保留。</summary>
    [TestMethod]
    [DataRow(DataGridSelectionUnit.FullRow)]
    [DataRow(DataGridSelectionUnit.Cell)]
    [DataRow(DataGridSelectionUnit.CellOrRowHeader)]
    public void NativeFocusLeave_ShouldClearCurrentItemButKeepRowSelection(DataGridSelectionUnit unit) => StaTest.Run(() =>
    {
        var rows = CreateRows();
        var grid = CreateGrid(rows, unit);
        var probe = new NativeCurrentItemProbe();
        grid.SetBinding(DataGrid.CurrentItemProperty, new Binding(nameof(probe.CurrentItem)) { Source = probe, Mode = BindingMode.TwoWay });
        var button = new Button { Content = "工具栏" };
        var panel = new DockPanel();
        DockPanel.SetDock(button, Dock.Top);
        panel.Children.Add(button);
        panel.Children.Add(grid);
        var window = new Window { Content = panel, Width = 400, Height = 220, ShowInTaskbar = false };
        try
        {
            window.Show();
            window.Activate();
            grid.UpdateLayout();
            if (unit == DataGridSelectionUnit.FullRow) grid.SelectedItem = rows[1];
            var cell = (DataGridCell)grid.Columns[0].GetCellContent(rows[1]).Parent;
            Assert.IsTrue(cell.Focus());
            Drain();
            Assert.AreSame(rows[1], probe.CurrentItem);
            Assert.IsTrue(button.Focus());
            Drain();
            Assert.IsNull(grid.CurrentItem);
            Assert.IsNull(probe.CurrentItem);
            if (unit == DataGridSelectionUnit.FullRow) Assert.AreSame(rows[1], grid.SelectedItem);
        }
        finally { window.Close(); Drain(); }
    });

    private static ObservableCollection<SelectionRow> CreateRows() => [new("A"), new("B"), new("C")];

    /// <summary>测试始终使用原生 DataGrid 和文本列，框架只启用其他模块生命周期。</summary>
    private static DataGrid CreateGrid(ObservableCollection<SelectionRow> rows, DataGridSelectionUnit unit)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false,
            SelectionUnit = unit, SelectionMode = DataGridSelectionMode.Extended
        };
        grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(SelectionRow.Name)) });

        return grid;
    }

    private static WindowScope Show(DataGrid grid)
    {
        var window = new Window { Content = grid, Width = 400, Height = 220, ShowInTaskbar = false };
        window.Show();
        grid.UpdateLayout();
        Drain();
        return new WindowScope(window);
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class SelectionRow(string name)
    {
        public string Name { get; } = name;
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public Window Window => window;
        public void Dispose() => window.Close();
    }
}

/// <summary>以真实属性通知验证原生 CurrentItem 的双向绑定。</summary>
public sealed partial class NativeCurrentItemProbe : ObservableObject
{
    /// <summary>由原生 CurrentItem 双向维护的活动行。</summary>
    [ObservableProperty] public partial object? CurrentItem { get; set; }
}
