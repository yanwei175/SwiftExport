using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.DemandPlanning.Materials.Establishment;

/// <summary>定义从成品需求整批建立物料需求的权威用例。</summary>
public interface IMaterialDemandEstablishmentService
{

    /// <summary>按稳定来源顺序锁定并建立完整物料依据。</summary>
    Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishAsync(
        EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken = default);

}
