using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Errors;

namespace SwiftExport.WebApi.Controllers.Inventory.Warehouses;

/// <summary>仓库 HTTP 边界；保留管理接口，采购候选使用独立只读资格及授权用例。</summary>
[ApiController]
[Route("api/inventory/warehouse-sites")]
public sealed class WarehouseSitesController(IWarehouseQueryService queries, IWarehouseManagementService management) : ControllerBase
{
    /// <summary>按库存读取权限返回活动仓库选择候选；选择结果不替代下游业务校验。</summary>
    [Authorize]
    [HttpGet("picker")]
    public Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(
        [FromQuery] PagedRequestBase request,
        CancellationToken cancellationToken) => queries.GetWarehousePickerPagedAsync(request, cancellationToken);

    /// <summary>只读查询已有采购候选；认证由管道处理，输入、权限及系统错误按 HTTP 状态区分。</summary>
    [Authorize]
    [HttpPost("candidates/search")]
    public async Task<ActionResult<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>>> SearchCandidatesAsync(
        [FromBody] PagedRequestBase request,
        [FromServices] IWarehouseCandidateQueryService candidates,
        [FromServices] ILogger<WarehouseSitesController> logger,
        CancellationToken cancellationToken)
    {
        try { return Ok(ServiceResult<PagedResponseBase<WarehouseCandidateDto>>.Ok(await candidates.SearchAsync(request, cancellationToken))); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException)
        {
            return new ObjectResult(ServiceResult<PagedResponseBase<WarehouseCandidateDto>>.Fail(ServiceErrorCodes.Forbidden, "没有仓库候选查询权限。"));
        }
        catch (ArgumentException)
        {
            return new ObjectResult(ServiceResult<PagedResponseBase<WarehouseCandidateDto>>.Fail(ServiceErrorCodes.Validation, "查询条件无效。"));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "仓库候选查询失败。");
            return new ObjectResult(ServiceResult<PagedResponseBase<WarehouseCandidateDto>>.Fail(ServiceErrorCodes.Unexpected, "查询失败，请稍后重试。"));
        }
    }

    /// <summary>转发既有管理列表查询，保留启用及停用筛选语义。</summary>
    [HttpPost("search")]
    public Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync([FromBody] WarehouseSiteSearchRequest request, CancellationToken cancellationToken) => queries.SearchWarehouseSitesAsync(request, cancellationToken);
    /// <summary>返回仓库管理专用活动供应商候选，不复用供应商管理宽契约。</summary>
    [HttpPost("suppliers/search")]
    public Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchSuppliersAsync([FromBody] PagedRequestBase request, CancellationToken cancellationToken) => queries.SearchWarehouseSuppliersAsync(request, cancellationToken);
    /// <summary>按稳定身份读取当前仓库资料。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken) => queries.GetWarehouseSiteAsync(uid, cancellationToken);
    /// <summary>转发显式建仓命令，候选查询不会调用本入口。</summary>
    [HttpPost("supplier-virtual")]
    public Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync([FromBody] CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken) => management.CreateSupplierVirtualWarehouseAsync(command, cancellationToken);
    /// <summary>转发既有仓库资料更新。</summary>
    [HttpPut("{uid:guid}")]
    public Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, [FromBody] UpdateWarehouseSiteCommand command, CancellationToken cancellationToken) => management.UpdateWarehouseSiteAsync(uid, command, cancellationToken);
    /// <summary>转发现有停用命令，不改变库存及历史约束。</summary>
    [HttpPost("{uid:guid}/deactivate")]
    public Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, [FromBody] DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken) => management.DeactivateWarehouseSiteAsync(uid, command, cancellationToken);
    /// <summary>转发虚拟仓启用命令；版本、删除状态与活动仓唯一性由应用服务校验。</summary>
    [HttpPost("{uid:guid}/activate")]
    public Task<ServiceResult<WarehouseSiteDto>> ActivateAsync(Guid uid, [FromBody] ActivateWarehouseSiteCommand command, CancellationToken cancellationToken) => management.ActivateWarehouseSiteAsync(uid, command, cancellationToken);
    /// <summary>转发虚拟仓删除命令；仓库历史与余额约束在服务内仍然生效。</summary>
    [HttpDelete("{uid:guid}")]
    public Task<ServiceResult<WarehouseSiteDto>> DeleteAsync(Guid uid, [FromBody] DeleteWarehouseSiteCommand command, CancellationToken cancellationToken) => management.DeleteWarehouseSiteAsync(uid, command, cancellationToken);
}
