using System.Collections.ObjectModel;
using System.Globalization;
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
using SwiftExport.Client.Wpf.Tests.Features.Products;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Customers.Components;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.Models;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.ViewModels;
using SwiftExportClient.Features.Customers.CustomerPoConfigs.Views;
using SwiftExportClient.Features.Suppliers.Components;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Behaviors;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using Microsoft.Xaml.Behaviors;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

/// <summary>配置页查询身份、四类权限、勾选与原生编辑验收，服务调用均离线。</summary>
[TestClass, DoNotParallelize]
public sealed class CustomerPoConfigInteractionTests
{
    /// <summary>客户和供应商可独立筛选，另一侧空值原样传递为不限制。</summary>
    [TestMethod]
    [DataRow(true, false)] [DataRow(false, true)]
    public void Filters_AllowEitherIdentityIndependently(bool customer, bool supplier) => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Vm.InitializeAsync();
        f.Vm.FilterCustomer = customer ? f.Customers[0] : null; f.Vm.FilterSupplier = supplier ? f.Suppliers[0] : null;
        f.Vm.Keyword = "关键词"; await f.Vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(customer ? f.Customers[0].Uid : (Guid?)null, f.Requests[^1].CustomerId);
        Assert.AreEqual(supplier ? f.Suppliers[0].Uid : (Guid?)null, f.Requests[^1].SupplierId);
        Assert.AreEqual("关键词", f.Requests[^1].Keyword);
    });

    /// <summary>无查看权限不读取数据；撤权后仍能按未保存确认离开页面。</summary>
    [TestMethod]
    public void ViewRevocation_DoesNotTrapDirtyPageOnClose() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); f.Permissions(); await f.Vm.InitializeAsync();
        Assert.IsEmpty(f.Requests); Assert.IsTrue(await f.Vm.CanCloseAsync(default));
        f.AllPermissions(); await f.Vm.InitializeAsync(); f.Vm.Lines.Items[0].Note = "保留";
        f.Permissions(); f.Confirm = false; Assert.IsFalse(await f.Vm.CanCloseAsync(default));
        Assert.AreEqual("保留", f.Vm.Lines.Items[0].Note);
        f.Confirm = true; Assert.IsTrue(await f.Vm.CanCloseAsync(default));
    });

    /// <summary>页面在缓存初始化等待期间释放，旧初始化不得回写候选或发送查询。</summary>
    [TestMethod]
    public void Initialization_DisposalPreventsLateCacheWrites() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var pending = new TaskCompletionSource<CacheRefreshResult>(); f.EnsureTask = pending.Task;
        var notifications = 0; f.Vm.PropertyChanged += (_, _) => notifications++;
        var initializing = f.Vm.InitializeAsync(); f.Vm.Dispose(); var before = notifications;
        pending.SetResult(await new ReferenceDataCacheServiceStub().EnsureLoadedAsync()); await initializing;
        Assert.IsEmpty(f.Requests); Assert.AreEqual(before, notifications);
    });

    /// <summary>向下填充复用逐行权限，混合新旧行或外来行时整体拒绝。</summary>
    [TestMethod]
    public void FillDown_RechecksPerRowPermissionsAndOwnership() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Vm.InitializeAsync(); var vm = f.Vm;
        var source = vm.Lines.Items[0]; var existing = vm.Lines.Items[1]; source.Currency = "USD";
        f.Permissions(PermissionKeys.CustomersPoConfigView, PermissionKeys.CustomersPoConfigCreate);
        vm.AddRowCommand.Execute(null); var added = vm.SelectedLine!;
        var mixed = new FillDownRequest(source, new object[] { added, existing }, new[] { nameof(source.Currency) });
        Assert.IsFalse(vm.FillDownCommand.CanExecute(mixed)); vm.FillDownCommand.Execute(mixed);
        Assert.AreEqual("CNY", added.Currency); Assert.AreEqual("CNY", existing.Currency);
        var allowed = new FillDownRequest(source, new[] { added }, new[] { nameof(source.Currency) });
        Assert.IsTrue(vm.FillDownCommand.CanExecute(allowed)); vm.FillDownCommand.Execute(allowed); Assert.AreEqual("USD", added.Currency);
        var foreign = new FillDownRequest(source, new[] { new CustomerPoConfigLineModel() }, new[] { nameof(source.Currency) });
        Assert.IsFalse(vm.FillDownCommand.CanExecute(foreign)); vm.FillDownCommand.Execute(foreign);
        f.Permissions(PermissionKeys.CustomersPoConfigView, PermissionKeys.CustomersPoConfigEdit);
        Assert.IsFalse(vm.FillDownCommand.CanExecute(mixed));
        var edit = new FillDownRequest(source, new[] { existing }, new[] { nameof(source.Currency) });
        vm.FillDownCommand.Execute(edit); Assert.AreEqual("USD", existing.Currency);
    });
    /// <summary>筛选取消及失败保留草稿，翻页只使用成功条件，清空同时清三项条件。</summary>
    [TestMethod]
    public void Filters_PreserveDraftAndPagingOwnership() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Vm; await vm.InitializeAsync();
        vm.FilterCustomer = f.Customers[1]; vm.FilterSupplier = f.Suppliers[1]; vm.Keyword = " abc ";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(f.Customers[1].Uid, f.Requests[^1].CustomerId); Assert.AreEqual(f.Suppliers[1].Uid, f.Requests[^1].SupplierId);
        Assert.AreEqual("abc", f.Requests[^1].Keyword); Assert.AreEqual(1, vm.PageIndex);
        var draft = vm.Lines.Items[0]; draft.Note = "保留草稿"; f.Confirm = false;
        vm.FilterCustomer = f.Customers[0]; vm.FilterSupplier = f.Suppliers[0]; vm.Keyword = "取消条件";
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(2, f.Requests.Count);
        Assert.AreSame(f.Customers[1], vm.FilterCustomer); Assert.AreSame(f.Suppliers[1], vm.FilterSupplier); Assert.AreEqual(" abc ", vm.Keyword);
        await vm.ClearFiltersCommand.ExecuteAsync(null); await vm.RefreshCommand.ExecuteAsync(null);
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 50 });
        Assert.AreEqual(2, f.Requests.Count); Assert.AreEqual(0, f.CacheRefreshes.Count); Assert.AreSame(draft, vm.Lines.Items[0]);
        f.Confirm = true; f.FailRead = true;
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 50 });
        Assert.AreEqual(1, vm.PageIndex); Assert.AreSame(draft, vm.Lines.Items[0]); Assert.AreEqual("保留草稿", draft.Note);
        f.FailRead = false; vm.FilterCustomer = f.Customers[0];
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 3, NewSize = 50 });
        Assert.AreEqual(f.Customers[1].Uid, f.Requests[^1].CustomerId); Assert.AreEqual(3, vm.PageIndex); Assert.AreEqual(50, vm.PageSize);
        f.FailRead = true; await vm.ClearFiltersCommand.ExecuteAsync(null);
        Assert.AreSame(f.Customers[1], vm.FilterCustomer); Assert.AreEqual(" abc ", vm.Keyword); Assert.AreEqual(3, vm.PageIndex);
        f.FailRead = false; await vm.ClearFiltersCommand.ExecuteAsync(null);
        Assert.IsNull(f.Requests[^1].CustomerId); Assert.IsNull(f.Requests[^1].SupplierId); Assert.IsNull(f.Requests[^1].Keyword);
        Assert.IsNull(vm.FilterCustomer); Assert.IsNull(vm.FilterSupplier); Assert.AreEqual("", vm.Keyword); Assert.AreEqual(1, vm.PageIndex);
        await vm.RefreshCommand.ExecuteAsync(null);
        CollectionAssert.AreEquivalent(new[] { "RefreshCustomersAsync", "RefreshSuppliersAsync", "RefreshCurrenciesAsync" }, f.CacheRefreshes);
    });

    /// <summary>旧查询忽略取消也不能覆盖新条件；撤权和释放后响应不得回写。</summary>
    [TestMethod]
    public void Queries_RejectStaleRevokedAndDisposedResults() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Vm; await vm.InitializeAsync();
        var first = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>>();
        var second = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>>();
        f.Read = request => request.Keyword == "旧" ? first.Task : second.Task;
        vm.Keyword = "旧"; var old = vm.SearchCommand.ExecuteAsync(null); var token = f.Tokens[^1];
        vm.Keyword = "新"; var current = vm.SearchCommand.ExecuteAsync(null); Assert.IsTrue(token.IsCancellationRequested);
        second.SetResult(f.Page(f.Requests[^1], "新结果")); await current;
        first.SetResult(f.Page(f.Requests[1], "旧结果")); await old;
        Assert.AreEqual("新结果", vm.Lines.Items[0].Note); Assert.AreEqual("新", vm.Keyword);
        var pending = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>>();
        f.Read = _ => pending.Task; var revoked = vm.SearchCommand.ExecuteAsync(null);
        f.Permissions(); Assert.IsTrue(f.Tokens[^1].IsCancellationRequested);
        pending.SetResult(f.Page(f.Requests[^1], "不应回写")); await revoked;
        Assert.AreEqual("新结果", vm.Lines.Items[0].Note); Assert.IsFalse(vm.CanView);
        f.Permissions(PermissionKeys.CustomersPoConfigView);
        pending = new(); f.Read = _ => pending.Task; var disposed = vm.SearchCommand.ExecuteAsync(null); vm.Dispose();
        pending.SetResult(f.Page(f.Requests[^1], "关闭结果")); await disposed;
        Assert.AreEqual("新结果", vm.Lines.Items[0].Note);
    });

    /// <summary>ID 匹配优先于冲突编码，缺少 ID 的历史记录才回退编码，缓存更新不制造草稿。</summary>
    [TestMethod]
    public void References_PreferIdentityAndRefreshNamesWithoutDirtyState() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Vm.InitializeAsync(); var rows = f.Vm.Lines.Items;
        Assert.AreSame(f.Customers[0], rows[0].SelectedCustomer); Assert.AreSame(f.Suppliers[0], rows[0].SelectedSupplier);
        Assert.AreSame(f.Customers[0], rows[1].SelectedCustomer); Assert.AreSame(f.Suppliers[0], rows[1].SelectedSupplier);
        Assert.IsNull(rows[2].SelectedCustomer); Assert.IsNull(rows[2].SelectedSupplier);
        Assert.AreEqual("C2", rows[0].CustomerCode); StringAssert.Contains(rows[0].CustomerDisplay, "客户一");
        f.Customers[0] = new CustomerDto { Uid = f.Customers[0].Uid, CustomerCode = "已更名", CustomerName = "新名称" };
        f.Suppliers[0] = new SupplierDto { Uid = f.Suppliers[0].Uid, SupplierCode = "已更名", SupplierName = "新供应商" };
        StringAssert.Contains(rows[0].CustomerDisplay, "新名称"); StringAssert.Contains(rows[0].SupplierDisplay, "新供应商");
        Assert.AreEqual("C2", rows[0].CustomerCode); Assert.IsFalse(f.Vm.HasUnsavedChanges);
        f.Vm.Dispose(); var selected = rows[0].SelectedCustomer;
        f.Customers[0] = new CustomerDto { Uid = selected!.Uid, CustomerCode = "后续", CustomerName = "关闭后" };
        Assert.AreSame(selected, rows[0].SelectedCustomer);
    });

    /// <summary>查看、新增、修改和删除使用独立权限，直接执行与混合变更不能绕过。</summary>
    [TestMethod]
    [DataRow("view")] [DataRow("create")] [DataRow("edit")] [DataRow("delete")]
    public void Permissions_SeparateNewExistingAndDeletion(string permission) => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Vm.InitializeAsync(); var vm = f.Vm; var persisted = vm.Lines.Items[0];
        f.Permissions(PermissionKeys.CustomersPoConfigView, permission switch
        {
            "create" => PermissionKeys.CustomersPoConfigCreate, "edit" => PermissionKeys.CustomersPoConfigEdit,
            "delete" => PermissionKeys.CustomersPoConfigDelete, _ => PermissionKeys.CustomersPoConfigView
        });
        Assert.AreEqual(permission == "create", vm.AddRowCommand.CanExecute(null));
        Assert.AreEqual(permission == "edit", vm.CanEditRow(persisted));
        Assert.AreEqual(permission == "delete", vm.DeleteSelectedCommand.CanExecute(null));
        vm.AddRowCommand.Execute(null);
        Assert.AreEqual(permission == "create" ? 4 : 3, vm.Lines.Items.Count);
        if (permission == "create")
        {
            var added = vm.SelectedLine!; Assert.IsTrue(vm.CanEditRow(added));
            added.CustomerCode = "新增客户"; added.SupplierCode = "新增供应商";
            Assert.IsTrue(vm.DeleteSelectedCommand.CanExecute(null)); await vm.SaveCommand.ExecuteAsync(null);
            Assert.HasCount(1, f.Saves); Assert.HasCount(1, f.Saves[0].CreateItems);
        }
        else if (permission == "edit")
        {
            persisted.Note = "修改"; await vm.SaveCommand.ExecuteAsync(null);
            Assert.HasCount(1, f.Saves); Assert.HasCount(1, f.Saves[0].UpdateItems);
        }
        else if (permission == "delete")
        {
            await vm.DeleteSelectedCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null);
            Assert.HasCount(1, f.Saves); Assert.HasCount(1, f.Saves[0].DeleteItems);
        }
        else
        {
            vm.ClearCustomerCommand.Execute(persisted); Assert.AreEqual("C2", persisted.CustomerCode);
            await vm.DeleteSelectedCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null);
            Assert.IsEmpty(f.Saves); Assert.HasCount(3, vm.Lines.Items);
        }
        // 创建权限不能提交既有行修改，修改权限也不能提交绕过 UI 混入的新增。
        if (permission == "create") vm.Lines.Items[0].Note = "无修改权限";
        else if (permission == "edit") { var row = vm.Lines.AddNewConfig(); row.CustomerCode = "C"; row.SupplierCode = "S"; }
        var saves = f.Saves.Count;
        if (permission is "create" or "edit") { Assert.IsFalse(vm.SaveCommand.CanExecute(null)); await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(saves, f.Saves.Count); }
    });

    /// <summary>确认期间撤权不能删除，发送后身份变化保留编辑；保存前先拒绝原生转换错误。</summary>
    [TestMethod]
    public void Writes_RecheckAfterConfirmationAndRetainDraftOnIdentityChange() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Vm; await vm.InitializeAsync();
        var confirmation = new TaskCompletionSource<bool>(); f.Confirmation = confirmation.Task;
        var target = vm.Lines.Items[0]; var deleting = vm.DeleteSelectedCommand.ExecuteAsync(null);
        f.Permissions(PermissionKeys.CustomersPoConfigView); confirmation.SetResult(true); await deleting;
        Assert.IsFalse(target.IsDeleted);
        f.AllPermissions(); target.Note = "本地草稿"; vm.CommitPendingEdits = () => false;
        await vm.SaveCommand.ExecuteAsync(null); Assert.IsEmpty(f.Saves);
        vm.CommitPendingEdits = () => true;
        var response = new TaskCompletionSource<ServiceResult<CustomerPoConfigBatchSaveResponse>>(); f.Save = _ => response.Task;
        var saving = vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsBusy); Assert.IsFalse(vm.CheckVisibleCommand.CanExecute(null));
        vm.CheckVisibleCommand.Execute(null); Assert.AreEqual(0, vm.Lines.CheckedCount);
        f.Permissions(PermissionKeys.CustomersPoConfigView); response.SetResult(Fixture.Receipt(f.Saves.Single())); await saving;
        Assert.AreEqual("本地草稿", target.Note); Assert.IsTrue(target.IsDirty); Assert.AreEqual(1, f.Requests.Count);
    });

    /// <summary>显示行范围排除隐藏和已删除行，格子模式禁选区动作，勾选不产生业务草稿。</summary>
    [TestMethod]
    public void CheckCommands_RespectVisibleOwnershipAndDisposal() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Vm; await vm.InitializeAsync(); var rows = vm.Lines.Items.ToArray();
        var view = CollectionViewSource.GetDefaultView(vm.Lines.Items); rows[1].IsChecked = true;
        view.Filter = item => !ReferenceEquals(item, rows[1]); vm.SetVisibleChecksCommand.Execute(false);
        vm.CheckSelectionCommand.Execute(new[] { rows[0], rows[1] }); Assert.IsFalse(rows[0].IsChecked);
        vm.CheckSelectionCommand.Execute(new object[] { rows[0], new CustomerPoConfigLineModel() }); Assert.IsFalse(rows[0].IsChecked);
        vm.CheckVisibleCommand.Execute(null); Assert.IsTrue(vm.IsAllVisibleChecked); Assert.IsTrue(rows[1].IsChecked);
        vm.IsCellSelectionMode = true;
        foreach (var command in new[] { vm.CheckSelectionCommand, vm.UncheckSelectionCommand, vm.InvertSelectionCommand })
        { Assert.IsFalse(command.CanExecute(rows)); command.Execute(rows); }
        Assert.IsTrue(rows[0].IsChecked); Assert.IsFalse(vm.DeleteSelectedCommand.CanExecute(null));
        vm.InvertVisibleCommand.Execute(null); Assert.IsFalse(rows[0].IsChecked); Assert.IsTrue(rows[1].IsChecked);
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.AreEqual(803, vm.TotalCount);
        vm.IsCellSelectionMode = false; rows[2].MarkAsDeleted();
        vm.CheckSelectionCommand.Execute(new[] { rows[0], rows[2] }); Assert.IsFalse(rows[0].IsChecked);
        vm.SetVisibleChecksCommand.Execute(true); Assert.IsTrue(rows[0].IsChecked); Assert.IsFalse(rows[2].IsChecked);
        var count = 0; vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.IsAllVisibleChecked)) count++; };
        vm.Dispose(); var previous = count; view.Filter = null; view.Refresh(); rows[0].IsChecked = false;
        Assert.AreEqual(previous, count); vm.CheckVisibleCommand.Execute(null); Assert.IsFalse(rows[0].IsChecked);
    });

    /// <summary>原生菜单、行复选框、表头与两个模式入口同步，筛选取消和撤权保留草稿。</summary>
    [TestMethod]
    public void ActualPage_MenuFiltersHeaderModeAndPermissions() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); var vm = f.Vm; await vm.InitializeAsync();
        var view = new CustomerPoConfigManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1500, Height = 800, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName("ConfigGrid"); var rows = vm.Lines.Items.ToArray();
            Assert.AreEqual(1, Interaction.GetBehaviors(grid).OfType<DataGridColumnVisibilityBehavior>().Count());
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(rows[0]); grid.SelectedItems.Add(rows[2]);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background);
            var menu = grid.ContextMenu; var items = menu.Items.OfType<MenuItem>().ToArray();
            CollectionAssert.AreEqual(new[] { "查看", "勾选(选区)", "取消勾选(选区)", "反选(选区)", "全选(当前显示行)", "反选(当前显示行)", "新增", "刷新", "删除", "表格模式" },
                items.Select(item => (string)item.Header).ToArray());
            Assert.AreSame(grid.SelectedItems, items[1].CommandParameter);
            await InvokeMenu(items[1], menu); Assert.AreEqual(2, vm.Lines.CheckedCount);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[2], menu); Assert.AreEqual(0, vm.Lines.CheckedCount);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[3], menu); Assert.AreEqual(2, vm.Lines.CheckedCount);
            var header = Descendants(grid).OfType<CheckBox>().Single(box => ReferenceEquals(box.Command, vm.SetVisibleChecksCommand));
            await Space(header); Assert.AreEqual(3, vm.Lines.CheckedCount);
            await Space(header); Assert.AreEqual(0, vm.Lines.CheckedCount);
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(rows[0]);
            var rowCheck = Descendants(row).OfType<CheckBox>().Single(box => BindingOperations.GetBinding(box, CheckBox.IsCheckedProperty)?.Path.Path == "IsChecked");
            await Space(rowCheck); Assert.IsTrue(rows[0].IsChecked); Assert.IsFalse(vm.HasUnsavedChanges);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[^1], menu);
            Assert.AreEqual(DataGridSelectionUnit.CellOrRowHeader, grid.SelectionUnit); Assert.IsTrue(vm.IsCellSelectionMode);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsTrue(items.Skip(1).Take(3).All(item => !item.IsEnabled)); Assert.IsFalse(items[8].IsEnabled);
            menu.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.Background);
            var toggle = Descendants(view).OfType<global::Wpf.Ui.Controls.ToggleSwitch>().Single(); await Space(toggle);
            Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit); Assert.IsFalse(vm.IsCellSelectionMode);
            rows[0].Note = "保留"; f.Confirm = false;
            var customer = (CustomerAutoSuggestComboBox)view.FindName("CustomerFilter"); customer.SelectedCustomer = f.Customers[1];
            var query = Descendants(view).OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.SearchCommand));
            await InvokeButton(query); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1, f.Requests.Count); Assert.IsNull(vm.FilterCustomer); Assert.IsNull(customer.SelectedCustomer); Assert.AreEqual("保留", rows[0].Note);
            f.Permissions(PermissionKeys.CustomersPoConfigView);
            Assert.IsTrue(grid.IsReadOnly); OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsFalse(items[6].IsEnabled); Assert.IsFalse(items[8].IsEnabled); menu.IsOpen = false;
            window.Content = null; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsNull(vm.CommitPendingEdits); Assert.AreEqual(9, menu.Items.OfType<MenuItem>().Count());
            window.Content = view; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1, menu.Items.OfType<MenuItem>().Count(item => Equals(item.Header, "查看")));
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>真实编辑器按行权限拦截并显示六位精度，转换失败阻断保存，ID 回显不改旧编码。</summary>
    [TestMethod]
    public void ActualPage_EditorsRespectPermissionsAndDiscountPrecision() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); var vm = f.Vm; await vm.InitializeAsync();
        var view = new CustomerPoConfigManagerView { DataContext = vm }; var window = new Window { Content = view, Width = 1500, Height = 800, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName("ConfigGrid"); var row = vm.Lines.Items[0];
            Assert.AreEqual((double)view.FindResource("DataGridRowHeightSmall"), grid.RowHeight);
            var discount = grid.Columns.OfType<DataGridTextColumn>().Single(column => ((Binding)column.Binding).Path.Path == nameof(row.DefaultDiscountRate));
            Assert.AreEqual("折扣率", discount.Header); Assert.AreEqual("0.######", ((Binding)discount.Binding).StringFormat);
            var currency = grid.Columns.Single(column => Equals(column.Header, "币种"));
            grid.ScrollIntoView(row, currency); grid.UpdateLayout();
            Assert.AreSame(view.FindResource("SwiftExportDataGridTextBlock"), Descendants(currency.GetCellContent(row)).OfType<TextBlock>().Single().Style);
            grid.ScrollIntoView(row, discount); grid.UpdateLayout();
            Assert.AreEqual("0.123456", ((TextBlock)discount.GetCellContent(row)).Text);
            f.Permissions(PermissionKeys.CustomersPoConfigView, PermissionKeys.CustomersPoConfigCreate);
            grid.CurrentCell = new(row, discount); Assert.IsFalse(grid.BeginEdit());
            f.Permissions(PermissionKeys.CustomersPoConfigView, PermissionKeys.CustomersPoConfigEdit);
            Assert.IsTrue(grid.BeginEdit()); await Dispatcher.Yield(DispatcherPriority.DataBind);
            var editor = (TextBox)discount.GetCellContent(row); editor.Text = "1000"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(Validation.GetHasError(editor)); await vm.SaveCommand.ExecuteAsync(null); Assert.IsEmpty(f.Saves);
            editor.Text = "0.000001"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(vm.CommitPendingEdits!()); Assert.AreEqual(0.000001m, row.DefaultDiscountRate);
            vm.RejectListChangesCommand.Execute(null); Assert.IsFalse(vm.HasUnsavedChanges);
            var customerColumn = grid.Columns.Single(column => Equals(column.Header, "客户")); grid.CurrentCell = new(row, customerColumn);
            Assert.IsTrue(grid.BeginEdit()); await Dispatcher.Yield(DispatcherPriority.DataBind);
            var customer = Descendants(customerColumn.GetCellContent(row)).OfType<CustomerAutoSuggestComboBox>().Single();
            Assert.AreSame(f.Customers[0], customer.SelectedCustomer); Assert.AreEqual("C2", row.CustomerCode); Assert.IsFalse(vm.HasUnsavedChanges);
            grid.CancelEdit(DataGridEditingUnit.Cell); grid.CancelEdit(DataGridEditingUnit.Row);
            var supplierColumn = grid.Columns.Single(column => Equals(column.Header, "供应商")); grid.CurrentCell = new(row, supplierColumn);
            Assert.IsTrue(grid.BeginEdit()); await Dispatcher.Yield(DispatcherPriority.DataBind);
            var supplier = Descendants(supplierColumn.GetCellContent(row)).OfType<SupplierAutoSuggestComboBox>().Single();
            Assert.AreSame(f.Suppliers[0], supplier.SelectedSupplier); Assert.AreEqual("S2", row.SupplierCode); Assert.IsFalse(vm.HasUnsavedChanges);
            f.Permissions(PermissionKeys.CustomersPoConfigView);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsTrue(grid.IsReadOnly); Assert.IsFalse(((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(row)).IsEditing);
            Assert.IsFalse(vm.HasUnsavedChanges);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>模型与原始文本均拒绝数据库范围外及超过六位精度，允许空值和大于一的倍率。</summary>
    [TestMethod]
    [DataRow("zh-CN", "999.999999")] [DataRow("fr-FR", "999,999999")]
    public void DiscountValidation_MatchesNumericRangeAndCulture(string name, string maximum)
    {
        var culture = CultureInfo.GetCultureInfo(name); var rule = new DiscountRateValidationRule();
        Assert.IsTrue(rule.Validate(maximum, culture).IsValid); Assert.IsTrue(rule.Validate("", culture).IsValid);
        Assert.IsFalse(rule.Validate("1000", culture).IsValid);
        Assert.IsFalse(rule.Validate(culture.Name == "fr-FR" ? "0,1234567" : "0.1234567", culture).IsValid);
        var row = new CustomerPoConfigLineModel { CustomerCode = "C", SupplierCode = "S", DefaultDiscountRate = 999.999999m };
        Assert.IsNull(row.ValidateRow()); row.DefaultDiscountRate = 1000; StringAssert.Contains(row.ValidateRow(), "999.999999");
        row.DefaultDiscountRate = 1.05m; Assert.IsNull(row.ValidateRow()); row.DefaultDiscountRate = null; Assert.IsNull(row.ValidateRow());
    }

    /// <summary>遍历真实视觉树，仅供定位当前呈现控件。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child; }
    /// <summary>通过原生菜单键建立当前表格的菜单上下文。</summary>
    private static void OpenMenu(DataGrid grid)
    { Assert.IsTrue(grid.Focus()); InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, 0, Key.Apps) { RoutedEvent = Keyboard.KeyUpEvent }); }
    /// <summary>经菜单自动化入口触发真实命令并等待弹出层关闭。</summary>
    private static async Task InvokeMenu(MenuItem item, ContextMenu menu)
    {
        Assert.IsTrue(item.IsEnabled); var closed = new TaskCompletionSource(); RoutedEventHandler handler = (_, _) => closed.TrySetResult(); menu.Closed += handler;
        try { ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { menu.Closed -= handler; }
    }
    /// <summary>经按钮自动化入口执行用户可见查询。</summary>
    private static async Task InvokeButton(Button button)
    { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    /// <summary>用实际控件空格输入验证复选框及模式开关。</summary>
    private static async Task Space(Control control)
    {
        Assert.IsTrue(control.Focus()); foreach (var routed in new[] { Keyboard.KeyDownEvent, Keyboard.KeyUpEvent })
            control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control)!, 0, Key.Space) { RoutedEvent = routed });
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    /// <summary>可替换查询、保存与确认时序的离线夹具；列表和权限服务均为真实实现。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly ObservableCollection<CustomerDto> Customers = [new() { Uid = Guid.NewGuid(), CustomerCode = "C1", CustomerName = "客户一" }, new() { Uid = Guid.NewGuid(), CustomerCode = "C2", CustomerName = "客户二" }];
        public readonly ObservableCollection<SupplierDto> Suppliers = [new() { Uid = Guid.NewGuid(), SupplierCode = "S1", SupplierName = "供应商一" }, new() { Uid = Guid.NewGuid(), SupplierCode = "S2", SupplierName = "供应商二" }];
        public readonly CurrentUserService User = new();
        public readonly CustomerPoConfigManagerViewModel Vm;
        public readonly List<CustomerPoConfigSearchRequest> Requests = [];
        public readonly List<CancellationToken> Tokens = [];
        public readonly List<CustomerPoConfigBatchSaveRequest> Saves = [];
        public readonly List<string> CacheRefreshes = [];
        public Func<CustomerPoConfigSearchRequest, Task<ServiceResult<PagedResponseBase<CustomerPoConfigDto>>>>? Read;
        public Func<CustomerPoConfigBatchSaveRequest, Task<ServiceResult<CustomerPoConfigBatchSaveResponse>>>? Save;
        public bool Confirm = true, FailRead;
        public Task<bool>? Confirmation;
        public Task<CacheRefreshResult>? EnsureTask;
        private readonly Guid[] _ids = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        public Fixture()
        {
            AllPermissions();
            var cacheBase = new ReferenceDataCacheServiceStub();
            var customerView = new ReadOnlyObservableCollection<CustomerDto>(Customers); var supplierView = new ReadOnlyObservableCollection<SupplierDto>(Suppliers);
            var cache = Proxy<IReferenceDataCacheService>((method, _) => method.Name switch
            {
                "get_Customers" => customerView, "get_Suppliers" => supplierView, "get_Currencies" => cacheBase.Currencies,
                nameof(IReferenceDataCacheService.EnsureLoadedAsync) => EnsureTask ?? cacheBase.EnsureLoadedAsync(),
                _ when method.Name.StartsWith("Refresh", StringComparison.Ordinal) => Refresh(method.Name, cacheBase),
                _ => throw new AssertFailedException(method.Name)
            });
            var api = Proxy<ICustomerPoConfigApi>((method, args) =>
            {
                if (method.Name == nameof(ICustomerPoConfigApi.GetPagedAsync))
                {
                    var request = (CustomerPoConfigSearchRequest)args[0]!; Requests.Add(request); Tokens.Add((CancellationToken)args[1]!);
                    return Read?.Invoke(request) ?? Task.FromResult(FailRead ? ServiceResult<PagedResponseBase<CustomerPoConfigDto>>.Fail("测试查询失败") : Page(request));
                }
                Assert.AreEqual(nameof(ICustomerPoConfigApi.BatchSaveAsync), method.Name);
                var save = (CustomerPoConfigBatchSaveRequest)args[0]!; Saves.Add(save); return Save?.Invoke(save) ?? Task.FromResult(Receipt(save));
            });
            var runner = Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
            var dialogs = Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ConfirmAsync) ? Confirmation ?? Task.FromResult(Confirm) : Task.CompletedTask);
            Vm = new(api, runner, cache, Proxy<IMessageService>((_, _) => null), dialogs, User);
        }

        /// <summary>三个样本分别覆盖ID优先、旧编码回退、失效ID禁止回退。</summary>
        public ServiceResult<PagedResponseBase<CustomerPoConfigDto>> Page(CustomerPoConfigSearchRequest request, string note = "") => ServiceResult<PagedResponseBase<CustomerPoConfigDto>>.Ok(new()
        {
            PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 803,
            Items = Enumerable.Range(0, 3).Select(index => new CustomerPoConfigDto { Uid = _ids[index], Version = 1,
                CustomerId = index == 0 ? Customers[0].Uid : index == 1 ? null : Guid.NewGuid(), CustomerCode = index == 1 ? "C1" : "C2",
                SupplierId = index == 0 ? Suppliers[0].Uid : index == 1 ? null : Guid.NewGuid(), SupplierCode = index == 0 ? "S2" : "S1",
                Currency = "CNY", DefaultDiscountRate = 0.123456m, Note = note }).ToList()
        });

        /// <summary>替换真实当前用户快照，触发生产权限变更通知。</summary>
        public void Permissions(params string[] keys) => User.SetCurrentUser(new MeDto { Permissions = keys.ToList() });
        /// <summary>授予本页四项权限，便于测试单独撤权边界。</summary>
        public void AllPermissions() => Permissions(PermissionKeys.CustomersPoConfigView, PermissionKeys.CustomersPoConfigCreate, PermissionKeys.CustomersPoConfigEdit, PermissionKeys.CustomersPoConfigDelete);
        private Task<CacheRefreshResult> Refresh(string name, ReferenceDataCacheServiceStub cache) { CacheRefreshes.Add(name); return cache.EnsureLoadedAsync(); }
        /// <summary>按真实 ClientKey 和操作类型返回版本，保持原批量结果接纳路径。</summary>
        public static ServiceResult<CustomerPoConfigBatchSaveResponse> Receipt(CustomerPoConfigBatchSaveRequest request) => ServiceResult<CustomerPoConfigBatchSaveResponse>.Ok(new()
        {
            Items = request.CreateItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, Uid = Guid.NewGuid(), NewVersion = 1, Operation = SaveOperation.Create, Success = true })
                .Concat(request.UpdateItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, Uid = item.Uid, NewVersion = item.Version + 1, Operation = SaveOperation.Update, Success = true }))
                .Concat(request.DeleteItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, Uid = item.Uid, Operation = SaveOperation.Delete, Success = true })).ToList()
        });
        /// <summary>释放生产 VM 及其全部页面级订阅。</summary>
        public void Dispose() => Vm.Dispose();
    }

    /// <summary>复用既有代理类型，只替代本用例明确需要的外部服务。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    { var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>(); ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call; return proxy; }
}
