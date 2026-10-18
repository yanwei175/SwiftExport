using DataGridExtensions;
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xaml.Behaviors;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Suppliers.SupplierManagement.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Views;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Features.Suppliers.SupplierProducts.Views;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Behaviors;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Resources.Commands;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

/// <summary>供应商双管理页的真实菜单、全量筛选、单VM上下文和权限生命周期验收。</summary>
[TestClass, DoNotParallelize]
public sealed class SupplierPageInteractionTests
{
    /// <summary>管理页使用完整快照；关闭可取消在途读取，忽略取消的响应不能回写已释放页面。</summary>
    [TestMethod]
    public void Manager_UsesFullSnapshotAndDiscardsDisposedRead() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Manager.InitializeAsync();
        Assert.AreEqual(35, f.Manager.Suppliers.Items.Count); Assert.AreEqual(1, f.AllReads); Assert.AreEqual(0, f.SupplierPageReads);
        var pending = new TaskCompletionSource<ServiceResult<IReadOnlyList<SupplierDto>>>();
        f.All = _ => pending.Task; var refresh = f.Manager.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(await f.Manager.CanCloseAsync(default)); f.Manager.Dispose(); Assert.IsTrue(f.AllToken.IsCancellationRequested);
        pending.SetResult(ServiceResult<IReadOnlyList<SupplierDto>>.Ok([])); await refresh;
        Assert.AreEqual(35, f.Manager.Suppliers.Items.Count);
    });

    /// <summary>切换供应商在等待前清空旧行，旧响应和旧候选均不能进入新上下文。</summary>
    [TestMethod]
    public void ProductSwitch_CancelsOldPageAndLookup() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); var vm = f.Products;
        var first = new TaskCompletionSource<ServiceResult<PagedResponseBase<SupplierProductDto>>>();
        var second = new TaskCompletionSource<ServiceResult<PagedResponseBase<SupplierProductDto>>>();
        f.Read = (request, _) => request.SupplierId == f.Options[0].Uid ? first.Task : second.Task;
        vm.SelectedSupplier = f.Options[0]; var a = vm.CommitSupplierCommand.ExecuteAsync(null); var oldToken = f.PageTokens[0];
        vm.SelectedSupplier = f.Options[1]; var b = vm.CommitSupplierCommand.ExecuteAsync(null);
        Assert.IsTrue(oldToken.IsCancellationRequested); Assert.IsEmpty(vm.Products.Items); Assert.AreEqual(f.Options[1].Uid, vm.ActiveSupplier!.Uid);
        second.SetResult(f.Page(f.Requests[^1])); await b; first.SetResult(f.Page(f.Requests[0])); await a;
        Assert.IsTrue(vm.Products.Items.All(row => row.SupplierId == f.Options[1].Uid));
        var lookup = new TaskCompletionSource<ServiceResult<List<ProductLookupDto>>>(); f.Lookup = _ => lookup.Task;
        var searching = vm.SearchProductsCommand.ExecuteAsync("旧"); var lookupToken = f.LookupToken;
        vm.SelectedSupplier = f.Options[2]; f.Read = (request, _) => Task.FromResult(f.Page(request));
        await vm.CommitSupplierCommand.ExecuteAsync(null); Assert.IsTrue(lookupToken.IsCancellationRequested);
        lookup.SetResult(ServiceResult<List<ProductLookupDto>>.Ok([new() { Uid = Guid.NewGuid(), Sku = "不应回写" }])); await searching;
        Assert.IsEmpty(vm.ProductCandidates);
        f.User.SetCurrentUser(new MeDto { Permissions = [] });
        var count = f.LookupReads; await vm.SearchProductsCommand.ExecuteAsync("撤权");
        Assert.AreEqual(count, f.LookupReads);
    });

    /// <summary>确认期间所有编辑、写入和关闭冻结，确认返回后撤权不能删除、切换或设置默认供应商。</summary>
    [TestMethod]
    [DataRow("delete")] [DataRow("default")] [DataRow("switch")]
    public void ProductConfirmation_FreezesAndRechecksIdentity(string action) => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.OpenProducts(); var vm = f.Products; var row = vm.Products.Items[0];
        if (action == "switch") row.SupplierSkuDescriptionZh = "保留草稿";
        var prompt = new TaskCompletionSource<bool>(); f.Confirmation = prompt.Task;
        Task task = action switch
        {
            "delete" => vm.DeleteSelectedCommand.ExecuteAsync(null), "default" => vm.SetProductDefaultSupplierCommand.ExecuteAsync(null),
            _ => vm.SelectSupplierAsync(f.Supplier(1))
        };
        Assert.IsTrue(vm.IsPrompting); Assert.IsFalse(vm.CanChangeSupplier); Assert.IsFalse(vm.AddItemCommand.CanExecute(null));
        Assert.IsFalse(vm.SaveCommand.CanExecute(null)); Assert.IsFalse(await vm.CanCloseAsync(default));
        var reads = f.LookupReads; await vm.SearchProductsCommand.ExecuteAsync("确认期间"); Assert.AreEqual(reads, f.LookupReads);
        await vm.SaveCommand.ExecuteAsync(null); vm.AddItemCommand.Execute(null);
        f.User.SetCurrentUser(new MeDto { Permissions = [] }); prompt.SetResult(true); await task;
        Assert.AreEqual(f.Options[0].Uid, vm.ActiveSupplier!.Uid); Assert.IsFalse(row.IsDeleted);
        Assert.AreEqual(0, f.DefaultWrites); Assert.AreEqual(0, f.ProductSaves);
        if (action == "switch") { Assert.AreEqual("保留草稿", row.SupplierSkuDescriptionZh); Assert.IsTrue(vm.HasUnsavedChanges); }
    });

    /// <summary>两页撤权后可按草稿确认关闭；取消不丢数据，无草稿时直接离开。</summary>
    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public void RevokedPages_CanCloseWithDraftConfirmation(bool product, bool dirty) => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Manager.InitializeAsync(); await f.OpenProducts();
        if (dirty) { if (product) f.Products.Products.Items[0].SupplierSkuDescriptionZh = "草稿"; else f.Manager.Suppliers.Items[0].SupplierName = "草稿"; }
        f.User.SetCurrentUser(new MeDto { Permissions = [] }); f.Confirm = false;
        Assert.AreEqual(!dirty, product ? await f.Products.CanCloseAsync(default) : await f.Manager.CanCloseAsync(default));
        if (dirty)
        {
            Assert.IsTrue(product ? f.Products.HasUnsavedChanges : f.Manager.HasUnsavedChanges);
            f.Confirm = true; Assert.IsTrue(product ? await f.Products.CanCloseAsync(default) : await f.Manager.CanCloseAsync(default));
        }
    });

    /// <summary>供应商产品分页只接纳成功页码与已应用关键词，搜索、清空、刷新及关闭均保护草稿。</summary>
    [TestMethod]
    public void ProductDraft_GuardsAllReloadsAndKeepsPageOwnership() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.OpenProducts(); var vm = f.Products; var row = vm.Products.Items[0];
        vm.Keyword = "已应用"; await vm.SearchCommand.ExecuteAsync(null); row = vm.Products.Items[0]; row.SupplierSku = "草稿";
        var count = f.Requests.Count; f.Confirm = false; vm.Keyword = "取消条件";
        await vm.SearchCommand.ExecuteAsync(null); await vm.RefreshCommand.ExecuteAsync(null);
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 50 });
        await vm.ClearSupplierCommand.ExecuteAsync(null); Assert.IsFalse(await vm.CanCloseAsync(default));
        Assert.AreSame(row, vm.Products.Items[0]); Assert.AreEqual("草稿", row.SupplierSku); Assert.AreEqual(count, f.Requests.Count); Assert.AreEqual("已应用", vm.Keyword);
        f.Confirm = true; f.FailPage = true;
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 50 });
        Assert.AreEqual(1, vm.PageIndex); Assert.AreSame(row, vm.Products.Items[0]); Assert.AreEqual("已应用", f.Requests[^1].Keyword);
        f.FailPage = false; vm.Keyword = "未提交"; await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 3, NewSize = 50 });
        Assert.AreEqual(3, vm.PageIndex); Assert.AreEqual(50, vm.PageSize); Assert.AreEqual("已应用", vm.Keyword);
        vm.CommitPendingEdits = () => false; var reads = f.Requests.Count;
        await vm.RefreshCommand.ExecuteAsync(null); await vm.ClearSupplierCommand.ExecuteAsync(null); Assert.IsFalse(await vm.CanCloseAsync(default));
        Assert.AreEqual(reads, f.Requests.Count);
    });

    /// <summary>实际共享弹窗覆盖文本、多选、全选和跨列组合；隐藏草稿保存，勾选仅作用于显示行。</summary>
    [TestMethod]
    public void ActualManager_PopupFiltersKeepHiddenDraftAndSaveAllRows() => WithManagerGrid(async (f, view, grid) =>
    {
        var vm = f.Manager;
        Assert.IsNull(view.FindName("SearchTextBox")); Assert.IsNull(view.FindName("SupplierSearchGrid"));
        Assert.AreEqual(14, grid.Columns.Count);
        Assert.IsFalse(grid.Columns[0].GetIsFilterVisible());
        foreach (var column in grid.Columns.Skip(1))
        {
            grid.ScrollIntoView(vm.Suppliers.Items[0], column); grid.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreSame(view.FindResource("SwiftExportColumnFilter"), column.GetTemplate(), column.Header.ToString());
            Assert.AreEqual(1, Descendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Count(), column.Header.ToString());
        }
        var hidden = vm.Suppliers.Items[0]; hidden.SupplierName = "隐藏草稿";
        var code = grid.Columns.Single(column => Equals(column.Header, "供应商代码"));
        var (filter, popup) = await OpenFilter(grid, code);
        Assert.IsTrue(popup.IsOpen); Assert.IsTrue(popup.AllowsTransparency);
        var input = (TextBox)filter.Template.FindName("SearchInput", filter);
        var choices = (ListBox)filter.Template.FindName("PART_ListBox", filter);
        var all = (ToggleButton)filter.Template.FindName("SelectAll", filter);
        Assert.IsTrue(input.IsKeyboardFocusWithin); Assert.AreEqual(35, choices.Items.Count);
        input.Text = "S03"; await Settle(); await Settle();
        Assert.AreEqual(6, grid.Items.Count); Assert.AreEqual(35, vm.Suppliers.Items.Count);
        input.Text = string.Empty; await Settle(); await Settle(); Assert.AreEqual(35, grid.Items.Count);
        choices.UnselectAll(); choices.SelectedItems.Add("S031"); choices.SelectedItems.Add("S033"); await Settle(); await Settle();
        CollectionAssert.AreEquivalent(new[] { "S031", "S033" }, grid.Items.Cast<SupplierLineModel>().Select(row => row.SupplierCode).ToArray());
        Assert.IsNull(all.IsChecked);
        Toggle(all); await Settle(); Assert.AreEqual(0, choices.SelectedItems.Count); Assert.AreEqual(0, grid.Items.Count);
        Toggle(all); await Settle(); Assert.AreEqual(35, choices.SelectedItems.Count); Assert.AreEqual(35, grid.Items.Count);
        choices.UnselectAll(); foreach (var value in new[] { "S030", "S031", "S033" }) choices.SelectedItems.Add(value);
        await Settle(); filter.IsPopupOpen = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var (nameFilter, _) = await OpenFilter(grid, grid.Columns.Single(column => Equals(column.Header, "供应商名称")));
        var names = (ListBox)nameFilter.Template.FindName("PART_ListBox", nameFilter);
        names.UnselectAll(); names.SelectedItems.Add("供应商31"); await Settle(); await Settle();
        Assert.AreEqual(1, grid.Items.Count); Assert.AreEqual("S031", ((SupplierLineModel)grid.Items[0]).SupplierCode);
        nameFilter.IsPopupOpen = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        vm.CheckVisibleCommand.Execute(null); Assert.AreEqual(1, vm.Suppliers.CheckedCount); Assert.IsFalse(hidden.IsChecked);
        Assert.IsTrue(vm.IsAllVisibleChecked);
        await vm.SaveCommand.ExecuteAsync(null); Assert.IsNotNull(f.SupplierSave);
        Assert.AreEqual(hidden.Uid, f.SupplierSave.UpdateItems.Single().Uid); Assert.AreEqual("隐藏草稿", f.SupplierSave.UpdateItems.Single().Data.SupplierName);
        hidden.SupplierName = "仍保留";
        var (clearFilter, _) = await OpenFilter(grid, code);
        var clear = (Button)clearFilter.Template.FindName("ClearAllFilters", clearFilter);
        Assert.AreSame(DataGridFilterCommands.Clear, clear.Command); Assert.AreSame(grid, clear.CommandParameter);
        await Click(clear); await Settle();
        Assert.AreEqual(35, grid.Items.Count);
        Assert.IsTrue(grid.GetFilter().FilterColumnControls.SelectMany(Descendants).OfType<MultipleChoiceFilter>().All(control => (control.Filter as MultipleChoiceContentFilter)?.IsFiltered != true));
        Assert.AreEqual(1, vm.Suppliers.CheckedCount); Assert.AreEqual("仍保留", hidden.SupplierName); Assert.IsTrue(vm.HasUnsavedChanges);
        clearFilter.IsPopupOpen = false; f.Confirm = false; await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual(1, f.AllReads); Assert.AreEqual("仍保留", hidden.SupplierName);
    });

    /// <summary>真实弹窗清除命令保留无效数字和已有条件；修正后提交输入并清空所有列条件。</summary>
    [TestMethod]
    public void ActualManager_PopupClearPreservesInvalidEdit() => WithManagerGrid(async (f, view, grid) =>
    {
        var row = f.Manager.Suppliers.Items[0];
        var code = grid.Columns.Single(column => Equals(column.Header, "供应商代码"));
        var (filter, _) = await OpenFilter(grid, code);
        ((TextBox)filter.Template.FindName("SearchInput", filter)).Text = "^S001$";
        await Settle(); await Settle(); Assert.AreEqual(1, grid.Items.Count);
        filter.IsPopupOpen = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var number = grid.Columns.Single(column => Equals(column.Header, "默认交期"));
        grid.ScrollIntoView(row, number); grid.CurrentCell = new DataGridCellInfo(row, number);
        Assert.IsTrue(grid.BeginEdit()); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var editor = (TextBox)Descendants(number.GetCellContent(row)).OfType<TextBox>().Single();
        editor.Text = "不是整数"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.IsTrue(Validation.GetHasError(editor));
        var condition = code.GetFilter();
        (filter, _) = await OpenFilter(grid, code);
        var clear = (Button)filter.Template.FindName("ClearAllFilters", filter);
        await Click(clear); await Settle();
        Assert.AreSame(condition, code.GetFilter()); Assert.AreEqual(1, grid.Items.Count);
        Assert.AreEqual("不是整数", editor.Text); Assert.IsTrue(Validation.GetHasError(editor)); Assert.IsNull(row.DefaultLeadTimeDays);
        editor.Text = "12"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        await Click(clear); await Settle();
        Assert.AreEqual(35, grid.Items.Count); Assert.AreEqual(12, row.DefaultLeadTimeDays); Assert.IsTrue(f.Manager.HasUnsavedChanges);
        Assert.IsFalse((filter.Filter as MultipleChoiceContentFilter)?.IsFiltered == true); filter.IsPopupOpen = false;
    });

    /// <summary>币种经原生编辑才可选择代码，候选显示双语；地址使用客户页同款换行样式，所有状态门禁继续有效。</summary>
    [TestMethod]
    public void ActualManager_CurrencyAndAddressRespectEditPermissions() => WithManagerGrid(async (f, view, grid) =>
    {
        var vm = f.Manager; var row = vm.Suppliers.Items[0]; var full = f.User.CurrentUser!;
        var currency = (DataGridTemplateColumn)grid.Columns.Single(column => Equals(column.Header, "币种"));
        Assert.AreEqual("Currency", currency.SortMemberPath);
        Assert.AreEqual("Currency", ((Binding)currency.ClipboardContentBinding).Path.Path);
        Assert.AreEqual(string.Empty, vm.CurrencyOptions[0].CurrencyCode); Assert.AreEqual("（空）", vm.CurrencyOptions[0].CurrencyNameZh);
        Assert.AreEqual(2, f.CurrencyCache.Count);
        f.User.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.SuppliersSupplierView] });
        Assert.IsFalse(await BeginCellEdit(grid, row, currency)); Assert.IsEmpty(Descendants(currency.GetCellContent(row)).OfType<ComboBox>());
        f.User.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.SuppliersSupplierView, PermissionKeys.SuppliersSupplierCreate] });
        Assert.IsFalse(await BeginCellEdit(grid, row, currency)); Assert.AreEqual("USD", row.Currency);
        f.User.SetCurrentUser(full);
        Assert.IsTrue(await BeginCellEdit(grid, row, currency));
        var combo = Descendants(currency.GetCellContent(row)).OfType<ComboBox>().Single();
        Assert.AreSame(vm.CurrencyOptions, combo.ItemsSource); Assert.AreEqual("CurrencyCode", combo.SelectedValuePath); Assert.AreEqual("USD", combo.SelectedValue);
        combo.IsDropDownOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var cny = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromItem(vm.CurrencyOptions.Single(item => item.CurrencyCode == "CNY"));
        var labels = Descendants(cny).OfType<TextBlock>().Select(text => text.Text).ToArray();
        CollectionAssert.Contains(labels, "CNY"); CollectionAssert.Contains(labels, "人民币");
        await SelectCurrency(combo, Key.End, Key.Enter);
        Assert.AreEqual("CNY", combo.SelectedValue);
        Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true)); Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.AreEqual("CNY", row.Currency); Assert.IsTrue(Descendants(currency.GetCellContent(row)).OfType<TextBlock>().Any(text => text.Text == "CNY"));
        var address = (DataGridTextColumn)grid.Columns.Single(column => Equals(column.Header, "地址"));
        Assert.AreSame(view.FindResource("SwiftExportDataGridWarpTextBlock"), address.ElementStyle);
        Assert.AreSame(view.FindResource("SwiftExportDataGridWarpTextBox"), address.EditingElementStyle);
        Assert.IsTrue(await BeginCellEdit(grid, row, address));
        var addressEditor = Descendants(address.GetCellContent(row)).OfType<TextBox>().Single();
        Assert.AreEqual(TextWrapping.Wrap, addressEditor.TextWrapping); addressEditor.Text = "第一行\n第二行";
        addressEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true)); Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
        var saving = new TaskCompletionSource<ServiceResult<SupplierBatchSaveResponse>>(); f.SaveResponse = saving.Task;
        var save = vm.SaveCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsBusy);
        Assert.IsFalse(await BeginCellEdit(grid, row, currency)); Assert.AreEqual("CNY", row.Currency);
        saving.SetResult(ServiceResult<SupplierBatchSaveResponse>.Fail("离线模拟保存失败")); await save;
        Assert.AreEqual("CNY", row.Currency); Assert.AreEqual("第一行\n第二行", row.Address);
        var confirmation = new TaskCompletionSource<bool>(); f.Confirmation = confirmation.Task; vm.SelectedLine = row;
        var deleting = vm.DeleteSelectedCommand.ExecuteAsync(null); Assert.IsTrue(vm.IsSelectionPrompt);
        Assert.IsFalse(await BeginCellEdit(grid, row, currency)); confirmation.SetResult(false); await deleting;
        Assert.AreEqual("CNY", row.Currency); Assert.IsTrue(vm.HasUnsavedChanges);
        f.User.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.SuppliersSupplierView, PermissionKeys.SuppliersSupplierCreate] });
        vm.AddRowCommand.Execute(null); var added = vm.SelectedLine!;
        Assert.IsTrue(await BeginCellEdit(grid, added, currency));
        var newCombo = Descendants(currency.GetCellContent(added)).OfType<ComboBox>().Single();
        await SelectCurrency(newCombo, Key.Home, Key.Down, Key.Enter); Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true)); Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
        Assert.AreEqual("USD", added.Currency); Assert.AreEqual("CNY", row.Currency);
    });

    /// <summary>缓存变化刷新币种选项且不污染缓存；释放后不再接纳选项更新，取消初始化也不继续读取业务资料。</summary>
    [TestMethod]
    public void Manager_CurrencyCacheAndInitializationRespectLifetime() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Manager.InitializeAsync();
        f.CurrencyCache.Add(new SysDicCurrencyDto { CurrencyCode = "EUR", CurrencyNameZh = "欧元" });
        Assert.AreEqual(4, f.Manager.CurrencyOptions.Count); Assert.AreEqual(3, f.CurrencyCache.Count);
        var options = f.Manager.CurrencyOptions; f.Manager.Dispose(); f.CurrencyCache.Clear();
        Assert.AreSame(options, f.Manager.CurrencyOptions);
        using var waiting = new Fixture(); var pending = new TaskCompletionSource<CacheRefreshResult>(); waiting.Ensure = pending.Task;
        var loading = waiting.Manager.InitializeAsync(); Assert.IsTrue(waiting.Manager.IsQuerying);
        waiting.Manager.Dispose(); await loading.WaitAsync(TimeSpan.FromSeconds(3)); Assert.AreEqual(0, waiting.AllReads);
        pending.SetResult(await new ReferenceDataCacheServiceStub().EnsureLoadedAsync()); await Task.Yield();
        Assert.AreEqual(0, waiting.AllReads);
    });

    /// <summary>两个真实页面复用列菜单注入器，原生选区、表头、行勾选和两处模式入口一致，重载不重复注入。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public void ActualPages_MenuHeaderModeAndLifetime(bool product) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.Manager.InitializeAsync(); await f.OpenProducts();
        UserControl view = product ? new SupplierProductManagerView { DataContext = f.Products } : new SupplierManagerView { DataContext = f.Manager };
        var window = new Window { Content = view, Width = 1500, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName(product ? "ProductGrid" : "SupplierGrid");
            Assert.AreEqual(1, Interaction.GetBehaviors(grid).OfType<DataGridColumnVisibilityBehavior>().Count());
            var rows = product ? f.Products.Products.Items.Cast<object>().ToArray() : f.Manager.Suppliers.Items.Cast<object>().ToArray();
            grid.SelectedItems.Clear(); grid.SelectedItems.Add(rows[0]); grid.SelectedItems.Add(rows[2]);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); var menu = grid.ContextMenu; var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.AreEqual(9, items.Length); Assert.AreEqual("查看", items[0].Header);
            var checkSelection = items.Single(item => Equals(item.Header, "勾选(选区)"));
            var uncheckSelection = items.Single(item => Equals(item.Header, "取消勾选(选区)"));
            var invertSelection = items.Single(item => Equals(item.Header, "反选(选区)"));
            Assert.AreSame(grid.SelectedItems, checkSelection.CommandParameter);
            await InvokeMenu(checkSelection, menu); Assert.AreEqual(2, Checked(f, product));
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(uncheckSelection, menu); Assert.AreEqual(0, Checked(f, product));
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(invertSelection, menu); Assert.AreEqual(2, Checked(f, product));
            var command = product ? (IRelayCommand)f.Products.SetVisibleChecksCommand : f.Manager.SetVisibleChecksCommand;
            var header = Descendants(grid).OfType<CheckBox>().Single(box => ReferenceEquals(box.Command, command));
            await Space(header); Assert.AreEqual(rows.Length, Checked(f, product)); await Space(header); Assert.AreEqual(0, Checked(f, product));
            grid.ScrollIntoView(rows[0], grid.Columns[0]); grid.UpdateLayout();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(rows[0]);
            var check = Descendants(row).OfType<CheckBox>().Single(box => BindingOperations.GetBinding(box, CheckBox.IsCheckedProperty)?.Path.Path == "IsChecked");
            await Space(check); Assert.AreEqual(1, Checked(f, product)); Assert.IsFalse(product ? f.Products.HasChanges : f.Manager.HasChanges);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background); await InvokeMenu(items[^1], menu);
            Assert.AreEqual(DataGridSelectionUnit.CellOrRowHeader, grid.SelectionUnit);
            OpenMenu(grid); await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsTrue(new[] { checkSelection, uncheckSelection, invertSelection }.All(item => !item.IsEnabled)); Assert.IsFalse(items.Single(item => Equals(item.Header, "删除")).IsEnabled);
            menu.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.Background);
            await Space(Descendants(view).OfType<global::Wpf.Ui.Controls.ToggleSwitch>().Single());
            Assert.AreEqual(DataGridSelectionUnit.FullRow, grid.SelectionUnit);
            if (product)
            {
                var selector = (UserControl)view.FindName("SupplierSelector"); Assert.AreEqual(0, Grid.GetColumn((UIElement)selector.Parent));
                Assert.AreEqual((double)view.FindResource("DataGridRowHeightMedium"), grid.RowHeight);
                Assert.IsTrue(f.Products.DocumentTitle.Contains(f.Options[0].SupplierCode));
            }
            window.Content = null; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.AreEqual(8, menu.Items.OfType<MenuItem>().Count());
            Assert.IsNull(product ? f.Products.CommitPendingEdits : f.Manager.CommitPendingEdits);
            window.Content = view; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1, menu.Items.OfType<MenuItem>().Count(item => Equals(item.Header, "查看")));
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>新建与已有行权限区分，直接执行删除或填充不能越权，产品解除标准关联仍能保存。</summary>
    [TestMethod]
    public void EditingPermissions_ProtectOwnedNewAndPersistedRows() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture(); await f.Manager.InitializeAsync(); await f.OpenProducts();
        f.User.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.SuppliersSupplierView, PermissionKeys.SuppliersSupplierCreate, PermissionKeys.SuppliersProductsView, PermissionKeys.SuppliersProductsCreate] });
        var oldSupplier = f.Manager.Suppliers.Items[0]; var oldProduct = f.Products.Products.Items[0];
        Assert.IsFalse(f.Manager.CanEditRow(oldSupplier)); Assert.IsFalse(f.Products.CanEditRow(oldProduct));
        Assert.IsFalse(f.Manager.DeleteSelectedCommand.CanExecute(null)); Assert.IsFalse(f.Products.DeleteSelectedCommand.CanExecute(null));
        await f.Manager.DeleteSelectedCommand.ExecuteAsync(null); await f.Products.DeleteSelectedCommand.ExecuteAsync(null);
        f.Manager.AddRowCommand.Execute(null); f.Products.AddItemCommand.Execute(null);
        var addedSupplier = f.Manager.SelectedLine!; var addedProduct = f.Products.SelectedLine!;
        Assert.IsTrue(f.Manager.CanEditRow(addedSupplier)); Assert.IsTrue(f.Products.CanEditRow(addedProduct));
        var fill = new FillDownRequest(addedProduct, new[] { oldProduct }, new[] { nameof(oldProduct.UnitPrice) });
        Assert.IsFalse(f.Products.FillDownCommand.CanExecute(fill)); f.Products.FillDownCommand.Execute(fill);
        addedProduct.SupplierSku = "独立资料"; Assert.IsNull(addedProduct.SkuId); Assert.IsNull(addedProduct.ValidateRow());
        Assert.IsTrue(f.Products.DeleteSelectedCommand.CanExecute(null)); await f.Products.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Products.Products.Items.Contains(addedProduct)); Assert.IsTrue(f.Products.Products.Items.Contains(oldProduct));
        addedSupplier.SupplierCode = "NEW"; addedSupplier.SupplierName = "新"; addedSupplier.DocRelateNo = "NEW";
        await f.Manager.SaveCommand.ExecuteAsync(null); Assert.IsNotNull(f.SupplierSave); Assert.AreEqual(1, f.SupplierSave.CreateItems.Count);
    });

    /// <summary>为供应商实际管理视图建立独立宿主，卸载后核对原生编辑回调已释放。</summary>
    private static void WithManagerGrid(Func<Fixture, SupplierManagerView, DataGrid, Task> action) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var fixture = new Fixture(); await fixture.Manager.InitializeAsync();
        var view = new SupplierManagerView { DataContext = fixture.Manager };
        var window = new Window { Content = view, Width = 1500, Height = 850, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await action(fixture, view, (DataGrid)view.FindName("SupplierGrid"));
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        Assert.IsNull(fixture.Manager.CommitPendingEdits);
    });

    /// <summary>滚动到目标业务列并通过真实漏斗按钮打开 Popup，不直接设置列筛选对象。</summary>
    private static async Task<(MultipleChoiceFilter Filter, Popup Popup)> OpenFilter(DataGrid grid, DataGridColumn column)
    {
        if (grid.Items.Count > 0) grid.ScrollIntoView(grid.Items[0], column);
        grid.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var filter = Descendants(column.GetDataGridFilterColumnControl()!).OfType<MultipleChoiceFilter>().Single();
        if (!filter.IsPopupOpen) Toggle((ToggleButton)filter.Template.FindName("FilterButton", filter));
        await Settle();
        var popup = (Popup)filter.Template.FindName("Popup", filter);
        Assert.IsTrue(popup.IsOpen); return (filter, popup);
    }

    /// <summary>经 Automation 操作实际筛选按钮，覆盖模板命令及绑定参数。</summary>
    private static async Task Click(Button button)
    {
        Assert.IsTrue(button.IsVisible); Assert.IsTrue(button.IsEnabled);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    /// <summary>保留原生开关切换顺序，覆盖实际弹窗与全选入口。</summary>
    private static void Toggle(ToggleButton button) => ((IToggleProvider)new ToggleButtonAutomationPeer(button).GetPattern(PatternInterface.Toggle)!).Toggle();

    /// <summary>通过下拉框原生导航键和 Enter 选择币种，验证键盘选择确实回写代码。</summary>
    private static async Task SelectCurrency(ComboBox combo, params Key[] keys)
    {
        Assert.IsTrue(combo.Focus());
        foreach (var key in keys)
            combo.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(combo)!, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    /// <summary>通过原生 DataGrid 进入编辑；权限和忙碌状态均由实际页面事件与绑定判定。</summary>
    private static async Task<bool> BeginCellEdit(DataGrid grid, SupplierLineModel row, DataGridColumn column)
    {
        grid.ScrollIntoView(row, column); grid.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        grid.CurrentCell = new DataGridCellInfo(row, column);
        Assert.AreSame(row, grid.CurrentCell.Item); Assert.AreSame(column, grid.CurrentCell.Column);
        var editing = grid.BeginEdit(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); return editing;
    }

    private static int Checked(Fixture f, bool product) => product ? f.Products.Products.CheckedCount : f.Manager.Suppliers.CheckedCount;
    /// <summary>与本页150毫秒筛选延迟一致，等待实际列条件发布后断言。</summary>
    private static async Task Settle() { await Task.Delay(250); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    /// <summary>遍历真实视觉树以定位模板生成的表头和行勾选控件。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child; }
    /// <summary>使用菜单键打开原生上下文菜单，覆盖实际焦点和命令参数。</summary>
    private static void OpenMenu(DataGrid grid)
    { Assert.IsTrue(grid.Focus()); InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, 0, Key.Apps) { RoutedEvent = Keyboard.KeyUpEvent }); }
    /// <summary>通过空格键验证表头、行勾选及模式开关的键盘行为。</summary>
    private static async Task Space(Control control)
    {
        Assert.IsTrue(control.Focus()); foreach (var routed in new[] { Keyboard.KeyDownEvent, Keyboard.KeyUpEvent })
            control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control)!, 0, Key.Space) { RoutedEvent = routed });
        await Dispatcher.Yield(DispatcherPriority.Background);
    }
    /// <summary>经 Automation 执行菜单并等待关闭，避免重开时仍使用旧菜单焦点。</summary>
    private static async Task InvokeMenu(MenuItem item, ContextMenu menu)
    {
        Assert.IsTrue(item.IsEnabled); var closed = new TaskCompletionSource(); RoutedEventHandler handler = (_, _) => closed.TrySetResult(); menu.Closed += handler;
        try { ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)!).Invoke(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { menu.Closed -= handler; }
    }

    /// <summary>只替代网络、缓存刷新与弹窗时序，使用两个真实VM与真实行模型。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly CurrentUserService User = SupplierTestSupport.FullUser();
        public readonly SupplierDto[] Options = Enumerable.Range(1, 35).Select(index => new SupplierDto { Uid = Guid.NewGuid(), Version = 1,
            SupplierCode = $"S{index:000}", SupplierName = $"供应商{index}", DocRelateNo = $"D{index}", Currency = "USD", TaxRate = 0.13m }).ToArray();
        public readonly SupplierManagerViewModel Manager;
        public readonly SupplierProductManagerViewModel Products;
        public int AllReads, SupplierPageReads, ProductSaves, LookupReads, DefaultWrites;
        public CancellationToken AllToken, LookupToken;
        public readonly List<SupplierProductSearchRequest> Requests = [];
        public readonly List<CancellationToken> PageTokens = [];
        public SupplierBatchSaveRequest? SupplierSave;
        public Task<bool>? Confirmation;
        public Task<ServiceResult<SupplierBatchSaveResponse>>? SaveResponse;
        public Task<CacheRefreshResult>? Ensure;
        public readonly ObservableCollection<SysDicCurrencyDto> CurrencyCache =
        [
            new() { Uid = Guid.NewGuid(), CurrencyCode = "USD", CurrencyNameZh = "美元", IsEnabled = true },
            new() { Uid = Guid.NewGuid(), CurrencyCode = "CNY", CurrencyNameZh = "人民币", IsEnabled = true }
        ];
        public bool Confirm = true, FailPage;
        public Func<CancellationToken, Task<ServiceResult<IReadOnlyList<SupplierDto>>>>? All;
        public Func<SupplierProductSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<SupplierProductDto>>>>? Read;
        public Func<CancellationToken, Task<ServiceResult<List<ProductLookupDto>>>>? Lookup;
        private readonly Guid _productId = Guid.NewGuid();

        /// <summary>以可控制时序的 API 替身构建真实页面，记录请求范围及取消令牌。</summary>
        public Fixture()
        {
            var sourceCache = new ReferenceDataCacheServiceStub(suppliers: Options);
            var currencies = new ReadOnlyObservableCollection<SysDicCurrencyDto>(CurrencyCache);
            var cache = SupplierTestSupport.Proxy<IReferenceDataCacheService>((method, args) => method.Name switch
            {
                "get_Currencies" => currencies, nameof(IReferenceDataCacheService.EnsureLoadedAsync) when Ensure is not null => Ensure,
                _ => method.Invoke(sourceCache, args)
            });
            var runner = SupplierTestSupport.Runner();
            var messages = SupplierTestSupport.Proxy<IMessageService>((_, _) => null);
            var dialogs = SupplierTestSupport.Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ConfirmAsync) ? Confirmation ?? Task.FromResult(Confirm) : Task.CompletedTask);
            var api = SupplierTestSupport.Proxy<ISupplierApi>((method, args) =>
            {
                if (method.Name == nameof(ISupplierApi.GetAllAsync)) { AllReads++; AllToken = (CancellationToken)args[0]!; return All?.Invoke(AllToken) ?? Task.FromResult(ServiceResult<IReadOnlyList<SupplierDto>>.Ok(Options)); }
                if (method.Name == nameof(ISupplierApi.GetPagedAsync)) { SupplierPageReads++; throw new AssertFailedException("管理页不得分页读取冒充全量"); }
                SupplierSave = (SupplierBatchSaveRequest)args[0]!;
                if (SaveResponse is not null) return SaveResponse;
                return Task.FromResult(ServiceResult<SupplierBatchSaveResponse>.Ok(new() { Items = SupplierSave.CreateItems.Select(item =>
                    new SaveItemResult { ClientKey = item.ClientKey, Uid = Guid.NewGuid(), NewVersion = 1, Operation = SaveOperation.Create, Success = true })
                    .Concat(SupplierSave.UpdateItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, Uid = item.Uid, NewVersion = item.Version + 1, Operation = SaveOperation.Update, Success = true })).ToList() }));
            });
            var productApi = SupplierTestSupport.Proxy<IProductApi>((method, args) =>
            {
                if (method.Name == nameof(IProductApi.GetByIdAsync)) return Task.FromResult(ServiceResult<ProductDto>.Ok(new() { Uid = _productId, Version = 7 }));
                LookupReads++; LookupToken = (CancellationToken)args[1]!;
                return Lookup?.Invoke(LookupToken) ?? Task.FromResult(ServiceResult<List<ProductLookupDto>>.Ok([]));
            });
            var relations = SupplierTestSupport.Proxy<ISupplierProductApi>((method, args) =>
            {
                if (method.Name == nameof(ISupplierProductApi.GetPagedAsync))
                {
                    var request = (SupplierProductSearchRequest)args[0]!; var token = (CancellationToken)args[1]!; Requests.Add(request); PageTokens.Add(token);
                    return Read?.Invoke(request, token) ?? Task.FromResult(FailPage ? ServiceResult<PagedResponseBase<SupplierProductDto>>.Fail("测试查询失败") : Page(request));
                }
                if (method.Name == nameof(ISupplierProductApi.SetProductDefaultSupplierAsync))
                { DefaultWrites++; return Task.FromResult(ServiceResult<SetProductDefaultSupplierResult>.Ok(new() { SupplierCode = Options[0].SupplierCode })); }
                ProductSaves++; throw new AssertFailedException("本夹具不应发起产品保存");
            });
            Manager = new(api, runner, messages, dialogs, cache, User);
            Products = new(relations, productApi, runner, messages, dialogs, cache, User);
        }
        /// <summary>从缓存快照创建有真实持久化身份的供应商行。</summary>
        public SwiftExportClient.Features.Suppliers.SupplierManagement.Models.SupplierLineModel Supplier(int index)
        { var row = new SwiftExportClient.Features.Suppliers.SupplierManagement.Models.SupplierLineModel(); row.Load(Options[index]); return row; }
        /// <summary>通过真实选择命令进入首个供应商的产品上下文。</summary>
        public async Task OpenProducts() { Products.SelectedSupplier = Options[0]; await Products.CommitSupplierCommand.ExecuteAsync(null); }
        /// <summary>生成属于请求供应商和页码的产品响应，便于识别过期上下文回写。</summary>
        public ServiceResult<PagedResponseBase<SupplierProductDto>> Page(SupplierProductSearchRequest request) => ServiceResult<PagedResponseBase<SupplierProductDto>>.Ok(new()
        {
            TotalCount = 803, PageIndex = request.PageIndex, PageSize = request.PageSize,
            Items = Enumerable.Range(1, 3).Select(index => new SupplierProductDto { Uid = Guid.NewGuid(), Version = 1, SupplierId = request.SupplierId,
                SupplierCode = Options.Single(item => item.Uid == request.SupplierId).SupplierCode, SupplierSku = $"SP{index}", SkuId = _productId,
                ProductSku = "P", UnitPrice = 19m, TaxPrice = 20m, Currency = "USD" }).ToList()
        });
        /// <summary>解除两个页面的查询及会话订阅，防止用例间共享存活状态。</summary>
        public void Dispose() { Manager.Dispose(); Products.Dispose(); }
    }
}
