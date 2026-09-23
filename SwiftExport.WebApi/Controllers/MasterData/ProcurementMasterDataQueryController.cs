using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.MasterData;

/// <summary>独立采购主数据查询 HTTP 边界；只转发查询和映射异常类型，不修改全局错误包装。</summary>
[ApiController]
[Authorize]
[Route("api/master-data/procurement-lookups")]
public sealed class ProcurementMasterDataQueryController(
    IProcurementMasterDataQueryService service, ILogger<ProcurementMasterDataQueryController> logger) : ControllerBase
{
    /// <summary>转发客户新增候选查询；成功返回统一分页包装，取消令牌透传。</summary>
    [HttpPost("customers/candidates/search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementCustomerCandidateDto>>>> SearchCustomersAsync(
        [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => service.SearchCustomersAsync(request, cancellationToken), cancellationToken);

    /// <summary>转发供应商新增候选查询；成功返回统一分页包装，取消令牌透传。</summary>
    [HttpPost("suppliers/candidates/search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementSupplierCandidateDto>>>> SearchSuppliersAsync(
        [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => service.SearchSuppliersAsync(request, cancellationToken), cancellationToken);

    /// <summary>转发标准采购产品新增候选查询；成功返回统一分页包装，取消令牌透传。</summary>
    [HttpPost("products/candidates/search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementProductCandidateDto>>>> SearchProductsAsync(
        [FromBody] ProcurementProductSearchRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => service.SearchProductsAsync(request, cancellationToken), cancellationToken);

    /// <summary>转发客户历史筛选项查询；成功返回统一分页包装，取消令牌透传。</summary>
    [HttpPost("customers/history-filter/search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementCustomerFilterDto>>>> SearchCustomerHistoryFiltersAsync(
        [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => service.SearchCustomerHistoryFiltersAsync(request, cancellationToken), cancellationToken);

    /// <summary>转发供应商历史筛选项查询；成功返回统一分页包装，取消令牌透传。</summary>
    [HttpPost("suppliers/history-filter/search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<ProcurementSupplierFilterDto>>>> SearchSupplierHistoryFiltersAsync(
        [FromBody] PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(() => service.SearchSupplierHistoryFiltersAsync(request, cancellationToken), cancellationToken);

    /// <summary>按异常类型返回400/403/500；日志仅记录技术异常，客户端不依赖中文消息或错误前缀分流。</summary>
    private async Task<ActionResult<ServiceResult<PagedResponseBase<T>>>> ExecuteAsync<T>(
        Func<Task<PagedResponseBase<T>>> query, CancellationToken cancellationToken)
    {
        try { return Ok(ServiceResult<PagedResponseBase<T>>.Ok(await query())); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ServiceResult<PagedResponseBase<T>>.Fail("没有当前查询权限。"));
        }
        catch (ArgumentException)
        {
            return BadRequest(ServiceResult<PagedResponseBase<T>>.Fail("查询条件无效：页码、每页数量或关键词超出允许范围。"));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "独立采购主数据查询失败。");
            return StatusCode(StatusCodes.Status500InternalServerError, ServiceResult<PagedResponseBase<T>>.Fail("查询失败，请稍后重试。"));
        }
    }
}
