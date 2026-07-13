using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Columns;

[TestClass]
public sealed class CheckBoxDataGridColumnTests
{
    [TestMethod]
    public void ItemAndHeaderCheckBox_ShouldBindTwoWayAndRemainCentered()
    {
        StaTest.Run(() =>
        {
            var rows = new[] { new TestRow(), new TestRow() };
            var viewModel = new TestGridViewModel(rows);
            var column = CreateColumn();
            var dataGrid = CreateGrid(rows, column, viewModel);
            using var host = Show(dataGrid);

            var itemCheckBox = GetCellCheckBox(dataGrid, rows[0], column);
            Assert.AreEqual(HorizontalAlignment.Center, itemCheckBox.HorizontalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, itemCheckBox.VerticalAlignment);
            var toggleProvider = (IToggleProvider)new CheckBoxAutomationPeer(itemCheckBox)
                .GetPattern(PatternInterface.Toggle)!;
            toggleProvider.Toggle();
            Assert.IsTrue(rows[0].IsChecked);

            var headerCheckBox = GetHeaderCheckBox(dataGrid, column);
            Assert.AreEqual(HorizontalAlignment.Center, headerCheckBox.HorizontalAlignment);
            Assert.AreEqual(VerticalAlignment.Center, headerCheckBox.VerticalAlignment);
            headerCheckBox.IsChecked = true;
            headerCheckBox.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.UpdateSource();
            Assert.IsTrue(viewModel.IsAllChecked);
            Assert.IsTrue(rows.All(row => row.IsChecked));

            headerCheckBox.IsChecked = false;
            headerCheckBox.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.UpdateSource();
            Assert.IsTrue(rows.All(row => !row.IsChecked));
        });
    }

    [TestMethod]
    public void HeaderMarginsAndReadOnlyState_ShouldApplyWithoutEnablingSort()
    {
        StaTest.Run(() =>
        {
            var rows = new[] { new TestRow() };
            var column = CreateColumn();
            column.HeaderCheckBoxMarginLeft = 3;
            column.HeaderCheckBoxMarginRight = 5;
            column.HeaderCheckBoxWidth = 17;
            column.HeaderCheckBoxHeight = 18;
            column.CellCheckBoxWidth = 15;
            column.CellCheckBoxHeight = 16;
            var compactStyle = new Style(typeof(CheckBox));
            compactStyle.Setters.Add(new Setter(FrameworkElement.TagProperty, "compact"));
            column.HeaderCheckBoxStyle = compactStyle;
            column.CellCheckBoxStyle = compactStyle;
            var dataGrid = CreateGrid(rows, column, new TestGridViewModel(rows));
            using var host = Show(dataGrid);

            var headerCheckBox = GetHeaderCheckBox(dataGrid, column);
            var headerViewbox = FindVisualAncestor<Viewbox>(headerCheckBox);
            Assert.IsNotNull(headerViewbox);
            Assert.AreEqual(new Thickness(3, 0, 5, 0), headerViewbox.Margin);
            Assert.AreEqual(17, headerViewbox.Width);
            Assert.AreEqual(18, headerViewbox.Height);
            Assert.AreSame(compactStyle, headerCheckBox.Style);
            var itemCheckBox = GetCellCheckBox(dataGrid, rows[0], column);
            var cellViewbox = FindVisualAncestor<Viewbox>(itemCheckBox);
            Assert.IsNotNull(cellViewbox);
            Assert.AreEqual(15, cellViewbox.Width);
            Assert.AreEqual(16, cellViewbox.Height);
            Assert.AreSame(compactStyle, itemCheckBox.Style);
            Assert.IsFalse(column.CanUserSort);

            dataGrid.IsReadOnly = true;
            dataGrid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.IsFalse(headerCheckBox.IsEnabled);
            Assert.IsFalse(GetCellCheckBox(dataGrid, rows[0], column).IsEnabled);

            dataGrid.IsReadOnly = false;
            column.IsReadOnly = true;
            dataGrid.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            Assert.IsFalse(headerCheckBox.IsEnabled);
            Assert.IsFalse(GetCellCheckBox(dataGrid, rows[0], column).IsEnabled);
        });
    }

    private static CheckBoxDataGridColumn CreateColumn() => new()
    {
        ItemCheckBinding = new Binding(nameof(TestRow.IsChecked))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        },
        AllCheckBinding = new Binding("DataContext.IsAllChecked")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        }
    };

    private static DataGrid CreateGrid(
        IReadOnlyList<TestRow> rows,
        CheckBoxDataGridColumn column,
        TestGridViewModel viewModel)
    {
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = rows,
            DataContext = viewModel,
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader
        };
        dataGrid.Columns.Add(column);
        return dataGrid;
    }

    private static CheckBox GetCellCheckBox(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
        return FindVisualDescendant<CheckBox>(column.GetCellContent(item)) ??
            throw new AssertFailedException("未生成 CheckBox Cell 内容。");
    }

    private static CheckBox GetHeaderCheckBox(DataGrid dataGrid, CheckBoxDataGridColumn column)
    {
        var headers = FindVisualDescendants<DataGridColumnHeader>(dataGrid);
        var header = headers.Single(candidate => ReferenceEquals(candidate.Column, column));
        return FindVisualDescendant<CheckBox>(header) ??
            throw new AssertFailedException("未生成 Header CheckBox 内容。");
    }

    private static T? FindVisualDescendant<T>(DependencyObject? parent)
        where T : DependencyObject
    {
        if (parent is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject child)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IDisposable Show(DataGrid dataGrid)
    {
        var window = new Window
        {
            Width = 320,
            Height = 180,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = dataGrid
        };
        window.Show();
        dataGrid.UpdateLayout();
        return new WindowScope(window);
    }

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class TestGridViewModel(TestRow[] rows) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsAllChecked
        {
            get => rows.Length > 0 && rows.All(row => row.IsChecked);
            set
            {
                foreach (var row in rows)
                {
                    row.IsChecked = value;
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAllChecked)));
            }
        }
    }

    private sealed class TestRow : INotifyPropertyChanged
    {
        private bool _isChecked;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                {
                    return;
                }

                _isChecked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            }
        }
    }
}
