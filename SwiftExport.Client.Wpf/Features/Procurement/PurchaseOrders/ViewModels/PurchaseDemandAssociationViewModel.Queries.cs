using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>关联工作区的只读加载；仅在当前请求仍属于存活页面时接纳数据。</summary>
public sealed partial class PurchaseDemandAssociationViewModel
{
    /// <summary>成功读取才替换采购；同单刷新保留意图并标为待复核，不能悄悄覆盖版本。</summary>
    private async Task LoadContextCoreAsync(Guid orderId, bool replaceOrder)
    {
        _query?.Cancel(); using var query = new CancellationTokenSource(); _query = query;
        try
        {
            var result = await _runner.RunAsync(() => _api.GetDemandAssociationContextAsync(orderId, query.Token), query.Token, "读取关联上下文失败");
            if (!IsCurrentQuery(query)) return;
            if (!result.Success || result.Data is not { } context) { StatusText = result.Message; return; }
            if (context.PurchaseOrderId != orderId || context.Version <= 0 || context.Lines.Any(x => x.PurchaseOrderLineId == Guid.Empty || x.Version <= 0)
                || context.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != context.Lines.Count)
            { StatusText = "关联上下文不完整，请重新读取。"; return; }
            // 成功回执已证明版本前进，迟到或缓存的旧上下文不能重新开放可分配数量。
            if (!replaceOrder && context.Version < Math.Max(Context?.Version ?? 0, _minimumReadVersion))
            { StatusText = "尚未读取到最新采购版本，请重读关联依据。"; return; }
            if (replaceOrder) ClearPlan();
            else { foreach (var row in Plan) row.NeedsReview = true; InvalidatePreview(); }
            Context = context; Lines.Clear(); foreach (var line in context.Lines) Lines.Add(line);
            _minimumReadVersion = 0;
            IsPickerOpen = false;
            SelectedLine = null; Candidates.Clear(); SelectedCandidate = null; CandidatePage = 1; CandidateTotal = 0;
            NeedsReload = false; RefreshRequired = false;
            OnPropertyChanged(nameof(SourceHint));
            StatusText = Plan.Count > 0 ? "已重读采购，暂存意图保留；请逐条重新选择候选并加入清单复核。"
                : "请选择采购行查询需求；可按剩余未关联数量多次追加，不要求全部关联。";
        }
        finally { if (ReferenceEquals(_query, query)) _query = null; }
    }
    /// <summary>候选只属于本次明确采购行及锁定来源；分页不改变暂存清单。</summary>
    private async Task LoadCandidatesCoreAsync(int page)
    {
        if (_disposed || !HasPermission || HasPendingCommand || NeedsReload || RefreshRequired || Context is null || SelectedLine is null) return;
        var context = Context; var line = SelectedLine; var first = Plan.FirstOrDefault();
        _query?.Cancel(); using var query = new CancellationTokenSource(); _query = query;
        try
        {
            var result = await _runner.RunAsync(() => _api.SearchDemandAssociationCandidatesAsync(context.PurchaseOrderId, new()
            {
                PurchaseOrderLineId = line.PurchaseOrderLineId, SourceUid = context.SourceUid ?? first?.Demand.SourceUid,
                SourceType = context.SourceUid.HasValue ? context.SourceType : first?.Demand.SourceType,
                Keyword = CandidateKeyword.Trim(), PageIndex = page, PageSize = 30
            }, query.Token), query.Token, "读取物料候选失败");
            if (!IsCurrentQuery(query) || !ReferenceEquals(Context, context) || !ReferenceEquals(SelectedLine, line)) return;
            if (!result.Success || result.Data is null) { StatusText = result.Message; return; }
            Candidates.Clear(); foreach (var candidate in result.Data.Items) Candidates.Add(candidate);
            SelectedCandidate = null; CandidatePage = page; CandidateTotal = result.Data.TotalCount;
            StatusText = $"共 {CandidateTotal} 条有剩余可分配数量的物料需求，第 {CandidatePage} 页。";
        }
        finally { if (ReferenceEquals(_query, query)) _query = null; }
    }
    private bool IsCurrentQuery(CancellationTokenSource query)
        => !_disposed && !query.IsCancellationRequested && ReferenceEquals(_query, query);
}
