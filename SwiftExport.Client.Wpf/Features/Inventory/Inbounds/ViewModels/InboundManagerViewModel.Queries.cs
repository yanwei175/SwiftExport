using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mapster;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Resources.Controls;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>入库管理只读 API 接线，负责分页快照、详情懒加载和过期结果保护。</summary>
public sealed partial class InboundManagerViewModel
{
    private readonly IInboundManagerApi? _api;
    private readonly IApiCallRunner? _runner;
    private readonly HashSet<InboundHeaderModel> _observedHeaders = [];
    private CancellationTokenSource? _readQuery;
    private long _readRequestId;
    private InboundManagerSearchRequest? _appliedSearch;
    private bool _initialSearchRequested;

    /// <summary>首次显示使用现有默认条件搜索；再次激活或已有本地数据时不覆盖页面。</summary>
    public Task InitializeAsync()
    {
        if (_initialSearchRequested || !CanRead()) return Task.CompletedTask;
        _initialSearchRequested = true;
        return Headers.Items.Count == 0 && !HasUnsavedChanges ? SearchAsync() : Task.CompletedTask;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int PageIndex { get; private set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageStatus))]
    public partial int TotalCount { get; private set; }

    /// <summary>固定服务端分页大小，明细仅在选中单据后获取。</summary>
    public int PageSize => 50;

    /// <summary>当前契约接线固定每页五十条，不提供尚未处理的页大小切换。</summary>
    public IReadOnlyList<int> PageSizeOptions { get; } = [50];

    private bool CanRead() => !_disposed && _api is not null && _runner is not null && (IsNotBusy || _readQuery is not null);

    /// <summary>显式应用输入条件，从第一页重新查询；输入改变本身不调用 API。</summary>
    [RelayCommand(CanExecute = nameof(CanRead), AllowConcurrentExecutions = true)]
    private Task SearchAsync() => QueryAsync(1, true);

    private bool CanRefresh() => CanRead() || (CanEdit() && _persistence is not null);

    /// <summary>优先确认原命令或重载冲突单，其余情况按已应用条件刷新。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        if (_disposed || IsBusy) return Task.CompletedTask;
        if (SelectedItem is { } row && _pendingWrites.ContainsKey(row)) return RecoverSelectedAsync();
        if (RequirePendingRecovery()) return Task.CompletedTask;
        if (SelectedItem is { HasVersionConflict: true }) return ReloadConflictedAsync();
        return SelectedItem is { IsDetailLoaded: false } ? LoadSelectedDetailAsync() : QueryAsync(PageIndex, false);
    }

    /// <summary>翻页沿用上次成功查询条件，不隐式应用尚未查询的输入。</summary>
    [RelayCommand(CanExecute = nameof(CanRead), AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request) => request is null ? Task.CompletedTask : QueryAsync(Math.Max(1, request.NewIndex), false);

    /// <summary>页面选择事件调用此命令；已加载或已编辑的单据直接复用，不清空本地明细。</summary>
    [RelayCommand]
    private Task LoadSelectedDetailAsync()
    {
        var row = SelectedItem;
        if (!CanRead() || IsBusy || row is null || row.IsPersistencePending || row.IsDetailLoaded || row.IsNew) return Task.CompletedTask;
        return ExecuteReadAsync((token, id) => LoadDetailCoreAsync(row, token, id), "加载入库详情");
    }

    /// <summary>先确认放弃编辑，成功响应后才替换集合，失败保留页码、勾选和编辑。</summary>
    private Task QueryAsync(int page, bool applyEditedFilters)
    {
        if (!CanRead() || RequirePendingRecovery() || !CommitGridEdits()) return Task.CompletedTask;
        var applied = applyEditedFilters ? null : _appliedSearch;
        var request = new InboundManagerSearchRequest
        {
            Keyword = applied is null ? Keyword : applied.Keyword,
            DocumentType = applied is null ? SelectedDocumentType : applied.DocumentType,
            PostingStatus = applied is null ? SelectedPostingStatus : applied.PostingStatus,
            PageIndex = page,
            PageSize = PageSize
        };
        return ExecuteReadAsync(async (token, id) =>
        {
            if (HasUnsavedChanges && !await _dialogs.ConfirmAsync("重新查询入库单",
                "当前页有未保存修改，查询成功后将放弃这些修改，是否继续？", cancellationToken: token)) return;
            if (!IsCurrentRead(token, id)) return;
            var result = await _runner!.RunAsync(() => _api!.SearchAsync(request, token), token, "入库列表查询失败");
            if (!IsCurrentRead(token, id)) return;
            if (!result.Success || result.Data is null)
            { ResultSummary = result.Message ?? "入库列表查询失败，已有编辑已保留。"; return; }
            var data = result.Data;
            if (data.Items is null || data.Items.Any(x => x.Uid == Guid.Empty)
                || data.Items.Select(x => x.Uid).Distinct().Count() != data.Items.Count)
            { ResultSummary = "入库列表响应缺少有效单据身份，未替换当前数据。"; return; }
            var selectedId = SelectedItem?.Uid;
            Headers.LoadFromDtos(data.Items.Adapt<List<InboundHeaderDetailDto>>());
            foreach (var row in Headers.Items)
            {
                row.IsDetailLoaded = false;
                row.SummaryQuantity = data.Items.First(x => x.Uid == row.Uid).TotalQuantity;
            }
            _appliedSearch = request;
            PageIndex = page; TotalCount = data.TotalCount;
            SelectedItem = Headers.Items.FirstOrDefault(x => x.Uid == selectedId) ?? Headers.Items.FirstOrDefault();
            NotifyEditState();
            ResultSummary = Headers.Items.Count == 0 ? "没有符合条件的入库单。" : "列表已加载；已保存草稿可预览过账，暂不支持反过账。";
            if (SelectedItem is not null) await LoadDetailCoreAsync(SelectedItem, token, id);
        }, "查询入库单");
    }

    /// <summary>详情只回填发起请求的同一单据；失败保持只读，不能将未加载明细作为空草稿编辑。</summary>
    private async Task LoadDetailCoreAsync(InboundHeaderModel row, CancellationToken token, long id)
    {
        var result = await _runner!.RunAsync(() => _api!.GetAsync(row.Uid, token), token, "入库详情查询失败");
        if (!IsCurrentRead(token, id) || !ReferenceEquals(SelectedItem, row) || !Headers.Items.Contains(row)) return;
        if (!result.Success || result.Data is null)
        { ResultSummary = result.Message ?? "入库详情加载失败，请点击刷新重试。"; return; }
        var detail = result.Data;
        if (detail.Header is null || detail.Header.Uid != row.Uid || detail.Lines is null
            || detail.Lines.Any(x => x.InboundDocumentId != row.Uid || x.Uid == Guid.Empty))
        { ResultSummary = "入库详情身份与当前单据不匹配，未接纳数据。"; return; }
        row.Load(detail.Header);
        row.Lines.LoadFromDtos(detail.Lines);
        row.IsDetailLoaded = true;
        row.HasVersionConflict = false;
        NotifyEditState();
        ResultSummary = row.CanEdit ? "详情已加载，可编辑并保存草稿；保存不增加库存。"
            : "当前单据只读：仅人工、活动、未过账且非 PackingList 触发的草稿可编辑。";
    }

    /// <summary>每个读取拥有独立取消源与请求身份；旧请求及释放后的延续不得修改页面。</summary>
    private async Task ExecuteReadAsync(Func<CancellationToken, long, Task> action, string message)
    {
        _readQuery?.Cancel();
        using var query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _readQuery = query;
        var id = ++_readRequestId;
        await RunBusyAsync(async () =>
        {
            try { await action(query.Token, id); }
            catch (OperationCanceledException) when (query.IsCancellationRequested) { }
            catch (Exception exception)
            {
                System.Diagnostics.Trace.TraceError("入库读取失败：{0}", exception.GetType().Name);
                if (IsCurrentRead(query.Token, id)) ResultSummary = "入库数据读取失败，请重试；已有数据未主动清空。";
            }
            finally { if (ReferenceEquals(_readQuery, query)) _readQuery = null; }
        }, message, skipIfBusy: false);
    }

    private bool IsCurrentRead(CancellationToken token, long id) => !_disposed && !token.IsCancellationRequested && id == _readRequestId;

    /// <summary>集合替换或删除时对称解除明细事件，避免旧单据持有页面和误报脏状态。</summary>
    private void SynchronizeDetailSubscriptions()
    {
        var current = Headers.AllLines.ToHashSet();
        foreach (var row in _observedHeaders.Except(current).ToArray())
        { row.DetailStateChanged -= OnDetailStateChanged; _observedHeaders.Remove(row); }
        foreach (var row in current.Except(_observedHeaders))
        { row.DetailStateChanged += OnDetailStateChanged; _observedHeaders.Add(row); }
    }

    private void OnDetailStateChanged(object? sender, EventArgs e) => NotifyEditState();
}
