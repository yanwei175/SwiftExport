using SwiftExportClient.Resources.Behaviors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;

namespace SwiftExport.Client.Wpf.Tests.Resources.GridRegression;

/// <summary>验证增强模块与原生虚拟化、集合视图及卸载释放兼容。</summary>
[TestClass]
public sealed class GridVirtualizationAndLifecycleTests
{
    /// <summary>原生虚拟化配置下按数据项导航到远端行列，不生成全部容器。</summary>
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

                dataGrid.Columns.Add(column);
            }

            dataGrid.EnableRowVirtualization = true;
            dataGrid.EnableColumnVirtualization = true;
            VirtualizingPanel.SetIsVirtualizing(dataGrid, true);
            VirtualizingPanel.SetVirtualizationMode(dataGrid, VirtualizationMode.Recycling);
            ScrollViewer.SetCanContentScroll(dataGrid, true);

            var last = rows[^1];
            dataGrid.CurrentCell = new DataGridCellInfo(last, dataGrid.Columns[39]);
            using var host = Show(dataGrid);
            dataGrid.ScrollIntoView(last, dataGrid.Columns[39]);
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

    /// <summary>页面拥有的原生筛选和多个 Grid 的活动位置互不干扰。</summary>
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
            CollectionViewSource.GetDefaultView(firstRows).Filter = item => ((LargeRow)item).Value >= 9_990;
            first.CurrentItem = firstRows[9_995];
            second.CurrentItem = secondRows[3];

            Assert.HasCount(10, first.Items.Cast<object>());
            Assert.HasCount(10, second.Items.Cast<object>());
            Assert.AreSame(firstRows[9_995], first.CurrentItem);
            Assert.AreSame(secondRows[3], second.CurrentItem);
        });
    }

    /// <summary>卸载解绑验证通知并保留宿主筛选，Grid 可以被回收。</summary>
    [TestMethod]
    public void UnloadAndDisable_ShouldDetachItemHandlersPreserveViewAndAllowCollection()
    {
        StaTest.Run(() =>
        {
            var result = CreateThenDetachGrid();
            Assert.AreEqual(0, result.Item.PropertyChangedSubscriberCount);
            Assert.IsNotNull(result.View.Filter, "Grid 卸载不得清除宿主拥有的筛选器。");

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

        dataGrid.Columns.Add(column);

        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return dataGrid;
    }

    /// <summary>在独立栈帧创建并卸载 Grid，避免局部变量延长 GC 测试的生命周期。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DetachResult CreateThenDetachGrid()
    {
        var item = new LeakAwareRow();
        var source = new ObservableCollection<LeakAwareRow> { item };
        var view = CollectionViewSource.GetDefaultView(source);
        var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = source };
        var column = new DataGridTextColumn { Binding = new Binding(nameof(LeakAwareRow.Value)) };

        dataGrid.Columns.Add(column);
        GridValidation.SetIsEnabled(dataGrid, true);
        view.Filter = item => ((LeakAwareRow)item).Value == "x";

        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.AreEqual(1, item.PropertyChangedSubscriberCount);
        Assert.IsNotNull(view.Filter);

        dataGrid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        GridValidation.SetIsEnabled(dataGrid, false);
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

    /// <summary>统计当前软提示模型的属性订阅，确保卸载后不保留 Grid 引用。</summary>
    private sealed class LeakAwareRow : IDataErrorInfo, INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _propertyChanged;
        public string Value { get; } = "x";
        public int PropertyChangedSubscriberCount { get; private set; }
        public string Error => string.Empty;
        /// <summary>本夹具没有业务错误，仅验证观察关系的释放。</summary>
        public string this[string columnName] => string.Empty;
        /// <summary>记录属性通知订阅，验证完整解绑。</summary>
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _propertyChanged += value; PropertyChangedSubscriberCount++; }
            remove { _propertyChanged -= value; PropertyChangedSubscriberCount--; }
        }
    }
}
