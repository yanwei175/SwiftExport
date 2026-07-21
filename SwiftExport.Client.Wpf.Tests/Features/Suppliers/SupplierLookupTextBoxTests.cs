using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Suppliers.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

[TestClass]
[DoNotParallelize]
public sealed class SupplierLookupTextBoxTests
{
    [TestMethod]
    public void InitializationAndFocus_ShouldNotSearchOrOpenPopup()
    {
        StaTest.RunAsync(async () =>
        {
            var search = new RecordingCommand();
            var candidate = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "SUP-1", SupplierName = "供应商一" };
            var lookup = new SupplierLookupTextBox
            {
                Text = candidate.SupplierCode,
                SearchCommand = search,
                ItemsSource = new[] { candidate },
                SearchDelay = TimeSpan.FromMilliseconds(20),
            };
            var window = new Window
            {
                Width = 460,
                Height = 140,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                await WaitForDispatcherAsync(40);
                var editor = FindVisualDescendants<LookupTextBox>(lookup).Single();
                Assert.AreEqual(LookupTextBoxSearchMode.Remote, editor.SearchMode);
                Assert.AreEqual(nameof(SupplierDto.SupplierCode), editor.DisplayMemberPath);
                Assert.AreEqual(nameof(SupplierDto.Uid), editor.SelectedValuePath);
                Assert.IsTrue(editor.RequireItemMatch);
                Assert.IsFalse(editor.IsDropDownOpen);
                Assert.AreEqual(0, search.ExecutionCount);

                window.Activate();
                lookup.FocusEditor();
                await WaitForDispatcherAsync(40);

                Assert.IsFalse(editor.IsDropDownOpen);
                Assert.AreEqual(0, search.ExecutionCount);

                editor.SelectedItem = candidate;
                await WaitForDispatcherAsync(20);
                Assert.AreSame(candidate, lookup.SelectedSupplier);
                Assert.AreEqual(candidate.Uid, lookup.SelectedSupplierId);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void ItemTemplate_ShouldShowCodeAndWrappedName()
    {
        StaTest.RunAsync(() =>
        {
            var lookup = new SupplierLookupTextBox();
            var editor = (LookupTextBox?)lookup.FindName("LookupEditor");
            Assert.IsNotNull(editor?.ItemTemplate);
            var content = (FrameworkElement)editor.ItemTemplate.LoadContent();
            content.DataContext = new SupplierDto { SupplierCode = "SUP-1", SupplierName = "可以换行显示的供应商名称" };
            content.Measure(new Size(500, 160));
            content.Arrange(new Rect(content.DesiredSize));

            var textBlocks = FindVisualDescendants<TextBlock>(content).ToArray();
            Assert.IsTrue(content.MinWidth >= 320);
            Assert.IsTrue(textBlocks.Any(text =>
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == nameof(SupplierDto.SupplierCode)));
            Assert.IsTrue(textBlocks.Any(text =>
                BindingOperations.GetBinding(text, TextBlock.TextProperty)?.Path.Path == nameof(SupplierDto.SupplierName)));
            Assert.IsTrue(textBlocks.Any(text => text.TextWrapping == TextWrapping.Wrap));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DataGridTemplateColumn_ShouldCreateLookupOnlyWhileEditing()
    {
        StaTest.RunAsync(() =>
        {
            var row = new TestRow { SupplierCode = "SUP-1" };
            var grid = new DataGrid { Width = 400, Height = 150, AutoGenerateColumns = false, ItemsSource = new[] { row } };
            var column = CreateLookupColumn();
            grid.Columns.Add(column);
            var window = new Window { Width = 480, Height = 200, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = grid };

            try
            {
                window.Show();
                PumpDispatcher();
                Assert.AreEqual(0, FindVisualDescendants<SupplierLookupTextBox>(grid).Count());

                grid.CurrentCell = new DataGridCellInfo(row, column);
                Assert.IsTrue(grid.BeginEdit());
                PumpDispatcher();
                Assert.AreEqual(1, FindVisualDescendants<SupplierLookupTextBox>(grid).Count());

                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                PumpDispatcher();
                Assert.AreEqual(0, FindVisualDescendants<SupplierLookupTextBox>(grid).Count());
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    private static DataGridTemplateColumn CreateLookupColumn()
    {
#pragma warning disable CS0618
        var displayFactory = new FrameworkElementFactory(typeof(TextBlock));
        var displayTemplate = new DataTemplate { VisualTree = displayFactory };
        var editingFactory = new FrameworkElementFactory(typeof(SupplierLookupTextBox));
        var editingTemplate = new DataTemplate { VisualTree = editingFactory };
#pragma warning restore CS0618
        return new DataGridTemplateColumn { CellTemplate = displayTemplate, CellEditingTemplate = editingTemplate };
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static async Task WaitForDispatcherAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class TestRow
    {
        public string SupplierCode { get; set; } = string.Empty;
    }

    private sealed class RecordingCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecutionCount { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => ExecutionCount++;
    }
}
