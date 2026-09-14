using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExportClient.Features.DemandPlanning.NetDemand.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Components;
using SwiftExportClient.Resources.Controls;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.NetDemand;

/// <summary>验证物料需求推荐供应商编辑器按行持有合法选择，并遵守原生 DataGrid 编辑边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NetDemandSuggestedSupplierEditingTests
{
    [TestMethod]
    public void ExistingSelection_ShouldSurviveEmptyOrUnrelatedRemoteCandidatesAndEditorRecreation()
    {
        StaTest.RunAsync(() =>
        {
            var row = CreateRow("OLD", "原供应商");
            var context = new GridContext(row);
            context.Candidates.Add(CreateSupplier("OTHER", "其他供应商"));
            var (window, grid, column) = CreateWindow(context);

            try
            {
                window.Show();
                PumpDispatcher();

                AssertEditorSelection(grid, column, row, "OLD");
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                PumpDispatcher();
                Assert.IsFalse(row.IsSuggestionDirty);
                Assert.AreEqual("OLD", row.SuggestedPurchaseSupplierCode);

                context.Candidates.Clear();
                AssertEditorSelection(grid, column, row, "OLD");
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                PumpDispatcher();

                Assert.IsFalse(row.IsSuggestionDirty);
                Assert.AreEqual("OLD", row.SuggestedPurchaseSupplierCode);
                Assert.AreEqual(0, FindVisualDescendants<PurchaseSupplierLookupTextBox>(grid).Count());
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ExplicitSelectionAndClear_ShouldUpdateOnlyEditingRow()
    {
        StaTest.RunAsync(() =>
        {
            var first = CreateRow("FIRST", "第一供应商");
            var second = CreateRow("SECOND", "第二供应商");
            var replacement = CreateSupplier("NEW", "新供应商");
            var context = new GridContext(first, second);
            context.Candidates.Add(replacement);
            var (window, grid, column) = CreateWindow(context);

            try
            {
                window.Show();
                PumpDispatcher();
                BeginEdit(grid, column, first);
                var lookup = FindVisualDescendants<PurchaseSupplierLookupTextBox>(grid).Single();
                var editor = FindVisualDescendants<LookupTextBox>(lookup).Single();
                var input = (TextBox)editor.FindName("InputTextBox");
                var results = (ListBox)editor.FindName("ResultList");

                editor.IsDropDownOpen = true;
                results.SelectedItem = replacement;
                RaisePreviewKey(input, Key.Enter);
                PumpDispatcher();

                Assert.AreEqual(replacement.Uid, first.SuggestedPurchaseSupplierId);
                Assert.AreEqual("NEW", first.SuggestedPurchaseSupplierCode);
                Assert.IsTrue(first.IsSuggestionDirty);
                Assert.AreEqual("SECOND", second.SuggestedPurchaseSupplierCode);
                Assert.IsFalse(second.IsSuggestionDirty);

                var clear = (Button)editor.FindName("BtnClear");
                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpDispatcher();

                Assert.IsNull(first.SuggestedPurchaseSupplierId);
                Assert.IsNull(first.SuggestedPurchaseSupplierSelection);
                Assert.AreEqual("SECOND", second.SuggestedPurchaseSupplierCode);
                Assert.IsFalse(second.IsSuggestionDirty);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void EscapeWithoutSelection_ShouldKeepLoadedSelectionAndCleanState()
    {
        StaTest.RunAsync(() =>
        {
            var row = CreateRow("OLD", "原供应商");
            var context = new GridContext(row);
            var (window, grid, column) = CreateWindow(context);

            try
            {
                window.Show();
                PumpDispatcher();
                BeginEdit(grid, column, row);
                var lookup = FindVisualDescendants<PurchaseSupplierLookupTextBox>(grid).Single();
                var editor = FindVisualDescendants<LookupTextBox>(lookup).Single();
                var input = (TextBox)editor.FindName("InputTextBox");

                input.Text = "未提交文本";
                RaisePreviewKey(input, Key.Escape);
                PumpDispatcher();
                grid.CancelEdit(DataGridEditingUnit.Cell);
                PumpDispatcher();

                Assert.AreEqual("OLD", row.SuggestedPurchaseSupplierCode);
                Assert.AreEqual("原供应商", row.SuggestedPurchaseSupplierName);
                Assert.AreEqual("OLD", row.SuggestedPurchaseSupplierSelection?.SupplierCode);
                Assert.IsFalse(row.IsSuggestionDirty);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>创建只允许推荐供应商列进入编辑态的代表性 DataGrid。</summary>
    private static (Window Window, DataGrid Grid, DataGridTemplateColumn Column) CreateWindow(GridContext context)
    {
        var grid = new DataGrid
        {
            Width = 560,
            Height = 220,
            AutoGenerateColumns = false,
            IsReadOnly = false,
            ItemsSource = context.Rows,
            DataContext = context
        };
        var column = CreateSuggestionColumn();
        grid.Columns.Add(column);
        return (new Window
        {
            Width = 640,
            Height = 280,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = grid
        }, grid, column);
    }

    /// <summary>按页面协议创建展示/编辑分离的推荐供应商列。</summary>
    private static DataGridTemplateColumn CreateSuggestionColumn()
    {
#pragma warning disable CS0618
        var displayFactory = new FrameworkElementFactory(typeof(TextBlock));
        displayFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(MaterialDemandManagerRowModel.SuggestedPurchaseSupplierDisplay)));
        var displayTemplate = new DataTemplate { VisualTree = displayFactory };

        var editingFactory = new FrameworkElementFactory(typeof(PurchaseSupplierLookupTextBox));
        editingFactory.SetBinding(
            PurchaseLookupTextBoxBase.ItemsSourceProperty,
            new Binding("DataContext.Candidates")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
            });
        editingFactory.SetBinding(
            PurchaseSupplierLookupTextBox.SelectedSupplierProperty,
            new Binding(nameof(MaterialDemandManagerRowModel.SuggestedPurchaseSupplierSelection)) { Mode = BindingMode.OneWay });
        editingFactory.SetBinding(
            PurchaseLookupTextBoxBase.SelectionCommittedCommandProperty,
            new Binding(nameof(MaterialDemandManagerRowModel.CommitSuggestedSupplierSelectionCommand)));
        editingFactory.SetBinding(
            PurchaseLookupTextBoxBase.SelectionClearedCommandProperty,
            new Binding(nameof(MaterialDemandManagerRowModel.ClearSuggestedSupplierSelectionCommand)));
        var editingTemplate = new DataTemplate { VisualTree = editingFactory };
#pragma warning restore CS0618

        return new DataGridTemplateColumn
        {
            Header = "推荐采购供应商",
            CellTemplate = displayTemplate,
            CellEditingTemplate = editingTemplate
        };
    }

    private static void AssertEditorSelection(
        DataGrid grid,
        DataGridTemplateColumn column,
        MaterialDemandManagerRowModel row,
        string expectedCode)
    {
        BeginEdit(grid, column, row);
        var lookup = FindVisualDescendants<PurchaseSupplierLookupTextBox>(grid).Single();
        var editor = FindVisualDescendants<LookupTextBox>(lookup).Single();
        var selection = (ProcurementSupplierCandidateDto?)editor.SelectedItem;
        Assert.IsNotNull(selection);
        Assert.AreEqual(expectedCode, selection.SupplierCode);
    }

    private static void BeginEdit(DataGrid grid, DataGridTemplateColumn column, MaterialDemandManagerRowModel row)
    {
        grid.SelectedItem = row;
        grid.CurrentCell = new DataGridCellInfo(row, column);
        grid.Focus();
        Assert.IsTrue(grid.BeginEdit());
        PumpDispatcher();
    }

    private static MaterialDemandManagerRowModel CreateRow(string code, string name)
    {
        var supplier = CreateSupplier(code, name);
        return MaterialDemandManagerRowModel.FromDto(new MaterialDemandManagerItemDto
        {
            Uid = Guid.NewGuid(),
            Version = 1,
            SuggestedPurchaseSupplierId = supplier.Uid,
            SuggestedPurchaseSupplierCode = supplier.SupplierCode,
            SuggestedPurchaseSupplierName = supplier.SupplierName
        });
    }

    private static ProcurementSupplierCandidateDto CreateSupplier(string code, string name) => new()
    {
        Uid = Guid.NewGuid(),
        SupplierCode = code,
        SupplierName = name
    };

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

    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
    }

    /// <summary>为测试提供行集合和可变化的远程候选集合。</summary>
    private sealed class GridContext(params MaterialDemandManagerRowModel[] rows)
    {
        public ObservableCollection<MaterialDemandManagerRowModel> Rows { get; } = new(rows);
        public ObservableCollection<ProcurementSupplierCandidateDto> Candidates { get; } = [];
    }
}
