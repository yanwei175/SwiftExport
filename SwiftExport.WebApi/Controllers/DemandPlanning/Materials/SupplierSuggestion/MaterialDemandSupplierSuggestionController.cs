using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.DemandPlanning.Materials.SupplierSuggestion;

/// <summary>转发物料需求推荐采购供应商的局部维护命令。</summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class MaterialDemandSupplierSuggestionController(
    IMaterialDemandSupplierSuggestionService suggestionService) : ControllerBase
{
    /// <summary>逐行保存推荐采购供应商，以命令回执和版本防重；不使用接口级冷却阻断同批其他物料行。</summary>
    [HttpPost("suggested-purchase-supplier")]
    public Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        [FromBody] UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken)
        => suggestionService.UpdateSuggestedPurchaseSupplierAsync(command, cancellationToken);
}
