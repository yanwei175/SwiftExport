using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Core;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Core;

[TestClass]
public sealed class GridRowHeaderTests
{
    [TestMethod]
    public void ShowRowNumber_ShouldNumberCurrentViewWithoutChangingHeaderWidth()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>
            {
                new("C"),
                new("A"),
                new("B")
            };
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid);

            Assert.AreEqual(32d, dataGrid.RowHeaderWidth);
            AssertRowNumbers(dataGrid);

            dataGrid.Items.SortDescriptions.Add(new SortDescription(nameof(TestRow.Name), ListSortDirection.Ascending));
            DrainDispatcher();
            Assert.AreEqual("A", ((TestRow)dataGrid.Items[0]).Name);
            AssertRowNumbers(dataGrid);

            dataGrid.Items.Filter = item => ((TestRow)item).Name != "B";
            DrainDispatcher();
            Assert.AreEqual(2, dataGrid.Items.Count);
            AssertRowNumbers(dataGrid);
            dataGrid.Items.Filter = null;
            DrainDispatcher();

            rows.RemoveAt(0);
            DrainDispatcher();
            AssertRowNumbers(dataGrid);

            dataGrid.ItemsSource = new ObservableCollection<TestRow> { new("X"), new("Y") };
            DrainDispatcher();
            AssertRowNumbers(dataGrid);

            GridRowHeader.SetShowRowNumber(dataGrid, false);
            Assert.AreEqual(32d, dataGrid.RowHeaderWidth);
            foreach (var item in dataGrid.Items.Cast<object>())
            {
                if (dataGrid.ItemContainerGenerator.ContainerFromItem(item) is DataGridRow row)
                {
                    Assert.AreEqual(0, GridRowHeader.GetRowNumber(row));
                    Assert.IsNull(row.Header);
                }
            }
        });
    }

    [TestMethod]
    public void ShowRowNumber_ShouldPreserveHostDefinedHeaderWidth()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A") };
            var dataGrid = CreateGrid(rows);
            dataGrid.RowHeaderWidth = 40;
            using var host = Show(dataGrid);

            Assert.AreEqual(40d, dataGrid.RowHeaderWidth);
            AssertRowNumbers(dataGrid);

            GridRowHeader.SetShowRowNumber(dataGrid, false);
            Assert.AreEqual(40d, dataGrid.RowHeaderWidth);

            GridRowHeader.SetShowRowNumber(dataGrid, true);
            Assert.AreEqual(40d, dataGrid.RowHeaderWidth);
            DrainDispatcher();
            AssertRowNumbers(dataGrid);
        });
    }

    [TestMethod]
    public void ShowRowNumber_ShouldPreserveHostDefinedRowHeaderPresentation()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow> { new("A") };
            var rowHeaderStyle = new Style(typeof(DataGridRowHeader));
            var rowHeaderTemplate = new DataTemplate();
            var dataGrid = CreateGrid(rows);
            dataGrid.RowHeaderStyle = rowHeaderStyle;
            dataGrid.RowHeaderTemplate = rowHeaderTemplate;
            dataGrid.RowHeaderWidth = 40;
            using var host = Show(dataGrid);

            AssertRowHeaderPresentation(dataGrid, rowHeaderStyle, rowHeaderTemplate, 40d);
            AssertRealizedRowHeader(dataGrid, expectedContent: 1, rowHeaderTemplate);

            GridRowHeader.SetShowRowNumber(dataGrid, false);
            AssertRowHeaderPresentation(dataGrid, rowHeaderStyle, rowHeaderTemplate, 40d);
            AssertRealizedRowHeader(dataGrid, expectedContent: null, rowHeaderTemplate);

            GridRowHeader.SetShowRowNumber(dataGrid, true);
            DrainDispatcher();
            AssertRowHeaderPresentation(dataGrid, rowHeaderStyle, rowHeaderTemplate, 40d);
            AssertRowNumbers(dataGrid);
            AssertRealizedRowHeader(dataGrid, expectedContent: 1, rowHeaderTemplate);
        });
    }

    [TestMethod]
    public void ShowRowNumber_VirtualizedRecycledRow_ShouldUseViewIndexWithoutBusinessRowIndex()
    {
        StaTest.Run(() =>
        {
            var rows = new ObservableCollection<TestRow>(
                Enumerable.Range(1, 100).Select(index => new TestRow($"R{index}")));
            var dataGrid = CreateGrid(rows);
            using var host = Show(dataGrid, height: 160);
            var last = rows[^1];

            dataGrid.ScrollIntoView(last);
            dataGrid.UpdateLayout();
            var row = (DataGridRow?)dataGrid.ItemContainerGenerator.ContainerFromItem(last);

            Assert.IsNotNull(row);
            Assert.AreEqual(100, GridRowHeader.GetRowNumber(row));
            Assert.AreEqual(100, row.Header);
            Assert.IsNull(typeof(TestRow).GetProperty("RowIndex"));
        });
    }

    private static DataGrid CreateGrid(ObservableCollection<TestRow> rows)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            RowHeaderWidth = 32,
            EnableRowVirtualization = true
        };
        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Binding = new Binding(nameof(TestRow.Name)),
            SortMemberPath = nameof(TestRow.Name)
        });
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        GridRowHeader.SetShowRowNumber(dataGrid, true);
        return dataGrid;
    }

    private static void AssertRowNumbers(DataGrid dataGrid)
    {
        dataGrid.UpdateLayout();
        for (var index = 0; index < dataGrid.Items.Count; index++)
        {
            if (dataGrid.ItemContainerGenerator.ContainerFromIndex(index) is DataGridRow row)
            {
                Assert.AreEqual(index + 1, GridRowHeader.GetRowNumber(row));
                Assert.AreEqual(index + 1, row.Header);
            }
        }
    }

    private static void AssertRowHeaderPresentation(
        DataGrid dataGrid,
        Style expectedStyle,
        DataTemplate expectedTemplate,
        double expectedWidth)
    {
        Assert.AreSame(expectedStyle, dataGrid.RowHeaderStyle);
        Assert.AreSame(expectedTemplate, dataGrid.RowHeaderTemplate);
        Assert.AreEqual(expectedWidth, dataGrid.RowHeaderWidth);
    }

    private static void AssertRealizedRowHeader(
        DataGrid dataGrid,
        object? expectedContent,
        DataTemplate expectedTemplate)
    {
        var row = dataGrid.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow;
        Assert.IsNotNull(row);
        var rowHeader = FindVisualChild<DataGridRowHeader>(row);
        Assert.IsNotNull(rowHeader);
        Assert.AreEqual(expectedContent, rowHeader.Content);
        Assert.AreSame(expectedTemplate, rowHeader.ContentTemplate);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IDisposable Show(DataGrid dataGrid, double height = 240)
    {
        var window = new Window
        {
            Width = 480,
            Height = height,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = dataGrid
        };
        window.Show();
        dataGrid.UpdateLayout();
        DrainDispatcher();
        return new WindowScope(window);
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed record TestRow(string Name);
}
