using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.QualityControl.Categories;
using SwiftExport.Contracts.DTOs.QualityControl.Categories;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.QualityControl.Categories;

/// <summary>售后品质分类 HTTP 边界，仅转发读取与整批保存，权限和树规则由服务裁决。</summary>
[ApiController]
[Authorize]
[Route("api/quality-control/after-sales/quality-categories")]
public sealed class AfterSalesQualityCategoryController(IAfterSalesQualityCategoryService service) : ControllerBase
{
    /// <summary>读取未软删分类树的扁平节点，供维护及历史显示。</summary>
    [HttpGet("all")]
    public Task<ServiceResult<List<AfterSalesQualityCategoryDto>>> GetAllAsync(CancellationToken cancellationToken)
        => service.GetAllAsync(cancellationToken);

    /// <summary>保存分类整批变更并返回身份及版本回执。</summary>
    [HttpPost("batch-save")]
    public Task<ServiceResult<AfterSalesQualityCategoryBatchSaveResponse>> BatchSaveAsync(
        [FromBody] AfterSalesQualityCategoryBatchSaveRequest request, CancellationToken cancellationToken)
        => service.BatchSaveAsync(request, cancellationToken);
}
