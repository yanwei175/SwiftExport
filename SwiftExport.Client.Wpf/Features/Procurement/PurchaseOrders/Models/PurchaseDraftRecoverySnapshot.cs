namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

/// <summary>采购创建未决请求及其输入的应用内快照，不写磁盘，不代表后端取消或失败。</summary>
public sealed partial class PurchaseDraftRecoverySnapshot
{
    /// <summary>原始执行身份，用于防止新会话覆盖未决请求。</summary>
    public Guid ExecutionRequestId { get; init; }
    /// <summary>不可变原命令；重试不能重新从页面组装。</summary>
    public string CommandJson { get; init; } = string.Empty;
    /// <summary>保留全部已选候选输入，包括筛选隐藏项。</summary>
    public List<PurchaseCandidateItemModel> Rows { get; init; } = [];
    /// <summary>已本地校验的输入快照。</summary>
    public List<PurchaseDraftSelectionItemModel> Items { get; init; } = [];
    /// <summary>原新建或追加选择，恢复时只读。</summary>
    public List<PurchaseDraftTargetSelectionModel> Targets { get; init; } = [];
    /// <summary>已收到的逐来源结果；未知来源仍保留原请求。</summary>
    public List<PurchaseDraftCreateResultModel> Results { get; init; } = [];
    /// <summary>原请求的发送尝试次数。</summary>
    public int AttemptCount { get; init; }
    /// <summary>最后一次尝试开始时间。</summary>
    public DateTimeOffset? LastAttemptAt { get; init; }
    /// <summary>最后一次请求反馈，供重开时辨认。</summary>
    public string ResultSummary { get; init; } = string.Empty;
}
