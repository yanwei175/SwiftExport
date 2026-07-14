using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Exporting;

[TestClass]
public sealed class GridExportTests
{
    [TestMethod]
    public void CreateRequest_ShouldUseViewOrderColumnMetadataLookupTemplateAndExplicitAccessor()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<ExportRow>
            {
                new("Beta", 20m, ExportStatus.Disabled, 2, "供应商二", "B"),
                new("Alpha", 10m, ExportStatus.Active, 1, "供应商一", null),
                new("Hidden", 30m, ExportStatus.Active, 1, "供应商一", "H")
            };
            var suppliers = new[] { new LookupItem(1, "供应商一"), new LookupItem(2, "供应商二") };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
            var price = AddTextColumn(dataGrid, "price", nameof(ExportRow.Price), "价格");
            price.Binding.StringFormat = "N2";
            var name = AddTextColumn(dataGrid, "name", nameof(ExportRow.Name), "名称");
            name.Visibility = Visibility.Collapsed;
            var lookup = new LookupDataGridColumn
            {
                Header = "供应商",
                ItemsSource = suppliers,
                SelectedValuePath = nameof(LookupItem.Id),
                DisplayMemberPath = nameof(LookupItem.Name),
                SelectedValueBinding = new Binding(nameof(ExportRow.SupplierId)),
                DisplayBinding = new Binding(nameof(ExportRow.SupplierName))
            };
            GridColumn.SetKey(lookup, "supplier");
            dataGrid.Columns.Add(lookup);
            var statusColumn = new DataGridTemplateColumn { Header = "状态" };
            GridColumn.SetKey(statusColumn, "status");
            GridColumn.SetFieldPath(statusColumn, nameof(ExportRow.Status));
            dataGrid.Columns.Add(statusColumn);
            var unbound = new DataGridTemplateColumn { Header = "不可导出模板" };
            GridColumn.SetKey(unbound, "unbound");
            dataGrid.Columns.Add(unbound);
            var accessorColumn = new DataGridTemplateColumn { Header = "备注" };
            GridColumn.SetKey(accessorColumn, "note");
            GridColumn.SetValueAccessor(accessorColumn, new NoteAccessor());
            dataGrid.Columns.Add(accessorColumn);

            price.DisplayIndex = 0;
            lookup.DisplayIndex = 1;
            statusColumn.DisplayIndex = 2;
            accessorColumn.DisplayIndex = 3;
            name.DisplayIndex = 4;
            unbound.DisplayIndex = 5;
            var view = CollectionViewSource.GetDefaultView(rows);
            view.Filter = item => ((ExportRow)item).Name != "Hidden";
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(
                nameof(ExportRow.Name),
                System.ComponentModel.ListSortDirection.Ascending));

            var request = GridExport.CreateRequest(dataGrid, new GridExportOptions
            {
                Culture = CultureInfo.GetCultureInfo("en-US")
            });

            CollectionAssert.AreEqual(
                new[] { "price", "supplier", "status", "note" },
                request.Columns.Select(column => column.ColumnKey).ToArray());
            Assert.HasCount(2, request.Rows);
            Assert.AreEqual("10.00", request.Rows[0].Cells[0].DisplayText);
            Assert.AreEqual(1, request.Rows[0].Cells[1].RawValue);
            Assert.AreEqual("供应商一", request.Rows[0].Cells[1].DisplayText);
            Assert.AreEqual(nameof(ExportStatus.Active), request.Rows[0].Cells[2].DisplayText);
            Assert.IsNull(request.Rows[0].Cells[3].RawValue);
            Assert.AreEqual(string.Empty, request.Rows[0].Cells[3].DisplayText);
        });
    }

    [TestMethod]
    public void RowScopesAndUnboundPolicy_ShouldBeExplicit()
    {
        StaTest.Run(() =>
        {
            var first = new ExportRow("A", 1m, ExportStatus.Active, 1, "S", "N");
            var second = new ExportRow("B", 2m, ExportStatus.Disabled, 2, "T", "M");
            var rows = new ObservableCollection<ExportRow> { first, second };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = rows,
                SelectionMode = DataGridSelectionMode.Extended
            };
            AddTextColumn(dataGrid, "name", nameof(ExportRow.Name), "名称");
            dataGrid.SelectedItems.Add(second);

            var selected = GridExport.CreateRequest(dataGrid, new GridExportOptions
            {
                RowScope = GridExportRowScope.SelectedItems
            });
            Assert.HasCount(1, selected.Rows);
            Assert.AreEqual("B", selected.Rows[0].Cells[0].DisplayText);

            CollectionViewSource.GetDefaultView(rows).Filter = item => ReferenceEquals(item, first);
            var all = GridExport.CreateRequest(dataGrid, new GridExportOptions
            {
                RowScope = GridExportRowScope.AllItems
            });
            Assert.HasCount(2, all.Rows);

            var unbound = new DataGridTemplateColumn { Header = "模板" };
            dataGrid.Columns.Add(unbound);
            Assert.Throws<InvalidOperationException>(() => GridExport.CreateRequest(dataGrid, new GridExportOptions
            {
                UnboundColumnBehavior = GridUnboundColumnBehavior.Throw
            }));
        });
    }

    [TestMethod]
    public void CurrentView_ShouldFlattenGroupedRowsInGroupOrderAndHonorSnapshotCancellation()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<ExportRow>
            {
                new("A", 1m, ExportStatus.Active, 1, "S", null),
                new("B", 2m, ExportStatus.Disabled, 2, "T", null)
            };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
            AddTextColumn(dataGrid, "name", nameof(ExportRow.Name), "名称");
            CollectionViewSource.GetDefaultView(rows).GroupDescriptions!.Add(
                new PropertyGroupDescription(nameof(ExportRow.Status)));

            var request = GridExport.CreateRequest(dataGrid);
            Assert.HasCount(2, request.Rows);
            CollectionAssert.AreEqual(
                new[] { "A", "B" },
                request.Rows.Select(row => row.Cells[0].DisplayText).ToArray());

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                GridExport.CreateRequest(dataGrid, cancellationToken: cancellation.Token));
        });
    }

    private static DataGridTextColumn AddTextColumn(
        DataGrid dataGrid,
        string key,
        string path,
        string header)
    {
        var column = new DataGridTextColumn { Header = header, Binding = new Binding(path) };
        GridColumn.SetKey(column, key);
        dataGrid.Columns.Add(column);
        return column;
    }

    private enum ExportStatus
    {
        Active,

        Disabled
    }

    private sealed record LookupItem(int Id, string Name);

    private sealed record ExportRow(
        string Name,
        decimal Price,
        ExportStatus Status,
        int SupplierId,
        string SupplierName,
        string? Note);

    private sealed class NoteAccessor : IGridValueAccessor
    {
        public object? GetValue(object item, GridExportValueContext context) => ((ExportRow)item).Note;
    }
}
