using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>入库管理用例；草稿与审计原子保存，正向过账通过库存内核提交，支持人工整单反向。</summary>
public interface IInboundManagerService
{
    /// <summary>只读预览原事实与整单余额扣减。</summary>
    Task<ServiceResult<InboundReversePreviewDto>> PreviewReverseAsync(Guid uid, InboundReversePreviewRequest request, CancellationToken token = default);
    /// <summary>原子追加反向库存事实并将原单保持只读。</summary>
    Task<ServiceResult<InboundReverseResult>> ReverseAsync(Guid uid, ReverseInboundCommand command, CancellationToken token = default);
    /// <summary>只读预览当前已保存单据的正向过账。</summary>
    Task<ServiceResult<InboundPostPreviewDto>> PreviewPostAsync(Guid uid, InboundPostPreviewRequest request, CancellationToken token = default);
    /// <summary>在同一事务内提交入库状态、库存及业务日志。</summary>
    Task<ServiceResult<InboundPostResult>> PostAsync(Guid uid, PostInboundCommand command, CancellationToken token = default);
    /// <summary>分页读取管理摘要。</summary>
    Task<ServiceResult<PagedResponseBase<InboundManagerListItemDto>>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token = default);
    /// <summary>读取整张单据。</summary>
    Task<ServiceResult<InboundManagerDetailDto>> GetAsync(Guid uid, CancellationToken token = default);
    /// <summary>按采购行选源，不按仓库筛选。</summary>
    Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token = default);
    /// <summary>原子创建人工草稿。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> CreateAsync(CreateInboundDraftCommand command, CancellationToken token = default);
    /// <summary>按整单版本原子保存完整明细。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken token = default);
    /// <summary>按版本软删除人工草稿。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteInboundDraftCommand command, CancellationToken token = default);
}
