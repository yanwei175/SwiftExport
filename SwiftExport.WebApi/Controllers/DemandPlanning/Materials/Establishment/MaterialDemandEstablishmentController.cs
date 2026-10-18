using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Establishment;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Establishment;

/// <summary>转发成品需求整批建立物料需求命令。</summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class MaterialDemandEstablishmentController(
    IMaterialDemandEstablishmentService establishmentService) : ControllerBase
{
    /// <summary>从成品需求原子建立物料需求。</summary>
    [HttpPost("source-demands/establish")]
    public Task<ServiceResult<EstablishMaterialDemandsResult>> EstablishFromSourcesAsync(
        [FromBody] EstablishMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => establishmentService.EstablishAsync(command, cancellationToken);
}
