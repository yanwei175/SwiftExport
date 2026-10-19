using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
namespace SwiftExportClient.Features.OrderTracking.Overview.ViewModels;
/// <summary>独立读取完整来源中命中已应用条件的身份，只标记和定位，不裁剪基础子表。</summary>
public sealed partial class OrderTrackingMatchesViewModel : ObservableObjectEx, IDisposable
{
    private readonly IOrderTrackingApi _api;
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private IReadOnlyList<OrderTrackingDemandRowModel> _rows = [];
    private OrderTrackingQuerySnapshot? _query;
    private string _type = string.Empty;
    private Guid _id;
    private CancellationTokenSource? _cancellation;
    private long _version;
    private bool _disposed;
    private int _next;
    private int? _sourceQueryCount;
    /// <summary>注入接口执行器与当前会话；父页面负责绑定和失效。</summary>
    public OrderTrackingMatchesViewModel(IOrderTrackingApi api, IApiCallRunner runner, ICurrentUserService user)
    { _api = api; _runner = runner; _user = user; }
    [ObservableProperty] public partial bool IsLoading { get; private set; }
    [ObservableProperty] public partial string StatusText { get; private set; } = "尚未定位查询命中";
    [ObservableProperty] public partial string? ErrorMessage { get; private set; }
    [ObservableProperty] public partial string? LocationMessage { get; set; }
    [ObservableProperty] public partial int? MatchedCount { get; private set; }
    [ObservableProperty] public partial DateTime? BusinessDate { get; private set; }
    /// <summary>通知视图只滚动定位，不自动解除或修改用户的本地列筛选。</summary>
    public event Action<OrderTrackingDemandRowModel>? LocateRequested;
    /// <summary>接纳命中标记后通知视图重评用户主动设置的列筛选。</summary>
    public event EventHandler? Updated;
    private bool CanRead() => !_disposed && _query is not null && _id != Guid.Empty && _user.HasPermission(PermissionKeys.OrderTrackingView);
    private bool CanLocate() => CanRead() && !IsLoading && MatchedCount > 0;
    /// <summary>提交新来源查询时仅取消在途命中；查询失败仍保留旧结果和可重试上下文。</summary>
    public void CancelPending()
    {
        ++_version; _cancellation?.Cancel();
        if (IsLoading)
        {
            IsLoading = false; StatusText = "定位已取消，可刷新命中";
            foreach (var row in _rows) row.MatchState = TrackingSummaryLoadState.NotLoaded;
        }
        LocateNextCommand.NotifyCanExecuteChanged();
    }
    /// <summary>切来源、查询或会话后取消旧读取，未知命中不伪装为零。</summary>
    public void Reset()
    {
        ++_version; _cancellation?.Cancel();
        foreach (var row in _rows) { row.QueryMatched = null; row.MatchState = TrackingSummaryLoadState.NotLoaded; }
        _rows = []; _query = null; _type = string.Empty; _id = Guid.Empty; _next = 0;
        IsLoading = false; MatchedCount = null; ErrorMessage = LocationMessage = null; BusinessDate = null; StatusText = "尚未定位查询命中";
        RefreshCommand.NotifyCanExecuteChanged(); LocateNextCommand.NotifyCanExecuteChanged();
    }
    /// <summary>完整基础表加载后一次请求命中身份，使用已应用条件快照。</summary>
    public void Bind(string type, Guid id, IReadOnlyList<OrderTrackingDemandRowModel> rows, OrderTrackingQuerySnapshot query, int? sourceQueryCount = null)
    {
        Reset(); _type = type; _id = id; _rows = rows.ToArray(); _query = query; _sourceQueryCount = sourceQueryCount;
        RefreshCommand.NotifyCanExecuteChanged(); if (CanRead()) RefreshCommand.Execute(null);
    }
    /// <summary>定位失败可独立重试，基础事实和摘要始终保留。</summary>
    [RelayCommand(CanExecute = nameof(CanRead), AllowConcurrentExecutions = true)]
    private async Task RefreshAsync()
    {
        if (!CanRead()) return;
        var type = _type; var id = _id; var rows = _rows; var query = _query!; var user = _user.CurrentUser; var version = ++_version;
        _cancellation?.Cancel(); using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        IsLoading = true; MatchedCount = null; ErrorMessage = LocationMessage = null; StatusText = "正在定位已应用查询的命中…";
        foreach (var row in rows) { row.QueryMatched = null; row.MatchState = TrackingSummaryLoadState.Loading; }
        LocateNextCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _runner.RunAsync(() => _api.GetSourceMatchesAsync(type, id, query.ForMatches(), cancellation.Token), cancellation.Token, "查询命中定位失败");
            if (cancellation.IsCancellationRequested || version != _version || !CanRead() || !ReferenceEquals(user, _user.CurrentUser) || !ReferenceEquals(query, _query)) return;
            if (!result.Success || result.Data is null)
            {
                ErrorMessage = result.Message; StatusText = "定位失败，完整成品需求保留";
                foreach (var row in rows) row.MatchState = TrackingSummaryLoadState.Failed;
                return;
            }
            if (result.Data.SourceType != type || result.Data.SourceGroupId != id || result.Data.MatchedGrossDemandIds.Any(x => x == Guid.Empty)
                || result.Data.MatchedGrossDemandIds.Distinct().Count() != result.Data.MatchedGrossDemandIds.Count)
            { ErrorMessage = "命中响应身份不一致或重复，请重试。"; StatusText = "定位失败，完整成品需求保留"; foreach (var row in rows) row.MatchState = TrackingSummaryLoadState.Failed; return; }
            var ids = result.Data.MatchedGrossDemandIds.ToHashSet();
            foreach (var row in rows) { row.QueryMatched = ids.Contains(row.GrossDemandId); row.MatchState = TrackingSummaryLoadState.Loaded; }
            MatchedCount = rows.Count(x => x.QueryMatched == true); BusinessDate = result.Data.BusinessDate;
            var baseIds = rows.Select(row => row.GrossDemandId).ToHashSet();
            var changed = ids.Any(id => !baseIds.Contains(id));
            var sourceDifference = _sourceQueryCount is not null && ids.Count != _sourceQueryCount ? $"；来源查询时命中 {_sourceQueryCount} 条，当前匹配已变化" : string.Empty;
            StatusText = $"已应用查询命中 {MatchedCount} / 全量 {rows.Count} 条 · 业务日 {BusinessDate:yyyy-MM-dd}" + (changed ? "；部分身份尚未在基础表，请刷新" : "") + sourceDifference;
            Updated?.Invoke(this, EventArgs.Empty);
        }
        finally { if (version == _version) { IsLoading = false; LocateNextCommand.NotifyCanExecuteChanged(); } if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null; }
    }
    /// <summary>依次定位命中身份，视图对本地隐藏行给出明确说明。</summary>
    [RelayCommand(CanExecute = nameof(CanLocate))]
    private void LocateNext()
    {
        if (!CanLocate()) return;
        var matches = _rows.Where(x => x.QueryMatched == true).ToArray(); if (matches.Length == 0) return;
        LocationMessage = null; LocateRequested?.Invoke(matches[_next++ % matches.Length]);
    }
    /// <summary>释放时取消并清除本页持有的命中状态。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; Reset(); }
}
