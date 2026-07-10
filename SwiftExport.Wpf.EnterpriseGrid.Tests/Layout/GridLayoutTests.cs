using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Layout;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Layout;

[TestClass]
public sealed class GridLayoutTests
{
    [TestMethod]
    public void CaptureAndApply_ShouldRestoreKnownLayoutIgnoreUnknownAndKeepNewColumnDefaults()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<LayoutRow>
            {
                new("B", 2m, "X"),
                new("A", 1m, "Y")
            };
            var dataGrid = CreateGrid(rows);
            var name = dataGrid.Columns[0];
            var price = dataGrid.Columns[1];
            var category = dataGrid.Columns[2];
            name.Width = new DataGridLength(120, DataGridLengthUnitType.Pixel);
            price.Width = new DataGridLength(2, DataGridLengthUnitType.Star);
            category.Width = DataGridLength.Auto;
            category.Visibility = Visibility.Collapsed;
            dataGrid.FrozenColumnCount = 1;
            price.SortDirection = ListSortDirection.Descending;
            CollectionViewSource.GetDefaultView(rows).SortDescriptions.Add(
                new SortDescription(nameof(LayoutRow.Price), ListSortDirection.Descending));

            var captured = GridLayoutManager.Capture(dataGrid);
            var stateWithUnknown = new GridLayoutState(
                captured.Version,
                captured.Columns.Append(new GridColumnLayout(
                    "removed-column",
                    false,
                    9,
                    new GridColumnWidth(50, GridColumnWidthUnit.Pixel))),
                captured.FrozenColumnCount);

            name.DisplayIndex = 2;
            name.Width = new DataGridLength(40);
            price.SortDirection = null;
            category.Visibility = Visibility.Visible;
            dataGrid.FrozenColumnCount = 0;
            CollectionViewSource.GetDefaultView(rows).SortDescriptions.Clear();
            var newColumn = AddColumn(dataGrid, "new-column", nameof(LayoutRow.Name));
            newColumn.Width = new DataGridLength(77);

            var result = GridLayoutManager.Apply(dataGrid, stateWithUnknown);

            Assert.IsTrue(result.Succeeded, result.Error);
            CollectionAssert.Contains(result.IgnoredColumnKeys.ToArray(), "removed-column");
            Assert.AreEqual(0, name.DisplayIndex);
            Assert.AreEqual(120d, name.Width.Value);
            Assert.AreEqual(DataGridLengthUnitType.Star, price.Width.UnitType);
            Assert.AreEqual(2d, price.Width.Value);
            Assert.AreEqual(Visibility.Collapsed, category.Visibility);
            Assert.AreEqual(1, dataGrid.FrozenColumnCount);
            Assert.AreEqual(ListSortDirection.Descending, price.SortDirection);
            Assert.AreEqual(nameof(LayoutRow.Price), CollectionViewSource.GetDefaultView(rows).SortDescriptions[0].PropertyName);
            Assert.AreEqual(Visibility.Visible, newColumn.Visibility);
            Assert.AreEqual(77d, newColumn.Width.Value);
        });
    }

    [TestMethod]
    public void AttachedState_ShouldCaptureColumnChangesAndApplyExternalState()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<LayoutRow> { new("A", 1m, "X") };
            var dataGrid = CreateGrid(rows);
            GridLayout.SetIsEnabled(dataGrid, true);
            EnterpriseGridCore.SetIsEnabled(dataGrid, true);
            dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.IsNotNull(GridLayout.GetState(dataGrid));
            dataGrid.Columns[1].Visibility = Visibility.Collapsed;
            FlushDispatcher();
            Assert.IsFalse(GridLayout.GetState(dataGrid)!.Columns.Single(layout => layout.ColumnKey == "price").IsVisible);

            var external = new GridLayoutState(
                GridLayoutManager.CurrentVersion,
                GridLayout.GetState(dataGrid)!.Columns.Select(layout =>
                    layout.ColumnKey switch
                    {
                        "price" => layout with { IsVisible = true, DisplayIndex = 0 },
                        "name" => layout with { DisplayIndex = 1 },
                        _ => layout
                    }));
            GridLayout.SetState(dataGrid, external);

            var applyResult = GridLayout.GetLastApplyResult(dataGrid);
            Assert.IsNotNull(applyResult);
            Assert.IsTrue(applyResult.Succeeded, applyResult.Error);
            Assert.AreEqual(Visibility.Visible, dataGrid.Columns[1].Visibility);
            Assert.AreEqual(0, dataGrid.Columns[1].DisplayIndex);
            Assert.IsTrue(GridLayout.GetLastApplyResult(dataGrid)?.Succeeded);
        });
    }

    [TestMethod]
    public void ColumnChooser_ShouldToggleAndTrackNativeColumnVisibilityAndOrder()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<LayoutRow> { new("A", 1m, "X") };
            var dataGrid = CreateGrid(rows);
            var chooser = new GridColumnChooser { TargetGrid = dataGrid };
            var panel = new StackPanel();
            panel.Children.Add(chooser);
            panel.Children.Add(dataGrid);
            using var host = Show(panel);

            Assert.AreEqual(3, chooser.Children.Count);
            var priceCheckBox = chooser.Children.OfType<CheckBox>()
                .Single(checkBox => ReferenceEquals(checkBox.Tag, dataGrid.Columns[1]));
            priceCheckBox.IsChecked = false;
            Assert.AreEqual(Visibility.Collapsed, dataGrid.Columns[1].Visibility);

            dataGrid.Columns[1].Visibility = Visibility.Visible;
            dataGrid.UpdateLayout();
            Assert.IsTrue(priceCheckBox.IsChecked);

            dataGrid.Columns[2].DisplayIndex = 0;
            Assert.AreSame(dataGrid.Columns[2], ((CheckBox)chooser.Children[0]).Tag);
        });
    }

    [TestMethod]
    public void Apply_WhenCurrentEditIsInvalid_ShouldLeaveLayoutUntouched()
    {
        StaTest.Run(() =>
        {
            var row = new EditableRow { Name = "A" };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { row } };
            var column = new DataGridTextColumn
            {
                Binding = new Binding(nameof(EditableRow.Name))
                {
                    NotifyOnValidationError = true,
                    ValidationRules = { new RequiredRule() }
                }
            };
            GridColumn.SetKey(column, "name");
            dataGrid.Columns.Add(column);
            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, row, column);
            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = column.GetCellContent(row) as TextBox;
            Assert.IsNotNull(editor);
            editor.Text = string.Empty;
            var state = new GridLayoutState(
                GridLayoutManager.CurrentVersion,
                new[]
                {
                    new GridColumnLayout("name", false, 0, new GridColumnWidth(50, GridColumnWidthUnit.Pixel))
                });

            var result = GridLayoutManager.Apply(dataGrid, state);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(Visibility.Visible, column.Visibility);
            Assert.IsTrue(System.Windows.Controls.Validation.GetHasError(editor));
        });
    }

    [TestMethod]
    public void Apply_ShouldRejectFutureVersionAndDuplicateStateKeys()
    {
        StaTest.Run(() =>
        {
            var dataGrid = CreateGrid(new ObservableCollection<LayoutRow>());
            var future = GridLayoutManager.Apply(
                dataGrid,
                new GridLayoutState(GridLayoutManager.CurrentVersion + 1, Array.Empty<GridColumnLayout>()));
            Assert.IsFalse(future.Succeeded);

            var duplicate = new GridColumnLayout(
                "name",
                true,
                0,
                new GridColumnWidth(100, GridColumnWidthUnit.Pixel));
            var duplicateResult = GridLayoutManager.Apply(
                dataGrid,
                new GridLayoutState(GridLayoutManager.CurrentVersion, new[] { duplicate, duplicate }));
            Assert.IsFalse(duplicateResult.Succeeded);
            StringAssert.Contains(duplicateResult.Error, "重复");
        });
    }

    private static DataGrid CreateGrid(ObservableCollection<LayoutRow> rows)
    {
        var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
        AddColumn(dataGrid, "name", nameof(LayoutRow.Name));
        AddColumn(dataGrid, "price", nameof(LayoutRow.Price));
        AddColumn(dataGrid, "category", nameof(LayoutRow.Category));
        return dataGrid;
    }

    private static DataGridTextColumn AddColumn(DataGrid dataGrid, string key, string path)
    {
        var column = new DataGridTextColumn { Header = key, Binding = new Binding(path) };
        GridColumn.SetKey(column, key);
        dataGrid.Columns.Add(column);
        return column;
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

    private static void FlushDispatcher()
    {
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed record LayoutRow(string Name, decimal Price, string Category);

    private sealed class EditableRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class RequiredRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
            string.IsNullOrWhiteSpace(value?.ToString())
                ? new ValidationResult(false, "必填")
                : ValidationResult.ValidResult;
    }
}
