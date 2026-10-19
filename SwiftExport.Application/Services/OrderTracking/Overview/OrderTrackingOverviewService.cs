using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Overview;
namespace SwiftExport.Application.Services.OrderTracking.Overview;
/// <summary>完整来源子表只验证身份，不把主表筛选或页大小带入读取。</summary>
public sealed class OrderTrackingOverviewService(ServiceBaseContext context,IOrderTrackingOverviewRepository repository) : IOrderTrackingOverviewService
{
    /// <summary>全量读取全部非删除需求，未知来源与空组身份在SQL前拒绝。</summary>
    public async Task<ServiceResult<OrderTrackingSourceDemandsDto>> GetSourceDemandsAsync(string sourceType,Guid sourceGroupId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(sourceType is not ("SO" or "CR" or "MD")||sourceGroupId==Guid.Empty)
            return ServiceResult<OrderTrackingSourceDemandsDto>.Fail(ServiceErrorCodes.Validation,"必须指定有效来源类型和组身份。");
        try{return ServiceResult<OrderTrackingSourceDemandsDto>.Ok(await repository.GetSourceDemandsAsync(sourceType,sourceGroupId,cancellationToken));}
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<OrderTrackingOverviewService>().LogError(ex,"读取来源全部需求失败。");
            return ServiceResult<OrderTrackingSourceDemandsDto>.Fail(ServiceErrorCodes.ServiceUnavailable,"读取来源需求失败，请稍后重试。");
        }
    }
    /// <summary>候选读取失败不转换为成功空集合。</summary>
    public async Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try{return ServiceResult<List<string>>.Ok(await repository.GetCustomerCodesAsync(cancellationToken));}
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<OrderTrackingOverviewService>().LogError(ex,"查询跟踪客户候选失败。");
            return ServiceResult<List<string>>.Fail(ServiceErrorCodes.ServiceUnavailable,"查询跟踪客户候选失败，请稍后重试。");
        }
    }
}
