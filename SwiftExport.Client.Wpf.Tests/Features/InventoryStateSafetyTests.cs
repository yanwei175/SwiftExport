using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Services;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using SwiftExportClient.Features.Inventory.WarehouseSites.Views;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Inventory;

/// <summary>库存查询事实保留、专用候选权限以及仓库冻结写命令的离线回归。</summary>
[TestClass]
public sealed class InventoryStateSafetyTests
{
    /// <summary>五类写命令未知后拒绝不能丢失原负载，状态操作没有编辑模型也必须保护关闭。</summary>
    [TestMethod]
    [DataRow("create")][DataRow("update")][DataRow("activate")][DataRow("deactivate")][DataRow("delete")]
    public async Task WarehouseUnknown_RetainsOriginalRequestThroughRefusal(string kind)
    {
        using var f = new Fixture(); await f.PrepareWrite(kind);
        f.Send = (_, _) => Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "未知"));
        await f.StartWrite(kind); var original = f.WritePayloads.Single();
        Assert.IsTrue(f.Warehouse.HasPendingWrite); Assert.IsFalse(f.Warehouse.CanEditInputs); Assert.IsTrue(f.Warehouse.HasUnsavedChanges);
        Assert.IsFalse(await f.Warehouse.CanCloseAsync(default)); await f.Warehouse.CloseDrawerCommand.ExecuteAsync(null);
        f.Send = (_, _) => Task.FromResult(ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Forbidden, "暂时拒绝"));
        await f.Warehouse.RefreshCommand.ExecuteAsync(null); Assert.IsTrue(f.Warehouse.HasPendingWrite);
        f.Send = f.Success; await f.Warehouse.RefreshCommand.ExecuteAsync(null);
        Assert.HasCount(3, f.WritePayloads); Assert.IsTrue(f.WritePayloads.All(x => x == original)); Assert.IsFalse(f.Warehouse.HasPendingWrite);
    }

    /// <summary>首次明确拒绝保留输入并恢复编辑；不完整成功回执则继续冻结。</summary>
    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task WarehouseReceipt_DistinguishesRefusalAndMalformedSuccess(bool malformed)
    {
        using var f = new Fixture(); await f.PrepareWrite("create");
        f.Send = (_, _) => Task.FromResult(malformed ? ServiceResult<WarehouseSiteDto>.Ok(new()) : ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.Validation, "名称无效"));
        await f.Warehouse.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(malformed, f.Warehouse.HasPendingWrite); Assert.AreEqual(!malformed, f.Warehouse.CanEditInputs);
        Assert.AreEqual("保存名称", f.Warehouse.EditModel!.Name);
    }

    /// <summary>迟到结果在权限变化、切换账号或释放后不接纳；原账号恢复后仍可核实相同请求。</summary>
    [TestMethod]
    [DataRow("permission")][DataRow("account")][DataRow("dispose")]
    public async Task WarehouseWrite_RejectsOldSessionResult(string change)
    {
        using var f = new Fixture(); await f.PrepareWrite("update");
        var source = new TaskCompletionSource<ServiceResult<WarehouseSiteDto>>(); f.Send = (_, _) => source.Task;
        var writing = f.Warehouse.SaveCommand.ExecuteAsync(null); var original = f.WritePayloads.Single();
        if (change == "dispose") f.Warehouse.Dispose();
        else f.User.SetCurrentUser(new() { UserId = change == "account" ? "other" : "u", Permissions = [PermissionKeys.InventoryWarehouseView] });
        source.SetResult(await f.Success("update", JsonSerializer.Deserialize<UpdateWarehouseSiteCommand>(original)!)); await writing;
        Assert.IsFalse(f.Messages.Events.Any(x => x.Kind == "success"));
        if (change != "dispose")
        {
            Assert.IsTrue(f.Warehouse.HasPendingWrite); Assert.IsFalse(f.Warehouse.RefreshCommand.CanExecute(null));
            f.RestoreUser(); f.Send = f.Success; await f.Warehouse.RefreshCommand.ExecuteAsync(null);
            Assert.AreEqual(original, f.WritePayloads.Last()); Assert.IsFalse(f.Warehouse.HasPendingWrite);
        }
    }

    /// <summary>实际抽屉在保存中禁用字段，未知后抽屉内原保存按钮可达且绑定相同原命令恢复。</summary>
    [TestMethod]
    public void WarehouseView_FreezesInputsAndKeepsRetryReachable() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load(); using var f = new Fixture(); await f.PrepareWrite("create");
        var source = new TaskCompletionSource<ServiceResult<WarehouseSiteDto>>(); f.Send = (_, _) => source.Task;
        var view = new WarehouseSitesView { DataContext = f.Warehouse };
        var window = new Window { Content = view, Width = 1200, Height = 850, Left = -5000, Top = -5000, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); await Idle(); var writing = f.Warehouse.SaveCommand.ExecuteAsync(null); await Idle();
            var fields = Children<TextBox>(view).Where(x => BindingPath(x) is "Code" or "Name").ToArray();
            Assert.HasCount(2, fields); Assert.IsTrue(fields.All(x => !x.IsEnabled));
            source.SetResult(ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "未知")); await writing; await Idle();
            var retry = Children<Button>(view).Single(x => ReferenceEquals(x.Command, f.Warehouse.SaveCommand));
            Assert.AreEqual("重试原命令", retry.Content); Assert.IsTrue(retry.IsEnabled); Assert.IsTrue(retry.IsVisible); Assert.IsTrue(retry.IsHitTestVisible);
            f.Send = f.Success; retry.Command.Execute(null); await f.Warehouse.SaveCommand.ExecutionTask!; await Idle();
            Assert.IsFalse(f.Warehouse.HasPendingWrite); Assert.AreEqual(f.WritePayloads[0], f.WritePayloads[1]);
        }
        finally { source.TrySetResult(ServiceResult<WarehouseSiteDto>.Fail(ServiceErrorCodes.OutcomeUnknown, "结束")); window.Close(); }
    });

    /// <summary>迟到名称不能被接纳清掉；成功后列表刷新失败只读重试，不重发写命令。</summary>
    [TestMethod]
    public async Task WarehouseWrite_PreservesLateEditAndDoesNotRepeatSuccessfulWrite()
    {
        using var f = new Fixture(); await f.PrepareWrite("update");
        var source = new TaskCompletionSource<ServiceResult<WarehouseSiteDto>>(); f.Send = (_, _) => source.Task;
        var writing = f.Warehouse.SaveCommand.ExecuteAsync(null); f.Warehouse.EditModel!.Name = "迟到修改";
        f.FailWarehouseQuery = true; source.SetResult(await f.Success("update", JsonSerializer.Deserialize<UpdateWarehouseSiteCommand>(f.WritePayloads[0])!)); await writing;
        Assert.AreEqual("迟到修改", f.Warehouse.EditModel.Name); Assert.IsTrue(f.Warehouse.EditModel.HasChanges); Assert.IsFalse(f.Warehouse.HasPendingWrite);
        await f.Warehouse.RefreshCommand.ExecuteAsync(null); Assert.HasCount(1, f.WritePayloads);
    }

    /// <summary>两个库存页完整读取超过二百仓库，不使用仓库管理权限或共享仓库缓存。</summary>
    [TestMethod]
    public async Task InventoryOptions_LoadAllPagesThroughInventoryApi()
    {
        using var f = new Fixture(); f.OptionCount = 201;
        await f.Balances.InitializeAsync(); await f.Ledger.InitializeAsync();
        Assert.AreEqual(201, f.Balances.WarehouseOptions.Count); Assert.AreEqual(202, f.Ledger.WarehouseOptions.Count);
        Assert.AreEqual(4, f.OptionPages.Count); CollectionAssert.AreEqual(new[] { 1, 2, 1, 2 }, f.OptionPages.ToArray());
        Assert.AreEqual(0, f.WarehouseQueries.Count);
    }

    /// <summary>分页失败保留全部旧事实，成功分页继续使用已应用筛选而不混入非法当前输入。</summary>
    [TestMethod]
    public async Task InventoryPaging_PreservesAcceptedRowsAndFiltersOnFailure()
    {
        using var f = new Fixture(); f.Balances.Keyword = f.Ledger.Keyword = "old";
        await f.Balances.InitializeAsync(); await f.Ledger.InitializeAsync(); var balance = f.Balances.Items.Single(); var ledger = f.Ledger.Items.Single();
        f.FailInventoryQuery = true; await f.Balances.NextPageCommand.ExecuteAsync(null);
        await f.Ledger.PageChangeCommand.ExecuteAsync(new() { NewIndex = 3, NewSize = 100 });
        Assert.AreSame(balance, f.Balances.Items.Single()); Assert.AreEqual(1, f.Balances.PageIndex); Assert.AreEqual(50, f.Balances.PageSize); Assert.AreEqual(200, f.Balances.TotalCount);
        Assert.AreSame(ledger, f.Ledger.Items.Single()); Assert.AreEqual(1, f.Ledger.PageIndex); Assert.AreEqual(50, f.Ledger.PageSize); Assert.AreEqual(200, f.Ledger.TotalCount);
        f.Balances.Keyword = f.Ledger.Keyword = new string('x', 201); f.Ledger.DateFrom = DateTime.Today.AddDays(2);
        Assert.IsFalse(f.Balances.SearchCommand.CanExecute(null)); Assert.IsFalse(f.Ledger.SearchCommand.CanExecute(null));
        f.FailInventoryQuery = false; await f.Balances.NextPageCommand.ExecuteAsync(null); await f.Ledger.PageChangeCommand.ExecuteAsync(new() { NewIndex = 2, NewSize = 100 });
        Assert.AreEqual("old", f.BalanceRequests.Last().Keyword); Assert.AreEqual("old", f.LedgerRequests.Last().Keyword);
        Assert.AreEqual(2, f.Balances.PageIndex); Assert.AreEqual(2, f.Ledger.PageIndex);
    }

    /// <summary>Trace失败保留页码、流水和总数，并显式标明仍为此前对账结果。</summary>
    [TestMethod]
    public async Task InventoryTrace_FailureRetainsAcceptedTraceFacts()
    {
        using var f = new Fixture(); await f.Balances.InitializeAsync(); f.Balances.SelectedItem = f.Balances.Items[0];
        await f.Balances.LoadTraceCommand.ExecuteAsync(null); var row = f.Balances.TraceItems.Single();
        f.FailTrace = true; await f.Balances.NextTracePageCommand.ExecuteAsync(null);
        Assert.AreSame(row, f.Balances.TraceItems.Single()); Assert.AreEqual(1, f.Balances.TracePageIndex); Assert.AreEqual(100, f.Balances.TraceTotalCount);
        StringAssert.Contains(f.Balances.ReconciliationSummary, "此前");
    }

    /// <summary>按钮和直接命令都阻断超限输入，边界合法且保留原输入，不截断。</summary>
    [TestMethod]
    [DataRow("balance", 200, true)][DataRow("balance", 201, false)]
    [DataRow("ledger", 200, true)][DataRow("ledger", 201, false)]
    [DataRow("source", 100, true)][DataRow("source", 101, false)]
    [DataRow("warehouse", 100, true)][DataRow("warehouse", 101, false)]
    public async Task InventoryInputLimits_MatchDirectCommand(string kind, int length, bool valid)
    {
        using var f = new Fixture(); var text = new string('x', length);
        if (kind == "warehouse") { f.Warehouse.Keyword = text; Assert.AreEqual(valid, f.Warehouse.SearchCommand.CanExecute(null)); await f.Warehouse.SearchCommand.ExecuteAsync(null); Assert.AreEqual(valid ? 1 : 0, f.WarehouseQueries.Count); }
        else if (kind == "balance") { f.Balances.Keyword = text; Assert.AreEqual(valid, f.Balances.SearchCommand.CanExecute(null)); await f.Balances.SearchCommand.ExecuteAsync(null); Assert.AreEqual(valid ? 1 : 0, f.BalanceRequests.Count); }
        else { if (kind == "source") f.Ledger.SourceDocumentNo = text; else f.Ledger.Keyword = text; Assert.AreEqual(valid, f.Ledger.SearchCommand.CanExecute(null)); await f.Ledger.SearchCommand.ExecuteAsync(null); Assert.AreEqual(valid ? 1 : 0, f.LedgerRequests.Count); }
    }

    /// <summary>纠正输入只撤销本地提示，期间产生的新结果不被旧摘要覆盖。</summary>
    [TestMethod]
    public void InventoryInputCorrection_PreservesResultOwnership()
    {
        using var f = new Fixture();
        f.Balances.ResultSummary = f.Ledger.ResultSummary = f.Warehouse.ResultSummary = "原结果";
        f.Balances.Keyword = f.Ledger.Keyword = new string('x', 201);
        f.Warehouse.Keyword = new string('x', 101);
        f.Balances.Keyword = f.Ledger.Keyword = f.Warehouse.Keyword = "有效";
        Assert.AreEqual("原结果", f.Balances.ResultSummary);
        Assert.AreEqual("原结果", f.Ledger.ResultSummary);
        Assert.AreEqual("原结果", f.Warehouse.ResultSummary);
        f.Balances.Keyword = f.Ledger.Keyword = new string('x', 201);
        f.Warehouse.Keyword = new string('x', 101);
        f.Balances.ResultSummary = f.Ledger.ResultSummary = f.Warehouse.ResultSummary = "新结果";
        f.Balances.Keyword = f.Ledger.Keyword = f.Warehouse.Keyword = "有效";
        Assert.AreEqual("新结果", f.Balances.ResultSummary);
        Assert.AreEqual("新结果", f.Ledger.ResultSummary);
        Assert.AreEqual("新结果", f.Warehouse.ResultSummary);
    }

    /// <summary>反向日期被按钮及直接命令共同拒绝，同日条件仍能查询。</summary>
    [TestMethod]
    public async Task InventoryDates_RejectReverseAndAcceptSameDay()
    {
        using var f = new Fixture();
        f.Ledger.DateFrom = DateTime.Today.AddDays(1);
        Assert.IsFalse(f.Ledger.SearchCommand.CanExecute(null));
        await f.Ledger.SearchCommand.ExecuteAsync(null);
        Assert.IsEmpty(f.LedgerRequests);
        f.Ledger.DateFrom = f.Ledger.DateTo;
        Assert.IsTrue(f.Ledger.SearchCommand.CanExecute(null));
        await f.Ledger.SearchCommand.ExecuteAsync(null);
        Assert.HasCount(1, f.LedgerRequests);
    }

    /// <summary>第二页失败不能把第一页候选作为完整集合展示，仍允许余额与流水查询。</summary>
    [TestMethod]
    public async Task InventoryOptions_RejectPartialSnapshot()
    {
        using var f = new Fixture(); f.OptionCount = 201;
        f.OptionsHandler = request => request.PageIndex == 1
            ? Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Ok(new()
              { Items = [new() { Uid = Guid.NewGuid() }], TotalCount = 201, PageIndex = 1, PageSize = 200 }))
            : Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Fail("候选第二页失败"));
        await f.Balances.InitializeAsync(); await f.Ledger.InitializeAsync();
        Assert.IsEmpty(f.Balances.WarehouseOptions);
        Assert.HasCount(1, f.Ledger.WarehouseOptions);
        StringAssert.Contains(f.Ledger.ResultSummary, "候选第二页失败");
        Assert.HasCount(1, f.BalanceRequests); Assert.HasCount(1, f.LedgerRequests);
    }

    /// <summary>释放时取消候选加载，即便旧接口忽略取消也不能继续发起列表查询。</summary>
    [TestMethod]
    public async Task InventoryOptions_DisposeRejectsLateResponse()
    {
        using var f = new Fixture();
        var completion = new TaskCompletionSource<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>();
        f.OptionsHandler = _ => completion.Task;
        var balanceLoad = f.Balances.InitializeAsync(); var ledgerLoad = f.Ledger.InitializeAsync();
        f.Balances.Dispose(); f.Ledger.Dispose();
        completion.SetResult(ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Ok(new()));
        await Task.WhenAll(balanceLoad, ledgerLoad);
        Assert.IsEmpty(f.Balances.WarehouseOptions); Assert.IsEmpty(f.Ledger.WarehouseOptions);
        Assert.IsEmpty(f.BalanceRequests); Assert.IsEmpty(f.LedgerRequests);
    }

    private static string? BindingPath(TextBox field) => field.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path;
    private static async Task Idle() => await System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T match) yield return match; foreach (var nested in Children<T>(child)) yield return nested; } }

    /// <summary>真实用户通知加内存API，全部请求可捕获，不连接业务数据库。</summary>
    private sealed class Fixture : IDisposable
    {
        public CurrentUserService User { get; } = new();
        public Messages Messages { get; } = new();
        public WarehouseSitesViewModel Warehouse { get; }
        public InventoryBalancesViewModel Balances { get; }
        public InventoryLedgerViewModel Ledger { get; }
        public List<string> WritePayloads { get; } = [];
        public List<WarehouseSiteSearchRequest> WarehouseQueries { get; } = [];
        public List<InventoryBalanceSearchRequest> BalanceRequests { get; } = [];
        public List<InventoryLedgerSearchRequest> LedgerRequests { get; } = [];
        public List<int> OptionPages { get; } = [];
        public int OptionCount { get; set; }
        public Func<PagedRequestBase, Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>>? OptionsHandler { get; set; }
        public bool FailWarehouseQuery { get; set; }
        public bool FailInventoryQuery { get; set; }
        public bool FailTrace { get; set; }
        public Func<string, object, Task<ServiceResult<WarehouseSiteDto>>>? Send { get; set; }
        private readonly WarehouseSiteDto _warehouse = new() { Uid = Guid.NewGuid(), Version = 1, Code = "WH", Name = "原名称", IsActive = true, SupplierId = Guid.NewGuid() };
        private readonly InventoryBalanceDto _balance = new() { Uid = Guid.NewGuid() };
        private readonly InventoryLedgerItemDto _ledger = new() { Uid = Guid.NewGuid() };

        /// <summary>为本轮接口逐一提供明确替身响应。</summary>
        public Fixture()
        {
            RestoreUser(); var warehouseApi = DispatchProxy.Create<IWarehouseSiteApi, ApiProxy>(); var inventoryApi = DispatchProxy.Create<IInventoryApi, ApiProxy>();
            ((ApiProxy)(object)warehouseApi).Call = (name, args) =>
            {
                if (name == nameof(IWarehouseSiteApi.SearchAsync)) { var request = (WarehouseSiteSearchRequest)args[0]!; WarehouseQueries.Add(request); return Task.FromResult(FailWarehouseQuery
                    ? ServiceResult<PagedResponseBase<WarehouseSiteDto>>.Fail("列表失败") : ServiceResult<PagedResponseBase<WarehouseSiteDto>>.Ok(new() { Items = [_warehouse], PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 100 })); }
                if (name == nameof(IWarehouseSiteApi.GetAsync)) return Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(_warehouse));
                var kind = name switch { nameof(IWarehouseSiteApi.CreateSupplierVirtualAsync) => "create", nameof(IWarehouseSiteApi.UpdateAsync) => "update", nameof(IWarehouseSiteApi.ActivateAsync) => "activate", nameof(IWarehouseSiteApi.DeactivateAsync) => "deactivate", nameof(IWarehouseSiteApi.DeleteAsync) => "delete", _ => throw new NotSupportedException(name) };
                var command = args[kind == "create" ? 0 : 1]!; WritePayloads.Add(JsonSerializer.Serialize(command)); return Send?.Invoke(kind, command) ?? Success(kind, command);
            };
            ((ApiProxy)(object)inventoryApi).Call = (name, args) =>
            {
                if (name == nameof(IInventoryApi.GetSupplierOptionsAsync)) return Task.FromResult(ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>.Ok(new() { Items = [] }));
                if (name == nameof(IInventoryApi.GetWarehouseOptionsAsync)) { var request = (PagedRequestBase)args[0]!; OptionPages.Add(request.PageIndex); return OptionsHandler?.Invoke(request) ?? Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Ok(new()
                    { Items = Enumerable.Range(0, OptionCount).Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).Select(_ => new WarehouseSitePickerDto { Uid = Guid.NewGuid(), Name = "仓库" }).ToList(), TotalCount = OptionCount, PageIndex = request.PageIndex, PageSize = request.PageSize })); }
                if (name == nameof(IInventoryApi.SearchBalancesAsync)) { var request = (InventoryBalanceSearchRequest)args[0]!; BalanceRequests.Add(request); return Task.FromResult(FailInventoryQuery ? ServiceResult<PagedResponseBase<InventoryBalanceDto>>.Fail("余额失败") : ServiceResult<PagedResponseBase<InventoryBalanceDto>>.Ok(new() { Items = [_balance], PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 200 })); }
                if (name == nameof(IInventoryApi.SearchLedgerAsync)) { var request = (InventoryLedgerSearchRequest)args[0]!; LedgerRequests.Add(request); return Task.FromResult(FailInventoryQuery ? ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>.Fail("流水失败") : ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>.Ok(new() { Items = [_ledger], PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 200 })); }
                if (name == nameof(IInventoryApi.GetTraceAsync)) return Task.FromResult(FailTrace ? ServiceResult<InventoryBalanceTraceDto>.Fail("追溯失败") : ServiceResult<InventoryBalanceTraceDto>.Ok(new() { Balance = _balance, Ledgers = [_ledger], PageIndex = (int)args[1]!, PageSize = (int)args[2]!, TotalCount = 100, Reconciliation = new() { IsReconciled = true } }));
                throw new NotSupportedException(name);
            };
            var cache = new ReferenceDataCacheServiceStub(); var runner = new Runner();
            Warehouse = new(warehouseApi, runner, Messages, new Dialogs(), new ReasonDialog(), User, cache);
            Balances = new(inventoryApi, warehouseApi, runner, Messages); Ledger = new(inventoryApi, warehouseApi, runner);
        }
        /// <summary>恢复原账号的明确权限，不使用管理员隐式放行。</summary>
        public void RestoreUser() => User.SetCurrentUser(new() { UserId = "u", Permissions = [PermissionKeys.InventoryWarehouseView, PermissionKeys.InventoryWarehouseManage, PermissionKeys.InventoryBalanceView] });
        /// <summary>返回满足真实头版本协议的写回执。</summary>
        public Task<ServiceResult<WarehouseSiteDto>> Success(string kind, object command)
        {
            var json = JsonSerializer.SerializeToElement(command); return Task.FromResult(ServiceResult<WarehouseSiteDto>.Ok(new()
            { Uid = _warehouse.Uid, Version = kind == "create" ? 1 : json.GetProperty("ExpectedVersion").GetInt32() + 1,
                Code = kind == "create" ? json.GetProperty("Code").GetString() : _warehouse.Code,
                Name = kind is "create" or "update" ? json.GetProperty("Name").GetString() : _warehouse.Name, IsActive = kind != "deactivate", SupplierId = _warehouse.SupplierId }));
        }
        /// <summary>打开对应操作的原始上下文。</summary>
        public async Task PrepareWrite(string kind)
        { await Warehouse.InitializeAsync(); if (kind == "create") { await Warehouse.OpenCreateCommand.ExecuteAsync(null); Warehouse.EditModel!.SelectedSupplier = new SupplierDto { Uid = _warehouse.SupplierId!.Value, SupplierName = "供应商" }; Warehouse.EditModel.Code = "NEW"; Warehouse.EditModel.Name = "保存名称"; }
            else if (kind == "update") { await Warehouse.OpenEditCommand.ExecuteAsync(null); Warehouse.EditModel!.Name = "保存名称"; } else if (kind == "activate") _warehouse.IsActive = false; }
        /// <summary>从实际公开命令入口发起操作。</summary>
        public Task StartWrite(string kind) => kind switch { "create" or "update" => Warehouse.SaveCommand.ExecuteAsync(null), "activate" => Warehouse.ActivateCommand.ExecuteAsync(null), "deactivate" => Warehouse.DeactivateCommand.ExecuteAsync(null), _ => Warehouse.DeleteCommand.ExecuteAsync(null) };
        /// <summary>释放所有被测试页面。</summary>
        public void Dispose() { Warehouse.Dispose(); Balances.Dispose(); Ledger.Dispose(); }
    }

    /// <summary>按接口方法转交内存响应。</summary>
    public class ApiProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args!);
    }
    /// <summary>直接执行内存响应，让业务失败和取消行为由被测页面处理。</summary>
    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }
    /// <summary>离线确认对话框，不创建真实业务窗口。</summary>
    private sealed class Dialogs : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    /// <summary>提供固定操作原因，便于比对重试请求负载。</summary>
    private sealed class ReasonDialog : IWarehouseSiteReasonDialogService
    { public Task<string?> RequestAsync(string operation, WarehouseSiteDto target, CancellationToken cancellationToken = default) => Task.FromResult<string?>("原因"); }
    /// <summary>记录提示以确认旧会话不会发布成功消息。</summary>
    private sealed class Messages : IMessageService
    {
        public List<(string Kind, string Title, string Message)> Events { get; } = [];
        public void ShowInfo(string title, string message) => Events.Add(("info", title, message));
        public void ShowSuccess(string title, string message) => Events.Add(("success", title, message));
        public void ShowWarning(string title, string message) => Events.Add(("warning", title, message));
        public void ShowError(string title, string message) => Events.Add(("error", title, message));
    }
}
