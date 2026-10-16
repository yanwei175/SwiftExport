using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Navigation;
using SwiftExportClient.Shell;
using SwiftExportClient.Resources.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExportClient.Resources.Behaviors;
using SwiftExportClient.Mvvm;
using Microsoft.Xaml.Behaviors;
using CommunityToolkit.Mvvm.Input;

namespace SwiftExport.Client.Wpf.Tests.Navigation;

[TestClass]
public sealed class KeepAliveTabContentHostTests
{
    [TestMethod]
    public void WindowSwitchAtoBtoA_ShouldReuseViewsWithoutUnloadingUntilTabRemoved()
    {
        StaTest.RunAsync(async () =>
        {
            TrackingView.Reset();
            var first = CreateTab("a");
            var second = CreateTab("b");
            var tabs = new ObservableCollection<NavigationTabItem> { first, second };
            var host = new KeepAliveTabContentHost
            {
                ItemsSource = tabs,
                SelectedItem = first
            };
            var window = new Window
            {
                Width = 480,
                Height = 320,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host
            };
            window.Resources[new DataTemplateKey(typeof(TrackingViewModel))] = CreateTrackingTemplate();

            try
            {
                window.Show();
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var firstView = FindViews(host).Single(view => ReferenceEquals(view.DataContext, first.ContentViewModel));

                host.SelectedItem = second;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var secondView = FindViews(host).Single(view => ReferenceEquals(view.DataContext, second.ContentViewModel));
                host.SelectedItem = first;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.AreSame(firstView, FindViews(host).Single(view => ReferenceEquals(view.DataContext, first.ContentViewModel)));
                Assert.AreSame(secondView, FindViews(host).Single(view => ReferenceEquals(view.DataContext, second.ContentViewModel)));
                Assert.AreEqual(2, TrackingView.CreatedCount);
                Assert.AreEqual(2, TrackingView.LoadedCount);
                Assert.AreEqual(0, TrackingView.UnloadedCount);

                tabs.Remove(first);
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.AreEqual(1, TrackingView.UnloadedCount);
                Assert.AreEqual(1, FindViews(host).Count);
            }
            finally
            {
                window.Close();
                first.Dispose();
                second.Dispose();
            }
        });
    }

    [TestMethod]
    public void SwitchingTabs_ShouldCloseLookupPopupWithoutUnloadingKeptAliveView()
    {
        StaTest.RunAsync(async () =>
        {
            var first = CreateTab("lookup-a", new LookupHostViewModel());
            var second = CreateTab("lookup-b", new LookupHostViewModel());
            var tabs = new ObservableCollection<NavigationTabItem> { first, second };
            var host = new KeepAliveTabContentHost { ItemsSource = tabs, SelectedItem = first };
            var window = new Window { Width = 480, Height = 320, ShowInTaskbar = false, Content = host };
            window.Resources[new DataTemplateKey(typeof(LookupHostViewModel))] = CreateTemplate(typeof(LookupHostViewModel), typeof(LookupHostView));

            try
            {
                window.Show();
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var firstView = FindVisualChildren<LookupHostView>(host).Single(view => ReferenceEquals(view.DataContext, first.ContentViewModel));
                firstView.Lookup.IsDropDownOpen = true;

                host.SelectedItem = second;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.IsFalse(firstView.Lookup.IsDropDownOpen);
                Assert.IsTrue(firstView.IsLoaded);
                Assert.AreEqual(2, FindVisualChildren<LookupHostView>(host).Count);
            }
            finally
            {
                window.Close();
                first.Dispose();
                second.Dispose();
            }
        });
    }

    [TestMethod]
    public void GridFillDownSwitch_ShouldKeepSelectionCurrentCellAndVirtualizationWithoutUndo()
    {
        StaTest.RunAsync(async () =>
        {
            var firstModel = new GridHostViewModel();
            var first = CreateTab("grid-a", firstModel);
            var second = CreateTab("grid-b", new GridHostViewModel());
            var tabs = new ObservableCollection<NavigationTabItem> { first, second };
            var host = new KeepAliveTabContentHost { ItemsSource = tabs, SelectedItem = first };
            var window = new Window { Width = 480, Height = 220, ShowInTaskbar = false, Content = host };
            window.Resources[new DataTemplateKey(typeof(GridHostViewModel))] = CreateTemplate(typeof(GridHostViewModel), typeof(GridHostView));

            try
            {
                window.Show();
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var firstView = FindVisualChildren<GridHostView>(host).Single(view => ReferenceEquals(view.DataContext, firstModel));
                var grid = firstView.Grid;
                var column = grid.Columns[0];
                foreach (var row in firstModel.Rows.Skip(80).Take(3))
                {
                    grid.SelectedCells.Add(new DataGridCellInfo(row, column));
                }

                grid.CurrentCell = new DataGridCellInfo(firstModel.Rows[80], column);
                grid.ScrollIntoView(firstModel.Rows[82], column);
                grid.UpdateLayout();
                var scrollViewer = FindVisualChildren<ScrollViewer>(grid).First();
                var verticalOffset = scrollViewer.VerticalOffset;
                DataGridFillDownBehavior.FillDownCommand.Execute(null, grid);
                CollectionAssert.AreEqual(
                    new[] { firstModel.Rows[80].Value, firstModel.Rows[80].Value, firstModel.Rows[80].Value },
                    firstModel.Rows.Skip(80).Take(3).Select(row => row.Value).ToArray());
                Assert.IsFalse(System.Windows.Input.ApplicationCommands.Undo.CanExecute(null, grid));
                Assert.IsTrue(verticalOffset > 0d);

                host.SelectedItem = second;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                host.SelectedItem = first;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.AreSame(grid, firstView.Grid);
                Assert.AreSame(firstModel.Rows[80], grid.CurrentCell.Item);
                Assert.AreSame(column, grid.CurrentCell.Column);
                Assert.AreEqual(3, grid.SelectedCells.Count);
                Assert.AreEqual(verticalOffset, scrollViewer.VerticalOffset, 0.01d);
                Assert.IsTrue(grid.EnableRowVirtualization);
                Assert.IsTrue(grid.EnableColumnVirtualization);

                ApplicationCommands.Undo.Execute(null, grid);
                CollectionAssert.AreEqual(
                    new[] { "Row-080", "Row-080", "Row-080" },
                    firstModel.Rows.Skip(80).Take(3).Select(row => row.Value).ToArray());
            }
            finally
            {
                window.Close();
                first.Dispose();
                second.Dispose();
            }
        });
    }

    [TestMethod]
    public void ActualShellTabTemplate_ShouldKeepAllTenHeadersAndSingleKeepAliveContentHost()
    {
        StaTest.RunAsync(async () =>
        {
            _ = Application.Current ?? new Application();
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ApplicationResourceLoader.Load();
            var tabs = new ObservableCollection<NavigationTabItem>(
                Enumerable.Range(1, 10).Select(index => CreateTab($"tab-{index}")));
            var tabControl = new TabControl
            {
                Width = 460,
                Height = 300,
                ItemsSource = tabs,
                SelectedItem = tabs[0],
                Style = (Style)Application.Current.FindResource("ShellHeaderOnlyTabControlStyle")
            };
            tabControl.ItemContainerStyle = new Style(typeof(TabItem))
            {
                Setters = { new Setter(FrameworkElement.MinWidthProperty, 100d) }
            };
            var window = new Window { Width = 480, Height = 340, ShowInTaskbar = false, Content = tabControl };
            window.Resources[new DataTemplateKey(typeof(TrackingViewModel))] = CreateTrackingTemplate();

            try
            {
                window.Show();
                tabControl.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var headers = FindVisualChildren<TabItem>(tabControl);
                var contentHosts = FindVisualChildren<KeepAliveTabContentHost>(tabControl);

                Assert.AreEqual(10, headers.Count);
                Assert.IsTrue(headers.All(header => header.IsVisible));
                Assert.AreEqual(1, contentHosts.Count);
                Assert.AreEqual(10, contentHosts[0].Children.Count);
                Assert.IsTrue(IsVisualDescendantOf(contentHosts[0], tabControl));
            }
            finally
            {
                window.Close();
                foreach (var tab in tabs)
                {
                    tab.Dispose();
                }
            }
        });
    }

    [TestMethod]
    public void InvalidNumericEdit_ShouldRemainVisibleAfterSwitchingAwayAndBack()
    {
        StaTest.RunAsync(async () =>
        {
            var firstModel = new GridHostViewModel();
            var first = CreateTab("invalid-a", firstModel);
            var second = CreateTab("invalid-b", new GridHostViewModel());
            var tabs = new ObservableCollection<NavigationTabItem> { first, second };
            var host = new KeepAliveTabContentHost { ItemsSource = tabs, SelectedItem = first };
            var window = new Window { Width = 480, Height = 220, ShowInTaskbar = false, Content = host };
            window.Resources[new DataTemplateKey(typeof(GridHostViewModel))] = CreateTemplate(typeof(GridHostViewModel), typeof(GridHostView));

            try
            {
                window.Show();
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var firstView = FindVisualChildren<GridHostView>(host).Single(view => ReferenceEquals(view.DataContext, firstModel));
                var grid = firstView.Grid;
                var row = firstModel.Rows[0];
                var quantityColumn = grid.Columns[1];
                grid.CurrentCell = new DataGridCellInfo(row, quantityColumn);
                grid.ScrollIntoView(row, quantityColumn);
                Assert.IsTrue(grid.BeginEdit());
                grid.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Input);
                var editor = (TextBox)quantityColumn.GetCellContent(row)!;
                editor.Text = "invalid-number";
                var binding = BindingOperations.GetBindingExpression(editor, TextBox.TextProperty)!;
                binding.UpdateSource();

                Assert.IsTrue(binding.HasError);
                Assert.AreEqual(0, row.Quantity);

                host.SelectedItem = second;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                host.SelectedItem = first;
                host.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                Assert.AreSame(editor, quantityColumn.GetCellContent(row));
                Assert.AreEqual("invalid-number", editor.Text);
                Assert.IsTrue(binding.HasError);
                Assert.AreEqual(0, row.Quantity);
            }
            finally
            {
                window.Close();
                first.Dispose();
                second.Dispose();
            }
        });
    }

    private static NavigationTabItem CreateTab(string routeKey) => new(
        new NavigationRoute(routeKey, routeKey, "test", typeof(TrackingViewModel), "test", null),
        new TrackingViewModel());

    private static NavigationTabItem CreateTab(string routeKey, object viewModel) => new(
        new NavigationRoute(routeKey, routeKey, "test", viewModel.GetType(), "test", null),
        viewModel);

    private static DataTemplate CreateTrackingTemplate()
    {
#pragma warning disable CS0618
        return new DataTemplate(typeof(TrackingViewModel))
        {
            VisualTree = new FrameworkElementFactory(typeof(TrackingView))
        };
#pragma warning restore CS0618
    }

    private static DataTemplate CreateTemplate(Type dataType, Type viewType)
    {
#pragma warning disable CS0618
        return new DataTemplate(dataType) { VisualTree = new FrameworkElementFactory(viewType) };
#pragma warning restore CS0618
    }

    private static List<TrackingView> FindViews(DependencyObject root)
    {
        var result = new List<TrackingView>();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is TrackingView view)
            {
                result.Add(view);
            }

            result.AddRange(FindViews(child));
        }

        return result;
    }

    private static List<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                result.Add(match);
            }

            result.AddRange(FindVisualChildren<T>(child));
        }

        return result;
    }

    private static bool IsVisualDescendantOf(DependencyObject descendant, DependencyObject ancestor)
    {
        for (DependencyObject? current = descendant; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TrackingViewModel;

    private sealed class LookupHostViewModel;

    private sealed class LookupHostView : UserControl
    {
        public LookupHostView()
        {
            Lookup = new LookupTextBox();
            Content = Lookup;
        }

        public LookupTextBox Lookup { get; }
    }

    /// <summary>缓存页测试宿主，显式处理唯一允许填充的字段。</summary>
    private sealed class GridHostViewModel
    {
        public ICommand FillCommand => new RelayCommand<FillDownRequest>(request =>
        {
            if (request is not null && request.TryGetRows(Rows, out var source, out var targets))
                foreach (var target in targets) target.Value = source.Value;
        });
        public ObservableCollection<GridRow> Rows { get; } = new(
            Enumerable.Range(0, 100).Select(index => new GridRow($"Row-{index:000}")));
    }

    private sealed class GridHostView : UserControl
    {
        public GridHostView()
        {
            Grid = new DataGrid
            {
                AutoGenerateColumns = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
                EnableRowVirtualization = true,
                EnableColumnVirtualization = true
            };
            var column = new DataGridTextColumn
            {
                Binding = new Binding(nameof(GridRow.Value)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            };

            Grid.Columns.Add(column);
            var quantityColumn = new DataGridTextColumn
            {
                Binding = new Binding(nameof(GridRow.Quantity))
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    ValidatesOnExceptions = true
                }
            };

            Grid.Columns.Add(quantityColumn);
            Grid.SetBinding(DataGrid.ItemsSourceProperty, new Binding(nameof(GridHostViewModel.Rows)));

            DataGridFillDownBehavior.SetField(column, nameof(GridRow.Value));
            var fill = new DataGridFillDownBehavior();
            BindingOperations.SetBinding(fill, DataGridFillDownBehavior.CommandProperty, new Binding(nameof(GridHostViewModel.FillCommand)));
            Interaction.GetBehaviors(Grid).Add(fill);
            Content = Grid;
        }

        public DataGrid Grid { get; }
    }

    private sealed class GridRow(string value) : INotifyPropertyChanged
    {
        private string _value = value;
        private int _quantity;
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Value
        {
            get => _value;
            set
            {
                if (_value == value)
                {
                    return;
                }

                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity == value)
                {
                    return;
                }

                _quantity = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
            }
        }
    }

    private sealed class TrackingView : UserControl
    {
        public static int CreatedCount { get; private set; }
        public static int LoadedCount { get; private set; }
        public static int UnloadedCount { get; private set; }

        public TrackingView()
        {
            CreatedCount++;
            Content = new DataGrid();
            Loaded += (_, _) => LoadedCount++;
            Unloaded += (_, _) => UnloadedCount++;
        }

        public static void Reset()
        {
            CreatedCount = 0;
            LoadedCount = 0;
            UnloadedCount = 0;
        }
    }
}
