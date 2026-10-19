using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Overview;

namespace SwiftExport.Application.Services.OrderTracking.Overview;

/// <summary>校验基础总览条件，不从下游读取进度，不开启或接管调用方事务。</summary>
public sealed class OrderTrackingOverviewService(ServiceBaseContext context, IOrderTrackingOverviewRepository repository) : IOrderTrackingOverviewService
{
    /// <summary>仅返回已有成品需求客户候选，异常不伪装为空集合。</summary>
    public async Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return ServiceResult<List<string>>.Ok(await repository.GetCustomerCodesAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<OrderTrackingOverviewService>().LogError(ex, "查询跟踪客户候选失败。");
            return ServiceResult<List<string>>.Fail(ServiceErrorCodes.ServiceUnavailable, "查询跟踪客户候选失败，请稍后重试。");
        }
    }
    /// <summary>仅接受 SO、CR、MD 来源，日期范围按日校验；分页由查询仓储统一归一化。</summary>
    public async Task<ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>> SearchAsync(OrderTrackingSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null)
            return ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>.Fail(ServiceErrorCodes.Validation, "查询条件不能为空。");
        if (OrderTrackingSearchValidation.Validate(request.SourceTypes, request.LifecycleStatuses, request.RequiredDateFrom, request.RequiredDateTo) is {} invalid)
            return ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>.Fail(ServiceErrorCodes.Validation, invalid.Message, invalid.Field);
        if (request.SourceGroupId.HasValue != !string.IsNullOrEmpty(request.SourceGroupType)
            || request.SourceGroupId == Guid.Empty
            || (!string.IsNullOrEmpty(request.SourceGroupType) && request.SourceGroupType is not ("SO" or "CR" or "MD")))
            return ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>.Fail(ServiceErrorCodes.Validation, "来源组类型与有效身份必须成对指定。", "SourceGroupId");
        try
        {
            return ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>.Ok(await repository.SearchAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger<OrderTrackingOverviewService>().LogError(ex, "查询订单跟踪总览失败。");
            return ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>.Fail(ServiceErrorCodes.ServiceUnavailable, "查询订单跟踪总览失败，请稍后重试。");
        }
    }
}
