using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xaml.Behaviors;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.PackagingMaterials.ViewModels;
using SwiftExportClient.Features.Products.PackagingMaterials.Views;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Behaviors;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>五张管理表的显示范围、原生菜单及模式接线验收，全部通过离线服务运行。</summary>
[TestClass, DoNotParallelize]
public sealed class ProductManagementGridSelectionTests
{
    /// <summary>只勾选当前显示行，整体拒绝混合、隐藏、删除或外来行；释放后不再接收视图通知。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public void Commands_RespectDisplayScopeModeAndDisposal(int table) => StaTest.RunAsync(async () =>
    {
        using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page(table);
        var rows = page.Rows;
        rows[1].SetChecked(true);
        page.Visible.Filter = row => !ReferenceEquals(row, rows[1].Value);
        page.SetVisible.Execute(false);
        Assert.IsTrue(rows[1].Checked());
        page.Check.Execute(new[] { rows[0].Value, rows[1].Value });
        page.Check.Execute(new[] { rows[0].Value, new object() });
        Assert.IsFalse(rows[0].Checked());
        // 同类型外来行也必须拒绝，不能只验证参数类型。
        var foreign = table switch
        {
            0 => (object)new ProductMasterLineModel(),
            1 => new ProductNeutralLineModel(),
            2 => new ProductNeutralManagerLineModel(),
            _ => new ProductLineModel()
        };
        page.Check.Execute(new[] { rows[0].Value, foreign });
        Assert.IsFalse(rows[0].Checked());
        page.Check.Execute(new[] { rows[0].Value, rows[2].Value });
        Assert.IsTrue(page.AllChecked());
        page.Uncheck.Execute(new[] { rows[0].Value });
        Assert.IsFalse(page.AllChecked());
        page.Invert.Execute(new[] { rows[0].Value });
        Assert.IsTrue(page.AllChecked());
        var changed = 0;
        page.Check.CanExecuteChanged += (_, _) => changed++;
        page.SetCellMode(true);
        Assert.IsTrue(changed > 0);
        foreach (var command in new[] { page.Check, page.Uncheck, page.Invert })
        {
            Assert.IsFalse(command.CanExecute(new[] { rows[0].Value }));
            command.Execute(new[] { rows[0].Value });
        }
        Assert.IsTrue(rows[0].Checked());
        page.SetVisible.Execute(false);
        Assert.IsFalse(rows[0].Checked()); Assert.IsFalse(rows[2].Checked()); Assert.IsTrue(rows[1].Checked());
        page.CheckVisible.Execute(null);
        Assert.IsTrue(page.AllChecked());
        page.InvertVisible.Execute(null);
        Assert.IsFalse(rows[0].Checked()); Assert.IsFalse(rows[2].Checked());
        Assert.IsFalse(page.Dirty(), "UI 勾选不能产生业务草稿。");
        Assert.AreEqual(503, page.Total(), "全选不得把服务端总量当作已加载范围。");
        Assert.AreEqual(1, fixture.DetailReads, "勾选不查询其他页或重新加载主型号明细。");
        page.SetCellMode(false);
        rows[2].Delete();
        page.Check.Execute(new[] { rows[0].Value, rows[2].Value });
        Assert.IsFalse(rows[0].Checked(), "包含已删除行的选区整体拒绝。");
        page.SetVisible.Execute(true);
        Assert.IsTrue(rows[0].Checked()); Assert.IsFalse(rows[2].Checked());
        var notifications = 0;
        page.Model.PropertyChanged += (_, args) => { if (args.PropertyName == page.HeaderProperty) notifications++; };
        fixture.Dispose();
        var before = notifications;
        page.Visible.Filter = null;
        page.Visible.Refresh();
        rows[0].SetChecked(false);
        Assert.AreEqual(before, notifications);
        Assert.IsFalse(page.SetVisible.CanExecute(true));
        page.SetVisible.Execute(true);
        page.Check.Execute(new[] { rows[0].Value });
        Assert.IsFalse(rows[0].Checked());
    });

    /// <summary>真实菜单键、自动化调用和空格触发表头及模式；卸载重载不重复注入列菜单。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public void ActualPage_MenuHeaderAndModeStaySynchronized(int table) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = await Fixture.CreateAsync();
        var page = fixture.Page(table);
        var view = page.CreateView();
        var window = new Window { Content = view, Width = 1500, Height = 950, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = Descendants(view).OfType<DataGrid>().Single(item => ReferenceEquals(item.ItemsSource, page.Source));
            Assert.AreEqual(1, Interaction.GetBehaviors(grid).OfType<DataGridColumnVisibilityBehavior>().Count());
            Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(page.Rows[0].Value); grid.SelectedItems.Add(page.Rows[2].Value);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background);
            var menu = grid.ContextMenu;
            var items = menu.Items.OfType<MenuItem>().ToArray();
            var operationHeaders = new[] { "查看", "勾选(选区)", "取消勾选(选区)", "反选(选区)", "全选(当前显示行)", "反选(当前显示行)" }
                .Concat(page.BusinessHeaders).Append("表格模式").ToArray();
            var expectedHeaders = table == 0
                ? new[] { "查看", "刷新" }.Concat(operationHeaders.Where(header => header is not "查看" and not "刷新")).ToArray()
                : operationHeaders;
            CollectionAssert.AreEqual(expectedHeaders, items.Select(item => (string)item.Header).ToArray());
            // 主型号刷新前移只改变布局；按职责取项继续验证全部勾选、模式和权限行为。
            items = operationHeaders.Select(header => items.Single(item => Equals(item.Header, header))).ToArray();
            Assert.AreSame(grid.SelectedItems, items[1].CommandParameter);
            await InvokeMenu(items[1], menu);
            Assert.AreEqual(2, page.Rows.Count(row => row.Checked()));
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[2], menu);
            Assert.IsTrue(page.Rows.All(row => !row.Checked()));
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[3], menu);
            Assert.AreEqual(2, page.Rows.Count(row => row.Checked()));
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[4], menu);
            Assert.IsTrue(page.Rows.All(row => row.Checked()));
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[5], menu);
            Assert.IsTrue(page.Rows.All(row => !row.Checked()));
            var header = Descendants(grid).OfType<CheckBox>().Single(box => ReferenceEquals(box.Command, page.SetVisible));
            await Space(header); Assert.IsTrue(page.Rows.All(row => row.Checked())); Assert.IsTrue(page.AllChecked());
            await Space(header); Assert.IsTrue(page.Rows.All(row => !row.Checked()));
            var firstRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(page.Rows[0].Value);
            var rowCheck = Descendants(firstRow).OfType<CheckBox>().Single(box => ReferenceEquals(box.DataContext, page.Rows[0].Value)
                && BindingOperations.GetBinding(box, CheckBox.IsCheckedProperty)?.Path.Path == "IsChecked");
            await Space(rowCheck);
            Assert.IsTrue(page.Rows[0].Checked(), $"表{table}控件值={rowCheck.IsChecked}，键盘焦点={rowCheck.IsKeyboardFocused}，绑定状态={BindingOperations.GetBindingExpression(rowCheck, CheckBox.IsCheckedProperty)?.Status}，默认更新={((FrameworkPropertyMetadata)CheckBox.IsCheckedProperty.GetMetadata(typeof(CheckBox))).DefaultUpdateSourceTrigger}");
            Assert.IsFalse(page.AllChecked());
            // 通过真实模式菜单修改 VM，再验证顶部开关和 DataGrid 均接纳同一状态。
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[^1], menu);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(page.CellMode()); Assert.AreEqual(DataGridSelectionUnit.CellOrRowHeader, grid.SelectionUnit);
            var toggle = Descendants(view).OfType<global::Wpf.Ui.Controls.ToggleSwitch>().Single(control =>
                BindingOperations.GetBinding(control, global::Wpf.Ui.Controls.ToggleSwitch.IsCheckedProperty)?.Path.Path == page.ModeProperty);
            Assert.IsTrue(toggle.IsChecked == true);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsTrue(items.Skip(1).Take(3).All(item => !item.IsEnabled));
            Assert.IsFalse(items.Single(item => Equals(item.Header, "删除")).IsEnabled);
            page.Check.Execute(new[] { page.Rows[1].Value });
            Assert.IsFalse(page.Rows[1].Checked());
            menu.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.Background);
            await Space(toggle); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(page.CellMode()); Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            Assert.IsFalse(page.Dirty());
            if (table is 3 or 4)
            {
                fixture.User.SetCurrentUser(new MeDto { Permissions = [] });
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsTrue(items.Skip(1).Take(5).All(item => !item.IsEnabled), "撤销读取权限后菜单勾选入口必须即时禁用。");
                Assert.IsFalse(items.Single(item => Equals(item.Header, "物料详情")).IsEnabled);
                Assert.IsFalse(header.IsEnabled);
                page.SetVisible.Execute(true); Assert.IsFalse(page.Rows[1].Checked());
                menu.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.Background);
            }
            var businessCount = page.BusinessHeaders.Length + 6;
            window.Content = null; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(businessCount, menu.Items.OfType<MenuItem>().Count());
            Assert.IsNull(page.CommitEdits(), "卸载必须释放原生编辑回调。");
            window.Content = view; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1, menu.Items.OfType<MenuItem>().Count(item => Equals(item.Header, "查看")));
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>Master 双表的模式和勾选互不混用，主型号切换后旧下表行不能再被命令使用。</summary>
    [TestMethod]
    public void MasterTables_KeepIndependentStateAndRejectOldContext() => StaTest.RunAsync(async () =>
    {
        using var fixture = await Fixture.CreateAsync();
        var vm = fixture.Masters; var old = vm.Neutrals.Items.ToArray();
        vm.CheckMasterVisibleCommand.Execute(null);
        Assert.IsTrue(vm.Masters.Items.All(row => row.IsChecked)); Assert.IsTrue(vm.Neutrals.Items.All(row => !row.IsChecked));
        vm.CheckNeutralSelectionCommand.Execute(vm.Masters.Items.ToArray()); Assert.IsTrue(vm.Neutrals.Items.All(row => !row.IsChecked));
        vm.IsNeutralCellSelectionMode = true;
        Assert.IsTrue(vm.CheckMasterSelectionCommand.CanExecute(vm.Masters.Items.ToArray()));
        Assert.IsFalse(vm.CheckNeutralSelectionCommand.CanExecute(old));
        vm.IsNeutralCellSelectionMode = false;
        await vm.SelectMasterAsync(vm.Masters.Items[1]);
        vm.CheckNeutralSelectionCommand.Execute(old);
        Assert.IsTrue(vm.Neutrals.Items.All(row => !row.IsChecked));
        vm.CheckNeutralVisibleCommand.Execute(null);
        Assert.IsTrue(vm.Neutrals.Items.All(row => row.IsChecked)); Assert.IsTrue(vm.Masters.Items.All(row => row.IsChecked));
        vm.IsCardView = true;
        Assert.IsFalse(vm.CheckMasterVisibleCommand.CanExecute(null));
        Assert.IsTrue(vm.OpenDetailCommand.CanExecute(vm.SelectedMaster), "卡片原有详情入口不能被列表勾选状态改变。");
        Assert.IsFalse(vm.HasUnsavedChanges);
    });

    /// <summary>菜单复用现有业务命令，取消丢弃、编辑提交失败、查询忙碌及撤销权限均阻断操作并保留草稿。</summary>
    [TestMethod]
    public void ExistingGuards_RemainEffectiveForMenus() => StaTest.RunAsync(async () =>
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.Confirm = false;
        fixture.Masters.Masters.Items[0].DefaultDescriptionZh = "父级草稿";
        fixture.Neutrals.Lines.Items[0].ProductCode = "中性草稿";
        fixture.Products.Products.Items[0].Note = "物料草稿";
        fixture.Packages.Products.Items[0].Note = "包材草稿";
        await fixture.Masters.RefreshCommand.ExecuteAsync(null);
        await fixture.Neutrals.RefreshCommand.ExecuteAsync(null);
        await fixture.Products.RefreshCommand.ExecuteAsync(null);
        await fixture.Packages.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.PageReads);
        Assert.AreEqual("父级草稿", fixture.Masters.Masters.Items[0].DefaultDescriptionZh);
        Assert.AreEqual("中性草稿", fixture.Neutrals.Lines.Items[0].ProductCode);
        Assert.AreEqual("物料草稿", fixture.Products.Products.Items[0].Note);
        Assert.AreEqual("包材草稿", fixture.Packages.Products.Items[0].Note);
        fixture.Products.CommitPendingEdits = () => false;
        fixture.Neutrals.CommitPendingEdits = () => false;
        fixture.Packages.CommitPendingEdits = () => false;
        fixture.Masters.CommitPendingEdits = () => false;
        fixture.Confirm = true;
        foreach (var command in new[] { fixture.Products.DeleteSelectedCommand, fixture.Neutrals.DeleteSelectedCommand,
                     fixture.Packages.DeleteSelectedCommand, fixture.Masters.DeleteSelectedCommand })
            await command.ExecuteAsync(null);
        Assert.IsTrue(fixture.Page(0).Rows.All(row => !row.Deleted()));
        Assert.IsTrue(fixture.Page(2).Rows.All(row => !row.Deleted()));
        Assert.IsTrue(fixture.Page(3).Rows.All(row => !row.Deleted()));
        Assert.IsTrue(fixture.Page(4).Rows.All(row => !row.Deleted()));
        fixture.Products.CommitPendingEdits = () => true; fixture.Neutrals.CommitPendingEdits = () => true;
        fixture.Products.IsCellSelectionMode = true; fixture.Neutrals.IsCellSelectionMode = true;
        Assert.IsFalse(fixture.Products.DeleteSelectedCommand.CanExecute(null));
        Assert.IsFalse(fixture.Neutrals.DeleteSelectedCommand.CanExecute(null));
        await fixture.Products.DeleteSelectedCommand.ExecuteAsync(null); await fixture.Neutrals.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.Page(2).Rows.All(row => !row.Deleted())); Assert.IsTrue(fixture.Page(3).Rows.All(row => !row.Deleted()));
        fixture.Masters.IsQuerying = true; fixture.Neutrals.IsQuerying = true;
        Assert.IsFalse(fixture.Masters.CheckMasterVisibleCommand.CanExecute(null)); Assert.IsFalse(fixture.Neutrals.CheckVisibleCommand.CanExecute(null));
        fixture.Masters.CheckMasterVisibleCommand.Execute(null); fixture.Neutrals.CheckVisibleCommand.Execute(null);
        Assert.IsTrue(fixture.Page(0).Rows.All(row => !row.Checked())); Assert.IsTrue(fixture.Page(2).Rows.All(row => !row.Checked()));
        fixture.User.SetCurrentUser(new MeDto { Permissions = [] });
        Assert.IsFalse(fixture.Packages.CheckVisibleCommand.CanExecute(null));
        Assert.IsFalse(fixture.Products.CheckVisibleCommand.CanExecute(null));
        Assert.IsFalse(fixture.Packages.DeleteSelectedCommand.CanExecute(null));
        Assert.IsFalse(fixture.Packages.OpenDetailCommand.CanExecute(null));
        fixture.Packages.CheckVisibleCommand.Execute(null); fixture.Products.CheckVisibleCommand.Execute(null);
        Assert.IsTrue(fixture.Page(3).Rows.All(row => !row.Checked())); Assert.IsTrue(fixture.Page(4).Rows.All(row => !row.Checked()));
    });

    /// <summary>只遍历当前呈现的视觉节点，不接管生产页面生命周期。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    /// <summary>使用原生菜单键建立 PlacementTarget，防止手工赋值掩盖上下文绑定错误。</summary>
    private static void OpenMenu(DataGrid grid)
    {
        Assert.IsTrue(grid.Focus());
        InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, 0, Key.Apps) { RoutedEvent = Keyboard.KeyUpEvent });
    }

    /// <summary>通过真实菜单自动化入口调用命令，等待弹出层关闭后再继续键盘交互。</summary>
    private static async Task InvokeMenu(MenuItem item, ContextMenu menu)
    {
        Assert.IsTrue(item.IsEnabled, (string)item.Header);
        var closed = new TaskCompletionSource();
        RoutedEventHandler handler = (_, _) => closed.TrySetResult();
        menu.Closed += handler;
        try
        {
            ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { menu.Closed -= handler; }
    }

    /// <summary>通过窗口内控件空格输入验证完整复选框或模式开关路径。</summary>
    private static async Task Space(Control control)
    {
        Assert.IsTrue(control.Focus());
        foreach (var routed in new[] { Keyboard.KeyDownEvent, Keyboard.KeyUpEvent })
            control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control)!, 0, Key.Space) { RoutedEvent = routed });
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    /// <summary>仅把不同泛型行适配为同一组行为断言，不模拟真实行的脏跟踪。</summary>
    private static RowProbe Probe<T>(BaseLineModel<T> row) where T : BaseDto =>
        new(row, () => row.IsChecked, value => row.IsChecked = value, row.MarkAsDeleted, () => row.IsDeleted);

    /// <summary>测试行的窄访问入口，所有变化仍由真实 LineModel 完成。</summary>
    private sealed record RowProbe(object Value, Func<bool> Checked, Action<bool> SetChecked, Action Delete, Func<bool> Deleted);

    /// <summary>描述各表的实际命令和绑定事实，供同一套交互场景逐表执行。</summary>
    private sealed record PageCase(INotifyPropertyChanged Model, IEnumerable Source, RowProbe[] Rows, IRelayCommand Check, IRelayCommand Uncheck,
        IRelayCommand Invert, IRelayCommand CheckVisible, IRelayCommand InvertVisible, IRelayCommand SetVisible, Func<bool> AllChecked,
        Func<bool> Dirty, Func<int> Total, Action<bool> SetCellMode, Func<bool> CellMode, string ModeProperty, string HeaderProperty,
        Func<UserControl> CreateView, string[] BusinessHeaders, Func<Func<bool>?> CommitEdits)
    {
        /// <summary>获取与真实表格 ItemsSource 相同的默认视图，以验证显示行范围。</summary>
        public ICollectionView Visible => CollectionViewSource.GetDefaultView(Source);
    }

    /// <summary>只替代服务端与 UI 服务，使用真实四个页面 VM、五张列表及共享缓存。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly CurrentUserService User = new();
        public ProductMasterManagerViewModel Masters { get; private set; } = null!;
        public ProductNeutralManagerViewModel Neutrals { get; private set; } = null!;
        public ProductsViewModel Products { get; private set; } = null!;
        public PackagingMaterialManagerViewModel Packages { get; private set; } = null!;
        public bool Confirm = true;
        public int DetailReads, PageReads;
        private bool _disposed;

        /// <summary>加载三个已保存样本及一个真实关联上下文，服务端总数刻意大于本页。</summary>
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.User.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.ProductsProductView, PermissionKeys.ProductsProductCreate,
                PermissionKeys.ProductsProductEdit, PermissionKeys.ProductsProductDelete] });
            var runner = Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
            var dialogs = Proxy<IDialogService>((method, _) => method.Name switch
            {
                nameof(IDialogService.ConfirmAsync) => Task.FromResult(fixture.Confirm),
                _ => Task.CompletedTask
            });
            var messages = Proxy<IMessageService>((_, _) => null);
            var navigation = Proxy<INavigationService>((_, _) => Task.CompletedTask);
            var cache = new ReferenceDataCacheServiceStub();
            var masters = Proxy<IProductMasterApi>((method, args) =>
            {
                if (method.Name != nameof(IProductMasterApi.GetDetailAsync)) { fixture.PageReads++; throw new AssertFailedException(method.Name); }
                fixture.DetailReads++;
                var id = (Guid)args[0]!;
                return Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(new()
                {
                    Uid = id, Version = 1, Neutrals = Enumerable.Range(1, 3).Select(index => new ProductNeutralDto
                    {
                        Uid = Guid.NewGuid(), Version = 1, ProductMasterId = id,
                        Data = new ProductNeutralWriteDto { ProductCode = $"N{index}", ColorId = Guid.NewGuid() }
                    }).ToList()
                }));
            });
            var products = Proxy<IProductApi>((method, _) => { fixture.PageReads++; throw new AssertFailedException(method.Name); });
            var neutrals = Proxy<IProductNeutralManagerApi>((method, _) => { fixture.PageReads++; throw new AssertFailedException(method.Name); });
            fixture.Masters = new(masters, runner, cache, messages, dialogs, navigation, null!, new ImageAddressResolverStub(), fixture.User);
            fixture.Neutrals = new(neutrals, masters, runner, cache, messages, dialogs, new ImageAddressResolverStub(), navigation, fixture.User);
            var bom = new BomDrawerViewModel(null!, products, runner, messages, dialogs);
            fixture.Products = new(products, null!, null!, runner, cache, messages, dialogs, bom, null!, fixture.User, navigation);
            fixture.Packages = new(products, runner, dialogs, fixture.User, navigation, cache, null!);
            fixture.Masters.Masters.LoadFromDtos(Enumerable.Range(1, 3).Select(index => new ProductMasterListItemDto
            { Uid = Guid.NewGuid(), Version = 1, Data = new ProductMasterWriteDto { MasterSku = $"M{index}" } }));
            await fixture.Masters.SelectMasterAsync(fixture.Masters.Masters.Items[0]);
            fixture.Neutrals.Lines.LoadFromDtos(Enumerable.Range(1, 3).Select(index => new ProductNeutralManagerListItemDto
            { Uid = Guid.NewGuid(), Version = 1, Data = new ProductNeutralManagerWriteDto { ProductCode = $"N{index}", ProductMasterId = Guid.NewGuid() } }));
            fixture.Neutrals.SelectedLine = fixture.Neutrals.Lines.Items[0];
            fixture.Products.Products.LoadFromDtos(Enumerable.Range(1, 3).Select(index => Product($"P{index}")));
            fixture.Products.SelectedLine = fixture.Products.Products.Items[0];
            fixture.Packages.Products.LoadFromDtos(Enumerable.Range(1, 3).Select(index => Product($"PACK{index}")));
            fixture.Packages.SelectedLine = fixture.Packages.Products.Items[0];
            fixture.Masters.TotalCount = fixture.Neutrals.TotalCount = fixture.Products.TotalCount = fixture.Packages.TotalCount = 503;
            return fixture;
        }

        /// <summary>按目标表返回真实命令；Master 下表与上表使用完全不同的属性和集合。</summary>
        public PageCase Page(int table) => table switch
        {
            0 => new(Masters, Masters.Masters.Items, Masters.Masters.Items.Select(row => Probe(row)).ToArray(),
                Masters.CheckMasterSelectionCommand, Masters.UncheckMasterSelectionCommand, Masters.InvertMasterSelectionCommand, Masters.CheckMasterVisibleCommand,
                Masters.InvertMasterVisibleCommand, Masters.SetMasterVisibleChecksCommand, () => Masters.IsAllMasterVisibleChecked, () => Masters.HasUnsavedChanges,
                () => Masters.TotalCount, value => Masters.IsCellSelectionMode = value, () => Masters.IsCellSelectionMode, nameof(Masters.IsCellSelectionMode),
                nameof(Masters.IsAllMasterVisibleChecked), () => new ProductMasterManagerView { DataContext = Masters }, ["编辑详情", "刷新", "删除"], () => Masters.CommitPendingEdits),
            1 => new(Masters, Masters.Neutrals.Items, Masters.Neutrals.Items.Select(row => Probe(row)).ToArray(),
                Masters.CheckNeutralSelectionCommand, Masters.UncheckNeutralSelectionCommand, Masters.InvertNeutralSelectionCommand, Masters.CheckNeutralVisibleCommand,
                Masters.InvertNeutralVisibleCommand, Masters.SetNeutralVisibleChecksCommand, () => Masters.IsAllNeutralVisibleChecked, () => Masters.HasUnsavedChanges,
                () => Masters.TotalCount, value => Masters.IsNeutralCellSelectionMode = value, () => Masters.IsNeutralCellSelectionMode, nameof(Masters.IsNeutralCellSelectionMode),
                nameof(Masters.IsAllNeutralVisibleChecked), () => new ProductMasterManagerView { DataContext = Masters }, ["新增中性产品", "删除", "刷新"], () => Masters.CommitPendingEdits),
            2 => new(Neutrals, Neutrals.Lines.Items, Neutrals.Lines.Items.Select(row => Probe(row)).ToArray(),
                Neutrals.CheckSelectionCommand, Neutrals.UncheckSelectionCommand, Neutrals.InvertSelectionCommand, Neutrals.CheckVisibleCommand,
                Neutrals.InvertVisibleCommand, Neutrals.SetVisibleChecksCommand, () => Neutrals.IsAllVisibleChecked, () => Neutrals.HasUnsavedChanges,
                () => Neutrals.TotalCount, value => Neutrals.IsCellSelectionMode = value, () => Neutrals.IsCellSelectionMode, nameof(Neutrals.IsCellSelectionMode),
                nameof(Neutrals.IsAllVisibleChecked), () => new ProductNeutralManagerView { DataContext = Neutrals }, ["刷新", "删除"], () => Neutrals.CommitPendingEdits),
            3 => new(Products, Products.Products.Items, Products.Products.Items.Select(row => Probe(row)).ToArray(),
                Products.CheckSelectionCommand, Products.UncheckSelectionCommand, Products.InvertSelectionCommand, Products.CheckVisibleCommand,
                Products.InvertVisibleCommand, Products.SetVisibleChecksCommand, () => Products.IsAllVisibleChecked, () => Products.HasChanges,
                () => Products.TotalCount, value => Products.IsCellSelectionMode = value, () => Products.IsCellSelectionMode, nameof(Products.IsCellSelectionMode),
                nameof(Products.IsAllVisibleChecked), () => new ProductsView { DataContext = Products }, ["物料详情", "刷新", "删除"], () => Products.CommitPendingEdits),
            _ => new(Packages, Packages.Products.Items, Packages.Products.Items.Select(row => Probe(row)).ToArray(),
                Packages.CheckSelectionCommand, Packages.UncheckSelectionCommand, Packages.InvertSelectionCommand, Packages.CheckVisibleCommand,
                Packages.InvertVisibleCommand, Packages.SetVisibleChecksCommand, () => Packages.IsAllVisibleChecked, () => Packages.HasUnsavedChanges,
                () => Packages.TotalCount, value => Packages.IsCellSelectionMode = value, () => Packages.IsCellSelectionMode, nameof(Packages.IsCellSelectionMode),
                nameof(Packages.IsAllVisibleChecked), () => new PackagingMaterialManagerView { DataContext = Packages }, ["物料详情", "刷新", "删除"], () => Packages.CommitPendingEdits)
        };

        private static ProductDto Product(string sku) => new() { Uid = Guid.NewGuid(), Version = 1, Sku = sku, MaterialType = MaterialTypesEnum.包材 };
        /// <summary>释放全部真实 VM，重复释放允许测试在 using 内显式验证解除订阅。</summary>
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            Masters.Dispose(); Neutrals.Dispose(); Products.Dispose(); Packages.Dispose();
        }
    }

    /// <summary>复用现有离线代理，只记录明确允许的服务调用。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call;
        return proxy;
    }
}
