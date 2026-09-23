using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products;

/// <summary>中性产品 HTTP 边界，仅转发独立明细保存请求。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductNeutralController(IProductMasterService service) : ControllerBase
{
    /// <summary>原子保存当前主型号的明细，禁止短时间重复提交。</summary>
    [HttpPost("{productMasterId:guid}/batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<ProductMasterDetailDto>> BatchSaveAsync(
        Guid productMasterId,
        [FromBody] BatchSaveRequestBase<ProductNeutralWriteDto> request,
        CancellationToken cancellationToken) => service.SaveNeutralsAsync(productMasterId, request, cancellationToken);
}
