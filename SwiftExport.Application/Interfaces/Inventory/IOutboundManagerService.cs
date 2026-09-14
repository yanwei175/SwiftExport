using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>出库管理用例；草稿与审计原子保存，正向过账通过库存内核提交，不开放反向。</summary>
public interface IOutboundManagerService
{
    /// <summary>只读预览当前已保存单据的正向过账。</summary>
    Task<ServiceResult<OutboundPostPreviewDto>> PreviewPostAsync(Guid uid, OutboundPostPreviewRequest request, CancellationToken token = default);
    /// <summary>在同一事务内提交出库状态、库存及业务日志。</summary>
    Task<ServiceResult<OutboundPostResult>> PostAsync(Guid uid, PostOutboundCommand command, CancellationToken token = default);
    /// <summary>分页读取管理摘要。</summary>
    Task<ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token = default);
    /// <summary>读取整张单据。</summary>
    Task<ServiceResult<OutboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default);
    /// <summary>按仓库余额选择产品，不查询采购或需求。</summary>
    Task<ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token = default);
    /// <summary>原子创建人工草稿。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken token = default);
    /// <summary>按整单版本原子保存完整明细。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken token = default);
    /// <summary>按版本软删除人工草稿。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken token = default);
}
