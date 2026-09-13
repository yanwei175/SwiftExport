using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Balances.ViewModels;
using SwiftExportClient.Features.Inventory.Ledger.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Inventory;

/// <summary>验证库存只读页面的 Trace 身份隔离与离页生命周期。</summary>
[TestClass]
public sealed class InventoryReadOnlyViewModelTests
{
    /// <summary>切换余额后，旧行的延迟 Trace 响应不得展示到新选中行。</summary>
    [TestMethod]
    public async Task BalanceTrace_SwitchSelection_ShouldRejectPreviousResponse()
    {
        var api = new InventoryApiStub();
        using var viewModel = new InventoryBalancesViewModel(api, new WarehouseApiStub(), new Runner(), new MessageStub(), new ReferenceDataCacheServiceStub());
        var first = new InventoryBalanceDto { Uid = Guid.NewGuid() };
        var second = new InventoryBalanceDto { Uid = Guid.NewGuid() };
        viewModel.SelectedItem = first;

        var loading = viewModel.LoadTraceCommand.ExecuteAsync(null);
        viewModel.SelectedItem = second;
        api.TraceResult.SetResult(ServiceResult<InventoryBalanceTraceDto>.Ok(new InventoryBalanceTraceDto
        {
            Balance = first,
            Ledgers = [new InventoryLedgerItemDto { Uid = Guid.NewGuid() }],
            TotalCount = 1, PageIndex = 1, PageSize = 50,
            Reconciliation = new InventoryReconciliationDto { IsReconciled = true }
        }));
        await loading;

        Assert.AreEqual(0, viewModel.TraceItems.Count);
        Assert.AreEqual("请选择加载 Trace。", viewModel.ReconciliationSummary);
        Assert.AreEqual(1, viewModel.TracePageIndex);
    }

    /// <summary>页面已经离开时 Initialize 不得再发仓库或库存查询。</summary>
    [TestMethod]
    public async Task Ledger_DisposedBeforeInitialize_ShouldNotSendRequests()
    {
        var inventory = new InventoryApiStub();
        var warehouse = new WarehouseApiStub();
        var viewModel = new InventoryLedgerViewModel(inventory, warehouse, new Runner(), new ReferenceDataCacheServiceStub());
        viewModel.Dispose();

        await viewModel.InitializeAsync();

        Assert.AreEqual(0, warehouse.SearchCount);
        Assert.AreEqual(0, inventory.LedgerSearchCount);
    }

    private sealed class InventoryApiStub : IInventoryApi
    {
        public TaskCompletionSource<ServiceResult<InventoryBalanceTraceDto>> TraceResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int LedgerSearchCount { get; private set; }
        public Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default) => Task.FromResult(ServiceResult<PagedResponseBase<InventoryBalanceDto>>.Ok(new PagedResponseBase<InventoryBalanceDto>()));
        public Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default) { LedgerSearchCount++; return Task.FromResult(ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>.Ok(new PagedResponseBase<InventoryLedgerItemDto>())); }
        public Task<ServiceResult<InventoryBalanceTraceDto>> GetTraceAsync(Guid uid, int pageIndex = 1, int pageSize = 50, CancellationToken cancellationToken = default) => TraceResult.Task;
    }

    private sealed class WarehouseApiStub : IWarehouseSiteApi
    {
        public int SearchCount { get; private set; }
        public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default) { SearchCount++; return Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSiteDto>>.Ok(new PagedResponseBase<WarehouseSiteDto>())); }
        public Task<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>> SearchCandidatesAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<WarehouseSiteDto>> DeleteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    private sealed class MessageStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}
