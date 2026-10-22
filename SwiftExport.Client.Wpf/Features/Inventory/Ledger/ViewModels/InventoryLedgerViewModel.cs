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
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExportClient.Features.Inventory.Services;
using System.Text.Json;

namespace SwiftExportClient.Features.Inventory.Ledger.ViewModels;

/// <summary>提供不可变库存流水的只读筛选与分页查询。</summary>
public sealed partial class InventoryLedgerViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    public const string RouteKey = "inventory-ledger";
    private readonly IInventoryApi _api;
    private readonly IApiCallRunner _runner;
    private CancellationTokenSource? _searchCancellation;
    private bool _loaded;
    private bool _disposed;
    private string? _appliedQuery;
    private CancellationTokenSource? _optionsCancellation;
    private string? _inputError;
    private string? _summaryBeforeInputError;

    /// <summary>创建库存流水只读页面。</summary>
    public InventoryLedgerViewModel(IInventoryApi api, IWarehouseSiteApi warehouseApi, IApiCallRunner runner)
    {
        _api = api;
        _runner = runner;
        Title = "库存流水";
        IsReadOnly = true;
        RegisterBusyAwareCommand(SearchCommand);
        RegisterBusyAwareCommand(PageChangeCommand);
    }

    public ObservableCollection<InventoryLedgerItemDto> Items { get; } = [];
    /// <summary>库存权限下完整分页取得的活动仓库候选，不使用仓库管理接口。</summary>
    public ObservableCollection<WarehouseSitePickerDto> WarehouseOptions { get; } = [];
    /// <summary>库存查看权限下完整分页读取的供应商身份，不访问基础资料全量缓存。</summary>
    public ObservableCollection<SupplierDto> SupplierOptions { get; } = [];
    /// <summary>所有候选均有实体对象；“全部”的查询值仍为空。</summary>
    public IReadOnlyList<InventoryTransactionTypeOption> TransactionTypes { get; } = [new(null), .. InventoryTransactionTypeList.All.Select(value => new InventoryTransactionTypeOption(value))];
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceDocumentNo { get; set; } = string.Empty;
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    [ObservableProperty] public partial WarehouseSitePickerDto? SelectedWarehouse { get; set; }
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
        var options = _optionsCancellation = new CancellationTokenSource();
        string? optionsError = null;
        string? supplierError = null;
        try
        {
            var suppliers = await InventorySupplierOptionsLoader.LoadAsync(_api.GetSupplierOptionsAsync, _runner, InventoryQueryLimits.MaxPageSize, options.Token);
            if (_disposed || options.IsCancellationRequested) return;
            if (suppliers.Success && suppliers.Data is not null) foreach (var row in suppliers.Data) SupplierOptions.Add(row);
            else supplierError = suppliers.Message;
            var warehouses = await InventoryWarehouseOptionsLoader.LoadAsync(_api, _runner, options.Token);
            if (_disposed || options.IsCancellationRequested) return;
            WarehouseOptions.Add(new WarehouseSitePickerDto { Uid = Guid.Empty, Name = "全部虚拟仓" });
            if (warehouses.Success && warehouses.Data is not null) foreach (var warehouse in warehouses.Data) WarehouseOptions.Add(warehouse);
            else optionsError = warehouses.Message;
        }
        catch (OperationCanceledException) when (options.IsCancellationRequested) { return; }
        finally { if (ReferenceEquals(_optionsCancellation, options)) _optionsCancellation = null; options.Dispose(); }
        SelectedWarehouse = WarehouseOptions[0];
        await LoadAsync(true);
        if (!_disposed && optionsError is not null) ResultSummary += $" 仓库筛选未加载：{optionsError}";
        if (!_disposed && supplierError is not null) ResultSummary += $" 供应商候选未加载：{supplierError}";
    }
    [RelayCommand(CanExecute = nameof(CanSearch))] private Task SearchAsync() => LoadAsync(true);

    /// <summary>接受共享分页控件的页码与每页条数请求，并继续使用可取消查询。</summary>
    [RelayCommand(CanExecute = nameof(CanChangePage))]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        return !CanChangePage(request) ? Task.CompletedTask : LoadAsync(false, request!.NewIndex, request.NewSize);
    }

    private bool CanChangePage(PageChangeRequest? request) => !_disposed && !IsBusy && request is not null;

    /// <summary>执行可替代的服务端分页查询，避免旧请求覆盖新筛选结果。</summary>
    private async Task LoadAsync(bool resetPage, int? page = null, int? size = null)
    {
        if (_disposed) return;
        var request = !resetPage && _appliedQuery is not null ? JsonSerializer.Deserialize<InventoryLedgerSearchRequest>(_appliedQuery)!
            : new InventoryLedgerSearchRequest { Keyword = string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim(),
                SourceDocumentNo = string.IsNullOrWhiteSpace(SourceDocumentNo) ? null : SourceDocumentNo.Trim(), SupplierId = SelectedSupplier?.Uid,
                WarehouseSiteId = SelectedWarehouse?.Uid == Guid.Empty ? null : SelectedWarehouse?.Uid,
                TransactionType = TransactionType, OccurredDateFrom = DateFrom, OccurredDateTo = DateTo, PageSize = PageSize };
        request.PageIndex = page ?? (resetPage ? 1 : PageIndex); request.PageSize = size ?? request.PageSize;
        if (request.PageIndex < 1) { ResultSummary = "页码必须大于零。"; return; }
        if (GetInputError(request.Keyword, request.SourceDocumentNo, request.OccurredDateFrom, request.OccurredDateTo, request.PageSize) is { } error)
        { ResultSummary = error; return; }
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        var cancellation = _searchCancellation = new CancellationTokenSource();
        try { await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchLedgerAsync(request, cancellation.Token), cancellation.Token, "查询库存流水失败");
            if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_searchCancellation, cancellation)) return;
            if (result.Success && result.Data is not null)
            {
                Items.Clear(); _appliedQuery = JsonSerializer.Serialize(request);
                foreach (var row in result.Data.Items) Items.Add(row);
                PageIndex = result.Data.PageIndex > 0 ? result.Data.PageIndex : request.PageIndex;
                PageSize = result.Data.PageSize > 0 ? result.Data.PageSize : request.PageSize;
                TotalCount = result.Data.TotalCount;
                ResultSummary = $"共 {TotalCount} 条流水，本页 {Items.Count} 条。";
            }
            else ResultSummary = result.Message;
            PageChangeCommand.NotifyCanExecuteChanged();
        }, "正在查询库存流水...", false); }
        finally { if (ReferenceEquals(_searchCancellation, cancellation)) _searchCancellation = null; cancellation.Dispose(); }
    }

    /// <summary>统一当前输入与实际请求的长度、分页及日期边界，失败不修改分页事实。</summary>
    private static string? GetInputError(string? keyword, string? sourceNo, DateTime? from, DateTime? to, int size)
    {
        if (keyword?.Length > InventoryQueryLimits.KeywordMaxLength) return $"关键词不能超过 {InventoryQueryLimits.KeywordMaxLength} 字。";
        if (sourceNo?.Length > InventoryQueryLimits.SourceDocumentNoMaxLength) return $"来源单号不能超过 {InventoryQueryLimits.SourceDocumentNoMaxLength} 字。";
        if (size is < 1 or > InventoryQueryLimits.MaxPageSize) return $"每页条数须为 1–{InventoryQueryLimits.MaxPageSize}。";
        return from > to ? "日期起点不能晚于终点。" : null;
    }
    private bool CanSearch() => !_disposed && IsNotBusy && GetInputError(Keyword.Trim(), SourceDocumentNo.Trim(), DateFrom, DateTo, PageSize) is null;
    private void NotifyQueryInputChanged()
    { SearchCommand.NotifyCanExecuteChanged(); UpdateInputError(GetInputError(Keyword.Trim(), SourceDocumentNo.Trim(), DateFrom, DateTo, PageSize)); }
    /// <summary>只撤回仍由本地输入校验占用的摘要，不覆盖新查询结果。</summary>
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
    partial void OnKeywordChanged(string value) => NotifyQueryInputChanged();
    partial void OnSourceDocumentNoChanged(string value) => NotifyQueryInputChanged();
    partial void OnDateFromChanged(DateTime? value) => NotifyQueryInputChanged();
    partial void OnDateToChanged(DateTime? value) => NotifyQueryInputChanged();
    partial void OnPageSizeChanged(int value) => NotifyQueryInputChanged();

    /// <inheritdoc/>
    public void Dispose() { if (_disposed) return; _disposed = true; _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _optionsCancellation?.Cancel(); _optionsCancellation?.Dispose(); }
}
