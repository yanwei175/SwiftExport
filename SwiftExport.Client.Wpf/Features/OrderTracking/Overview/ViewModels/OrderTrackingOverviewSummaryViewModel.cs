using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.OrderTracking;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.OrderTracking.Overview.Models;
using SwiftExportClient.Features.OrderTracking.Overview.Policies;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.OrderTracking.Overview.ViewModels;

/// <summary>独立管理当前页来源批量摘要和完整成品需求摘要，不阻塞或清除基础事实。</summary>
public sealed partial class OrderTrackingOverviewSummaryViewModel : ObservableObjectEx, IDisposable
{
    private readonly IOrderTrackingApi _api;
    private readonly IApiCallRunner _runner;
    private readonly ICurrentUserService _user;
    private IReadOnlyList<OrderTrackingSourceRowModel> _sources = [];
    private IReadOnlyList<OrderTrackingDemandRowModel> _demands = [];
    private string _sourceType = string.Empty;
    private Guid _sourceId;
    private CancellationTokenSource? _sourceCancellation;
    private CancellationTokenSource? _demandCancellation;
    private long _sourceVersion;
    private long _demandVersion;
    private bool _disposed;

    /// <summary>注入批量摘要接口及同一会话的权限、错误执行器。</summary>
    public OrderTrackingOverviewSummaryViewModel(IOrderTrackingApi api, IApiCallRunner runner, ICurrentUserService user)
    { _api = api; _runner = runner; _user = user; }
    [ObservableProperty] public partial bool IsLoadingSources { get; private set; }
    [ObservableProperty] public partial bool IsLoadingDemands { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SourceActionText))] public partial string? SourceError { get; private set; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DemandActionText))] public partial string? DemandError { get; private set; }
    [ObservableProperty] public partial string SourceStamp { get; private set; } = "摘要尚未加载";
    [ObservableProperty] public partial string DemandStamp { get; private set; } = "摘要尚未加载";
    /// <summary>成功接纳来源摘要后通知视图重评本地列筛选；失败不会主动隐藏基础行。</summary>
    public event EventHandler? SourcesUpdated;
    /// <summary>成功接纳需求摘要后通知视图重评本地列筛选及显示条数。</summary>
    public event EventHandler? DemandsUpdated;
    /// <summary>正常刷新与失败重试使用明确的动作名称。</summary>
    public string SourceActionText => SourceError is null ? "刷新进度" : "重试来源进度";
    /// <summary>需求摘要失败不混用基础需求重试入口。</summary>
    public string DemandActionText => DemandError is null ? "刷新需求进度" : "重试需求进度";
    private bool CanRead => !_disposed && _user.HasPermission(PermissionKeys.OrderTrackingView);
    private bool CanReadSources() => CanRead && _sources.Count > 0;
    private bool CanReadDemands() => CanRead && _sourceId != Guid.Empty && _demands.Count > 0;

    /// <summary>接纳当前页基础行后一次批量启动摘要，基础初始化不等待摘要网络响应。</summary>
    public void BindSources(IReadOnlyList<OrderTrackingSourceRowModel> rows)
    {
        ResetSources(); _sources = rows.ToArray();
        foreach (var row in _sources) row.Access = OrderTrackingAttentionPolicy.GetAccess(_user);
        RefreshSourcesCommand.NotifyCanExecuteChanged();
        if (CanReadSources()) RefreshSourcesCommand.Execute(null);
    }
    /// <summary>完整子表基础行接纳后只发一次来源摘要请求，不按需求逐行请求。</summary>
    public void BindDemands(string type, Guid id, IReadOnlyList<OrderTrackingDemandRowModel> rows)
    {
        ResetDemands(); _sourceType = type; _sourceId = id; _demands = rows.ToArray();
        foreach (var row in _demands) row.Access = OrderTrackingAttentionPolicy.GetAccess(_user);
        RefreshDemandsCommand.NotifyCanExecuteChanged();
        if (CanReadDemands()) RefreshDemandsCommand.Execute(null);
    }
    /// <summary>分页、筛选及会话失效时取消旧批次，并清除旧行摘要引用。</summary>
    public void ResetSources()
    {
        ++_sourceVersion; _sourceCancellation?.Cancel();
        foreach (var row in _sources) { row.Summary = null; row.Access = TrackingSummaryAccess.None; row.SummaryState = TrackingSummaryLoadState.NotLoaded; row.ReadAt = null; row.BusinessDate = null; }
        _sources = []; IsLoadingSources = false; SourceError = null; SourceStamp = "摘要尚未加载";
        RefreshSourcesCommand.NotifyCanExecuteChanged();
    }
    /// <summary>切来源、筛选及会话失效时取消旧完整来源摘要。</summary>
    public void ResetDemands()
    {
        ++_demandVersion; _demandCancellation?.Cancel();
        foreach (var row in _demands) { row.Summary = null; row.Access = TrackingSummaryAccess.None; row.SummaryState = TrackingSummaryLoadState.NotLoaded; row.ReadAt = null; row.BusinessDate = null; }
        _demands = []; _sourceId = Guid.Empty; _sourceType = string.Empty; IsLoadingDemands = false; DemandError = null; DemandStamp = "摘要尚未加载";
        RefreshDemandsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>当前页独立重试；整批失败及缺失摘要不改变任何基础行。</summary>
    [RelayCommand(CanExecute = nameof(CanReadSources), AllowConcurrentExecutions = true)]
    private async Task RefreshSourcesAsync()
    {
        if (!CanReadSources()) return;
        var rows = _sources; var version = ++_sourceVersion; _sourceCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _sourceCancellation = cancellation;
        var token = cancellation.Token; var user = _user.CurrentUser;
        var keys = rows.Select(x => new OrderTrackingSourceKeyDto { SourceType = x.SourceType, SourceGroupId = x.SourceGroupId })
            .DistinctBy(x => (x.SourceType, x.SourceGroupId)).ToList();
        IsLoadingSources = true; SourceError = null; SourceStamp = "正在独立读取来源摘要…";
        foreach (var row in rows) { row.Summary = null; row.SummaryState = TrackingSummaryLoadState.Loading; }
        try
        {
            if (keys.Count > 200) { SourceError = "当前页来源超过摘要批量范围。"; SourceStamp = "摘要读取失败，基础事实保留"; foreach (var row in rows) row.SummaryState = TrackingSummaryLoadState.Failed; return; }
            var result = await _runner.RunAsync(() => _api.GetSourceSummariesAsync(new() { Sources = keys }, token), token, "来源摘要读取失败");
            if (token.IsCancellationRequested || version != _sourceVersion || !CanRead || !ReferenceEquals(user, _user.CurrentUser)) return;
            if (!result.Success || result.Data is null)
            {
                SourceError = result.Message; SourceStamp = "摘要读取失败，基础事实保留";
                foreach (var row in rows) row.SummaryState = result.Error?.Code == ServiceErrorCodes.Forbidden ? TrackingSummaryLoadState.Forbidden : TrackingSummaryLoadState.Failed;
                return;
            }
            var groups = result.Data.Items.GroupBy(x => (x.SourceType, x.SourceGroupId)).ToArray();
            if (groups.Any(x => x.Count() != 1) || groups.Any(x => !keys.Any(key => key.SourceType == x.Key.SourceType && key.SourceGroupId == x.Key.SourceGroupId)))
            { SourceError = "来源摘要返回重复或不匹配身份。"; SourceStamp = "摘要读取失败，基础事实保留"; foreach (var row in rows) row.SummaryState = TrackingSummaryLoadState.Failed; return; }
            var indexed = groups.ToDictionary(x => x.Key, x => x.Single());
            foreach (var row in rows)
            {
                row.Access = OrderTrackingAttentionPolicy.GetAccess(_user);
                if (!indexed.TryGetValue((row.SourceType, row.SourceGroupId), out var summary)) { row.SummaryState = TrackingSummaryLoadState.Failed; SourceError = "部分来源摘要缺失，请重试。"; continue; }
                row.Summary = summary; row.BusinessDate = result.Data.BusinessDate; row.ReadAt = result.Data.ReadAt; row.SummaryState = TrackingSummaryLoadState.Loaded;
            }
            SourceStamp = TrackingSummaryText.Stamp(result.Data.BusinessDate, result.Data.ReadAt);
            SourcesUpdated?.Invoke(this, EventArgs.Empty);
        }
        finally { if (version == _sourceVersion) IsLoadingSources = false; if (ReferenceEquals(_sourceCancellation, cancellation)) _sourceCancellation = null; }
    }

    /// <summary>完整来源一次获取全部摘要；基线版本不一致由行模型提示刷新，不自动重试。</summary>
    [RelayCommand(CanExecute = nameof(CanReadDemands), AllowConcurrentExecutions = true)]
    private async Task RefreshDemandsAsync()
    {
        if (!CanReadDemands()) return;
        var rows = _demands; var type = _sourceType; var id = _sourceId; var version = ++_demandVersion; _demandCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource(); _demandCancellation = cancellation;
        var token = cancellation.Token; var user = _user.CurrentUser;
        IsLoadingDemands = true; DemandError = null; DemandStamp = "正在独立读取完整来源摘要…";
        foreach (var row in rows) { row.Summary = null; row.SummaryState = TrackingSummaryLoadState.Loading; }
        try
        {
            var result = await _runner.RunAsync(() => _api.GetSourceDemandSummariesAsync(type, id, token), token, "成品需求摘要读取失败");
            if (token.IsCancellationRequested || version != _demandVersion || id != _sourceId || type != _sourceType || !CanRead || !ReferenceEquals(user, _user.CurrentUser)) return;
            if (!result.Success || result.Data is null)
            {
                DemandError = result.Message; DemandStamp = "摘要读取失败，基础事实保留";
                foreach (var row in rows) row.SummaryState = result.Error?.Code == ServiceErrorCodes.Forbidden ? TrackingSummaryLoadState.Forbidden : TrackingSummaryLoadState.Failed;
                return;
            }
            if (result.Data.SourceType != type || result.Data.SourceGroupId != id || result.Data.Items.Any(x => x.SourceType != type || x.SourceGroupId != id) || result.Data.Items.GroupBy(x => x.GrossDemandId).Any(x => x.Count() != 1))
            { DemandError = "需求摘要身份不一致或重复。"; DemandStamp = "摘要读取失败，基础事实保留"; foreach (var row in rows) row.SummaryState = TrackingSummaryLoadState.Failed; return; }
            var indexed = result.Data.Items.ToDictionary(x => x.GrossDemandId);
            foreach (var row in rows)
            {
                row.Access = OrderTrackingAttentionPolicy.GetAccess(_user);
                if (!indexed.TryGetValue(row.GrossDemandId, out var summary)) { row.SummaryState = TrackingSummaryLoadState.Failed; DemandError = "部分需求摘要缺失，请刷新基础或重试摘要。"; continue; }
                row.Summary = summary; row.BusinessDate = result.Data.BusinessDate; row.ReadAt = result.Data.ReadAt; row.SummaryState = TrackingSummaryLoadState.Loaded;
            }
            DemandStamp = TrackingSummaryText.Stamp(result.Data.BusinessDate, result.Data.ReadAt);
            DemandsUpdated?.Invoke(this, EventArgs.Empty);
        }
        finally { if (version == _demandVersion) IsLoadingDemands = false; if (ReferenceEquals(_demandCancellation, cancellation)) _demandCancellation = null; }
    }
    /// <summary>父页面负责会话失效，释放时取消并清空两个摘要上下文。</summary>
    public void Dispose() { if (_disposed) return; _disposed = true; ResetSources(); ResetDemands(); }
}
