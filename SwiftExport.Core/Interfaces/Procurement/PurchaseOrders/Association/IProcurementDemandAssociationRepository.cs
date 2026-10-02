using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>已有采购行的需求候选、执行身份读取和定向关联写入；不修改库存或原采购数量。</summary>
public interface IProcurementDemandAssociationRepository
{
    /// <summary>检索同客户和相容产品的待关联物料；不占用容量，提交仍需重验。</summary>
    Task<PagedResponseBase<ProcurementDemandCandidateDto>> SearchCandidatesAsync(Guid uid, ProcurementDemandCandidateSearchRequest request, CancellationToken ct);
    /// <summary>读取锁定需求贡献的活动采购目标及供应商；来源锁已由 Application 取得。</summary>
    Task<IReadOnlyList<ProcurementAssociationTarget>> ReadTargetsAsync(IReadOnlyCollection<Guid> lineIds, CancellationToken ct);
    /// <summary>按头行版本定向写入来源摘要和未关联量；任一步失败立即停止，由公共事务回滚。</summary>
    Task<RepositoryWriteResult> ApplyAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct);
}
