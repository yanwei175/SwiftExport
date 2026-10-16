using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerManagement.ViewModels;
using SwiftExportClient.Features.Customers.CustomerManagement.Views;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>验证客户页批量勾选的范围、通知、真实菜单接线及原生选区和编辑器兼容。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CustomerBatchCheckTests
{
    /// <summary>重复格对应同一行只反转一次，批量通知合并且勾选不写业务字段或制造保存请求。</summary>
    [TestMethod]
    public void SelectionCommands_ShouldDeduplicateAndPreserveBusinessState() => StaTest.RunAsync(() =>
    {
        var vm = CreateVm();
        var rows = vm.Customers.Items.ToArray();
        var notifications = 0;
        vm.Customers.ListStateChanged += (_, _) => notifications++;
        vm.CheckSelectionCommand.Execute(new[] { rows[0], rows[2] });
        Assert.AreEqual(1, notifications);
        Assert.AreEqual(2, vm.Customers.CheckedCount);
        Assert.IsTrue(vm.DeleteSelectedCommand.CanExecute(null), "既有删除命令必须感知勾选结果。");
        vm.UncheckSelectionCommand.Execute(new[] { rows[0] });
        vm.InvertSelectionCommand.Execute(new[] { rows[0], rows[0], rows[2] });
        CollectionAssert.AreEqual(new[] { true, false, false }, rows.Select(row => row.IsChecked).ToArray());
        Assert.AreEqual(3, notifications);
        Assert.IsFalse(vm.HasChanges);
        Assert.IsFalse(vm.Customers.CreateSaveRequest().HasChanges);
        CollectionAssert.AreEqual(new[] { "客户0", "客户1", "客户2" }, rows.Select(row => row.CustomerName).ToArray());
        return Task.CompletedTask;
    });

    /// <summary>当前筛选结果包含未生成容器的行，隐藏行保留勾选；全选不会加载其他页。</summary>
    [TestMethod]
    public void VisibleCommands_ShouldRespectFilterAndEmptyView() => StaTest.RunAsync(() =>
    {
        var vm = CreateVm(200);
        var rows = vm.Customers.Items.ToArray();
        rows[1].IsChecked = true;
        var view = CollectionViewSource.GetDefaultView(vm.Customers.Items);
        view.Filter = item => !ReferenceEquals(item, rows[1]);
        vm.CheckVisibleCommand.Execute(null);
        Assert.AreEqual(200, vm.Customers.CheckedCount);
        vm.UncheckVisibleCommand.Execute(null);
        Assert.AreEqual(1, vm.Customers.CheckedCount);
        Assert.IsTrue(rows[1].IsChecked);
        Assert.IsFalse(vm.CheckSelectionCommand.CanExecute(new[] { rows[1] }));
        vm.UncheckSelectionCommand.Execute(new[] { rows[1] });
        Assert.IsTrue(rows[1].IsChecked);
        view.Filter = _ => false;
        Assert.IsFalse(vm.CheckVisibleCommand.CanExecute(null));
        vm.CheckVisibleCommand.Execute(null);
        Assert.AreEqual(1, vm.Customers.CheckedCount);
        return Task.CompletedTask;
    });

    /// <summary>空选区不补当前行，外来行、删除行和换页前快照不能通过直接调用命令写入。</summary>
    [TestMethod]
    public void SelectionCommands_ShouldRejectStaleAndForeignRows() => StaTest.RunAsync(() =>
    {
        var vm = CreateVm();
        var rows = vm.Customers.Items.ToArray();
        vm.SelectedLine = rows[0];
        Assert.IsFalse(vm.CheckSelectionCommand.CanExecute(null));
        Assert.IsFalse(vm.CheckSelectionCommand.CanExecute(Array.Empty<CustomerLineModel>()));
        vm.CheckSelectionCommand.Execute(null);
        Assert.AreEqual(0, vm.Customers.CheckedCount);
        var foreign = new CustomerLineModel();
        vm.CheckSelectionCommand.Execute(new[] { rows[0], foreign });
        Assert.IsFalse(rows[0].IsChecked);
        Assert.IsFalse(foreign.IsChecked);
        vm.Customers.Remove(rows[1]);
        Assert.IsFalse(vm.CheckSelectionCommand.CanExecute(new[] { rows[1] }));
        vm.Customers.LoadFromDtos([Dto(9)]);
        vm.CheckSelectionCommand.Execute(rows);
        Assert.IsTrue(rows.All(row => !row.IsChecked));
        Assert.AreEqual(0, vm.Customers.CheckedCount);
        return Task.CompletedTask;
    });

    /// <summary>沿用基类 Busy 生命周期，禁用菜单命令且直接 Execute 也不会绕过；结束后恢复。</summary>
    [TestMethod]
    public void Busy_ShouldDisableAllFiveCommands() => StaTest.RunAsync(async () =>
    {
        var vm = CreateVm();
        var rows = vm.Customers.Items.ToArray();
        var completion = new TaskCompletionSource();
        var busy = (Task)typeof(ViewModelBaseEx).GetMethod("RunBusyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object?[] { new Func<Task>(() => completion.Task), "测试忙碌", true })!;
        try
        {
            Assert.IsTrue(vm.IsBusy);
            foreach (var command in new ICommand[] { vm.CheckSelectionCommand, vm.UncheckSelectionCommand, vm.InvertSelectionCommand })
            {
                Assert.IsFalse(command.CanExecute(rows));
                command.Execute(rows);
            }
            Assert.IsFalse(vm.CheckVisibleCommand.CanExecute(null));
            Assert.IsFalse(vm.UncheckVisibleCommand.CanExecute(null));
            vm.CheckVisibleCommand.Execute(null);
            Assert.AreEqual(0, vm.Customers.CheckedCount);
        }
        finally { completion.SetResult(); await busy; }
        Assert.IsTrue(vm.CheckSelectionCommand.CanExecute(rows));
        Assert.IsTrue(vm.CheckVisibleCommand.CanExecute(null));
    });

    /// <summary>真实页面菜单按原生行/格选区取数；开关菜单不改活动格，关闭释放快照并支持卸载重载。</summary>
    [TestMethod]
    [DataRow(DataGridSelectionUnit.FullRow)]
    [DataRow(DataGridSelectionUnit.Cell)]
    [DataRow(DataGridSelectionUnit.CellOrRowHeader)]
    public void Menu_ShouldUseNativeSelectionAndReleaseSnapshot(DataGridSelectionUnit unit) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var vm = CreateVm(200);
        var view = new CustomerManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 750, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Drain();
            var grid = Descendants(view).OfType<DataGrid>().Single(g => ReferenceEquals(g.ItemsSource, vm.Customers.Items));
            grid.SelectionUnit = unit;
            var rows = vm.Customers.Items.ToArray();
            grid.CurrentCell = new DataGridCellInfo(rows[0], grid.Columns[1]);
            grid.UnselectAllCells();
            if (unit == DataGridSelectionUnit.FullRow)
            {
                grid.SelectedItems.Add(rows[0]);
                grid.SelectedItems.Add(rows[2]);
            }
            else
            {
                grid.SelectedCells.Add(new DataGridCellInfo(rows[0], grid.Columns[1]));
                grid.SelectedCells.Add(new DataGridCellInfo(rows[0], grid.Columns[2]));
                grid.SelectedCells.Add(new DataGridCellInfo(rows[2], grid.Columns[1]));
            }
            var current = grid.CurrentCell;
            var selected = grid.SelectedCells.ToArray();
            var menu = grid.ContextMenu;
            grid.Focus();
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, 0, Key.Apps)
            { RoutedEvent = Keyboard.KeyUpEvent });
            await Drain();
            Assert.IsTrue(menu.IsOpen, "键盘菜单键应打开同一批量勾选菜单。");
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.AreEqual(5, items.Length);
            Assert.IsTrue(items.All(item => item.IsEnabled));
            Assert.AreSame(vm.CheckSelectionCommand, items[0].Command);
            var payload = (IReadOnlyList<CustomerLineModel>)items[0].CommandParameter;
            CollectionAssert.AreEquivalent(new[] { rows[0], rows[2] }, payload.ToArray());
            await Invoke(items[0], menu);
            Assert.AreEqual(2, vm.Customers.CheckedCount);
            OpenMenu(grid);
            await Drain();
            await Invoke(items[1], menu);
            Assert.AreEqual(0, vm.Customers.CheckedCount);
            OpenMenu(grid);
            await Drain();
            await Invoke(items[2], menu);
            await Drain();
            Assert.AreEqual(2, vm.Customers.CheckedCount);
            Assert.IsTrue(rows[0].IsChecked && rows[2].IsChecked);
            Assert.IsFalse(menu.IsOpen);
            Assert.IsNull(items[2].CommandParameter);
            Assert.AreEqual(current, grid.CurrentCell);
            CollectionAssert.AreEquivalent(selected, grid.SelectedCells.ToArray());
            Assert.IsTrue(grid.EnableRowVirtualization);
            Assert.IsNull(grid.ItemContainerGenerator.ContainerFromItem(rows[^1]));
            rows[1].IsChecked = true;
            CollectionViewSource.GetDefaultView(vm.Customers.Items).Filter = item => !ReferenceEquals(item, rows[1]);
            OpenMenu(grid);
            await Drain();
            Assert.IsTrue(menu.IsOpen);
            await Invoke(items[3], menu);
            await Drain();
            Assert.AreEqual(200, vm.Customers.CheckedCount);
            OpenMenu(grid);
            await Drain();
            Assert.IsTrue(menu.IsOpen);
            await Invoke(items[4], menu);
            await Drain();
            Assert.AreEqual(1, vm.Customers.CheckedCount);
            Assert.IsTrue(rows[1].IsChecked, "清空筛选结果必须保留隐藏行。");
            OpenMenu(grid);
            await Drain();
            var unloadedClose = new TaskCompletionSource();
            RoutedEventHandler onClosed = (_, _) => unloadedClose.TrySetResult();
            menu.Closed += onClosed;
            window.Content = null;
            try { await unloadedClose.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { menu.Closed -= onClosed; }
            Assert.IsFalse(menu.IsOpen);
            Assert.IsTrue(items.Take(3).All(item => item.CommandParameter is null));
            await Drain();
            window.Content = view;
            await Drain();
            grid.UnselectAllCells();
            OpenMenu(grid);
            await Drain();
            Assert.IsTrue(menu.IsOpen);
            Assert.IsTrue(items.Take(3).All(item => !item.IsEnabled));
            Assert.IsTrue(items.Skip(3).All(item => item.IsEnabled));
            menu.IsOpen = false;
            Assert.IsFalse(vm.HasChanges);
        }
        finally { window.Close(); }
    });

    /// <summary>页面编辑器继续使用原生文本菜单，表格批量菜单不覆盖编辑器复制粘贴。</summary>
    [TestMethod]
    public void Editor_ShouldKeepNativeTextContextMenu() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var vm = CreateVm();
        var view = new CustomerManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 750, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Drain();
            var grid = Descendants(view).OfType<DataGrid>().Single(g => ReferenceEquals(g.ItemsSource, vm.Customers.Items));
            grid.CurrentCell = new DataGridCellInfo(vm.Customers.Items[0], grid.Columns[2]);
            Assert.IsTrue(grid.BeginEdit());
            await Drain();
            var editor = Descendants(grid).OfType<TextBox>().First(box => box.IsKeyboardFocusWithin);
            editor.SelectAll();
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor)!, 0, Key.Apps)
            { RoutedEvent = Keyboard.KeyUpEvent });
            await Drain();
            Assert.IsFalse(grid.ContextMenu.IsOpen);
            var editorMenu = Keyboard.FocusedElement as ContextMenu
                ?? (Keyboard.FocusedElement is MenuItem focusedItem ? ItemsControl.ItemsControlFromItemContainer(focusedItem) as ContextMenu : null);
            Assert.IsNotNull(editorMenu);
            Assert.AreNotSame(grid.ContextMenu, editorMenu);
            Assert.IsTrue(editorMenu.Items.OfType<MenuItem>().Any(item => ReferenceEquals(item.Command, ApplicationCommands.Copy)));
            editorMenu.IsOpen = false;
            Assert.AreEqual("客户0", editor.Text);
            grid.CancelEdit();
        }
        finally { window.Close(); }
    });

    /// <summary>构造当前页快照和离线缓存；任何意外 API 调用都会暴露为失败。</summary>
    private static CustomerManagerViewModel CreateVm(int count = 3)
    {
        var vm = new CustomerManagerViewModel(null!, null!, null!, null!,
            new CustomerDictionaryColumnsTests.ReferenceDataCacheStub([]),
            new CustomerProductManagerViewModel(null!, null!, null!, null!, null!));
        vm.Customers.LoadFromDtos(Enumerable.Range(0, count).Select(Dto));
        return vm;
    }

    private static CustomerDto Dto(int index) => new()
    { Uid = Guid.NewGuid(), CustomerCode = $"C{index}", CustomerName = $"客户{index}", Version = 1 };

    /// <summary>通过菜单 Automation 调用真实点击路径，覆盖命令执行与关闭顺序。</summary>
    private static async Task Invoke(MenuItem item, ContextMenu menu)
    {
        var closed = new TaskCompletionSource();
        RoutedEventHandler handler = (_, _) => closed.TrySetResult();
        menu.Closed += handler;
        try
        {
            ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
            // 原生 Popup 的关闭动画结束后才发出 Closed，不能用一次 Dispatcher 空转代替。
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { menu.Closed -= handler; }
    }

    /// <summary>每次通过原生菜单键打开，交由 WPF 建立本次 PlacementTarget 和焦点路由。</summary>
    private static void OpenMenu(DataGrid grid)
    {
        grid.Focus();
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, 0, Key.Apps)
        { RoutedEvent = Keyboard.KeyUpEvent });
    }

    private static Task Drain() => Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
