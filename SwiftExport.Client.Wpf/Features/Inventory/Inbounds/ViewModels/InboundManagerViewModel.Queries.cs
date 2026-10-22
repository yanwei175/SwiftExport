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
    private string? _queryInputWarning;
    private bool _applyingList;
    private Guid? _documentUid;

    /// <summary>首次显示使用现有默认条件搜索；再次激活或已有本地数据时不覆盖页面。</summary>
    public Task InitializeAsync()
    {
        if (_initialSearchRequested || !CanRead()) return Task.CompletedTask;
        _initialSearchRequested = true;
        return Headers.Items.Count == 0 && !HasUnsavedChanges ? SearchAsync() : Task.CompletedTask;
    }

    /// <summary>追溯文档按稳定 Uid 读取最新头行，不以旧单号或过账状态筛选；已有编辑及未决命令不得被替换。</summary>
    public async Task InitializeDocumentAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not InboundDocumentOpenRequest request || request.InboundDocumentId == Guid.Empty)
            return;
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed) return;
        if (IsBusy && _readQuery is null) { ResultSummary = "当前操作尚未结束，不能切换入库文档。"; return; }
        if (!CommitGridEdits() || HasUnsavedChanges)
        { ResultSummary = "存在未保存修改或待确认命令，原编辑和命令已保留；请先处理后再打开入库文档。"; return; }
        IsReadOnly = true;
        _initialSearchRequested = true;
        _documentUid = request.InboundDocumentId;
        if (!HasViewPermission) { ResultSummary = "当前账号没有查看入库单的权限。"; return; }
        await LoadDocumentAsync(request.InboundDocumentId, cancellationToken);
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

    private bool CanRead() => !_disposed && HasViewPermission && _api is not null && _runner is not null && (IsNotBusy || _readQuery is not null);
    private bool CanSearch() => CanRead() && GetQueryInputError(Keyword, 1, PageSize) is null;

    /// <summary>显式应用输入条件，从第一页重新查询；输入改变本身不调用 API。</summary>
    [RelayCommand(CanExecute = nameof(CanSearch), AllowConcurrentExecutions = true)]
    private Task SearchAsync() => _documentUid is { } uid ? LoadDocumentAsync(uid) : QueryAsync(1, true);

    private bool CanRefresh() => SelectedItem is { } row && _pendingWrites.TryGetValue(row, out var pending)
        ? CanRecover(pending) : CanRead() || (CanEdit() && _persistence is not null);

    /// <summary>优先确认原命令或重载冲突单，其余情况按已应用条件刷新。</summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync()
    {
        if (_disposed || IsBusy) return Task.CompletedTask;
        if (SelectedItem is { } row && _pendingWrites.ContainsKey(row)) return RecoverSelectedAsync();
        if (RequirePendingRecovery()) return Task.CompletedTask;
        if (_documentUid is { } uid) return LoadDocumentAsync(uid);
        if (SelectedItem is { HasVersionConflict: true }) return ReloadConflictedAsync();
        return SelectedItem is { IsDetailLoaded: false } ? LoadSelectedDetailAsync() : QueryAsync(PageIndex, false);
    }

    /// <summary>翻页沿用上次成功查询条件，不隐式应用尚未查询的输入。</summary>
    [RelayCommand(CanExecute = nameof(CanRead), AllowConcurrentExecutions = true)]
    private Task PageChangeAsync(PageChangeRequest? request)
    {
        if (request is null || !CanRead()) return Task.CompletedTask;
        if (_documentUid is { } uid) return LoadDocumentAsync(uid);
        if (GetQueryInputError(_appliedSearch?.Keyword, request.NewIndex, request.NewSize) is { } error)
        { ResultSummary = error; return Task.CompletedTask; }
        return QueryAsync(request.NewIndex, false);
    }

    /// <summary>页面选择事件调用此命令；已加载或已编辑的单据直接复用，不清空本地明细。</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LoadSelectedDetailAsync()
    {
        var row = SelectedItem;
        if (_applyingList || !CanRead() || row is null || row.IsPersistencePending || row.IsDetailLoaded || row.IsNew) return Task.CompletedTask;
        return ExecuteReadAsync((token, id) => LoadDetailCoreAsync(row, token, id), "加载入库详情");
    }

    /// <summary>先确认放弃编辑，成功响应后才替换集合，失败保留页码、勾选和编辑。</summary>
    private Task QueryAsync(int page, bool applyEditedFilters)
    {
        if (!CanRead() || RequirePendingRecovery() || !CommitGridEdits()) return Task.CompletedTask;
        var applied = applyEditedFilters ? null : _appliedSearch;
        var request = new InboundManagerSearchRequest
        {
            Keyword = applied is null ? Keyword?.Trim() : applied.Keyword,
            // 请求独立复制选择；筛选控件后续清选、多选不能改变正在执行或已应用的查询。
            DocumentTypes = applied is null ? SelectedDocumentTypes.Distinct().ToList() : applied.DocumentTypes.ToList(),
            PostingStatuses = applied is null ? SelectedPostingStatuses.Distinct().ToList() : applied.PostingStatuses.ToList(),
            PageIndex = page,
            PageSize = PageSize
        };
        if (GetQueryInputError(request.Keyword, request.PageIndex, request.PageSize) is { } error)
        { SetQueryInputWarning(error); return Task.CompletedTask; }
        return ExecuteReadAsync(async (token, id) =>
        {
            if (HasUnsavedChanges && !await _dialogs.ConfirmAsync("重新查询入库单",
                "当前页有未保存修改，查询成功后将放弃这些修改，是否继续？", cancellationToken: token)) return;
            if (!IsCurrentRead(token, id)) return;
            var revision = _editRevision;
            var result = await _runner!.RunAsync(() => _api!.SearchAsync(request, token), token, "入库列表查询失败");
            if (!IsCurrentRead(token, id)) return;
            if (revision != _editRevision)
            { ResultSummary = "查询期间产生了新的本地修改，结果未覆盖当前编辑，请处理后重新查询。"; return; }
            if (!result.Success || result.Data is null)
            { ResultSummary = result.Message ?? "入库列表查询失败，已有编辑已保留。"; return; }
            var data = result.Data;
            if (data.Items is null || data.Items.Any(x => x.Uid == Guid.Empty)
                || data.Items.Select(x => x.Uid).Distinct().Count() != data.Items.Count)
            { ResultSummary = "入库列表响应缺少有效单据身份，未替换当前数据。"; return; }
            var selectedId = SelectedItem?.Uid;
            _applyingList = true;
            try
            {
                Headers.LoadFromDtos(data.Items.Adapt<List<InboundHeaderDetailDto>>());
                foreach (var row in Headers.Items)
                {
                    row.IsDetailLoaded = false;
                    row.SummaryQuantity = data.Items.First(x => x.Uid == row.Uid).TotalQuantity;
                }
                _appliedSearch = request;
                PageIndex = page; TotalCount = data.TotalCount;
                SelectedItem = Headers.Items.FirstOrDefault(x => x.Uid == selectedId) ?? Headers.Items.FirstOrDefault();
            }
            finally { _applyingList = false; }
            NotifyEditState();
            ResultSummary = Headers.Items.Count == 0 ? "没有符合条件的入库单。" : "列表已加载；已保存草稿可预览过账，已过账人工单可按资格整单反过账。";
            if (SelectedItem is not null) await LoadDetailCoreAsync(SelectedItem, token, id);
        }, "查询入库单");
    }

    /// <summary>详情只回填发起请求的同一单据；失败保持只读，不能将未加载明细作为空草稿编辑。</summary>
    private async Task LoadDetailCoreAsync(InboundHeaderModel row, CancellationToken token, long id)
    {
        var revision = _editRevision;
        var detail = await ReadDetailAsync(row.Uid, token, id);
        if (!IsCurrentRead(token, id) || !ReferenceEquals(SelectedItem, row) || !Headers.Items.Contains(row)) return;
        if (revision != _editRevision)
        { ResultSummary = "读取期间产生了新的本地修改，详情未覆盖当前编辑。"; return; }
        if (detail is null) return;
        AcceptDetail(row, detail);
        ResultSummary = row.CanEdit ? "详情已加载，可编辑并保存草稿；保存不增加库存。"
            : "当前单据只读：仅人工、活动、未过账且非 PackingList 触发的草稿可编辑。";
    }

    /// <summary>只读追溯直接取得单个最新详情；成功后才替换集合，取消、失败或新输入均保留已有数据。</summary>
    private Task LoadDocumentAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (!CanRead()) { if (!_disposed) ResultSummary = "入库详情读取不可用，请确认权限后重试。"; return Task.CompletedTask; }
        if (!CommitGridEdits() || HasUnsavedChanges) { ResultSummary = "原编辑和待确认命令已保留，不能覆盖当前入库文档。"; return Task.CompletedTask; }
        return ExecuteReadAsync(async (token, id) =>
        {
            var revision = _editRevision;
            var detail = await ReadDetailAsync(uid, token, id);
            if (!IsCurrentRead(token, id) || detail is null) return;
            if (revision != _editRevision || HasUnsavedChanges) { ResultSummary = "读取期间产生了新的本地修改，详情未覆盖原内容。"; return; }
            _applyingList = true;
            try
            {
                Headers.LoadFromDtos([detail.Header]);
                var row = Headers.Items.Single();
                AcceptDetail(row, detail);
                SelectedItem = row;
                SelectedDocumentTypes.Clear(); SelectedDocumentTypes.Add(detail.Header.DocumentType);
                SelectedPostingStatuses.Clear(); SelectedPostingStatuses.Add(detail.Header.PostingStatus);
                Keyword = detail.Header.DocumentNo ?? string.Empty;
                PageIndex = 1; TotalCount = 1; _appliedSearch = null;
            }
            finally { _applyingList = false; }
            NotifyEditState();
            ResultSummary = $"已按 Uid 加载最新入库详情，当前状态 {detail.Header.PostingStatus}；此文档只读。";
        }, "读取最新入库详情", cancellationToken);
    }

    /// <summary>统一一次 Get 的身份、行归属和版本校验，列表懒加载与追溯文档不重复发请求。</summary>
    private async Task<InboundManagerDetailDto?> ReadDetailAsync(Guid uid, CancellationToken token, long id)
    {
        var result = await _runner!.RunAsync(() => _api!.GetAsync(uid, token), token, "入库详情查询失败");
        if (!IsCurrentRead(token, id)) return null;
        if (!result.Success || result.Data is null) { ResultSummary = result.Message ?? "入库详情加载失败，请点击刷新重试。"; return null; }
        if (!ValidDetail(result.Data, uid)) { ResultSummary = "入库详情身份、版本或行归属不匹配，未接纳数据。"; return null; }
        return result.Data;
    }

    /// <summary>接纳同一单据的完整头行快照，保持原有脏状态接纳和订阅边界。</summary>
    private void AcceptDetail(InboundHeaderModel row, InboundManagerDetailDto detail)
    {
        row.Load(detail.Header);
        row.Lines.LoadFromDtos(detail.Lines);
        row.IsDetailLoaded = true;
        row.HasVersionConflict = false;
        NotifyEditState();
    }

    /// <summary>每个读取拥有独立取消源与请求身份；旧请求及释放后的延续不得修改页面。</summary>
    private async Task ExecuteReadAsync(Func<CancellationToken, long, Task> action, string message, CancellationToken cancellationToken = default)
    {
        _readQuery?.Cancel();
        using var query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
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

    private void OnDetailStateChanged(object? sender, EventArgs e) { _editRevision++; NotifyEditState(); }

    /// <summary>查询前校验契约上限；翻页只校验已应用条件，不受未查询输入阻断。</summary>
    private static string? GetQueryInputError(string? keyword, int page, int size) => keyword?.Trim().Length > InboundQueryLimits.KeywordMaxLength
        ? $"查询关键词最多 {InboundQueryLimits.KeywordMaxLength} 字。"
        : page < 1 || size is < 1 or > InboundQueryLimits.MaxPageSize ? $"页码须为正数，每页最多 {InboundQueryLimits.MaxPageSize} 条。" : null;

    /// <summary>只撤销本输入拥有的旧提示，不覆盖其后出现的 API 结果或业务反馈。</summary>
    partial void OnKeywordChanged(string value)
    {
        var error = GetQueryInputError(value, 1, PageSize);
        if (error is not null) SetQueryInputWarning(error);
        else { if (_queryInputWarning is not null && ResultSummary == _queryInputWarning) ResultSummary = "查询条件已修正，请点击查询。"; _queryInputWarning = null; }
        SearchCommand.NotifyCanExecuteChanged();
    }

    /// <summary>记录本地输入提示的所有权，供纠正时只清理本提示。</summary>
    private void SetQueryInputWarning(string error) { _queryInputWarning = error; ResultSummary = error; }
}
