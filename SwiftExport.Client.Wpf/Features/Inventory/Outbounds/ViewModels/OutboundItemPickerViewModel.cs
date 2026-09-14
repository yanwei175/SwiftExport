using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExportClient.Features.Inventory.Outbounds.Models;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.UI;
using System.ComponentModel;

namespace SwiftExportClient.Features.Inventory.Outbounds.ViewModels;

/// <summary>按固定仓库分页选择正库存产品；只返回查询快照，不检查业务并发，不预占库存。</summary>
public partial class OutboundItemPickerViewModel : ObservableObject, IDialogContentState, IDisposable
{
    private readonly IOutboundManagerApi? _api;
    private readonly IApiCallRunner? _runner;
    private CancellationTokenSource? _query;
    private bool _disposed;
    private bool _hasResult;
    private long _requestId;
    private OutboundItemSearchRequest? _appliedSearch;
    private Guid _warehouseSiteId;
    private Guid[] _excludedProductIds = [];

    /// <summary>仅供设计器及本地模型测试创建，不提供任何远程数据。</summary>
    public OutboundItemPickerViewModel() { }

    /// <summary>运行时注入只读库存选项 API。</summary>
    public OutboundItemPickerViewModel(IOutboundManagerApi api, IApiCallRunner runner)
    { _api = api; _runner = runner; }
    /// <summary>通知弹窗刷新确认按钮状态。</summary>
    public event EventHandler? StateChanged;

    [ObservableProperty] public partial string Keyword { get; set; } = string.Empty;
    [ObservableProperty] public partial IReadOnlyList<OutboundItemPickerRowModel> Items { get; private set; } = Array.Empty<OutboundItemPickerRowModel>();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrimaryButtonEnabled))]
    [NotifyPropertyChangedFor(nameof(CanSelect))]
    public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial int PageIndex { get; private set; } = 1;
    [ObservableProperty] public partial int TotalCount { get; private set; }
    /// <summary>选源按服务端分页加载，避免一次获取全部库存产品。</summary>
    public int PageSize => 50;
    /// <summary>固定每页五十条，与服务端候选请求保持一致。</summary>
    public IReadOnlyList<int> PageSizeOptions { get; } = [50];
    [ObservableProperty] public partial string StatusText { get; private set; } = "修改条件后点击查询；已有勾选请先确认或清空。";
    /// <summary>保留上次成功结果可供选择，仅查询加载或页面释放期间禁用。</summary>
    public bool CanSelect => !_disposed && !IsLoading && _hasResult;
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => CanSelect && Items.Any(x => x.IsChecked);
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <summary>当前页全部行是否勾选。</summary>
    public bool IsAllChecked => Items.Count > 0 && Items.All(x => x.IsChecked);
    /// <summary>当前页选中数量。</summary>
    public int CheckedCount => Items.Count(x => x.IsChecked);
    /// <inheritdoc/>
    public bool CanClose(DialogResult result) => result != DialogResult.Primary || IsPrimaryButtonEnabled;

    /// <summary>初始化固定仓库和排除集合，加载正库存产品第一页。</summary>
    public async Task InitializeAsync(OutboundItemPickerRequest request, CancellationToken cancellationToken)
    {
        _warehouseSiteId = request.WarehouseSiteId;
        _excludedProductIds = request.ExcludedProductIds.ToArray();
        if (_warehouseSiteId == Guid.Empty) { StatusText = "请先选择仓库。"; return; }
        await QueryAsync(1, cancellationToken);
    }

    /// <summary>显式查询第一页，不查询采购或需求。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync(CancellationToken token) => QueryAsync(1, token);

    /// <summary>翻页重新加载快照，当前页勾选不被误当作跨页选择。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request, CancellationToken token) => request is null
        ? Task.CompletedTask : QueryAsync(Math.Max(1, request.NewIndex), token, applyEditedFilters: false);

    /// <summary>可取消且 latest-request-wins；这是 UI 请求防串回写，不是业务并发检查。</summary>
    private async Task QueryAsync(int page, CancellationToken token, bool applyEditedFilters = true)
    {
        if (_disposed || _api is null || _runner is null) return;
        if (Items.Any(x => x.IsChecked))
        {
            StatusText = "已有勾选，请先确认加入 Manager，或点击清空勾选后再查询/翻页。";
            return;
        }
        // 输入框是待查询条件；翻页继续使用当前结果的条件，避免未点查询就隐式切换筛选。
        var applied = applyEditedFilters ? null : _appliedSearch;
        var request = new OutboundItemSearchRequest
        {
            WarehouseSiteId = _warehouseSiteId,
            ExcludedProductIds = _excludedProductIds.ToList(),
            Keyword = applied is null ? Keyword : applied.Keyword,
            PageIndex = page,
            PageSize = PageSize
        };
        _query?.Cancel();
        using var query = CancellationTokenSource.CreateLinkedTokenSource(token);
        _query = query;
        var requestId = ++_requestId;
        IsLoading = true; NotifySelection();
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchItemsAsync(request, query.Token),
                query.Token, "出库来源查询失败，请重试。");

            if (_disposed || requestId != _requestId || query.IsCancellationRequested) return;

            if (!result.Success || result.Data is null)
            { StatusText = result.Message ?? "来源查询失败。"; return; }

            if (result.Data.Items.Any(x => x.WarehouseSiteId != _warehouseSiteId || x.ProductId == Guid.Empty || x.OnHandQty <= 0 || _excludedProductIds.Contains(x.ProductId)))
            { StatusText = "来源响应的类型或行标识不符合本次查询，未接纳数据。"; return; }
            LoadItems(result.Data.Items.Adapt<List<OutboundItemPickerRowModel>>());
            _appliedSearch = request;
            PageIndex = page; TotalCount = result.Data.TotalCount; _hasResult = true;
            StatusText = Items.Count == 0 ? "没有符合条件的库存产品。" : "修改条件后点击查询；仅当前页勾选有效，换页前请先确认或清空勾选。";
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("出库选源查询失败：{0}", ex.GetType().Name);
            if (!_disposed && requestId == _requestId) StatusText = "来源查询失败，请重试。";
        }
        finally
        {
            if (ReferenceEquals(_query, query)) { _query = null; IsLoading = false; NotifySelection(); }
        }
    }

    /// <summary>替换当前页并对称管理勾选事件；设计器也复用此入口。</summary>
    public void LoadItems(IEnumerable<OutboundItemPickerRowModel> items)
    {
        var rows = items.ToArray();
        foreach (var row in Items) row.PropertyChanged -= OnRowChanged;
        Items = rows;
        foreach (var row in Items) row.PropertyChanged += OnRowChanged;
        _hasResult = true;
        NotifySelection();
    }
    /// <summary>读取选中行，保留原始来源数量，不执行版本复核。</summary>
    public IReadOnlyList<OutboundItemPickerRowModel> GetCheckedItems() => Items.Where(x => x.IsChecked).ToArray();
    /// <summary>确认时创建独立传输快照，不把可变行模型交给 Manager。</summary>
    public IReadOnlyList<OutboundItemCandidateDto> GetSelectedResult() => GetCheckedItems().Adapt<List<OutboundItemCandidateDto>>();

    /// <summary>表头切换当前页全选或取消。</summary>
    [RelayCommand]
    private void ToggleAll()
    { var check = !IsAllChecked; foreach (var item in Items) item.IsChecked = check; }

    /// <summary>逐行反转当前页勾选，与全选/取消分开。</summary>
    [RelayCommand]
    private void InvertSelection()
    { foreach (var item in Items) item.IsChecked = !item.IsChecked; }

    /// <summary>显式放弃当前页勾选后才允许换页或重新搜索，避免静默丢失选择。</summary>
    [RelayCommand]
    private void ClearSelection()
    { foreach (var item in Items) item.IsChecked = false; }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(OutboundItemPickerRowModel.IsChecked)) NotifySelection(); }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(IsAllChecked)); OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(IsPrimaryButtonEnabled)); OnPropertyChanged(nameof(CanSelect));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>关闭或取消后停止查询，释放行订阅并拒绝晚到结果。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _requestId++; _query?.Cancel();
        foreach (var row in Items) row.PropertyChanged -= OnRowChanged;
    }
}
