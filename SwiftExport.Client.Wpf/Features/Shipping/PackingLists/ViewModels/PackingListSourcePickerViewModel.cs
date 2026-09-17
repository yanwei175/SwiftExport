using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Features.Shipping.PackingLists.Models;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.ComponentModel;

namespace SwiftExportClient.Features.Shipping.PackingLists.ViewModels;

/// <summary>分页选择客户待交付来源；未知进度不可确认，客户端不产生交付事实。</summary>
public sealed partial class PackingListSourcePickerViewModel(IPackingListApi api, IApiCallRunner runner)
    : ObservableObject, IDialogContentState, IDisposable
{
    private CancellationTokenSource? _query;
    private long _requestId;
    private bool _disposed;
    private IReadOnlyDictionary<Guid, decimal> _draft = new Dictionary<Guid, decimal>();
    private PackingListSourceSearchRequest? _applied;
    /// <summary>当前客户及待应用查询条件。</summary>
    public PackingListSourcePickerSearchModel Search { get; } = new();
    [ObservableProperty] public partial IReadOnlyList<PackingListSourcePickerRowModel> Items { get; private set; } = [];
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial int TotalCount { get; private set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = "查询当前仍有余额的待交付来源。";
    /// <summary>分页大小选项。</summary>
    public IReadOnlyList<int> PageSizeOptions { get; } = [20, 50, 100];
    /// <inheritdoc/>
    public event EventHandler? StateChanged;
    /// <inheritdoc/>
    public bool IsPrimaryButtonEnabled => !_disposed && !IsLoading && GetSelectionError() is null;
    /// <inheritdoc/>
    public bool IsSecondaryButtonEnabled => false;
    /// <inheritdoc/>
    public bool CanClose(DialogResult requestedResult)
    {
        if (requestedResult != DialogResult.Primary) return true;
        var error = GetSelectionError();
        if (error is not null) StatusText = error;
        return IsPrimaryButtonEnabled;
    }
    /// <summary>冻结客户及草稿数量快照后加载第一页。</summary>
    public Task InitializeAsync(Guid customerId, IReadOnlyDictionary<Guid, decimal> draft, CancellationToken token)
    { Search.CustomerId = customerId; _draft = new Dictionary<Guid, decimal>(draft); return QueryAsync(1, true, token); }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task SearchAsync(CancellationToken token) => QueryAsync(1, true, token);
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request, CancellationToken token)
    {
        if (request is null) return Task.CompletedTask;
        if (Items.Any(x => x.IsChecked)) { StatusText = "请先确认或清空勾选，再翻页。"; return Task.CompletedTask; }
        return QueryAsync(request.NewIndex, false, token, request.NewSize);
    }
    [RelayCommand] private void ClearSelection() { foreach (var row in Items) row.IsChecked = false; }

    /// <summary>查询同时检查请求身份和取消状态；已有选择时不隐式丢弃。</summary>
    private async Task QueryAsync(int page, bool applyFilters, CancellationToken token, int? pageSize = null)
    {
        if (_disposed) return;
        if (Items.Any(x => x.IsChecked)) { StatusText = "请先确认或清空勾选，再查询或翻页。"; return; }
        if (Search.CustomerId == Guid.Empty) { StatusText = "请先确定客户。"; return; }
        var request = Search.Adapt<PackingListSourceSearchRequest>();
        request.PageIndex = Math.Max(1, page);
        request.PageSize = pageSize ?? Search.PageSize;
        if (!applyFilters && _applied is not null)
        { request.Keyword = _applied.Keyword; request.Keywords = new List<string>(_applied.Keywords); }
        if (request.Keywords.Count > 200 || request.Keywords.Any(x => x.Length > 200))
        { StatusText = "批量搜索最多 200 个关键词，每个最多 200 字符。"; return; }
        _query?.Cancel();
        using var query = CancellationTokenSource.CreateLinkedTokenSource(token);
        _query = query; var id = ++_requestId; IsLoading = true; NotifyState();
        try
        {
            var result = await runner.RunAsync(() => api.SearchSourcesAsync(request, query.Token), query.Token, "待交付来源查询失败");
            if (_disposed || id != _requestId || query.IsCancellationRequested) return;
            if (!result.Success || result.Data is null) { StatusText = result.Message; return; }
            if (result.Data.Items.Any(x => x.CustomerId != Search.CustomerId || x.GrossDemandId == Guid.Empty)
                || result.Data.Items.GroupBy(x => x.GrossDemandId).Any(x => x.Count() > 1))
            { StatusText = "来源响应客户或身份异常，未接纳。"; return; }
            foreach (var row in Items) row.PropertyChanged -= OnRowChanged;
            var rows = result.Data.Items.Adapt<List<PackingListSourcePickerRowModel>>();
            foreach (var row in rows) row.DraftQty = _draft.GetValueOrDefault(row.GrossDemandId);
            Items = rows.Where(x => x.AvailableQty > 0 && string.IsNullOrWhiteSpace(x.DeliveryUnavailableReason)).ToArray();
            foreach (var row in Items) row.PropertyChanged += OnRowChanged;
            _applied = request; Search.PageIndex = request.PageIndex; Search.PageSize = request.PageSize; TotalCount = result.Data.TotalCount;
            StatusText = $"服务器共 {TotalCount} 条，本页扣除草稿后可选 {Items.Count} 条；Ctrl/Shift 扩展选行，加入后在草稿调整数量。";
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("PackingList选源异常：{0}", ex.GetType().Name);
            if (!_disposed && id == _requestId) StatusText = "来源查询失败，请重试。";
        }
        finally { if (ReferenceEquals(_query, query)) { _query = null; IsLoading = false; NotifyState(); } }
    }
    /// <summary>确认只导出独立快照；草稿已有数量与新选数量共同校验。</summary>
    public IReadOnlyList<PackingListSourcePickerSelection> GetSelectedResult() =>
        IsPrimaryButtonEnabled ? Items.Where(x => x.IsChecked).Adapt<List<PackingListSourcePickerSelection>>() : [];

    private string? GetSelectionError()
    {
        var rows = Items.Where(x => x.IsChecked).ToArray();
        if (rows.Length == 0) return "请选择待交付来源。";
        foreach (var row in rows)
        {
            var error = row.ValidateSelection();
            if (error is not null) return error;
        }
        return null;
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => NotifyState();
    private void NotifyState() { OnPropertyChanged(nameof(IsPrimaryButtonEnabled)); StateChanged?.Invoke(this, EventArgs.Empty); }
    /// <summary>关闭弹窗后取消查询并解除行订阅。</summary>
    public void Dispose()
    { _disposed = true; _requestId++; _query?.Cancel(); foreach (var row in Items) row.PropertyChanged -= OnRowChanged; }
}
