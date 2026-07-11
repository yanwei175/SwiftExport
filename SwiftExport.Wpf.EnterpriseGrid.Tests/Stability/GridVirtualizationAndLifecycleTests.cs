using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Core;
using SwiftExport.Wpf.EnterpriseGrid.Filtering;
using SwiftExport.Wpf.EnterpriseGrid.Layout;
using SwiftExport.Wpf.EnterpriseGrid.Selection;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;
using SwiftExport.Wpf.EnterpriseGrid.Validation;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Stability;

[TestClass]
public sealed class GridVirtualizationAndLifecycleTests
{
    [TestMethod]
    public void VirtualizationOption_ShouldApplyRecyclingAndRestoreOriginalLocalValues()
    {
        StaTest.Run(() =>
        {
            var dataGrid = new DataGrid
            {
                EnableRowVirtualization = false,
                EnableColumnVirtualization = false
            };
            VirtualizingPanel.SetIsVirtualizing(dataGrid, false);
            VirtualizingPanel.SetVirtualizationMode(dataGrid, VirtualizationMode.Standard);
            ScrollViewer.SetCanContentScroll(dataGrid, false);

            GridVirtualization.SetIsEnabled(dataGrid, true);
            Assert.IsTrue(dataGrid.EnableRowVirtualization);
            Assert.IsTrue(dataGrid.EnableColumnVirtualization);
            Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(dataGrid));
            Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(dataGrid));
            Assert.IsTrue(ScrollViewer.GetCanContentScroll(dataGrid));

            GridVirtualization.SetIsEnabled(dataGrid, false);
            Assert.IsFalse(dataGrid.EnableRowVirtualization);
            Assert.IsFalse(dataGrid.EnableColumnVirtualization);
            Assert.IsFalse(VirtualizingPanel.GetIsVirtualizing(dataGrid));
            Assert.AreEqual(VirtualizationMode.Standard, VirtualizingPanel.GetVirtualizationMode(dataGrid));
            Assert.IsFalse(ScrollViewer.GetCanContentScroll(dataGrid));
        });
    }

    [TestMethod]
    public void VirtualizationOption_ShouldPreserveExistingBindingWhenDisabled()
    {
        StaTest.Run(() =>
        {
            var source = new VirtualizationSource { IsRowVirtualizationEnabled = false };
            var dataGrid = new DataGrid();
            BindingOperations.SetBinding(
                dataGrid,
                DataGrid.EnableRowVirtualizationProperty,
                new Binding(nameof(VirtualizationSource.IsRowVirtualizationEnabled))
                {
                    Source = source,
                    Mode = BindingMode.OneWay
                });

            GridVirtualization.SetIsEnabled(dataGrid, true);
            Assert.IsTrue(BindingOperations.IsDataBound(dataGrid, DataGrid.EnableRowVirtualizationProperty));
            Assert.IsTrue(dataGrid.EnableRowVirtualization);

            GridVirtualization.SetIsEnabled(dataGrid, false);
            Assert.IsTrue(BindingOperations.IsDataBound(dataGrid, DataGrid.EnableRowVirtualizationProperty));
            Assert.IsFalse(dataGrid.EnableRowVirtualization);
        });
    }

    [TestMethod]
    public void TenThousandRowsAndManyColumns_ShouldKeepContainersVirtualizedAndNavigateByDataReference()
    {
        StaTest.Run(() =>
        {
            var rows = Enumerable.Range(0, 10_000).Select(index => new LargeRow(index)).ToArray();
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = rows,
                Width = 640,
                Height = 260,
                SelectionMode = DataGridSelectionMode.Single
            };
            for (var index = 0; index < 40; index++)
            {
                var column = new DataGridTextColumn
                {
                    Header = $"C{index}",
                    Width = 100,
                    Binding = new Binding(nameof(LargeRow.Value))
                };
                GridColumn.SetKey(column, $"column-{index}");
                dataGrid.Columns.Add(column);
            }

            GridVirtualization.SetIsEnabled(dataGrid, true);
            GridSelection.SetIsCellClickSelectionEnabled(dataGrid, true);
            EnterpriseGridCore.SetIsEnabled(dataGrid, true);
            var last = rows[^1];
            GridSelection.SetCurrentCell(dataGrid, new GridCellReference(last, "column-39"));
            using var host = Show(dataGrid);
            dataGrid.UpdateLayout();

            Assert.AreSame(last, dataGrid.CurrentCell.Item);
            Assert.AreSame(dataGrid.Columns[39], dataGrid.CurrentCell.Column);
            var realizedRows = rows.Count(row => dataGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow);
            Assert.IsLessThan(200, realizedRows);
            var lastRow = dataGrid.ItemContainerGenerator.ContainerFromItem(last) as DataGridRow;
            Assert.IsNotNull(lastRow);
            Assert.IsTrue(dataGrid.EnableColumnVirtualization);
            var viewportCells = CountViewportChildren<DataGridCell>(lastRow, dataGrid);
            Assert.IsLessThan(40, viewportCells);
        });
    }

    [TestMethod]
    public void LargeLocalFilterAndMultipleGrids_ShouldRemainIndependent()
    {
        StaTest.Run(() =>
        {
            var firstRows = new ObservableCollection<LargeRow>(
                Enumerable.Range(0, 10_000).Select(index => new LargeRow(index)));
            var secondRows = new ObservableCollection<LargeRow>(
                Enumerable.Range(20_000, 10).Select(index => new LargeRow(index)));
            var first = CreateSmallGrid(firstRows);
            var second = CreateSmallGrid(secondRows);
            GridFilter.SetMode(first, GridFilterMode.Local);
            GridFilter.SetFilterSet(first, new GridFilterSet(new[]
            {
                new GridFilterDescriptor("value", nameof(LargeRow.Value), GridFilterOperator.GreaterThanOrEqual, 9_990)
            }));
            GridSelection.SetCurrentRow(first, firstRows[9_995]);
            GridSelection.SetCurrentRow(second, secondRows[3]);

            Assert.HasCount(10, first.Items.Cast<object>());
            Assert.HasCount(10, second.Items.Cast<object>());
            Assert.AreSame(firstRows[9_995], GridSelection.GetCurrentRow(first));
            Assert.AreSame(secondRows[3], GridSelection.GetCurrentRow(second));
            Assert.AreNotSame(GridLayout.GetState(first), GridLayout.GetState(second));
        });
    }

    [TestMethod]
    public void UnloadAndDisable_ShouldDetachItemHandlersRestoreViewAndAllowCollection()
    {
        StaTest.Run(() =>
        {
            var result = CreateThenDetachGrid();
            Assert.AreEqual(0, result.Item.ErrorsChangedSubscriberCount);
            Assert.IsNull(result.View.Filter);

            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => { }));
            ForceCollection();
            Assert.IsFalse(result.GridReference.IsAlive, "DataGrid 在完整 Detach 后不应被静态事件或模块持有。");
        });
    }

    private static DataGrid CreateSmallGrid(ObservableCollection<LargeRow> rows)
    {
        var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows };
        var column = new DataGridTextColumn { Binding = new Binding(nameof(LargeRow.Value)) };
        GridColumn.SetKey(column, "value");
        dataGrid.Columns.Add(column);
        GridLayout.SetIsEnabled(dataGrid, true);
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return dataGrid;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DetachResult CreateThenDetachGrid()
    {
        var item = new LeakAwareRow();
        var source = new ObservableCollection<LeakAwareRow> { item };
        var view = CollectionViewSource.GetDefaultView(source);
        var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = source };
        var column = new DataGridTextColumn { Binding = new Binding(nameof(LeakAwareRow.Value)) };
        GridColumn.SetKey(column, "value");
        dataGrid.Columns.Add(column);
        GridValidation.SetIsEnabled(dataGrid, true);
        GridLayout.SetIsEnabled(dataGrid, true);
        GridFilter.SetMode(dataGrid, GridFilterMode.Local);
        GridFilter.SetFilterSet(dataGrid, new GridFilterSet(new[]
        {
            new GridFilterDescriptor("value", nameof(LeakAwareRow.Value), GridFilterOperator.Contains, "x")
        }));
        EnterpriseGridCore.SetIsEnabled(dataGrid, true);
        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.AreEqual(1, item.ErrorsChangedSubscriberCount);
        Assert.IsNotNull(view.Filter);

        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        EnterpriseGridCore.SetIsEnabled(dataGrid, false);
        var reference = new WeakReference(dataGrid);
        dataGrid = null!;
        return new DetachResult(reference, item, view);
    }

    private static void ForceCollection()
    {
        for (var index = 0; index < 3; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static int CountViewportChildren<T>(DependencyObject parent, FrameworkElement viewport)
        where T : FrameworkElement
    {
        var count = 0;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element && element.ActualWidth > 0 && element.ActualHeight > 0)
            {
                try
                {
                    var bounds = element.TransformToAncestor(viewport)
                        .TransformBounds(new Rect(new Point(), element.RenderSize));
                    if (bounds.IntersectsWith(new Rect(new Point(), viewport.RenderSize)))
                    {
                        count++;
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            count += CountViewportChildren<T>(child, viewport);
        }

        return count;
    }

    private static IDisposable Show(FrameworkElement content)
    {
        var window = new Window
        {
            Width = 680,
            Height = 320,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = content
        };
        window.Show();
        content.UpdateLayout();
        return new WindowScope(window);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed record LargeRow(int Value);

    private sealed record DetachResult(WeakReference GridReference, LeakAwareRow Item, ICollectionView View);

    private sealed class LeakAwareRow : INotifyDataErrorInfo
    {
        private EventHandler<DataErrorsChangedEventArgs>? _errorsChanged;

        public string Value { get; } = "x";

        public bool HasErrors => false;

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

        public IEnumerable GetErrors(string? propertyName) => Array.Empty<string>();
    }

    private sealed class VirtualizationSource
    {
        public bool IsRowVirtualizationEnabled { get; set; }
    }
}
