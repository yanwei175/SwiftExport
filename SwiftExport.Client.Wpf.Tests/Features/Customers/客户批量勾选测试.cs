using DataGridExtensions;
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
    /// <summary>重复参数对应同一行只反转一次，批量通知合并且勾选不写业务字段或制造保存请求。</summary>
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
        rows[0].IsChecked = true;
        vm.InvertVisibleCommand.Execute(null);
        Assert.IsFalse(rows[0].IsChecked);
        Assert.IsTrue(rows[2].IsChecked);
        Assert.IsTrue(rows[1].IsChecked, "反选不能改变隐藏行。");
        Assert.AreEqual(199, vm.Customers.CheckedCount);
        vm.CheckVisibleCommand.Execute(null);
        Assert.AreEqual(200, vm.Customers.CheckedCount);
        vm.InvertVisibleCommand.Execute(null);
        Assert.AreEqual(1, vm.Customers.CheckedCount);
        Assert.IsTrue(rows[1].IsChecked);
        Assert.IsTrue(vm.CheckSelectionCommand.CanExecute(new[] { rows[1] }));
        vm.UncheckSelectionCommand.Execute(new[] { rows[1] });
        Assert.IsTrue(rows[1].IsChecked);
        view.Filter = _ => false;
        Assert.IsFalse(vm.CheckVisibleCommand.CanExecute(null));
        Assert.IsFalse(vm.InvertVisibleCommand.CanExecute(null));
        vm.InvertVisibleCommand.Execute(null);
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
        Assert.IsTrue(vm.CheckSelectionCommand.CanExecute(null));
        Assert.IsTrue(vm.CheckSelectionCommand.CanExecute(Array.Empty<CustomerLineModel>()));
        vm.CheckSelectionCommand.Execute(Array.Empty<CustomerLineModel>());
        vm.CheckSelectionCommand.Execute(null);
        Assert.AreEqual(0, vm.Customers.CheckedCount);
        var foreign = new CustomerLineModel();
        vm.CheckSelectionCommand.Execute(new[] { rows[0], foreign });
        Assert.IsFalse(rows[0].IsChecked);
        Assert.IsFalse(foreign.IsChecked);
        vm.Customers.Remove(rows[1]);
        Assert.IsTrue(vm.CheckSelectionCommand.CanExecute(new[] { rows[1] }));
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
            Assert.IsFalse(vm.InvertVisibleCommand.CanExecute(null));
            vm.CheckVisibleCommand.Execute(null);
            vm.InvertVisibleCommand.Execute(null);
            Assert.AreEqual(0, vm.Customers.CheckedCount);
        }
        finally { completion.SetResult(); await busy; }
        Assert.IsTrue(vm.CheckSelectionCommand.CanExecute(rows));
        Assert.IsTrue(vm.CheckVisibleCommand.CanExecute(null));
    });

    /// <summary>真实菜单直接读取原生整行集合；格子模式禁用选区命令，筛选结果命令不受影响。</summary>
    [TestMethod]
    [DataRow(DataGridSelectionUnit.FullRow)]
    [DataRow(DataGridSelectionUnit.Cell)]
    [DataRow(DataGridSelectionUnit.CellOrRowHeader)]
    public void Menu_ShouldBindNativeRowsAndDisableCellSelection(DataGridSelectionUnit unit) => DatePickerAssistTests.RunWithApplication(async () =>
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
            CollectionAssert.AreEqual(new[] { "查看", "刷新", "勾选(选区)", "取消勾选(选区)", "反选(选区)", "全选", "反选", "删除", "表格模式" },
                items.Select(item => (string)item.Header).ToArray());
            Assert.IsInstanceOfType<global::Wpf.Ui.Controls.SymbolIcon>(items[0].Icon);
            Assert.AreEqual(global::Wpf.Ui.Controls.SymbolRegular.Grid16, ((global::Wpf.Ui.Controls.SymbolIcon)items[0].Icon).Symbol);
            // 顺序单独按用户布局验证；操作断言按职责取项，避免刷新位置影响勾选与权限验证。
            items = new[] { "勾选(选区)", "取消勾选(选区)", "反选(选区)", "全选", "反选", "刷新", "删除", "表格模式" }
                .Select(header => items.Single(item => Equals(item.Header, header))).ToArray();
            Assert.AreSame(vm.RefreshCommand, items[5].Command);
            Assert.AreSame(vm.DeleteSelectedCommand, items[6].Command);
            Assert.IsTrue(items.Where((_, index) => index > 2 && index != 6).All(item => item.IsEnabled));
            Assert.IsTrue(items.Take(3).All(item => item.IsEnabled == (unit == DataGridSelectionUnit.FullRow)));
            // 格选区只用于批量勾选，不能隐式变成删除目标；删除仍按勾选项或原生整行选中项执行。
            Assert.AreEqual(unit == DataGridSelectionUnit.FullRow, items[6].IsEnabled);
            Assert.AreSame(grid.SelectedItem, vm.SelectedLine);
            Assert.AreSame(vm.CheckSelectionCommand, items[0].Command);
            Assert.AreSame(grid.SelectedItems, items[0].CommandParameter);
            if (unit == DataGridSelectionUnit.FullRow)
            {
                CollectionAssert.AreEquivalent(new[] { rows[0], rows[2] }, grid.SelectedItems.Cast<CustomerLineModel>().ToArray());
                await Invoke(items[0], menu);
                Assert.AreEqual(2, vm.Customers.CheckedCount);
                OpenMenu(grid);
                await Drain();
                await Invoke(items[1], menu);
                Assert.AreEqual(0, vm.Customers.CheckedCount);
                OpenMenu(grid);
                await Drain();
                await Invoke(items[2], menu);
                Assert.AreEqual(2, vm.Customers.CheckedCount);
                Assert.IsTrue(rows[0].IsChecked && rows[2].IsChecked);
            }
            else
            {
                // 即使直接执行并传入客户行，VM 也必须拒绝格子模式的选区操作。
                vm.CheckSelectionCommand.Execute(new[] { rows[0], rows[2] });
                vm.UncheckSelectionCommand.Execute(new[] { rows[0], rows[2] });
                vm.InvertSelectionCommand.Execute(new[] { rows[0], rows[2] });
                Assert.AreEqual(0, vm.Customers.CheckedCount);
                menu.IsOpen = false;
            }
            await Drain();
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
            Assert.IsTrue(rows[1].IsChecked, "反选筛选结果必须保留隐藏行。");
            OpenMenu(grid);
            await Drain();
            var unloadedClose = new TaskCompletionSource();
            RoutedEventHandler onClosed = (_, _) => unloadedClose.TrySetResult();
            menu.Closed += onClosed;
            window.Content = null;
            try { await unloadedClose.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
            finally { menu.Closed -= onClosed; }
            Assert.IsFalse(menu.IsOpen);
            await Drain();
            window.Content = view;
            await Drain();
            grid.UnselectAllCells();
            OpenMenu(grid);
            await Drain();
            Assert.IsTrue(menu.IsOpen);
            Assert.IsTrue(items.Take(3).All(item => item.IsEnabled == (unit == DataGridSelectionUnit.FullRow)));
            vm.CheckSelectionCommand.Execute(grid.SelectedItems);
            Assert.AreEqual(1, vm.Customers.CheckedCount, "空选区直接返回，不能补当前行。");
            Assert.IsTrue(items.Skip(3).Take(3).All(item => item.IsEnabled));
            menu.IsOpen = false;
            await Drain();
            if (unit == DataGridSelectionUnit.FullRow) grid.SelectedItems.Add(rows[2]);
            else grid.SelectedCells.Add(new DataGridCellInfo(rows[2], grid.Columns[1]));
            OpenMenu(grid);
            await Drain();
            Assert.AreSame(grid.SelectedItems, items[0].CommandParameter);
            if (unit == DataGridSelectionUnit.FullRow)
            {
                CollectionAssert.AreEqual(new[] { rows[2] }, grid.SelectedItems.Cast<CustomerLineModel>().ToArray());
                await Invoke(items[0], menu);
                Assert.IsTrue(rows[2].IsChecked, "关闭菜单后执行命令仍应读取原生集合。");
            }
            menu.IsOpen = false;
            await Drain();
            Assert.IsFalse(vm.HasChanges);
        }
        finally { window.Close(); }
    });

    /// <summary>菜单与工具栏共用选择模式，真实菜单点击更新原生选区单位，不再受产品编辑器控制。</summary>
    [TestMethod]
    public void SelectionModeMenu_ShouldSynchronizeWithToolbarAndDrawerGate() => DatePickerAssistTests.RunWithApplication(async () =>
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
            var toggle = Descendants(view).OfType<global::Wpf.Ui.Controls.ToggleSwitch>().Single(t => Equals(t.OnContent, "格子模式"));
            OpenMenu(grid);
            await Drain();
            var mode = grid.ContextMenu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, "表格模式"));
            Assert.IsTrue(mode.IsCheckable);
            Assert.IsFalse(mode.IsChecked);
            await Invoke(mode, grid.ContextMenu);
            await Drain();
            Assert.IsTrue(vm.IsCellSelectionMode);
            Assert.IsFalse(vm.CheckSelectionCommand.CanExecute(null));
            Assert.IsFalse(vm.UncheckSelectionCommand.CanExecute(null));
            Assert.IsFalse(vm.InvertSelectionCommand.CanExecute(null));
            Assert.IsTrue(vm.CheckVisibleCommand.CanExecute(null));
            Assert.IsTrue(vm.InvertVisibleCommand.CanExecute(null));
            Assert.AreEqual(true, toggle.IsChecked);
            Assert.AreEqual(DataGridSelectionUnit.CellOrRowHeader, grid.SelectionUnit);
            // 使用控件当前值模拟工具栏切换，保留 TwoWay 绑定。
            toggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, false);
            await Drain();
            OpenMenu(grid);
            await Drain();
            Assert.IsFalse(vm.IsCellSelectionMode);
            Assert.IsFalse(mode.IsChecked);
            Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            await Invoke(mode, grid.ContextMenu);
            Assert.IsTrue(vm.IsCellSelectionMode);
            Assert.IsFalse(vm.CheckSelectionCommand.CanExecute(null));
            Assert.IsFalse(vm.UncheckSelectionCommand.CanExecute(null));
            Assert.IsFalse(vm.InvertSelectionCommand.CanExecute(null));
            Assert.IsTrue(vm.CheckVisibleCommand.CanExecute(null));
            Assert.IsTrue(vm.InvertVisibleCommand.CanExecute(null));
            Assert.IsTrue(mode.IsEnabled);
            Assert.IsTrue(toggle.IsEnabled);
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

    /// <summary>真实扩展组合多个列条件，筛选不替换模型；表头只勾选当前结果，清除筛选保留隐藏草稿。</summary>
    [TestMethod]
    public void Filters_ShouldComposeAndHeaderShouldCheckOnlyVisibleRows() => WithGrid(async (vm, view, grid) =>
    {
        var rows = vm.Customers.Items.ToArray();
        rows[0].CustomerName = "未保存的客户";
        var code = grid.Columns.Single(column => Equals(column.Header, "客户代码"));
        code.SetFilter(new MultipleChoiceContentFilter(null, "C19"));
        await SettleFilter();
        Assert.AreEqual(11, grid.Items.Count);
        Assert.IsTrue(vm.HasChanges);
        grid.Columns.Single(column => Equals(column.Header, "客户名称")).SetFilter(new MultipleChoiceContentFilter(null, "客户199"));
        await SettleFilter();
        Assert.AreEqual(1, grid.Items.Count);
        Assert.AreSame(rows[199], grid.Items[0]);
        var header = Descendants(grid).OfType<CheckBox>().Single(box => ReferenceEquals(box.Command, vm.SetVisibleChecksCommand));
        typeof(CheckBox).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(header, null);
        Assert.IsTrue(rows[199].IsChecked);
        Assert.AreEqual(1, vm.Customers.CheckedCount);
        Assert.IsTrue(vm.IsAllVisibleChecked);
        await ClearFiltersFromPopup(grid);
        await SettleFilter();
        Assert.AreEqual(200, grid.Items.Count);
        Assert.AreSame(rows[0], vm.Customers.Items[0]);
        Assert.AreEqual("未保存的客户", rows[0].CustomerName);
        Assert.IsTrue(vm.Customers.CreateSaveRequest().HasChanges);
        Assert.IsTrue(rows[199].IsChecked);
        Assert.IsFalse(vm.IsAllVisibleChecked);
        Assert.IsFalse(Descendants(view).OfType<SwiftExportClient.Resources.Controls.PageNavigator>().Any());
        Assert.IsTrue(grid.EnableRowVirtualization);
    });

    /// <summary>列显隐菜单引用真实列，隐藏后可重新显示，筛选入口不依赖其他业务编辑器。</summary>
    [TestMethod]
    public void ColumnMenuAndFilters_ShouldRemainIndependent() => WithGrid(async (vm, view, grid) =>
    {
        OpenMenu(grid);
        await Drain();
        var columnsMenu = grid.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "查看"));
        columnsMenu.IsSubmenuOpen = true;
        await Drain();
        var column = columnsMenu.Items.OfType<DataGridColumn>().Single(entry => Equals(entry.Header, "客户名称"));
        var item = (MenuItem)columnsMenu.ItemContainerGenerator.ContainerFromItem(column);
        column.SetFilter(new MultipleChoiceContentFilter(null, "客户199"));
        await SettleFilter();
        Assert.AreEqual(1, grid.Items.Count);
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
        await Drain();
        Assert.AreEqual(Visibility.Collapsed, column.Visibility);
        await SettleFilter();
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
        await Drain();
        Assert.AreEqual(Visibility.Visible, column.Visibility);
        await SettleFilter();
        Assert.AreEqual(1, grid.Items.Count);
        column.SetFilter(null);
        await SettleFilter();
        var mode = grid.ContextMenu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, "表格模式"));
        mode.ApplyTemplate();
        var surface = (Border)mode.Template.FindName("TemplateCell", mode);
        Assert.AreEqual(mode.ActualWidth, surface.ActualWidth, 0.5, "切换按钮背景必须覆盖整个菜单项宽度。");
        await Invoke(mode, grid.ContextMenu);
        Assert.IsEmpty(columnsMenu.Items);
        Assert.IsTrue(grid.IsEnabled);
        Assert.IsTrue(grid.GetFilter().FilterColumnControls.SelectMany(Descendants).OfType<MultipleChoiceFilter>().All(control => control.IsEnabled));
        Assert.IsFalse(vm.HasChanges);
    });

    /// <summary>绑定响应外部显隐，重新打开按原生显示顺序排列；重复加载不累积菜单项或事件。</summary>
    [TestMethod]
    public void ColumnMenuBinding_ShouldTrackVisibilityOrderAndReload() => WithGrid(async (vm, view, grid) =>
    {
        var column = grid.Columns.Single(entry => Equals(entry.Header, "客户名称"));
        column.DisplayIndex = 1;
        for (var pass = 0; pass < 2; pass++)
        {
            OpenMenu(grid);
            await Drain();
            var menu = grid.ContextMenu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, "查看"));
            menu.IsSubmenuOpen = true;
            await Drain();
            Assert.AreSame(column, menu.Items[0]);
            Assert.AreEqual(grid.Columns.Count(entry => entry.Header is string), menu.Items.Count);
            var item = (MenuItem)menu.ItemContainerGenerator.ContainerFromItem(column);
            column.Visibility = Visibility.Collapsed;
            await Drain();
            Assert.IsFalse(item.IsChecked);
            column.Visibility = Visibility.Visible;
            await Drain();
            Assert.IsTrue(item.IsChecked);
            grid.ContextMenu.IsOpen = false;
            await Drain();
            Assert.IsEmpty(menu.Items);
            if (pass == 0)
            {
                var window = Window.GetWindow(view);
                window.Content = null;
                await Drain();
                window.Content = view;
                await SettleFilter();
            }
        }
        Assert.IsFalse(vm.HasChanges);
    });

    /// <summary>显隐绑定必须等原生编辑提交成功；非法输入时恢复菜单勾选且保留编辑器，修正后正常隐藏。</summary>
    [TestMethod]
    public void ColumnMenu_WhenEditCannotCommit_ShouldRestoreCheckAndPreserveInput() => WithGrid(async (vm, view, grid) =>
    {
        var row = vm.Customers.Items[0];
        var column = grid.Columns.Single(entry => Equals(entry.Header, "交期(天)"));
        grid.ScrollIntoView(row, column);
        grid.CurrentCell = new DataGridCellInfo(row, column);
        Assert.IsTrue(grid.BeginEdit());
        await Drain();
        var editor = Descendants(grid).OfType<TextBox>().First(box => box.IsKeyboardFocusWithin);
        editor.Text = "不是整数";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        OpenMenu(grid);
        await Drain();
        var menu = grid.ContextMenu.Items.OfType<MenuItem>().Single(entry => Equals(entry.Header, "查看"));
        menu.IsSubmenuOpen = true;
        await Drain();
        var item = (MenuItem)menu.ItemContainerGenerator.ContainerFromItem(column);
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
        await Drain();
        Assert.AreEqual(Visibility.Visible, column.Visibility);
        Assert.IsTrue(item.IsChecked);
        Assert.AreEqual("不是整数", editor.Text);
        Assert.IsTrue(Validation.GetHasError(editor));
        editor.Text = "12";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
        await SettleFilter();
        Assert.AreEqual(Visibility.Collapsed, column.Visibility);
        Assert.AreEqual(12, row.LeadTimeDays);
        Assert.IsTrue(vm.HasChanges);
        grid.ContextMenu.IsOpen = false;
    });

    /// <summary>数字转换错误阻止筛选替换视图，取消原生编辑后重新操作筛选。</summary>
    [TestMethod]
    public void Filter_WhenEditCannotCommit_ShouldPreserveInputAndAllowExplicitRetry() => WithGrid(async (vm, view, grid) =>
    {
        var row = vm.Customers.Items[0];
        var number = grid.Columns.Single(column => Equals(column.Header, "交期(天)"));
        grid.ScrollIntoView(row, number);
        grid.CurrentCell = new DataGridCellInfo(row, number);
        Assert.IsTrue(grid.BeginEdit());
        await Drain();
        var editor = Descendants(grid).OfType<TextBox>().First(box => box.IsKeyboardFocusWithin);
        editor.Text = "不是整数";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.IsTrue(Validation.GetHasError(editor));
        grid.Columns.Single(column => Equals(column.Header, "客户代码")).SetFilter(new MultipleChoiceContentFilter(null, "C199"));
        await SettleFilter();
        Assert.AreEqual(200, grid.Items.Count);
        Assert.AreEqual("不是整数", editor.Text);
        grid.CancelEdit(DataGridEditingUnit.Cell);
        grid.CancelEdit(DataGridEditingUnit.Row);
        grid.Columns.Single(column => Equals(column.Header, "客户代码")).SetFilter(new MultipleChoiceContentFilter(null, "C199"));
        await SettleFilter();
        Assert.AreEqual(1, grid.Items.Count);
        Assert.AreEqual(0, row.LeadTimeDays);
    });

    /// <summary>公共清除命令拒绝无效编辑，修正后提交编辑并清除筛选，保留草稿状态。</summary>
    [TestMethod]
    public void ClearFilterCommand_ShouldPreserveInvalidInputAndCommitValidEdit() => WithGrid(async (vm, view, grid) =>
    {
        var code = grid.Columns.Single(column => Equals(column.Header, "客户代码"));
        code.SetFilter(new MultipleChoiceContentFilter(null, "^C0$"));
        await SettleFilter();
        var row = vm.Customers.Items[0];
        var number = grid.Columns.Single(column => Equals(column.Header, "交期(天)"));
        grid.ScrollIntoView(row, number);
        grid.CurrentCell = new DataGridCellInfo(row, number);
        Assert.IsTrue(grid.BeginEdit());
        await Drain();
        var editor = Descendants(grid).OfType<TextBox>().First(box => box.IsKeyboardFocusWithin);
        editor.Text = "不是整数";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        var clear = SwiftExportClient.Resources.Commands.DataGridFilterCommands.Clear;
        clear.Execute(grid);
        await SettleFilter();
        Assert.AreEqual(1, grid.Items.Count);
        Assert.AreEqual("不是整数", editor.Text);
        Assert.IsTrue(Validation.GetHasError(editor));
        editor.Text = "12";
        editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        clear.Execute(grid);
        await SettleFilter();
        Assert.AreEqual(200, grid.Items.Count);
        Assert.AreEqual(12, row.LeadTimeDays);
        Assert.IsTrue(vm.HasUnsavedChanges);
    });

    /// <summary>模板全选展示三态，键盘关闭回到表格，重新打开保持筛选状态。</summary>
    [TestMethod]
    [DataRow(Key.Escape)]
    [DataRow(Key.Enter)]
    [DataRow(Key.Tab)]
    public void FilterTemplate_ShouldKeepSelectAllAndKeyboardFocus(Key closeKey) => WithGrid(async (vm, view, grid) =>
    {
        var column = grid.Columns.Single(column => Equals(column.Header, "客户代码"));
        grid.CurrentCell = new DataGridCellInfo(vm.Customers.Items[0], column);
        ((UIElement)column.GetCellContent(vm.Customers.Items[0]).Parent).Focus();
        var filter = Descendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single();
        filter.IsPopupOpen = true;
        await SettleFilter();
        var popup = (System.Windows.Controls.Primitives.Popup)filter.Template.FindName("Popup", filter);
        var input = Descendants(popup.Child).OfType<TextBox>().Single();
        Assert.IsTrue(input.IsKeyboardFocusWithin, "弹窗打开应聚焦搜索框。");
        var choices = Descendants(popup.Child).OfType<ListBox>().Single();
        var selectAll = (System.Windows.Controls.Primitives.ToggleButton)filter.Template.FindName("SelectAll", filter);
        Assert.AreEqual(true, selectAll.IsChecked);
        choices.SelectedItems.Remove("C1");
        await SettleFilter();
        Assert.IsNull(selectAll.IsChecked);
        // 保留原生 ToggleButton 的切换次序：部分选择 -> 取消全选 -> 全选。
        var selectAllPeer = (IToggleProvider)new ToggleButtonAutomationPeer(selectAll).GetPattern(PatternInterface.Toggle)!;
        selectAllPeer.Toggle();
        await SettleFilter();
        Assert.AreEqual(0, choices.SelectedItems.Count);
        Assert.AreEqual(false, selectAll.IsChecked);
        selectAllPeer.Toggle();
        await SettleFilter();
        Assert.AreEqual(choices.Items.Count, choices.SelectedItems.Count);
        Assert.AreEqual(true, selectAll.IsChecked);
        input.Focus();
        input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input)!, 0, closeKey)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await Drain();
        Assert.IsFalse(popup.IsOpen);
        Assert.IsFalse(filter.IsPopupOpen);
        Assert.IsTrue(grid.IsKeyboardFocusWithin, "关闭后由库恢复表格焦点。");
        filter.IsPopupOpen = true;
        await SettleFilter();
        Assert.AreEqual(true, selectAll.IsChecked);
        Assert.IsTrue(input.IsKeyboardFocusWithin);
        grid.Focus();
        await Drain();
        Assert.IsFalse(filter.IsPopupOpen, "焦点离开弹窗应同步关闭。");
        Assert.IsFalse(vm.HasChanges);
    });

    /// <summary>真实漏斗按钮打开共享 WPF-UI Popup，多选只改变结果范围，清除条件恢复全部行。</summary>
    [TestMethod]
    [DataRow(global::Wpf.Ui.Appearance.ApplicationTheme.Light)]
    [DataRow(global::Wpf.Ui.Appearance.ApplicationTheme.Dark)]
    public void PopupFilter_ShouldSelectValuesAndClearWithoutHeaderInput(global::Wpf.Ui.Appearance.ApplicationTheme theme) => WithGrid(async (vm, view, grid) =>
    {
        Application.Current.Resources.MergedDictionaries.OfType<global::Wpf.Ui.Markup.ThemesDictionary>().Single().Theme = theme;
        await Drain();
        var column = grid.Columns.Single(column => Equals(column.Header, "客户代码"));
        var header = column.GetDataGridFilterColumnControl()!;
        var filter = Descendants(header).OfType<MultipleChoiceFilter>().Single();
        Assert.IsFalse(Descendants(header).OfType<TextBox>().Any());
        var button = Descendants(filter).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single();
        var peer = new ToggleButtonAutomationPeer(button);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
        await Drain();
        var popup = (System.Windows.Controls.Primitives.Popup)filter.Template.FindName("Popup", filter);
        Assert.IsTrue(popup.IsOpen);
        Assert.IsTrue(popup.AllowsTransparency, "圆角外层 Popup 必须允许透明，避免原生窗口出现黑色外圈。");
        Assert.IsTrue(((System.Windows.Interop.HwndSource)PresentationSource.FromVisual(popup.Child)!).UsesPerPixelOpacity);
        await SettleFilter();
        var input = Descendants(popup.Child).OfType<TextBox>().Single();
        Assert.IsTrue(input.IsVisible);
        Assert.AreSame(view.FindResource("DefaultUiTextBoxControlTemplate"), input.Template);
        var surface = (Border)popup.Child;
        Assert.AreEqual((Color)view.FindResource("SolidBackgroundFillColorBase"), ((SolidColorBrush)surface.Background).Color);
        Assert.AreEqual((byte)255, ((SolidColorBrush)surface.Background).Color.A, "独立 Popup 需要不透明表面，深色主题不能透出白底。");
        Assert.AreEqual((Color)view.FindResource("CardStrokeColorDefault"), ((SolidColorBrush)surface.BorderBrush).Color);
        Assert.AreEqual((CornerRadius)view.FindResource("ControlCornerRadius"), surface.CornerRadius);
        Assert.AreEqual("搜索筛选值", System.Windows.Automation.AutomationProperties.GetName(input));
        var selectAll = (System.Windows.Controls.Primitives.ToggleButton)filter.Template.FindName("SelectAll", filter);
        Assert.AreEqual(((SolidColorBrush)view.FindResource("TextFillColorPrimaryBrush")).Color, ((SolidColorBrush)selectAll.Foreground).Color);
        var choices = Descendants(popup.Child).OfType<ListBox>().Single();
        Assert.AreEqual(200, choices.Items.Count);
        Assert.IsTrue(choices.IsVisible);
        Assert.IsTrue(choices.ActualHeight > 0);
        SaveFilterPreview(surface, theme.ToString());
        choices.UnselectAll();
        choices.SelectedItems.Add("C1");
        choices.SelectedItems.Add("C19");
        await SettleFilter();
        await SettleFilter();
        CollectionAssert.AreEquivalent(new[] { "C1", "C19" }, grid.Items.Cast<CustomerLineModel>().Select(row => row.CustomerCode).ToArray());
        Assert.IsFalse(vm.HasChanges);
        filter.IsPopupOpen = false;
        await Drain();
        await ClearFiltersFromPopup(grid);
        await SettleFilter();
        Assert.AreEqual(200, grid.Items.Count);
        Assert.IsFalse(vm.HasChanges);
    });

    /// <summary>普通原生表格可直接复用公共筛选模板，不依赖客户页面资源或 ViewModel。</summary>
    [TestMethod]
    public void SharedFilter_ShouldWorkWithoutCustomerPage() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        var column = new DataGridTextColumn { Header = "名称", Binding = new Binding("Name"), SortMemberPath = "Name" };
        column.SetTemplate((ControlTemplate)Application.Current.FindResource("SwiftExportColumnFilter"));
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = new[] { new { Name = "甲" }, new { Name = "乙" } },
            ColumnHeaderStyle = (Style)Application.Current.FindResource("SwiftExportFilterColumnHeader")
        };
        // 与 XAML 一致：先启用扩展，再添加列，由库初始化列头筛选控件。
        grid.SetIsAutoFilterEnabled(true);
        grid.SetFilterEvaluationDelay(TimeSpan.FromMilliseconds(150));
        grid.Columns.Add(column);
        var window = new Window { Content = grid, Width = 400, Height = 300, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Drain();
            var filter = Descendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single();
            filter.IsPopupOpen = true;
            await SettleFilter();
            var popup = (System.Windows.Controls.Primitives.Popup)filter.Template.FindName("Popup", filter);
            Assert.IsTrue(popup.AllowsTransparency);
            var choices = Descendants(popup.Child).OfType<ListBox>().Single();
            CollectionAssert.AreEquivalent(new[] { "甲", "乙" }, choices.Items.Cast<string>().ToArray());
            choices.UnselectAll();
            choices.SelectedItems.Add("乙");
            await SettleFilter();
            Assert.AreEqual(1, grid.Items.Count);
            filter.IsPopupOpen = false;
        }
        finally
        {
            window.Close();
            await Drain();
        }
    });

    /// <summary>新增保留筛选条件，隐藏的新行和已有草稿仍参与保存。</summary>
    [TestMethod]
    public void AddUnderFilter_ShouldPreserveConditionsAndHiddenDraft() => WithGrid(async (vm, view, grid) =>
    {
        var code = grid.Columns.Single(column => Equals(column.Header, "客户代码"));
        var control = code.GetDataGridFilterColumnControl()!;
        var filter = Descendants(control).OfType<MultipleChoiceFilter>().Single();
        Assert.IsFalse(Descendants(control).OfType<TextBox>().Any(), "表头不得常驻筛选输入框。");
        filter.IsPopupOpen = true;
        await Drain();
        var popup = (System.Windows.Controls.Primitives.Popup)filter.Template.FindName("Popup", filter);
        Assert.IsTrue(popup.IsOpen);
        var input = Descendants(popup.Child).OfType<TextBox>().Single();
        input.Text = "C199";
        await SettleFilter();
        await SettleFilter();
        Assert.AreEqual(1, grid.Items.Count);
        filter.IsPopupOpen = false;
        await Drain();
        var old = vm.Customers.Items[0];
        old.CustomerName = "草稿仍保留";
        vm.AddRowCommand.Execute(null);
        await SettleFilter();
        Assert.AreEqual(1, grid.Items.Count);
        var added = vm.Customers.Items.Single(row => row.IsNew);
        Assert.IsFalse(grid.Items.Contains(added));
        Assert.IsTrue(vm.Customers.CreateSaveRequest().HasChanges);
        grid.GetFilter().Clear();
        await SettleFilter();
        Assert.IsTrue(grid.Items.Contains(added));
        Assert.AreEqual("草稿仍保留", old.CustomerName);
        Assert.IsTrue(vm.HasChanges);
    });

    /// <summary>扩展筛选后的原生格选区仍走既有 Ctrl+D 页面命令，仅改可见目标行并可放弃修改。</summary>
    [TestMethod]
    public void FilteredGrid_ShouldKeepBusinessFillDownAndRejectChanges() => WithGrid(async (vm, view, grid) =>
    {
        var rows = vm.Customers.Items.ToArray();
        grid.Columns.Single(column => Equals(column.Header, "客户代码")).SetFilter(new MultipleChoiceContentFilter(null, "C19"));
        await SettleFilter();
        vm.IsCellSelectionMode = true;
        await Drain();
        var name = grid.Columns.Single(column => Equals(column.Header, "客户名称"));
        grid.CurrentCell = new DataGridCellInfo(rows[19], name);
        grid.UnselectAllCells();
        grid.SelectedCells.Add(new DataGridCellInfo(rows[19], name));
        grid.SelectedCells.Add(new DataGridCellInfo(rows[190], name));
        var command = SwiftExportClient.Resources.Behaviors.DataGridFillDownBehavior.FillDownCommand;
        Assert.IsTrue(command.CanExecute(null, grid));
        command.Execute(null, grid);
        Assert.AreEqual(rows[19].CustomerName, rows[190].CustomerName);
        Assert.AreEqual("客户20", rows[20].CustomerName);
        Assert.IsTrue(vm.HasChanges);
        vm.RejectCommand.Execute(null);
        Assert.AreEqual("客户190", rows[190].CustomerName);
        Assert.IsFalse(vm.HasChanges);
        Assert.AreEqual(11, grid.Items.Count);
    });

    /// <summary>创建独立页面与真实主题，保证测试结束释放窗口和页面订阅。</summary>
    private static void WithGrid(Func<CustomerManagerViewModel, CustomerManagerView, DataGrid, Task> test) =>
        DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load();
            using var vm = CreateVm(200);
            var view = new CustomerManagerView { DataContext = vm };
            var window = new Window { Content = view, Width = 1600, Height = 750, ShowInTaskbar = false };
            try
            {
                window.Show();
                await SettleFilter();
                var grid = Descendants(view).OfType<DataGrid>().Single(g => ReferenceEquals(g.ItemsSource, vm.Customers.Items));
                await test(vm, view, grid);
            }
            finally
            {
                window.Close();
                // 库在 Unloaded 清除模板绑定；先完成卸载，再由测试宿主清空应用主题资源。
                await Drain();
            }
        });

    /// <summary>仅在本轮指定输出目录时保存真实 Popup 渲染，便于检查主题，不读取其他应用屏幕。</summary>
    private static void SaveFilterPreview(FrameworkElement surface, string theme)
    {
        var directory = Environment.GetEnvironmentVariable("SWIFTEXPORT_GRID_PREVIEW_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        System.IO.Directory.CreateDirectory(directory);
        surface.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(System.IO.Path.Combine(directory, $"filter-popup-{theme}.png"));
        encoder.Save(output);
    }

    private static async Task SettleFilter() { await Task.Delay(300); await Drain(); }

    /// <summary>构造当前页快照和离线缓存；任何意外 API 调用都会暴露为失败。</summary>
    private static CustomerManagerViewModel CreateVm(int count = 3)
    {
        var vm = new CustomerManagerViewModel(null!, null!, null!, null!,
            new CustomerDictionaryColumnsTests.ReferenceDataCacheStub([]));
        vm.Customers.LoadFromDtos(Enumerable.Range(0, count).Select(Dto));
        return vm;
    }

    private static CustomerDto Dto(int index) => new()
    { Uid = Guid.NewGuid(), CustomerCode = $"C{index}", CustomerName = $"客户{index}", Version = 1 };

    /// <summary>使用筛选弹窗中的公共清除按钮，覆盖宿主参数绑定。</summary>
    private static async Task ClearFiltersFromPopup(DataGrid grid)
    {
        var filter = grid.GetFilter().FilterColumnControls.SelectMany(Descendants).OfType<MultipleChoiceFilter>().First();
        filter.IsPopupOpen = true;
        await Drain();
        var button = (Button)filter.Template.FindName("ClearAllFilters", filter);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        await SettleFilter();
        filter.IsPopupOpen = false;
    }

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
