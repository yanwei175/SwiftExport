using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products.Attributes;

/// <summary>分类标签定义的 HTTP 边界；查询供基础资料缓存使用，写入交给应用服务原子处理。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductAttributeController(IProductAttributeDefinitionService service) : ControllerBase
{
    /// <summary>一次读取全部有效组及选项，空数据也返回成功。</summary>
    [HttpGet("definitions")]
    public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken) =>
        service.GetDefinitionsAsync(null, cancellationToken);

    /// <summary>读取具体分类的完整标签定义和逐项版本。</summary>
    [HttpGet("definitions/{categoryId:guid}")]
    public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetCategoryDefinitionsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        service.GetDefinitionsAsync(categoryId, cancellationToken);

    /// <summary>在同一事务中保存当前分类的组及选项变更。</summary>
    [HttpPost("definitions/{categoryId:guid}/batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId,
        [FromBody] SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken) =>
        service.BatchSaveAsync(categoryId, request, cancellationToken);
}
