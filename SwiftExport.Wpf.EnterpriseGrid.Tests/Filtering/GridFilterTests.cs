using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Filtering;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Filtering;

[TestClass]
public sealed class GridFilterTests
{
    [TestMethod]
    public void LocalMode_ShouldComposeExistingFilterSupportTypedAndOrAndRestoreView()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<FilterRow>
            {
                new("Alpha", 20m, true),
                new("alphabet", 5m, true),
                new("Beta", 30m, false)
            };
            var view = CollectionViewSource.GetDefaultView(rows);
            Predicate<object> existingFilter = item => ((FilterRow)item).IsActive;
            view.Filter = existingFilter;
            var dataGrid = CreateGrid(rows);

            GridFilter.SetMode(dataGrid, GridFilterMode.Local);
            GridFilter.SetFilterSet(dataGrid, new GridFilterSet(
                new[]
                {
                    new GridFilterDescriptor("name", nameof(FilterRow.Name), GridFilterOperator.Contains, "ALPHA"),
                    new GridFilterDescriptor("price", nameof(FilterRow.Price), GridFilterOperator.GreaterThan, "10")
                }));

            Assert.HasCount(1, dataGrid.Items.Cast<object>());
            Assert.AreEqual("Alpha", ((FilterRow)dataGrid.Items[0]).Name);

            GridFilter.SetFilterSet(dataGrid, new GridFilterSet(
                new[]
                {
                    new GridFilterDescriptor("name", nameof(FilterRow.Name), GridFilterOperator.Equals, "missing"),
                    new GridFilterDescriptor("price", nameof(FilterRow.Price), GridFilterOperator.LessThan, 10m)
                },
                GridFilterLogicalOperator.Or));
            Assert.HasCount(1, dataGrid.Items.Cast<object>());
            Assert.AreEqual("alphabet", ((FilterRow)dataGrid.Items[0]).Name);

            GridFilter.SetMode(dataGrid, GridFilterMode.Disabled);
            Assert.AreSame(existingFilter, view.Filter);
            Assert.HasCount(2, dataGrid.Items.Cast<object>());
        });
    }

    [TestMethod]
    public void LocalMode_ShouldApplyGroupingAndNewItemsSourceWithoutCopyingCollections()
    {
        StaTest.Run(() =>
        {
            var firstSource = new ObservableCollection<FilterRow>
            {
                new("A", 1m, true),
                new("B", 2m, false)
            };
            var dataGrid = CreateGrid(firstSource);
            GridFilter.SetMode(dataGrid, GridFilterMode.Local);
            GridFilter.SetFilterSet(dataGrid, new GridFilterSet(
                new[] { new GridFilterDescriptor("active", nameof(FilterRow.IsActive), GridFilterOperator.Equals, true) }));
            GridFilter.SetGroups(dataGrid, new[] { new GridGroupDescriptor("active", nameof(FilterRow.IsActive)) });

            var firstView = CollectionViewSource.GetDefaultView(firstSource);
            Assert.HasCount(1, firstView.GroupDescriptions!);
            Assert.HasCount(1, dataGrid.Items.Cast<object>());

            var secondSource = new ObservableCollection<FilterRow>
            {
                new("C", 3m, true),
                new("D", 4m, true),
                new("E", 5m, false)
            };
            dataGrid.ItemsSource = secondSource;
            var secondView = CollectionViewSource.GetDefaultView(secondSource);
            Assert.HasCount(0, firstView.GroupDescriptions!);
            Assert.HasCount(1, secondView.GroupDescriptions!);
            Assert.HasCount(2, dataGrid.Items.Cast<object>());
        });
    }

    [TestMethod]
    public void RemoteMode_ShouldEmitRequestWithoutFilteringLocalView()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<FilterRow>
            {
                new("A", 1m, true),
                new("B", 2m, false)
            };
            var dataGrid = CreateGrid(rows);
            var command = new CaptureCommand();
            GridFilter.SetRemoteFilterCommand(dataGrid, command);
            GridFilter.SetMode(dataGrid, GridFilterMode.Remote);
            var filter = new GridFilterSet(
                new[] { new GridFilterDescriptor("name", nameof(FilterRow.Name), GridFilterOperator.Contains, "A") });
            GridFilter.SetFilterSet(dataGrid, filter);

            Assert.HasCount(2, dataGrid.Items.Cast<object>());
            Assert.IsNotNull(command.LastRequest);
            Assert.AreSame(filter, command.LastRequest.Filter);
        });
    }

    [TestMethod]
    public void InvalidEdit_ShouldBlockFilterAndKeepCurrentView()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<FilterRow>
            {
                new("A", 1m, true),
                new("B", 2m, true)
            };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
            var column = new DataGridTextColumn
            {
                Binding = new Binding(nameof(FilterRow.Name))
                {
                    NotifyOnValidationError = true,
                    ValidationRules = { new RequiredRule() }
                }
            };
            GridColumn.SetKey(column, "name");
            dataGrid.Columns.Add(column);
            Enable(dataGrid);
            GridFilter.SetMode(dataGrid, GridFilterMode.Local);
            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, rows[0], column);
            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = column.GetCellContent(rows[0]) as TextBox;
            Assert.IsNotNull(editor);
            editor.Text = string.Empty;

            GridFilter.SetFilterSet(dataGrid, new GridFilterSet(
                new[] { new GridFilterDescriptor("name", nameof(FilterRow.Name), GridFilterOperator.Equals, "B") }));

            Assert.IsFalse(GridFilter.GetLastApplySucceeded(dataGrid));
            StringAssert.Contains(GridFilter.GetLastApplyError(dataGrid), "验证");
            Assert.HasCount(2, dataGrid.Items.Cast<object>());
            Assert.AreSame(rows[0], dataGrid.CurrentCell.Item);
        });
    }

    [TestMethod]
    public void FilterRow_ShouldTrackVisibleFilterableColumnsAndUpdateDescriptor()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<FilterRow> { new("Alpha", 1m, true), new("Beta", 2m, true) };
            var dataGrid = CreateGrid(rows);
            GridFilter.SetMode(dataGrid, GridFilterMode.Local);
            GridColumn.SetCanFilter(dataGrid.Columns[1], false);
            var filterRow = new GridFilterRow { TargetGrid = dataGrid, FilterDelay = TimeSpan.Zero };
            var panel = new StackPanel();
            panel.Children.Add(filterRow);
            panel.Children.Add(dataGrid);
            using var host = Show(panel);

            Assert.AreEqual(3, filterRow.Children.Count);
            filterRow.ApplyFilterText(dataGrid.Columns[0], "alp");
            Assert.HasCount(1, dataGrid.Items.Cast<object>());

            var extra = new DataGridTextColumn { Header = "Extra", Binding = new Binding(nameof(FilterRow.Name)) };
            GridColumn.SetKey(extra, "extra");
            dataGrid.Columns.Add(extra);
            Assert.AreEqual(4, filterRow.Children.Count);
        });
    }

    private static DataGrid CreateGrid(ObservableCollection<FilterRow> rows)
    {
        var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
        AddColumn(dataGrid, "name", nameof(FilterRow.Name));
        AddColumn(dataGrid, "price", nameof(FilterRow.Price));
        AddColumn(dataGrid, "active", nameof(FilterRow.IsActive));
        Enable(dataGrid);
        return dataGrid;
    }

    private static void AddColumn(DataGrid dataGrid, string key, string path)
    {
        var column = new DataGridTextColumn { Header = key, Binding = new Binding(path) };
        GridColumn.SetKey(column, key);
        dataGrid.Columns.Add(column);
    }

    private static void Enable(DataGrid dataGrid)
    {
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    }

    private static IDisposable Show(FrameworkElement content)
    {
        var window = new Window
        {
            Width = 640,
            Height = 320,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = content
        };
        window.Show();
        content.UpdateLayout();
        return new WindowScope(window);
    }

    private static void SetCurrentCell(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.SelectedItem = item;
        dataGrid.CurrentCell = new DataGridCellInfo(item, column);
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed record FilterRow(string Name, decimal Price, bool IsActive);

    private sealed class CaptureCommand : ICommand
    {
        public GridFilterRequest? LastRequest { get; private set; }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => LastRequest = (GridFilterRequest?)parameter;
    }

    private sealed class RequiredRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
            string.IsNullOrWhiteSpace(value?.ToString())
                ? new ValidationResult(false, "必填")
                : ValidationResult.ValidResult;
    }
}
