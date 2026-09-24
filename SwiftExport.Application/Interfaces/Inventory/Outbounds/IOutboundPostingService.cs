using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Outbounds;

/// <summary>预览并提交人工库存出库的正式过账。</summary>
public interface IOutboundPostingService
{
    /// <summary>只读计算过账阻断和库存影响，不预留库存。</summary>
    Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken token = default);
    /// <summary>按命令身份和期望版本整单过账。</summary>
    Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken token = default);
}
