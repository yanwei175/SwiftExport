using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
[DoNotParallelize]
public sealed class ProductReferenceLookupTextBoxTests
{
    [TestMethod]
    public void ProductColorLookup_ShouldUseLocalCacheFieldsAndOpenOnlyAfterUserInput()
    {
        StaTest.RunAsync(async () =>
        {
            var lookup = new ProductColorLookupTextBox
            {
                ItemsSource = new[]
                {
                    new ProductColorDto { Uid = Guid.NewGuid(), ColorCodeUnique = "BLU", ColorCode = "B", ColorNameZh = "蓝色", ColorNameEn = "Blue" },
                    new ProductColorDto { Uid = Guid.NewGuid(), ColorCodeUnique = "RED", ColorCode = "R", ColorNameZh = "红色", ColorNameEn = "Red" },
                },
            };
            var window = new Window { Width = 400, Height = 160, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = lookup };
            try
            {
                window.Show();
                window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var editor = FindVisualDescendants<LookupTextBox>(lookup).Single();
                var input = (TextBox?)editor.FindName("InputTextBox");
                var results = (ListBox?)editor.FindName("ResultList");
                Assert.IsNotNull(input);
                Assert.IsNotNull(results);
                Assert.AreEqual(LookupTextBoxSearchMode.Local, editor.SearchMode);
                StringAssert.Contains(editor.FilterMemberPaths, nameof(ProductColorDto.ColorCodeUnique));
                StringAssert.Contains(editor.FilterMemberPaths, nameof(ProductColorDto.ColorNameZh));
                Assert.IsFalse(editor.ClearSelectionOnEdit);
                Assert.IsFalse(editor.IsDropDownOpen);

                input.Focus();
                input.Text = "Blue";
                await Task.Delay(360);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.IsTrue(editor.IsDropDownOpen);
                Assert.AreEqual(1, results.Items.Count);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void ProductCategoryFlatLookup_ShouldSearchCategoryPathWithoutTreeHost()
    {
        StaTest.RunAsync(() =>
        {
            var lookup = new ProductCategoryFlatLookupTextBox();
            var editor = (LookupTextBox?)lookup.FindName("LookupEditor");
            Assert.IsNotNull(editor);

            Assert.AreEqual(LookupTextBoxSearchMode.Local, editor.SearchMode);
            Assert.AreEqual("CategoryName", editor.DisplayMemberPath);
            StringAssert.Contains(editor.FilterMemberPaths, "CategoryPath");
            Assert.IsNull(editor.CustomDropDownContent);
            Assert.IsFalse(editor.ClearSelectionOnEdit);
            Assert.IsNotNull(editor.ItemTemplate);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DataGridEditors_ShouldFocusColorAndFlatCategoryTextBoxesOnBeginEdit()
    {
        StaTest.RunAsync(() =>
        {
            AssertEditorFocus<ProductColorLookupTextBox>(nameof(TestRow.Finish), "蓝色");
            AssertEditorFocus<ProductCategoryFlatLookupTextBox>(nameof(TestRow.CategoryName), "五金 / 拉手");
            return Task.CompletedTask;
        });
    }

    private static void AssertEditorFocus<TLookup>(string propertyName, string value)
        where TLookup : FrameworkElement
    {
        var row = new TestRow();
        typeof(TestRow).GetProperty(propertyName)!.SetValue(row, value);
        var grid = new DataGrid { Width = 420, Height = 140, AutoGenerateColumns = false, ItemsSource = new[] { row } };
        var column = CreateLookupColumn<TLookup>(propertyName);
        grid.Columns.Add(column);
        var window = new Window { Width = 480, Height = 200, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = grid };

        try
        {
            window.Show();
            window.Activate();
            PumpDispatcher();
            grid.CurrentCell = new DataGridCellInfo(row, column);
            Assert.IsTrue(grid.BeginEdit());
            PumpDispatcher();

            var lookup = FindVisualDescendants<TLookup>(grid).Single();
            var editor = FindVisualDescendants<LookupTextBox>(lookup).Single();
            var input = (TextBox?)editor.FindName("InputTextBox");
            Assert.IsNotNull(input);
            Assert.AreEqual(Visibility.Visible, input.Visibility);
            Assert.AreEqual(value, input.Text);
            Assert.AreEqual(value.Length, input.SelectionLength);
            Assert.IsTrue(input.IsKeyboardFocusWithin);
            Assert.IsFalse(editor.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    private static DataGridTemplateColumn CreateLookupColumn<TLookup>(string propertyName)
        where TLookup : FrameworkElement
    {
#pragma warning disable CS0618
        var displayFactory = new FrameworkElementFactory(typeof(TextBlock));
        displayFactory.SetBinding(TextBlock.TextProperty, new Binding(propertyName));
        var editingFactory = new FrameworkElementFactory(typeof(TLookup));
        var textProperty = typeof(TLookup) == typeof(ProductColorLookupTextBox)
            ? ProductColorLookupTextBox.TextProperty
            : ProductCategoryFlatLookupTextBox.TextProperty;
        editingFactory.SetBinding(textProperty, new Binding(propertyName) { Mode = BindingMode.OneWay });
        var displayTemplate = new DataTemplate { VisualTree = displayFactory };
        var editingTemplate = new DataTemplate { VisualTree = editingFactory };
#pragma warning restore CS0618
        return new DataGridTemplateColumn { CellTemplate = displayTemplate, CellEditingTemplate = editingTemplate };
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
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

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class TestRow
    {
        public string Finish { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
    }
}
