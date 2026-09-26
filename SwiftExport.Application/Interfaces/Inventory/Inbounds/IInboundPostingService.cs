using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Inbounds;

/// <summary>提供人工采购入库只读过账预览与正式过账命令。</summary>
public interface IInboundPostingService
{
    /// <summary>按已保存单据和版本计算只读预览，不预留库存。</summary>
    Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, InboundPostPreviewRequest request, CancellationToken token = default);
    /// <summary>复核最新来源及单据事实，在同一事务提交库存、状态、审计和回执。</summary>
    Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, PostInboundCommand command, CancellationToken token = default);
}
