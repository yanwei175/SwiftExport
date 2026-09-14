using SwiftExport.Contracts.DTOs.Inventory.Outbounds;

namespace SwiftExportClient.Features.Inventory.Outbounds.Models;

/// <summary>仅在当前窗口保留一次已发送意图；命令快照不能用后续编辑重新生成。</summary>
internal sealed class OutboundPendingWrite
{
    public Guid DocumentId { get; init; }
    public CreateOutboundDraftCommand? Create { get; init; }
    public UpdateOutboundDraftCommand? Update { get; init; }
    public DeleteOutboundDraftCommand? Delete { get; init; }
    public PostOutboundCommand? Post { get; init; }
    public bool WasUncertain { get; set; }
    public OutboundDraftCommandResult? AcceptedResult { get; set; }
}
