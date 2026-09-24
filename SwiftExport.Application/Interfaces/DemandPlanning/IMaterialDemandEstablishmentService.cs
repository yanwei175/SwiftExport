using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning;

/// <summary>定义物料领域从来源需求建立及回退物料需求的权威用例。</summary>
public interface IMaterialDemandEstablishmentService
{

    Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackAsync(
        RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);

}
