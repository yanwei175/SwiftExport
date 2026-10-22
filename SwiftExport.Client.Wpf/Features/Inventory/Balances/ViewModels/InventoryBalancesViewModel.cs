using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Text.Json;
using SwiftExportClient.Features.Inventory.Services;

namespace SwiftExportClient.Features.Inventory.Balances.ViewModels;

/// <summary>提供库存余额只读分页查询、流水 Trace 与对账状态展示。</summary>
public sealed partial class InventoryBalancesViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "inventory-balances";
    private readonly IInventoryApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IMessageService _messages;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _traceCancellation;
    private Guid? _traceBalanceId;
    private bool _loaded;
    private bool _disposed;
    private string? _appliedQuery;
    private CancellationTokenSource? _optionsCancellation;
    private int _tracePageSize = 50;
    private string _acceptedReconciliation = "尚未加载追溯。";
    private string? _inputError;
    private string? _summaryBeforeInputError;

    /// <summary>创建只读余额页面。</summary>
    public InventoryBalancesViewModel(IInventoryApi api, IWarehouseSiteApi warehouseApi, IApiCallRunner runner, IMessageService messages)
    {
        _api = api;
        _runner = runner;
        _messages = messages;
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
    /// <summary>按库存查看权限完整加载活动仓库；空选择表示全部，不改变共享缓存权限。</summary>
    public ObservableCollection<WarehouseSitePickerDto> WarehouseOptions { get; } = [];
    /// <summary>库存查看权限下完整分页读取的供应商身份，保持既有选择控件绑定。</summary>
    public ObservableCollection<SupplierDto> SupplierOptions { get; } = [];
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial bool NonZeroOnly { get; set; } = true;
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    [ObservableProperty] public partial WarehouseSitePickerDto? SelectedWarehouse { get; set; }
    [ObservableProperty] public partial int PageIndex { get; set; } = 1;
    [ObservableProperty] public partial int PageSize { get; set; } = 50;
    [ObservableProperty] public partial int TotalCount { get; set; }
    [ObservableProperty] public partial int TracePageIndex { get; set; } = 1;
    [ObservableProperty] public partial int TraceTotalCount { get; set; }
    [ObservableProperty] public partial string ReconciliationSummary { get; set; } = "尚未加载追溯。";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(LoadTraceCommand))] public partial InventoryBalanceDto? SelectedItem { get; set; }
    [ObservableProperty] public partial string ResultSummary { get; set; } = "尚未加载。";
    public bool CanPreviousPage => !_disposed && IsNotBusy && PageIndex > 1;
    public bool CanNextPage => !_disposed && IsNotBusy && (long)PageIndex * PageSize < TotalCount;
    public bool CanPreviousTracePage => !_disposed && IsNotBusy && _traceBalanceId == SelectedItem?.Uid && TracePageIndex > 1;
    public bool CanNextTracePage => !_disposed && IsNotBusy && _traceBalanceId == SelectedItem?.Uid && (long)TracePageIndex * _tracePageSize < TraceTotalCount;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        if (_loaded || _disposed) return;
        _loaded = true;
        var options = _optionsCancellation = new CancellationTokenSource();
        try
        {
            var suppliers = await InventorySupplierOptionsLoader.LoadAsync(_api.GetSupplierOptionsAsync, _runner, InventoryQueryLimits.MaxPageSize, options.Token);
            if (_disposed || options.IsCancellationRequested) return;
            if (suppliers.Success && suppliers.Data is not null) foreach (var row in suppliers.Data) SupplierOptions.Add(row);
            else _messages.ShowError("库存供应商候选未加载", suppliers.Message);
            var warehouses = await InventoryWarehouseOptionsLoader.LoadAsync(_api, _runner, options.Token);
            if (_disposed || options.IsCancellationRequested) return;
            if (warehouses.Success && warehouses.Data is not null) foreach (var row in warehouses.Data) WarehouseOptions.Add(row);
            else _messages.ShowError("库存仓库候选未更新", warehouses.Message);
        }
        catch (OperationCanceledException) when (options.IsCancellationRequested) { return; }
        finally { if (ReferenceEquals(_optionsCancellation, options)) _optionsCancellation = null; options.Dispose(); }
        await LoadAsync(true);
    }

    [RelayCommand(CanExecute = nameof(CanSearch))] private Task SearchAsync() => LoadAsync(true);
    [RelayCommand(CanExecute = nameof(CanPreviousPage))] private Task PreviousPageAsync() => CanPreviousPage ? LoadAsync(false, PageIndex - 1) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanNextPage))] private Task NextPageAsync() => CanNextPage ? LoadAsync(false, PageIndex + 1) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanPreviousTracePage))] private Task PreviousTracePageAsync() => CanPreviousTracePage ? LoadTraceCoreAsync(TracePageIndex - 1, _tracePageSize) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanNextTracePage))] private Task NextTracePageAsync() => CanNextTracePage ? LoadTraceCoreAsync(TracePageIndex + 1, _tracePageSize) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanLoadTrace))] private Task LoadTraceAsync() => CanLoadTrace() ? LoadTraceCoreAsync(1, PageSize) : Task.CompletedTask;

    partial void OnSelectedItemChanged(InventoryBalanceDto? value)
    {
        _traceCancellation?.Cancel();
        _traceBalanceId = null;
        TracePageIndex = 1;
        TraceTotalCount = 0;
        TraceItems.Clear();
        ReconciliationSummary = value is null ? "尚未选择库存余额。" : "请选择加载 Trace。";
        _acceptedReconciliation = ReconciliationSummary;
        NotifyPaging();
    }

    /// <summary>加载选中余额的分页流水与对账结果，过期请求不得回写页面。</summary>
    private async Task LoadTraceCoreAsync(int requestedPage, int requestedSize)
    {
        var selected = SelectedItem;
        if (selected is null || _disposed) return;
        var targetId = selected.Uid;
        if (requestedPage < 1 || requestedSize is < 1 or > InventoryQueryLimits.MaxPageSize) { ReconciliationSummary = "追溯分页参数无效。"; return; }
        var cancellation = Replace(ref _traceCancellation);
        try { await RunBusyAsync(async () =>
        {
        var result = await _runner.RunAsync(() => _api.GetTraceAsync(targetId, requestedPage, requestedSize, cancellation.Token), cancellation.Token, "加载库存追溯失败");
        if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_traceCancellation, cancellation) || SelectedItem?.Uid != targetId) return;
        if (!result.Success || result.Data is null)
        {
            ReconciliationSummary = $"追溯未更新，仍保留此前结果：{_acceptedReconciliation}";
            _messages.ShowError("追溯失败", result.Message);
            return;
        }
        TraceItems.Clear(); _traceBalanceId = targetId; _tracePageSize = result.Data.PageSize > 0 ? result.Data.PageSize : requestedSize;
        foreach (var row in result.Data.Ledgers) TraceItems.Add(row);
        TracePageIndex = result.Data.PageIndex > 0 ? result.Data.PageIndex : requestedPage;
        TraceTotalCount = result.Data.TotalCount;
        ReconciliationSummary = result.Data.Reconciliation?.IsReconciled == true
            ? $"对账一致：余额 {result.Data.Reconciliation.BalanceQty:N4}，流水净额 {result.Data.Reconciliation.LedgerNetQty:N4}。"
            : "对账异常：余额数量、流水净额或最后流水序号不一致。";
        _acceptedReconciliation = ReconciliationSummary;
        NotifyPaging();
        }, "正在加载库存追溯..."); }
        finally { if (ReferenceEquals(_traceCancellation, cancellation)) _traceCancellation = null; cancellation.Dispose(); }
    }

    /// <summary>执行可替代的余额分页查询。</summary>
    private async Task LoadAsync(bool resetPage, int? requestedPage = null)
    {
        if (_disposed) return;
        var request = !resetPage && _appliedQuery is not null ? JsonSerializer.Deserialize<InventoryBalanceSearchRequest>(_appliedQuery)!
            : new InventoryBalanceSearchRequest { Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(), SupplierId = SelectedSupplier?.Uid,
                WarehouseSiteId = SelectedWarehouse?.Uid == Guid.Empty ? null : SelectedWarehouse?.Uid, NonZeroOnly = NonZeroOnly, PageSize = PageSize };
        request.PageIndex = requestedPage ?? (resetPage ? 1 : PageIndex);
        if (request.Keyword?.Length > InventoryQueryLimits.KeywordMaxLength || request.PageSize is < 1 or > InventoryQueryLimits.MaxPageSize)
        { ResultSummary = $"关键词不能超过 {InventoryQueryLimits.KeywordMaxLength} 字，每页条数须为 1–{InventoryQueryLimits.MaxPageSize}。"; return; }
        var cancellation = Replace(ref _searchCancellation);
        try { await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchBalancesAsync(request, cancellation.Token), cancellation.Token, "查询库存余额失败");
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation)) return;
            if (result.Success && result.Data is not null)
            {
                Items.Clear(); _appliedQuery = JsonSerializer.Serialize(request);
                foreach (var row in result.Data.Items) Items.Add(row);
                PageIndex = result.Data.PageIndex > 0 ? result.Data.PageIndex : request.PageIndex;
                PageSize = result.Data.PageSize > 0 ? result.Data.PageSize : request.PageSize;
                TotalCount = result.Data.TotalCount;
                ResultSummary = $"共 {TotalCount} 条余额，本页 {Items.Count} 条。";
            }
            else ResultSummary = result.Message;
            NotifyPaging();
        }, "正在查询库存余额...", false); }
        finally { if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null; cancellation.Dispose(); }
    }

    private bool CanSearch() => !_disposed && IsNotBusy && Keyword.Trim().Length <= InventoryQueryLimits.KeywordMaxLength && PageSize is > 0 and <= InventoryQueryLimits.MaxPageSize;
    private bool CanLoadTrace() => !_disposed && IsNotBusy && SelectedItem is not null;
    partial void OnKeywordChanged(string value)
    {
        SearchCommand.NotifyCanExecuteChanged();
        UpdateInputError(value.Trim().Length > InventoryQueryLimits.KeywordMaxLength ? $"关键词不能超过 {InventoryQueryLimits.KeywordMaxLength} 字。" : null);
    }
    /// <summary>输入纠正只撤回本地校验提示，保留期间产生的查询结果。</summary>
    private void UpdateInputError(string? error)
    {
        if (error is not null)
        {
            if (_inputError is null || ResultSummary != _inputError) _summaryBeforeInputError = ResultSummary;
            _inputError = error; ResultSummary = error;
        }
        else
        {
            if (_inputError is not null && ResultSummary == _inputError) ResultSummary = _summaryBeforeInputError ?? string.Empty;
            _inputError = null; _summaryBeforeInputError = null;
        }
    }
    partial void OnPageSizeChanged(int value) => SearchCommand.NotifyCanExecuteChanged();
    private static CancellationTokenSource Replace(ref CancellationTokenSource? owner) { owner?.Cancel(); owner?.Dispose(); return owner = new CancellationTokenSource(); }
    private void NotifyPaging() { OnPropertyChanged(nameof(CanPreviousPage)); OnPropertyChanged(nameof(CanNextPage)); OnPropertyChanged(nameof(CanPreviousTracePage)); OnPropertyChanged(nameof(CanNextTracePage)); PreviousPageCommand.NotifyCanExecuteChanged(); NextPageCommand.NotifyCanExecuteChanged(); PreviousTracePageCommand.NotifyCanExecuteChanged(); NextTracePageCommand.NotifyCanExecuteChanged(); }

    /// <inheritdoc/>
    public void Dispose() { if (_disposed) return; _disposed = true; _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _traceCancellation?.Cancel(); _traceCancellation?.Dispose(); _optionsCancellation?.Cancel(); _optionsCancellation?.Dispose(); }
}
