using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient.Features.Inventory.Ledger.ViewModels;

/// <summary>提供不可变库存流水的只读筛选与分页查询。</summary>
public sealed partial class InventoryLedgerViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "inventory-ledger";
    private readonly IInventoryApi _api;
    private readonly IWarehouseSiteApi _warehouseApi;
    private readonly IApiCallRunner _runner;
    private readonly IReferenceDataCacheService _referenceDataCache;
    private CancellationTokenSource? _searchCancellation;
    private bool _loaded;
    private bool _disposed;

    /// <summary>创建库存流水只读页面。</summary>
    public InventoryLedgerViewModel(IInventoryApi api, IWarehouseSiteApi warehouseApi, IApiCallRunner runner, IReferenceDataCacheService referenceDataCache)
    {
        _api = api;
        _warehouseApi = warehouseApi;
        _runner = runner;
        _referenceDataCache = referenceDataCache;
        Title = "库存流水";
        IsReadOnly = true;
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<InventoryLedgerItemDto> Items { get; } = [];
    public ObservableCollection<WarehouseSiteDto> WarehouseOptions { get; } = [];
    public IEnumerable<SupplierDto> SupplierOptions => _referenceDataCache.Suppliers;
    public IReadOnlyList<InventoryTransactionTypeEnum?> TransactionTypes { get; } = [null, .. InventoryTransactionTypeList.All.Select(value => (InventoryTransactionTypeEnum?)value)];
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceDocumentNo { get; set; } = string.Empty;
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    [ObservableProperty] public partial WarehouseSiteDto? SelectedWarehouse { get; set; }
    [ObservableProperty] public partial InventoryTransactionTypeEnum? TransactionType { get; set; }
    [ObservableProperty] public partial DateTime? DateFrom { get; set; } = DateTime.Today.AddMonths(-1);
    [ObservableProperty] public partial DateTime? DateTo { get; set; } = DateTime.Today;
    [ObservableProperty] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] public partial int PageSize { get; set; } = 50;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载。";

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_loaded || _disposed) return;
        _loaded = true;
        await _referenceDataCache.EnsureLoadedAsync();
        if (_disposed) return;
        OnPropertyChanged(nameof(SupplierOptions));
        var warehouses = await _runner.RunAsync(() => _warehouseApi.SearchAsync(new WarehouseSiteSearchRequest { IsActive = true, PageIndex = 1, PageSize = 200 }), "加载虚拟仓筛选失败");
        if (_disposed) return;
        WarehouseOptions.Add(new WarehouseSiteDto { Uid = Guid.Empty, Name = "全部虚拟仓" });
        if (warehouses.Success && warehouses.Data is not null) foreach (var warehouse in warehouses.Data.Items) WarehouseOptions.Add(warehouse);
        SelectedWarehouse = WarehouseOptions[0];
        await LoadAsync(true);
    }
    [RelayCommand] private Task SearchAsync() => LoadAsync(true);

    /// <summary>接受共享分页控件的页码与每页条数请求，并继续使用可取消查询。</summary>
    [RelayCommand(CanExecute = nameof(CanChangePage))]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null) return Task.CompletedTask;
        PageSize = Math.Clamp(request.NewSize <= 0 ? 50 : request.NewSize, 1, 200);
        PageIndex = Math.Max(1, request.NewIndex);
        return LoadAsync(false);
    }

    private bool CanChangePage(PageChangeRequest? request) => !_disposed && !IsBusy && request is not null;

    /// <summary>执行可替代的服务端分页查询，避免旧请求覆盖新筛选结果。</summary>
    private async Task LoadAsync(bool resetPage)
    {
        if (_disposed) return;
        if (resetPage) PageIndex = 1;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = _searchCancellation = new CancellationTokenSource();
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchLedgerAsync(new InventoryLedgerSearchRequest
            {
                Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                SourceDocumentNo = string.IsNullOrWhiteSpace(SourceDocumentNo) ? null : SourceDocumentNo.Trim(),
                SupplierId = SelectedSupplier?.Uid, WarehouseSiteId = SelectedWarehouse?.Uid == Guid.Empty ? null : SelectedWarehouse?.Uid,
                TransactionType = TransactionType, OccurredDateFrom = DateFrom, OccurredDateTo = DateTo,
                PageIndex = PageIndex, PageSize = PageSize
            }, cancellation.Token), cancellation.Token, "查询库存流水失败");
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation)) return;
            Items.Clear();
            if (result.Success && result.Data is not null)
            {
                foreach (var row in result.Data.Items) Items.Add(row);
                PageIndex = result.Data.PageIndex;
                PageSize = result.Data.PageSize;
                TotalCount = result.Data.TotalCount;
                ResultSummary = $"共 {TotalCount} 条流水，本页 {Items.Count} 条。";
            }
            else ResultSummary = result.Message;
            PageChangeCommand.NotifyCanExecuteChanged();
        }, "正在查询库存流水...", false);
    }

    /// <inheritdoc/>
    public void Dispose() { if (_disposed) return; _disposed = true; _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); }
}
