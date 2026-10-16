using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xaml.Behaviors;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Behaviors;

namespace SwiftExport.Client.Wpf.Tests.Resources.Behaviors;

/// <summary>验证客户端薄行为的选区、原生提交、默认禁用和生命周期，不依赖框架操作模块。</summary>
[TestClass]
[DoNotParallelize]
public sealed class DataGridFillDownBehaviorTests
{
    /// <summary>只提交显式开放的列；行为不自行写值，FullRow 与非矩形选区不会执行。</summary>
    [TestMethod]
    public void Rectangle_ShouldDispatchOnlyExplicitFieldsAndRespectCurrentPermission() => StaTest.RunAsync(() =>
    {
        using var host = new Host();
        FillDownRequest? received = null;
        var allowed = true;
        host.Behavior.Command = new RelayCommand<FillDownRequest>(value => received = value, _ => allowed);
        Select(host.Grid, 0, 2, 0, 1);
        Assert.IsTrue(DataGridFillDownBehavior.FillDownCommand.CanExecute(null, host.Grid));
        DataGridFillDownBehavior.FillDownCommand.Execute(null, host.Grid);
        Assert.IsNotNull(received);
        Assert.AreSame(host.Rows[0], received.Source);
        CollectionAssert.AreEqual(new[] { nameof(Row.Value) }, received.Fields.ToArray());
        Assert.HasCount(2, received.Targets);
        Assert.AreEqual("Row-1", host.Rows[1].Value, "行为本身不得写属性。");
        received = null;
        allowed = false;
        DataGridFillDownBehavior.FillDownCommand.Execute(null, host.Grid);
        Assert.IsNull(received);
        allowed = true;
        host.Grid.IsReadOnly = true;
        Assert.IsFalse(DataGridFillDownBehavior.FillDownCommand.CanExecute(null, host.Grid));
        host.Grid.IsReadOnly = false;
        host.Grid.SelectedCells.Remove(host.Grid.SelectedCells[1]);
        Assert.IsFalse(DataGridFillDownBehavior.FillDownCommand.CanExecute(null, host.Grid));
        host.Grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        host.Grid.SelectAll();
        Assert.IsFalse(DataGridFillDownBehavior.FillDownCommand.CanExecute(null, host.Grid));
        return Task.CompletedTask;
    });

    /// <summary>按排序筛选后的视图取源行；隐藏列和未生成的行容器不影响请求。</summary>
    [TestMethod]
    public void SortedFilteredVirtualizedSelection_ShouldUseViewOrder() => StaTest.RunAsync(() =>
    {
        using var host = new Host(200);
        host.Grid.Items.Filter = item => ((Row)item).Quantity >= 50;
        host.Grid.Items.SortDescriptions.Add(new SortDescription(nameof(Row.Quantity), ListSortDirection.Descending));
        host.Grid.Columns[1].Visibility = Visibility.Collapsed;
        host.Grid.Columns[1].DisplayIndex = 0;
        FillDownRequest? received = null;
        host.Behavior.Command = new RelayCommand<FillDownRequest>(value => received = value);
        Select(host.Grid, 0, 99, 0, 0);
        DataGridFillDownBehavior.FillDownCommand.Execute(null, host.Grid);
        Assert.IsNotNull(received);
        Assert.AreSame(host.Rows[199], received.Source);
        Assert.AreSame(host.Rows[100], received.Targets[^1]);
        Assert.HasCount(99, received.Targets);
        Assert.IsTrue(host.Grid.EnableRowVirtualization);
        Assert.IsNull(host.Grid.ItemContainerGenerator.ContainerFromItem(host.Rows[100]));
        return Task.CompletedTask;
    });

    /// <summary>卸载、重载、移除行为只改变自身 Ctrl+D 绑定；不注册 Undo 或删除宿主命令。</summary>
    [TestMethod]
    public void Lifecycle_ShouldDetachOnlyOwnedBindingAndNeverRegisterUndo() => StaTest.RunAsync(() =>
    {
        using var host = new Host();
        var existing = new CommandBinding(ApplicationCommands.Help, (_, _) => { });
        host.Grid.CommandBindings.Add(existing);
        Assert.AreEqual(1, CountFillBindings(host.Grid));
        host.Window.Content = null;
        Pump(host.Grid);
        Assert.AreEqual(0, CountFillBindings(host.Grid));
        host.Window.Content = host.Grid;
        Pump(host.Grid);
        Assert.AreEqual(1, CountFillBindings(host.Grid));
        Interaction.GetBehaviors(host.Grid).Remove(host.Behavior);
        Assert.AreEqual(0, CountFillBindings(host.Grid));
        Assert.IsTrue(host.Grid.CommandBindings.Contains(existing));
        Assert.IsFalse(host.Grid.CommandBindings.Cast<CommandBinding>().Any(binding => binding.Command == ApplicationCommands.Undo));
        return Task.CompletedTask;
    });

    /// <summary>原生转换失败保留编辑输入；修正后先提交源值，再执行页面命令。</summary>
    [TestMethod]
    public void ConversionFailure_ShouldBlockFillUntilSourceEditCommits() => StaTest.RunAsync(() =>
    {
        using var host = new Host();
        var column = host.Grid.Columns[1];
        DataGridFillDownBehavior.SetField(column, nameof(Row.Quantity));
        var calls = 0;
        host.Behavior.Command = new RelayCommand<FillDownRequest>(request =>
        {
            calls++;
            Assert.IsNotNull(request);
            foreach (Row target in request.Targets) target.Quantity = ((Row)request.Source).Quantity;
        });
        host.Grid.CurrentCell = new DataGridCellInfo(host.Rows[0], column);
        host.Grid.ScrollIntoView(host.Rows[0], column);
        host.Grid.UpdateLayout();
        Assert.IsTrue(host.Grid.BeginEdit());
        var editor = column.GetCellContent(host.Rows[0]) as TextBox;
        Assert.IsNotNull(editor);
        editor.Text = "invalid";
        Select(host.Grid, 0, 1, 1, 1);
        DataGridFillDownBehavior.FillDownCommand.Execute(null, host.Grid);
        Assert.AreEqual(0, calls);
        Assert.AreEqual("invalid", editor.Text);
        Assert.AreEqual(1, host.Rows[1].Quantity);
        editor.Text = "42";
        DataGridFillDownBehavior.FillDownCommand.Execute(null, host.Grid);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(42, host.Rows[0].Quantity);
        Assert.AreEqual(42, host.Rows[1].Quantity);
        Assert.IsFalse(ApplicationCommands.Undo.CanExecute(null, host.Grid));
        return Task.CompletedTask;
    });

    private static int CountFillBindings(DataGrid grid) => grid.CommandBindings.Cast<CommandBinding>()
        .Count(binding => binding.Command == DataGridFillDownBehavior.FillDownCommand);

    /// <summary>构造原生选区，行索引来自当前集合视图。</summary>
    private static void Select(DataGrid grid, int first, int last, int firstColumn, int lastColumn)
    {
        grid.SelectedCells.Clear();
        for (var row = first; row <= last; row++)
            for (var column = firstColumn; column <= lastColumn; column++)
                grid.SelectedCells.Add(new DataGridCellInfo(grid.Items[row], grid.Columns[column]));
    }

    private static void Pump(DataGrid grid) => grid.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>有限窗口宿主，测试结束释放行为及窗口。</summary>
    private sealed class Host : IDisposable
    {
        /// <summary>创建有限窗口及原生虚拟化表格，只声明测试需要的一个填充字段。</summary>
        public Host(int count = 3)
        {
            Rows = new(Enumerable.Range(0, count).Select(index => new Row { Value = $"Row-{index}", Quantity = index }));
            Grid = new DataGrid { ItemsSource = Rows, AutoGenerateColumns = false, CanUserAddRows = false,
                CanUserDeleteRows = false, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                SelectionMode = DataGridSelectionMode.Extended, EnableRowVirtualization = true };
            var value = new DataGridTextColumn { Binding = new Binding(nameof(Row.Value)) };
            DataGridFillDownBehavior.SetField(value, nameof(Row.Value));
            Grid.Columns.Add(value);
            Grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(Row.Quantity))
                { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true } });
            Behavior = new DataGridFillDownBehavior();
            Interaction.GetBehaviors(Grid).Add(Behavior);
            Window = new Window { Content = Grid, Width = 350, Height = 200, ShowInTaskbar = false };
            Window.Show();
            Grid.UpdateLayout();
            Pump(Grid);
        }
        public ObservableCollection<Row> Rows { get; }
        public DataGrid Grid { get; }
        public DataGridFillDownBehavior Behavior { get; }
        public Window Window { get; }
        /// <summary>释放行为订阅与测试窗口。</summary>
        public void Dispose() { Interaction.GetBehaviors(Grid).Clear(); Window.Close(); }
    }

    /// <summary>行为测试的独立数据项；没有隐式字段填充能力。</summary>
    private sealed class Row
    {
        public string Value { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
