using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products;

/// <summary>主产品 HTTP 边界，仅负责路由、绑定和委托应用服务。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductMasterController(IProductMasterService service) : ControllerBase
{
    /// <summary>分页查询主型号。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(
        [FromBody] ProductMasterSearchRequest request,
        CancellationToken cancellationToken) => service.GetPagedAsync(request, cancellationToken);

    /// <summary>批量设置主型号分类并防止短时间重复提交。</summary>
    [HttpPost("assign-category")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(
        [FromBody] AssignProductMastersToCategoryRequest request,
        CancellationToken cancellationToken) => service.AssignCategoryAsync(request, cancellationToken);

    /// <summary>读取主型号及全部有效中性产品。</summary>
    [HttpGet("{productMasterId:guid}")]
    public Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(
        Guid productMasterId,
        CancellationToken cancellationToken) => service.GetDetailAsync(productMasterId, cancellationToken);

    /// <summary>批量保存管理页主型号变更并防止短时间重复提交。</summary>
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(
        [FromBody] ProductMasterBatchSaveRequest request,
        CancellationToken cancellationToken) => service.BatchSaveAsync(request, cancellationToken);

    /// <summary>原子保存主型号详情及中性产品变更并防止短时间重复提交。</summary>
    [HttpPost("save-detail")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(
        [FromBody] SaveProductMasterDetailRequest request,
        CancellationToken cancellationToken) => service.SaveDetailAsync(request, cancellationToken);
}
