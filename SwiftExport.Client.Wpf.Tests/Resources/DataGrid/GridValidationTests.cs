using SwiftExportClient.Resources.Behaviors;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Resources.GridRegression;

/// <summary>验证模型软错误关联、合并刷新及事件订阅，不接管原生活动位置。</summary>
[TestClass]
public sealed class GridValidationTests
{
    /// <summary>卸载立即解绑；卸载期间换源后重载只观察新行，反复加载不重复订阅。</summary>
    [TestMethod]
    public void UnloadReload_ShouldReleaseOldRowsAndObserveCurrentSourceOnce()
    {
        StaTest.RunAsync(async () =>
        {
            var oldRow = new ErrorRow("A", "1");
            var newRow = new ErrorRow("B", "2");
            var grid = CreateGrid(oldRow);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.AreEqual(0, oldRow.PropertyChangedSubscriberCount);
            grid.ItemsSource = new[] { newRow };
            newRow.SetErrors(nameof(ErrorRow.Name), "重载前错误");
            Assert.AreEqual(0, GridValidation.GetErrorCount(grid));
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.AreEqual(1, newRow.PropertyChangedSubscriberCount);
            await Drain();
            Assert.AreEqual(1, GridValidation.GetErrorCount(grid));
            GridValidation.SetIsEnabled(grid, false);
            Assert.AreEqual(0, newRow.PropertyChangedSubscriberCount);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.AreEqual(0, newRow.PropertyChangedSubscriberCount);
            GridValidation.SetIsEnabled(grid, true);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.AreEqual(1, newRow.PropertyChangedSubscriberCount);
            GridValidation.SetIsEnabled(grid, false);
        });
    }

    [TestMethod]
    public void ErrorsProperty_ShouldInheritToGridContainersWithoutAncestorBinding()
    {
        var metadata = (FrameworkPropertyMetadata)GridValidation.ErrorsProperty.GetMetadata(typeof(DependencyObject));

        Assert.IsTrue(metadata.Inherits);
    }

    /// <summary>列重排后仍按数据项和列对象关联，不承诺摘要顺序。</summary>
    [TestMethod]
    public void DataErrors_ShouldKeepAssociationAfterColumnReorder()
    {
        StaTest.RunAsync(async () =>
        {
            var first = new ErrorRow("A", "One");
            var second = new ErrorRow("B", "Two");
            var dataGrid = CreateGrid(first, second);
            var nameColumn = dataGrid.Columns[0];
            var codeColumn = dataGrid.Columns[1];
            codeColumn.DisplayIndex = 0;
            nameColumn.DisplayIndex = 1;

            first.SetErrors(nameof(ErrorRow.Name), "名称错误");
            first.SetErrors(nameof(ErrorRow.Code), "代码错误");
            second.SetErrors(nameof(ErrorRow.Code), "第二行错误");

            await Drain();
            var errors = GridValidation.GetErrors(dataGrid);
            Assert.HasCount(3, errors);
            CollectionAssert.AreEquivalent(new[]
            {
                new GridValidationError(first, codeColumn, "代码错误"),
                new GridValidationError(first, nameColumn, "名称错误"),
                new GridValidationError(second, codeColumn, "第二行错误")
            }, errors.ToArray());
            Assert.AreEqual(3, GridValidation.GetErrorCount(dataGrid));

        });
    }

    /// <summary>替换数据源后解除旧行通知，并继续接收新行错误。</summary>
    [TestMethod]
    public void PropertyChangedAndItemsReplacement_ShouldRefreshAndUnsubscribeOldItems()
    {
        StaTest.RunAsync(async () =>
        {
            var oldItem = new ErrorRow("A", "One");
            var newItem = new ErrorRow("B", "Two");
            var dataGrid = CreateGrid(oldItem);
            Assert.AreEqual(1, oldItem.PropertyChangedSubscriberCount);

            oldItem.SetErrors(nameof(ErrorRow.Name), "旧错误");
            await Drain();
            Assert.AreEqual(1, GridValidation.GetErrorCount(dataGrid));

            dataGrid.ItemsSource = new[] { newItem };
            Assert.AreEqual(0, oldItem.PropertyChangedSubscriberCount);
            Assert.AreEqual(1, newItem.PropertyChangedSubscriberCount);
            await Drain();
            Assert.AreEqual(0, GridValidation.GetErrorCount(dataGrid));

            newItem.SetErrors(nameof(ErrorRow.Code), "异步到达错误");
            await Drain();
            Assert.AreEqual("异步到达错误", GridValidation.GetErrors(dataGrid).FirstOrDefault()?.Message);

            GridValidation.SetIsEnabled(dataGrid, false);
            Assert.AreEqual(0, newItem.PropertyChangedSubscriberCount);
            await Drain();
            Assert.AreEqual(0, GridValidation.GetErrorCount(dataGrid));
        });
    }

    /// <summary>旧式模型接口保持行错误及模板字段映射，刷新只改变提示呈现。</summary>
    [TestMethod]
    public void DataErrorInfo_ShouldUseExplicitFieldPathForTemplateColumn()
    {
        StaTest.RunAsync(async () =>
        {
            var item = new LegacyErrorRow { Error = "行错误", FieldError = "模板字段错误" };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { item } };
            var templateColumn = new DataGridTemplateColumn();

            GridValidation.SetFieldPath(templateColumn, nameof(LegacyErrorRow.Value));
            dataGrid.Columns.Add(templateColumn);
            Enable(dataGrid);

            await Drain();
            var errors = GridValidation.GetErrors(dataGrid);
            Assert.HasCount(2, errors);
            Assert.IsTrue(errors.Any(error => error.Column is null && error.Message == "行错误"));
            Assert.IsTrue(errors.Any(error => ReferenceEquals(error.Column, templateColumn) && error.Message == "模板字段错误"));
        });
    }

    [TestMethod]
    public void CellStateConverter_ShouldExposeSoftErrorWithoutCreatingWpfValidationError()
    {
        StaTest.Run(() =>
        {
            var nameColumn = new DataGridTextColumn();
            var codeColumn = new DataGridTextColumn();
        var item = new object();
        var errors = new[]
        {
            new GridValidationError(item, nameColumn, "名称不能为空"),
            new GridValidationError(item, nameColumn, "名称格式错误"),
            new GridValidationError(item, codeColumn, "代码错误")
        };
        var converter = new GridCellValidationStateConverter();

        var state = (GridCellValidationState)converter.Convert(
            [item, nameColumn, errors],
            typeof(GridCellValidationState),
            parameter: null!,
            CultureInfo.CurrentCulture);

        Assert.IsTrue(state.HasError);
        StringAssert.Contains(state.Message, "名称不能为空");
        StringAssert.Contains(state.Message, "名称格式错误");

        var rowState = (GridCellValidationState)converter.Convert(
            [item, errors],
            typeof(GridCellValidationState),
            parameter: null!,
            CultureInfo.CurrentCulture);
        Assert.IsTrue(rowState.HasError);
        StringAssert.Contains(rowState.Message, "代码错误");
        });
    }

    /// <summary>同一批字段和派生属性通知只读取一次最新错误，属性通知和解绑保持有效。</summary>
    [TestMethod]
    public void PropertyBurst_ShouldCoalesceLatestErrors() => StaTest.RunAsync(async () =>
    {
        var row = new ErrorRow("A", "1");
        var grid = CreateGrid(row);
        await Drain();
        Assert.AreEqual(1, row.PropertyChangedSubscriberCount);
        var reads = row.ErrorReadCount;
        row.SetErrors(nameof(ErrorRow.Name), "旧消息");
        row.SetErrors(nameof(ErrorRow.Name), "最新消息");
        row.SetErrors(nameof(ErrorRow.Code), "代码错误");
        Assert.AreEqual(reads, row.ErrorReadCount);
        await Drain();
        Assert.AreEqual(reads + 3, row.ErrorReadCount, "两个字段及整行各读取一次，不能按通知次数扫描。");
        CollectionAssert.AreEquivalent(new[] { "最新消息", "代码错误" }, GridValidation.GetErrors(grid).Select(e => e.Message).ToArray());
        row.SetErrors(nameof(ErrorRow.Name));
        row.SetErrors(nameof(ErrorRow.Code));
        await Drain();
        Assert.AreEqual(0, GridValidation.GetErrorCount(grid));
        GridValidation.SetIsEnabled(grid, false);
        Assert.AreEqual(0, row.PropertyChangedSubscriberCount);
    });

    /// <summary>取消尚未发布的刷新，换源和重载后不会重新发布旧行错误或重复订阅。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PendingRefresh_ShouldBeCancelledOnUnloadOrDisable(bool disable) => StaTest.RunAsync(async () =>
    {
        var row = new ErrorRow("A", "1");
        var grid = CreateGrid(row);
        row.SetErrors(nameof(ErrorRow.Name), "卸载前错误");
        if (disable) GridValidation.SetIsEnabled(grid, false);
        else grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        await Drain();
        Assert.AreEqual(0, row.ErrorReadCount);
        Assert.AreEqual(0, row.PropertyChangedSubscriberCount);
        Assert.AreEqual(0, GridValidation.GetErrorCount(grid));
        var replacement = new ErrorRow("B", "2");
        grid.ItemsSource = new[] { replacement };
        replacement.SetErrors(nameof(ErrorRow.Code), "新行错误");
        if (disable) GridValidation.SetIsEnabled(grid, true);
        grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        await Drain();
        Assert.AreEqual(1, replacement.PropertyChangedSubscriberCount);
        Assert.AreSame(replacement, GridValidation.GetErrors(grid).Single().Item);
        GridValidation.SetIsEnabled(grid, false);
    });

    /// <summary>后台错误通知由宿主 Dispatcher 发布，替换数据源后旧行通知不再触发扫描。</summary>
    [TestMethod]
    public void BackgroundErrors_ShouldPublishOnDispatcherAndReleaseReplacedRows() => StaTest.RunAsync(async () =>
    {
        var row = new ErrorRow("A", "1");
        var grid = CreateGrid(row);
        await Drain();
        await Task.Run(() => row.SetErrors(nameof(ErrorRow.Name), "后台错误"));
        await Drain();
        Assert.AreEqual("后台错误", GridValidation.GetErrors(grid).Single().Message);
        grid.ItemsSource = Array.Empty<ErrorRow>();
        await Drain();
        var reads = row.ErrorReadCount;
        await Task.Run(() => row.SetErrors(nameof(ErrorRow.Name), "已移除行错误"));
        await Drain();
        Assert.AreEqual(reads, row.ErrorReadCount);
        Assert.AreEqual(0, GridValidation.GetErrorCount(grid));
        GridValidation.SetIsEnabled(grid, false);
    });

    private static Task Drain() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task;

    private static DataGrid CreateGrid(params ErrorRow[] rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Single
        };
        AddColumn(dataGrid, nameof(ErrorRow.Name));
        AddColumn(dataGrid, nameof(ErrorRow.Code));
        Enable(dataGrid);
        return dataGrid;
    }

    private static void AddColumn(DataGrid dataGrid, string path)
    {
        var column = new DataGridTextColumn { Binding = new Binding(path) };

        dataGrid.Columns.Add(column);
    }

    private static void Enable(DataGrid dataGrid)
    {
        GridValidation.SetIsEnabled(dataGrid, true);

        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    }

    /// <summary>模拟当前五处软提示使用的 IDataErrorInfo 和派生属性通知。</summary>
    private sealed class ErrorRow(string name, string code) : IDataErrorInfo, INotifyPropertyChanged
    {
        private readonly Dictionary<string, string> _errors = [];
        private PropertyChangedEventHandler? _propertyChanged;
        public string Name { get; } = name;
        public string Code { get; } = code;
        public int PropertyChangedSubscriberCount { get; private set; }
        public int ErrorReadCount { get; private set; }
        /// <summary>记录行错误读取，字段消息不重复作为行错误。</summary>
        public string Error { get { ErrorReadCount++; return string.Empty; } }
        /// <summary>记录字段读取次数并返回最新软错误。</summary>
        public string this[string columnName]
        {
            get { ErrorReadCount++; return _errors.GetValueOrDefault(columnName, string.Empty); }
        }
        /// <summary>记录实际属性订阅数量，验证换源及卸载释放。</summary>
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _propertyChanged += value; PropertyChangedSubscriberCount++; }
            remove { _propertyChanged -= value; PropertyChangedSubscriberCount--; }
        }

        /// <summary>发送字段和派生属性通知，模拟真实行模型一次编辑产生的连续事件。</summary>
        public void SetErrors(string propertyName, string error = "")
        {
            if (string.IsNullOrEmpty(error)) _errors.Remove(propertyName);
            else _errors[propertyName] = error;
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            _propertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsDirty"));
        }
    }

    private sealed class LegacyErrorRow : IDataErrorInfo
    {
        public string? Value { get; set; }

        public string Error { get; set; } = string.Empty;

        public string FieldError { get; set; } = string.Empty;

        public string this[string columnName] => columnName == nameof(Value) ? FieldError : string.Empty;
    }
}
