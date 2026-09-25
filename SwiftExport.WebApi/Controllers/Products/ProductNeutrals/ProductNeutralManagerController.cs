using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Query;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products.ProductNeutrals;

/// <summary>中性产品管理 HTTP 边界，仅负责分页与批量保存请求转发。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductNeutralManagerController(IProductNeutralQueryService query, IProductNeutralBatchMaintenanceService maintenance) : ControllerBase
{
    /// <summary>分页查询跨主型号中性产品。</summary>
    [HttpPost("page")]
    public Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
        [FromBody] ProductNeutralManagerSearchRequest request,
        CancellationToken cancellationToken) => query.GetPagedAsync(request, cancellationToken);

    /// <summary>原子保存新增、修改、重新归属和软删除变更。</summary>
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
        [FromBody] ProductNeutralManagerBatchSaveRequest request,
        CancellationToken cancellationToken) => maintenance.BatchSaveAsync(request, cancellationToken);
}
