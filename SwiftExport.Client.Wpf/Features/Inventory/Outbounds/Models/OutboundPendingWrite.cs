using SwiftExport.Contracts.DTOs.Inventory.Outbounds;

namespace SwiftExportClient.Features.Inventory.Outbounds.Models;

/// <summary>仅在当前窗口保留一次已发送意图；命令快照不能用后续编辑重新生成。</summary>
internal sealed class OutboundPendingWrite
{
    /// <summary>冻结命令发起账号，切换账号不得重放或接纳该命令的页面反馈。</summary>
    public string UserKey { get; set; } = string.Empty;
    /// <summary>合法详情已前进到原成功回执后的版本，反馈不能再假称原状态。</summary>
    public bool HasLaterDetail { get; set; }
    public Guid DocumentId { get; init; }
    public CreateOutboundDraftCommand? Create { get; init; }
    public UpdateOutboundDraftCommand? Update { get; init; }
    public DeleteOutboundDraftCommand? Delete { get; init; }
    public PostOutboundCommand? Post { get; init; }
    public ReverseOutboundCommand? Reverse { get; init; }
    public OutboundReverseResult? ReverseResult { get; set; }
    public bool WasUncertain { get; set; }
    public OutboundDraftCommandResult? AcceptedResult { get; set; }
}
