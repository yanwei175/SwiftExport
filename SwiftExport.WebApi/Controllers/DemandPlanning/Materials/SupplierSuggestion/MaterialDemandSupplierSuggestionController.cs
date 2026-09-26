using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.DemandPlanning.Materials.SupplierSuggestion;

/// <summary>转发物料需求推荐采购供应商的局部维护命令。</summary>
[ApiController]
[Route("api/demand-planning/net-demands")]
public sealed class MaterialDemandSupplierSuggestionController(
    IMaterialDemandSupplierSuggestionService suggestionService) : ControllerBase
{
    /// <summary>保存推荐采购供应商，不修改既有采购与送达事实。</summary>
    [HttpPost("suggested-purchase-supplier")]
    [PreventDuplicateRequest(WindowSeconds = 3)]
    public Task<ServiceResult<UpdateSuggestedPurchaseSupplierResult>> UpdateSuggestedPurchaseSupplierAsync(
        [FromBody] UpdateSuggestedPurchaseSupplierCommand command,
        CancellationToken cancellationToken)
        => suggestionService.UpdateSuggestedPurchaseSupplierAsync(command, cancellationToken);
}
