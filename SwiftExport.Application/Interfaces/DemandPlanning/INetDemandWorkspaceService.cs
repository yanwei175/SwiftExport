using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>待退出的混合接口：物料入口已迁移，其余方法按注释废弃或迁往采购领域。</summary>
public interface INetDemandWorkspaceService
{
    /// <summary>兼容入口，已迁移至 INetDemandServices.SearchSourceDemandCandidatesAsync；新调用方使用新接口。</summary>
    Task<ServiceResult<PagedResponseBase<SourceDemandCandidateDto>>> SearchSourceDemandCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>兼容入口，已迁移至 INetDemandServices.EstablishFromSourcesAsync；新调用方使用新接口。</summary>
    Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);
    /// <summary>兼容入口，已迁移至 INetDemandServices.RollbackSourceEstablishmentAsync；新调用方使用新接口。</summary>
    Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);
    /// <summary>兼容入口，已迁移至 INetDemandServices.SearchAsync；新调用方使用新接口。</summary>
    Task<ServiceResult<PagedResponseBase<NetDemandListItemDto>>> SearchAsync(
        NetDemandSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>兼容入口，已迁移至 INetDemandServices.SearchSourceGroupsAsync；新调用方使用新接口。</summary>
    Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchSourceGroupsAsync(
        NetDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default);
    /// <summary>兼容入口，已迁移至 INetDemandServices.GetDetailAsync；新调用方使用新接口。</summary>
    Task<ServiceResult<NetDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>待废弃：旧 Allocation 替代命令，独立计划退出后移除。</summary>
    Task<ServiceResult<NetDemandPlanCommandResult>> ReplacePlanAsync(ReplaceNetDemandPlanCommand command);
    /// <summary>待废弃：旧计划应用默认供应商，实际供应商选择归采购。</summary>
    Task<ServiceResult<NetDemandPlanCommandResult>> ApplyDefaultSupplierAsync(ApplyProductDefaultSupplierCommand command);
    /// <summary>待废弃：旧计划批量分配供应商，实际供应商选择归采购。</summary>
    Task<ServiceResult<NetDemandPlanCommandResult>> BatchAssignSupplierAsync(BatchAssignSupplierCommand command);
}
