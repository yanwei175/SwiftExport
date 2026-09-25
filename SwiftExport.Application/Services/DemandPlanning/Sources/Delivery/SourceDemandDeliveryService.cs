using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Delivery;
using SwiftExport.Application.Interfaces.DemandPlanning;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Services.DemandPlanning.Sources.Delivery;

/// <summary>向需求计划 API 暴露 Shipping 的唯一来源交付实现，并执行模块权限校验。</summary>
public sealed class SourceDemandDeliveryService(
    ISourceDeliveryService delivery,
    ICurrentUser currentUser,
    IPermissionEvaluationService permissions) : ISourceDemandDeliveryService
{
    /// <inheritdoc />
    public Task<ServiceResult<PagedResponseBase<SourceDemandDeliveryItemDto>>> SearchAsync(
        SourceDemandDeliverySearchRequest request,
        CancellationToken cancellationToken = default)
        => AuthorizedAsync(() => delivery.SearchAsync(request, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<SourceDemandDeliveryItemDto>> GetAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default)
        => AuthorizedAsync(() => delivery.GetAsync(grossDemandId, cancellationToken), cancellationToken);

    /// <summary>非 HTTP 调用同样要求当前用户具有需求计划模块访问权限。</summary>
    private async Task<ServiceResult<T>> AuthorizedAsync<T>(
        Func<Task<ServiceResult<T>>> action,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue
            || !await permissions.HasPermissionAsync(
                currentUser.UserId.Value,
                PermissionKeys.ModuleDemandPlanningAccess,
                cancellationToken))
            return ServiceResult<T>.Fail("FORBIDDEN", "当前用户没有需求计划模块访问权限。");
        return await action();
    }
}
