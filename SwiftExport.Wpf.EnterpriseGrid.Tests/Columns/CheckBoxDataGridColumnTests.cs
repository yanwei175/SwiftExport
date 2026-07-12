using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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

            var headerCheckBox = GetHeaderCheckBox(column);
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
            var dataGrid = CreateGrid(rows, column, new TestGridViewModel(rows));
            using var host = Show(dataGrid);

            var headerContainer = (Border)column.Header;
            var headerCheckBox = GetHeaderCheckBox(column);
            Assert.AreEqual(new Thickness(3, 0, 5, 0), headerContainer.Padding);
            Assert.AreEqual(new Thickness(0), headerCheckBox.Margin);
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
        return column.GetCellContent(item) as CheckBox ??
            throw new AssertFailedException("未生成 CheckBox Cell 内容。");
    }

    private static CheckBox GetHeaderCheckBox(CheckBoxDataGridColumn column) =>
        ((Border)column.Header).Child as CheckBox ??
        throw new AssertFailedException("未生成 Header CheckBox 内容。");

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
