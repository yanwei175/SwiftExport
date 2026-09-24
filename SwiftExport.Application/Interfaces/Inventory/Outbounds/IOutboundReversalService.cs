using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Outbounds;

/// <summary>预览并执行人工库存出库的整单反过账。</summary>
public interface IOutboundReversalService
{
    /// <summary>只读证明原库存事实与当前余额可完整反向。</summary>
    Task<ServiceResult<OutboundReversePreviewDto>> PreviewReverseAsync(Guid uid, OutboundReversePreviewRequest request, CancellationToken token = default);
    /// <summary>按命令身份整单恢复库存并保留原单事实。</summary>
    Task<ServiceResult<OutboundReverseResult>> ReverseAsync(Guid uid, ReverseOutboundCommand command, CancellationToken token = default);
}
