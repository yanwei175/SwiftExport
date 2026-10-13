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
    /// <summary>一次读取全部有效组、选项及分类直接关联，保留未关联分类的组。</summary>
    [HttpGet("definitions")]
    public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken) =>
        service.GetDefinitionsAsync(null, cancellationToken);

    /// <summary>读取具体分类及祖先提供的完整标签定义、来源关联和逐项版本。</summary>
    [HttpGet("definitions/{categoryId:guid}")]
    public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetCategoryDefinitionsAsync(Guid categoryId, CancellationToken cancellationToken) =>
        service.GetDefinitionsAsync(categoryId, cancellationToken);

    /// <summary>在同一事务中保存全页组、选项及分类关联变更。</summary>
    [HttpPost("definitions/batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(
        [FromBody] SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken) =>
        service.BatchSaveDefinitionsAsync(request, cancellationToken);

    /// <summary>旧分类维护端点只返回升级提示，不接受全局定义写入。</summary>
    [HttpPost("definitions/{categoryId:guid}/batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId,
        [FromBody] SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken) =>
        service.BatchSaveAsync(categoryId, request, cancellationToken);
}
