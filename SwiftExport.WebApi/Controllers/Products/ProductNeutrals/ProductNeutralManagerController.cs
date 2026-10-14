using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Import;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Query;
using SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.RequestProtection;

namespace SwiftExport.WebApi.Controllers.Products.ProductNeutrals;

/// <summary>中性产品管理 HTTP 边界，仅转发分页、批量保存及管理员导入请求。</summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ProductNeutralManagerController(IProductNeutralQueryService query, IProductNeutralBatchMaintenanceService maintenance,
    IProductNeutralImportService imports) : ControllerBase
{
    /// <summary>转发一至一百行导入；逐行事务与活动管理员检查由服务执行，不限制连续批次提交。</summary>
    [Authorize]
    [HttpPost("import")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public Task<ServiceResult<BatchSaveResponseBase>> ImportAsync([FromBody] List<ProductNeutralImportRowDto> rows,
        CancellationToken cancellationToken) => imports.ImportAsync(rows, cancellationToken);

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
