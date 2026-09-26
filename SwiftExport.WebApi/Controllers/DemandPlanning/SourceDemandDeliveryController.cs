using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.WebApi.Controllers.DemandPlanning;

/// <summary>暴露来源需求统一可交付只读边界；来源接收仍仅供单体内上游领域调用。</summary>
[ApiController]
[Authorize]
[Route("api/demand-planning/source-demands/delivery")]
public sealed class SourceDemandDeliveryController(
    ISourceDemandDeliveryService service,
    ICurrentUser currentUser,
    IUserPermissionService permissions) : ControllerBase
{
    /// <summary>按来源、客户和产品条件查询当前可交付需求。</summary>
    [HttpPost("search")]
    public Task<ActionResult<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>>> SearchAsync(
        [FromBody] SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken)
        => AuthorizedAsync(() => service.SearchAsync(request, cancellationToken), cancellationToken);

    /// <summary>读取单项来源需求的交付上下文。</summary>
    [HttpGet("{grossDemandId:guid}")]
    public Task<ActionResult<ServiceResult<SourceDemandDeliveryItemDto>>> GetAsync(
        [FromRoute] Guid grossDemandId,
        CancellationToken cancellationToken)
        => AuthorizedAsync(() => service.GetAsync(grossDemandId, cancellationToken), cancellationToken);

    /// <summary>在 HTTP 边界返回真实 403，Application 仍保留同一权限校验。</summary>
    private async Task<ActionResult<ServiceResult<T>>> AuthorizedAsync<T>(
        Func<Task<ServiceResult<T>>> action,
        CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue
            || !await permissions.HasPermissionAsync(
                currentUser.UserId.Value,
                PermissionKeys.ModuleDemandPlanningAccess,
                cancellationToken))
            return Forbid();
        return await action();
    }
}
