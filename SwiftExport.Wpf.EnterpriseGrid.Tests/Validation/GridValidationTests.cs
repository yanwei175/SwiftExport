using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using SwiftExport.Wpf.EnterpriseGrid.Validation;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Validation;

[TestClass]
public sealed class GridValidationTests
{
    [TestMethod]
    public void ErrorsProperty_ShouldInheritToGridContainersWithoutAncestorBinding()
    {
        var metadata = (FrameworkPropertyMetadata)GridValidation.ErrorsProperty.GetMetadata(typeof(DependencyObject));

        Assert.IsTrue(metadata.Inherits);
    }

    [TestMethod]
    public void NotifyDataErrors_ShouldAggregateInViewAndDisplayOrderAndFocusFirstError()
    {
        StaTest.Run(() =>
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

            var errors = GridValidation.GetErrors(dataGrid);
            Assert.HasCount(3, errors);
            Assert.AreEqual(new GridValidationError(first, "code", "代码错误"), errors[0]);
            Assert.AreEqual(new GridValidationError(first, "name", "名称错误"), errors[1]);
            Assert.AreEqual(new GridValidationError(second, "code", "第二行错误"), errors[2]);
            Assert.AreEqual(3, GridValidation.GetErrorCount(dataGrid));
            Assert.IsTrue(GridValidation.GetHasErrors(dataGrid));

            Assert.IsTrue(GridValidation.FocusFirstError(dataGrid));
            Assert.AreSame(first, dataGrid.SelectedItem);
            Assert.AreSame(codeColumn, dataGrid.CurrentCell.Column);
        });
    }

    [TestMethod]
    public void ErrorsChangedAndItemsReplacement_ShouldRefreshAndUnsubscribeOldItems()
    {
        StaTest.Run(() =>
        {
            var oldItem = new ErrorRow("A", "One");
            var newItem = new ErrorRow("B", "Two");
            var dataGrid = CreateGrid(oldItem);
            Assert.AreEqual(1, oldItem.ErrorsChangedSubscriberCount);

            oldItem.SetErrors(nameof(ErrorRow.Name), "旧错误");
            Assert.AreEqual(1, GridValidation.GetErrorCount(dataGrid));

            dataGrid.ItemsSource = new[] { newItem };
            Assert.AreEqual(0, oldItem.ErrorsChangedSubscriberCount);
            Assert.AreEqual(1, newItem.ErrorsChangedSubscriberCount);
            Assert.AreEqual(0, GridValidation.GetErrorCount(dataGrid));

            newItem.SetErrors(nameof(ErrorRow.Code), "异步到达错误");
            Assert.AreEqual("异步到达错误", GridValidation.GetFirstError(dataGrid)?.Message);

            GridValidation.SetIsEnabled(dataGrid, false);
            Assert.AreEqual(0, newItem.ErrorsChangedSubscriberCount);
            Assert.AreEqual(0, GridValidation.GetErrorCount(dataGrid));
        });
    }

    [TestMethod]
    public void DataErrorInfo_ShouldUseExplicitFieldPathForTemplateColumn()
    {
        StaTest.Run(() =>
        {
            var item = new LegacyErrorRow { Error = "行错误", FieldError = "模板字段错误" };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { item } };
            var templateColumn = new DataGridTemplateColumn();
            GridColumn.SetKey(templateColumn, "template");
            GridColumn.SetFieldPath(templateColumn, nameof(LegacyErrorRow.Value));
            dataGrid.Columns.Add(templateColumn);
            Enable(dataGrid);

            var errors = GridValidation.GetErrors(dataGrid);
            Assert.HasCount(2, errors);
            Assert.IsTrue(errors.Any(error => error.ColumnKey is null && error.Message == "行错误"));
            Assert.IsTrue(errors.Any(error => error.ColumnKey == "template" && error.Message == "模板字段错误"));
        });
    }

    [TestMethod]
    public void CellStateConverter_ShouldExposeSoftErrorWithoutCreatingWpfValidationError()
    {
        var item = new object();
        var errors = new[]
        {
            new GridValidationError(item, "name", "名称不能为空"),
            new GridValidationError(item, "name", "名称格式错误"),
            new GridValidationError(item, "code", "代码错误")
        };
        var converter = new GridCellValidationStateConverter();

        var state = (GridCellValidationState)converter.Convert(
            [item, "name", errors],
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
    }

    private static DataGrid CreateGrid(params ErrorRow[] rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            SelectionMode = DataGridSelectionMode.Single
        };
        AddColumn(dataGrid, "name", nameof(ErrorRow.Name));
        AddColumn(dataGrid, "code", nameof(ErrorRow.Code));
        Enable(dataGrid);
        return dataGrid;
    }

    private static void AddColumn(DataGrid dataGrid, string key, string path)
    {
        var column = new DataGridTextColumn { Binding = new Binding(path) };
        GridColumn.SetKey(column, key);
        dataGrid.Columns.Add(column);
    }

    private static void Enable(DataGrid dataGrid)
    {
        GridValidation.SetIsEnabled(dataGrid, true);
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    }

    private sealed class ErrorRow : INotifyDataErrorInfo, INotifyPropertyChanged
    {
        private readonly Dictionary<string, string[]> _errors = [];
        private EventHandler<DataErrorsChangedEventArgs>? _errorsChanged;

        public ErrorRow(string name, string code)
        {
            Name = name;
            Code = code;
        }

        public string Name { get; }

        public string Code { get; }

        public bool HasErrors => _errors.Count > 0;

        public int ErrorsChangedSubscriberCount { get; private set; }

        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged
        {
            add
            {
                _errorsChanged += value;
                ErrorsChangedSubscriberCount++;
            }
            remove
            {
                _errorsChanged -= value;
                ErrorsChangedSubscriberCount--;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return _errors.Values.SelectMany(errors => errors).ToArray();
            }

            return _errors.TryGetValue(propertyName, out var errors) ? errors : Array.Empty<string>();
        }

        public void SetErrors(string propertyName, params string[] errors)
        {
            if (errors.Length == 0)
            {
                _errors.Remove(propertyName);
            }
            else
            {
                _errors[propertyName] = errors;
            }

            _errorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
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
