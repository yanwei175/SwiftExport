using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Columns.Lookup;
using SwiftExport.Wpf.EnterpriseGrid.Tests.TestInfrastructure;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Columns;

[TestClass]
public sealed class LookupDataGridColumnTests
{
    [TestMethod]
    public void LocalLookup_ShouldResolveDisplayAndCommitSelectedValueThroughNativeColumn()
    {
        StaTest.Run(() =>
        {
            var suppliers = new[]
            {
                new LookupItem(1, "供应商一"),
                new LookupItem(2, "供应商二")
            };
            var row = new LookupRow { SupplierId = 1 };
            var column = new LookupDataGridColumn
            {
                Header = "供应商",
                ItemsSource = suppliers,
                SelectedValuePath = nameof(LookupItem.Id),
                DisplayMemberPath = nameof(LookupItem.Name),
                SelectedValueBinding = new Binding(nameof(LookupRow.SupplierId))
                {
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                }
            };
            var dataGrid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { row } };
            dataGrid.Columns.Add(column);
            using var host = Show(dataGrid);
            SetCurrentCell(dataGrid, row, column);

            var display = column.GetCellContent(row) as TextBlock;
            Assert.IsNotNull(display);
            Assert.AreEqual("供应商一", display.Text);

            Assert.IsTrue(dataGrid.BeginEdit());
            dataGrid.UpdateLayout();
            var editor = FindVisualChild<GridLookupEditor>(dataGrid);
            Assert.IsNotNull(editor);
            editor.LookupValue = 2;
            Assert.IsTrue(dataGrid.CommitEdit(DataGridEditingUnit.Cell, true));
            Assert.AreEqual(2, row.SupplierId);
        });
    }

    [TestMethod]
    public void RemoteLookup_ShouldSearchCancelHandleEmptyErrorAndResolveSavedValue()
    {
        StaTest.RunAsync(async () =>
        {
            var provider = new TestLookupProvider();
            var editor = new GridLookupEditor
            {
                Provider = provider,
                SelectedValuePath = nameof(LookupItem.Id),
                DisplayMemberPath = nameof(LookupItem.Name),
                MinimumSearchLength = 2,
                SearchDelay = TimeSpan.Zero
            };
            using var host = Show(editor);

            await editor.SearchAsync("a");
            Assert.AreEqual(0, provider.SearchCount);
            Assert.HasCount(0, editor.Items.Cast<object>());

            await editor.SearchAsync("sup");
            Assert.AreEqual(1, provider.SearchCount);
            Assert.HasCount(2, editor.Items.Cast<object>());
            Assert.IsTrue(editor.IsDropDownOpen);

            await editor.SearchAsync("none");
            Assert.HasCount(0, editor.Items.Cast<object>());
            Assert.IsFalse(editor.IsDropDownOpen);

            using (var cancellation = new CancellationTokenSource())
            {
                var canceledSearch = editor.SearchAsync("cancel", cancellation.Token);
                cancellation.Cancel();
                await canceledSearch;
            }

            Assert.IsTrue(provider.CancellationObserved);

            await editor.SearchAsync("error");
            Assert.AreEqual("远程查找失败", editor.SearchError);

            editor.LookupValue = 99;
            await editor.ResolveSelectedValueAsync();
            Assert.AreEqual("已保存供应商", ((LookupItem)editor.SelectedItem).Name);
        });
    }

    private static IDisposable Show(FrameworkElement element)
    {
        var window = new Window
        {
            Width = 480,
            Height = 240,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = element
        };
        window.Show();
        element.UpdateLayout();
        return new WindowScope(window);
    }

    private static void SetCurrentCell(DataGrid dataGrid, object item, DataGridColumn column)
    {
        dataGrid.SelectedItem = item;
        dataGrid.CurrentCell = new DataGridCellInfo(item, column);
        dataGrid.ScrollIntoView(item, column);
        dataGrid.UpdateLayout();
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

    private sealed class WindowScope(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed record LookupItem(int Id, string Name);

    private sealed class LookupRow
    {
        public int SupplierId { get; set; }
    }

    private sealed class TestLookupProvider : IGridLookupProvider
    {
        public int SearchCount { get; private set; }

        public bool CancellationObserved { get; private set; }

        public async ValueTask<IReadOnlyList<object>> SearchAsync(
            string searchText,
            CancellationToken cancellationToken)
        {
            SearchCount++;
            if (searchText == "cancel")
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }

            if (searchText == "error")
            {
                throw new InvalidOperationException("远程查找失败");
            }

            return searchText == "none"
                ? Array.Empty<object>()
                : new object[] { new LookupItem(1, "供应商一"), new LookupItem(2, "供应商二") };
        }

        public ValueTask<object?> ResolveAsync(object selectedValue, CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>(new LookupItem(Convert.ToInt32(selectedValue), "已保存供应商"));
    }
}
