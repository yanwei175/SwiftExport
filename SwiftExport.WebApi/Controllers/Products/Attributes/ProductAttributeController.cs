using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products.Attributes;

/// <summary>共享标签定义的 HTTP 边界；查询供缓存使用，写入交给应用服务原子处理。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductAttributeController(IProductAttributeDefinitionService service) : ControllerBase
{
    /// <summary>一次读取全部有效组、选项及模板关联，保留未关联模板的组。</summary>
    [HttpGet("definitions")]
    public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken) =>
        service.GetDefinitionsAsync(null, cancellationToken);

    /// <summary>读取具体属性模板直接关联的完整标签定义及逐项版本。</summary>
    [HttpGet("definitions/profiles/{profileId:guid}")]
    public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetProfileDefinitionsAsync(Guid profileId, CancellationToken cancellationToken) =>
        service.GetDefinitionsAsync(profileId, cancellationToken);

    /// <summary>在同一事务中保存全页组、选项及模板关联变更。</summary>
    [HttpPost("definitions/batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(
        [FromBody] SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken) =>
        service.BatchSaveDefinitionsAsync(request, cancellationToken);

}
