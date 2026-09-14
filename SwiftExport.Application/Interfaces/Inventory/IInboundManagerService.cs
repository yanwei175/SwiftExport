using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory;

/// <summary>入库管理非过账用例；所有写操作只影响草稿及审计。</summary>
public interface IInboundManagerService
{
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
