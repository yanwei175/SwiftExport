using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Inventory.Balances.ViewModels;

/// <summary>提供库存余额只读分页查询、流水 Trace 与对账状态展示。</summary>
public sealed partial class InventoryBalancesViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "inventory-balances";
    private readonly IInventoryApi _api;
    private readonly IWarehouseSiteApi _warehouseApi;
    private readonly IApiCallRunner _runner;
    private readonly IMessageService _messages;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _traceCancellation;
    private Guid? _traceBalanceId;
    private bool _loaded;
    private bool _disposed;

    /// <summary>创建只读余额页面。</summary>
    public InventoryBalancesViewModel(IInventoryApi api, IWarehouseSiteApi warehouseApi, IApiCallRunner runner, IMessageService messages, IReferenceDataCacheService referenceDataCache)
    {
        _api = api;
        _warehouseApi = warehouseApi;
        _runner = runner;
        _messages = messages;
        _referenceDataCache = referenceDataCache;
        Title = "库存余额";
        IsReadOnly = true;
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(LoadTraceCommand);
        RegisterBusyAwareCommand(PreviousPageCommand);
        RegisterBusyAwareCommand(NextPageCommand);
        RegisterBusyAwareCommand(PreviousTracePageCommand);
        RegisterBusyAwareCommand(NextTracePageCommand);
    }

    public ObservableCollection<InventoryBalanceDto> Items { get; } = [];
    public ObservableCollection<InventoryLedgerItemDto> TraceItems { get; } = [];
    public ObservableCollection<WarehouseSiteDto> WarehouseOptions { get; } = [];
    public IEnumerable<SupplierDto> SupplierOptions => _referenceDataCache.Suppliers;
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial bool NonZeroOnly { get; set; } = true;
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    [ObservableProperty] public partial WarehouseSiteDto? SelectedWarehouse { get; set; }
    [ObservableProperty] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] public partial int PageSize { get; set; } = 50;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial int TracePageIndex { get; set; } = 1;
    [ObservableProperty] public partial int TraceTotalCount { get; set; }
    [ObservableProperty] public partial string ReconciliationSummary { get; set; } = "尚未加载追溯。";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(LoadTraceCommand))] public partial InventoryBalanceDto? SelectedItem { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载。";
    public bool CanPreviousPage => PageIndex > 1;
    public bool CanNextPage => PageIndex * PageSize < TotalCount;
    public bool CanPreviousTracePage => _traceBalanceId == SelectedItem?.Uid && TracePageIndex > 1;
    public bool CanNextTracePage => _traceBalanceId == SelectedItem?.Uid && TracePageIndex * PageSize < TraceTotalCount;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_loaded || _disposed) return;
        _loaded = true;
        var cache = await _referenceDataCache.EnsureLoadedAsync();
        if (_disposed) return;
        OnPropertyChanged(nameof(SupplierOptions));
        if (!cache.Success) _messages.ShowError("供应商缓存未更新", cache.Message);
        var warehouses = await _runner.RunAsync(() => _warehouseApi.SearchAsync(new WarehouseSiteSearchRequest { IsActive = true, PageIndex = 1, PageSize = 200 }), "加载虚拟仓筛选失败");
        if (_disposed) return;
        WarehouseOptions.Add(new WarehouseSiteDto { Uid = Guid.Empty, Name = "全部虚拟仓" });
        if (warehouses.Success && warehouses.Data is not null) foreach (var warehouse in warehouses.Data.Items) WarehouseOptions.Add(warehouse);
        SelectedWarehouse = WarehouseOptions[0];
        await LoadAsync(true);
    }

    [RelayCommand] private Task SearchAsync() => LoadAsync(true);
    [RelayCommand(CanExecute = nameof(CanPreviousPage))] private Task PreviousPageAsync() { PageIndex--; return LoadAsync(false); }
    [RelayCommand(CanExecute = nameof(CanNextPage))] private Task NextPageAsync() { PageIndex++; return LoadAsync(false); }
    [RelayCommand(CanExecute = nameof(CanPreviousTracePage))] private Task PreviousTracePageAsync() { TracePageIndex--; return LoadTraceCoreAsync(); }
    [RelayCommand(CanExecute = nameof(CanNextTracePage))] private Task NextTracePageAsync() { TracePageIndex++; return LoadTraceCoreAsync(); }
    [RelayCommand(CanExecute = nameof(CanLoadTrace))] private Task LoadTraceAsync() { TracePageIndex = 1; return LoadTraceCoreAsync(); }

    partial void OnSelectedItemChanged(InventoryBalanceDto? value)
    {
        _traceCancellation?.Cancel();
        _traceBalanceId = null;
        TracePageIndex = 1;
        TraceTotalCount = 0;
        TraceItems.Clear();
        ReconciliationSummary = value is null ? "尚未选择库存余额。" : "请选择加载 Trace。";
        NotifyPaging();
    }

    /// <summary>加载选中余额的分页流水与对账结果，过期请求不得回写页面。</summary>
    private async Task LoadTraceCoreAsync()
    {
        var selected = SelectedItem;
        if (selected is null || _disposed) return;
        var targetId = selected.Uid;
        _traceBalanceId = targetId;
        var cancellation = Replace(ref _traceCancellation);
        var requestedPage = TracePageIndex;
        var result = await _runner.RunAsync(() => _api.GetTraceAsync(targetId, requestedPage, PageSize, cancellation.Token), cancellation.Token, "加载库存追溯失败");
        if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_traceCancellation, cancellation) || SelectedItem?.Uid != targetId || _traceBalanceId != targetId) return;
        TraceItems.Clear();
        if (!result.Success || result.Data is null)
        {
            _messages.ShowError("追溯失败", result.Message);
            return;
        }
        foreach (var row in result.Data.Ledgers) TraceItems.Add(row);
        TracePageIndex = result.Data.PageIndex;
        TraceTotalCount = result.Data.TotalCount;
        ReconciliationSummary = result.Data.Reconciliation?.IsReconciled == true
            ? $"对账一致：余额 {result.Data.Reconciliation.BalanceQty:N4}，流水净额 {result.Data.Reconciliation.LedgerNetQty:N4}。"
            : "对账异常：余额数量、流水净额或最后流水序号不一致。";
        NotifyPaging();
    }

    /// <summary>执行可替代的余额分页查询。</summary>
    private async Task LoadAsync(bool resetPage)
    {
        if (_disposed) return;
        if (resetPage) PageIndex = 1;
        var cancellation = Replace(ref _searchCancellation);
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchBalancesAsync(new InventoryBalanceSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                SupplierId = SelectedSupplier?.Uid, WarehouseSiteId = SelectedWarehouse?.Uid == Guid.Empty ? null : SelectedWarehouse?.Uid,
                NonZeroOnly = NonZeroOnly, PageIndex = PageIndex, PageSize = PageSize
            }, cancellation.Token), cancellation.Token, "查询库存余额失败");
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation)) return;
            Items.Clear();
            if (result.Success && result.Data is not null)
            {
                foreach (var row in result.Data.Items) Items.Add(row);
                PageIndex = result.Data.PageIndex;
                PageSize = result.Data.PageSize;
                TotalCount = result.Data.TotalCount;
                ResultSummary = $"共 {TotalCount} 条余额，本页 {Items.Count} 条。";
            }
            else ResultSummary = result.Message;
            NotifyPaging();
        }, "正在查询库存余额...", false);
    }

    private bool CanLoadTrace() => IsNotBusy && SelectedItem is not null;
    private static CancellationTokenSource Replace(ref CancellationTokenSource? owner) { owner?.Cancel(); owner?.Dispose(); return owner = new CancellationTokenSource(); }
    private void NotifyPaging() { OnPropertyChanged(nameof(CanPreviousPage)); OnPropertyChanged(nameof(CanNextPage)); OnPropertyChanged(nameof(CanPreviousTracePage)); OnPropertyChanged(nameof(CanNextTracePage)); PreviousPageCommand.NotifyCanExecuteChanged(); NextPageCommand.NotifyCanExecuteChanged(); PreviousTracePageCommand.NotifyCanExecuteChanged(); NextTracePageCommand.NotifyCanExecuteChanged(); }

    /// <inheritdoc/>
    public void Dispose() { if (_disposed) return; _disposed = true; _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _traceCancellation?.Cancel(); _traceCancellation?.Dispose(); }
}
