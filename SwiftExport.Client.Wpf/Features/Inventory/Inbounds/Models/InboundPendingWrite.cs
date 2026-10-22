
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
namespace SwiftExportClient.Features.Inventory.Inbounds.Models;

/// <summary>仅在当前窗口保留一次已发送意图；命令快照不能用后续编辑重新生成。</summary>
internal sealed class InboundPendingWrite
{
    /// <summary>原命令所属账号；切换账号不能重放或接纳其页面状态。</summary>
    public string UserKey { get; set; } = string.Empty;
    /// <summary>详情已合法前进至成功回执后的版本，反馈时不得再描述为原状态。</summary>
    public bool HasLaterDetail { get; set; }
    public Guid DocumentId { get; init; }
    public CreateInboundDraftCommand? Create { get; init; }
    public UpdateInboundDraftCommand? Update { get; init; }
    public DeleteInboundDraftCommand? Delete { get; init; }
    public PostInboundCommand? Post { get; init; }
    public ReverseInboundCommand? Reverse { get; init; }
    public InboundReverseResult? ReverseResult { get; set; }
    public bool WasUncertain { get; set; }
    public InboundDraftCommandResult? AcceptedResult { get; set; }
}
