using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>定义物料领域从来源需求建立、回退及校验活动物料事实的权威用例。</summary>
public interface IMaterialDemandEstablishmentService
{

    Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>在调用方事务内锁定并判断来源需求是否存在活动物料需求。</summary>
    Task<bool> HasActiveAsync(Guid grossDemandId);
    /// <summary>
    /// 在调用方事务内批量锁定并评估来源需求的物料历史；不扫描 Link、采购、入库或交付事实。
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MaterialDemandSourceExitAssessment>> AssessSourceExitAsync(
        IEnumerable<Guid> grossDemandIds);
}
