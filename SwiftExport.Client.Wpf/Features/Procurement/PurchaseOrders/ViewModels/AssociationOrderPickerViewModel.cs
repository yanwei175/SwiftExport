using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>采购关联页的采购选择区；仅检索待关联采购，筛选与分页不改动关联清单。</summary>
public sealed partial class AssociationOrderPickerViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IPurchaseOrderManagerApi _api;
    private readonly IApiCallRunner _runner;
    private readonly Func<bool> _canQuery;
    private readonly IReferenceDataCacheService? _referenceData;
    private readonly ILogger? _logger;
    private CancellationTokenSource? _query;
    private bool _disposed;
    /// <summary>创建具有独立查询生命周期的选择区，父页面控制写入期间的查询门禁。</summary>
    public AssociationOrderPickerViewModel(IPurchaseOrderManagerApi api, IApiCallRunner runner, Func<bool> canQuery, IReferenceDataCacheService? referenceData = null, ILogger? logger = null)
    {
        _api = api; _runner = runner; _canQuery = canQuery; _referenceData = referenceData; _logger = logger;
        RegisterBusyAwareCommand(SearchCommand); RegisterBusyAwareCommand(PreviousPageCommand);
        RegisterBusyAwareCommand(NextPageCommand);
    }
    /// <summary>当前页只读采购摘要。</summary>
    public ObservableCollection<PurchaseOrderManagerListItemDto> Orders { get; } = [];
    [ObservableProperty] public partial PurchaseOrderManagerListItemDto? SelectedOrder { get; set; }
    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial CustomerDto? SelectedCustomer { get; set; }
    [ObservableProperty] public partial SupplierDto? SelectedSupplier { get; set; }
    /// <summary>直接使用完整会话客户缓存，不按采购历史裁剪。</summary>
    public ReadOnlyObservableCollection<CustomerDto>? CustomerOptions => _referenceData?.Customers;
    /// <summary>直接使用完整会话供应商缓存。</summary>
    public ReadOnlyObservableCollection<SupplierDto>? SupplierOptions => _referenceData?.Suppliers;
    [ObservableProperty] public partial string ReferenceStatusText { get; private set; } = string.Empty;
    [ObservableProperty] public partial DateTime? OrderDateFrom { get; set; }
    [ObservableProperty] public partial DateTime? OrderDateTo { get; set; }
    [ObservableProperty] public partial int PageIndex { get; private set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; private set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = string.Empty;
    private bool CanQuery() => !_disposed && !IsBusy && _canQuery();
    /// <summary>加载现有会话基础资料缓存后查询采购；缓存失败不伪造选择候选。</summary>
    public async Task InitializeAsync()
    {
        if (!CanQuery()) return;
        if (_referenceData is not null)
        {
            await RunBusyAsync(async () =>
            {
                var result = await _referenceData.EnsureLoadedAsync();
                if (!_disposed && !result.Success) ReferenceStatusText = $"基础资料加载失败：{result.Message}";
            }, "读取客户与供应商...");
        }
        if (!_disposed) await SearchAsync();
    }
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private Task SearchAsync() => LoadAsync(1);
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private Task PreviousPageAsync() => LoadAsync(Math.Max(1, PageIndex - 1));
    [RelayCommand(CanExecute = nameof(CanQuery))]
    private Task NextPageAsync() => LoadAsync(Math.Min(Math.Max(1, (TotalCount + 19) / 20), PageIndex + 1));
    /// <summary>在服务端计数及分页之前限制待关联资格；失败保持原页，忽略释放后的响应。</summary>
    private async Task LoadAsync(int page)
    {
        if (!CanQuery()) return;
        if (OrderDateFrom > OrderDateTo) { StatusText = "下单日期开始不能晚于结束。"; return; }
        _query?.Cancel();
        using var query = new CancellationTokenSource();
        _query = query;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.SearchAsync(new()
            {
                OnlyWithUnlinkedQuantity = true, Keyword = Keyword.Trim(),
                CustomerCode = SelectedCustomer?.CustomerCode, SupplierId = SelectedSupplier?.Uid,
                OrderDateFrom = OrderDateFrom, OrderDateTo = OrderDateTo, PageIndex = page, PageSize = 20
            }, query.Token), query.Token, "读取待关联采购失败");
            if (_disposed || query.IsCancellationRequested || !ReferenceEquals(_query, query)) return;
            if (!result.Success || result.Data is null) { StatusText = result.Message; return; }
            Orders.Clear(); foreach (var order in result.Data.Items) Orders.Add(order);
            SelectedOrder = null; PageIndex = page; TotalCount = result.Data.TotalCount;
            StatusText = $"共 {TotalCount} 张待关联采购，第 {PageIndex} 页；关联并非必填。";
        }, "读取待关联采购...");
        if (ReferenceEquals(_query, query)) _query = null;
    }
    /// <summary>父页面状态变化后刷新命令门禁。</summary>
    public void NotifyGateChanged()
    {
        SearchCommand.NotifyCanExecuteChanged(); PreviousPageCommand.NotifyCanExecuteChanged(); NextPageCommand.NotifyCanExecuteChanged();
    }
    /// <summary>技术异常仅写日志，选择区保留现有采购列表并给出稳定提示。</summary>
    protected override Task HandleExceptionAsync(Exception exception)
    {
        _logger?.LogError(exception, "待关联采购选择区读取失败");
        StatusText = "读取待关联采购失败，请重新查询。";
        return Task.CompletedTask;
    }
    /// <summary>取消查询，迟到响应不得回写已关闭页面。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; _query?.Cancel(); }
}
