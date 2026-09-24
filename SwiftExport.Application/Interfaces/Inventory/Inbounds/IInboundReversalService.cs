using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Inbounds;

/// <summary>提供人工入库整单反向的只读预览与正式命令。</summary>
public interface IInboundReversalService
{
    /// <summary>根据原库存事实和当前余额预览可反向性，不写业务事实。</summary>
    Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, InboundReversePreviewRequest request, CancellationToken token = default);
    /// <summary>在同一事务中追加整单反向事实、状态、审计和可重放回执。</summary>
    Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, ReverseInboundCommand command, CancellationToken token = default);
}
