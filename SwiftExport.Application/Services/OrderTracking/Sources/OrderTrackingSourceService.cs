using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.Sources;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.OrderTracking.Sources;
namespace SwiftExport.Application.Services.OrderTracking.Sources;
/// <summary>校验来源主表条件，仅读取基础事实。</summary>
public sealed class OrderTrackingSourceService(ServiceBaseContext context,IOrderTrackingSourceRepository repository) : IOrderTrackingSourceService
{
    /// <summary>独立加载来源组，不开启或接管事务。</summary>
    public async Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> SearchSourcesAsync(OrderTrackingSourceSearchRequest request,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(request is null) return ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Fail(ServiceErrorCodes.Validation,"查询条件不能为空。");
        if(OrderTrackingSearchValidation.Validate(request.SourceTypes,request.LifecycleStatuses,request.RequiredDateFrom,request.RequiredDateTo) is {} invalid)
            return ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Fail(ServiceErrorCodes.Validation,invalid.Message,invalid.Field);
        try{return ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Ok(await repository.SearchSourcesAsync(request,cancellationToken));}
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<OrderTrackingSourceService>().LogError(ex,"查询跟踪来源失败。");
            return ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>.Fail(ServiceErrorCodes.ServiceUnavailable,"查询来源分组失败，请稍后重试。");
        }
    }
}
