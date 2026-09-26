using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.DemandPlanning.Materials.Reversal;

/// <summary>转发来源需求对应物料修订的整批回退命令。</summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class MaterialDemandReversalController(
    IMaterialDemandReversalService reversalService) : ControllerBase
{
    /// <summary>安全回退来源对应的活动物料需求修订。</summary>
    [HttpPost("source-demands/rollback")]
    public Task<ServiceResult<RollbackMaterialDemandsResult>> RollbackSourceEstablishmentAsync(
        [FromBody] RollbackMaterialDemandsCommand command,
        CancellationToken cancellationToken)
        => reversalService.RollbackAsync(command, cancellationToken);
}
