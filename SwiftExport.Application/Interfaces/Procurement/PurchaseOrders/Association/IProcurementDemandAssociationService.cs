using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Procurement;

/// <summary>已有采购追加需求关联用例；供独立关联页面使用，不依赖采购详情的选择状态。</summary>
public interface IProcurementDemandAssociationService
{
    /// <summary>读取明确选定采购的分配上下文和有效贡献。</summary>
    Task<ServiceResult<ProcurementDemandAssociationContextDto>> GetContextAsync(Guid uid, CancellationToken ct = default);
    /// <summary>检索明确选定采购行的需求候选；所有选择由调用页面完成。</summary>
    Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>> SearchCandidatesAsync(Guid uid, ProcurementDemandCandidateSearchRequest request, CancellationToken ct = default);
    /// <summary>验证全部人工分配并生成只读令牌，不预留数量。</summary>
    Task<ServiceResult<ProcurementDemandAssociationPreviewDto>> PreviewAsync(Guid uid, PreviewProcurementDemandAssociationRequest request, CancellationToken ct = default);
    /// <summary>事务内重验后新增贡献和审计；相同命令重放原结果。</summary>
    Task<ServiceResult<ProcurementOrderCommandResult>> AssociateAsync(Guid uid, AssociateProcurementDemandsCommand command, CancellationToken ct = default);
}
