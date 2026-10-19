using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.Purchases;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Purchases;
using SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;
namespace SwiftExport.Application.Services.OrderTracking.Purchases;
/// <summary>独立加载本区域；只核对基础需求存在，不调用整包 Trace。</summary>
public sealed class DemandTrackingPurchaseService(ServiceBaseContext context,IDemandTrackingHeaderRepository headers,IDemandTrackingPurchaseRepository repository) : IDemandTrackingPurchaseService
{
    /// <summary>不存在、取消及技术失败保持独立语义，已核实的业务异常由区域证据说明。</summary>
    public async Task<ServiceResult<List<DemandTrackingPurchaseDto>>> GetPurchasesAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(grossDemandId==Guid.Empty) return ServiceResult<List<DemandTrackingPurchaseDto>>.Fail(ServiceErrorCodes.Validation,"成品需求身份不能为空。","GrossDemandId");
        try
        {
            if(await headers.GetDemandHeaderAsync(grossDemandId,cancellationToken) is null)
                return ServiceResult<List<DemandTrackingPurchaseDto>>.Fail(ServiceErrorCodes.NotFound,"未找到成品需求。");
            return ServiceResult<List<DemandTrackingPurchaseDto>>.Ok(await repository.GetPurchasesAsync(grossDemandId,cancellationToken));
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<DemandTrackingPurchaseService>().LogError(ex,"读取跟踪区域 Purchases 失败，需求 {GrossDemandId}。",grossDemandId);
            return ServiceResult<List<DemandTrackingPurchaseDto>>.Fail(ServiceErrorCodes.ServiceUnavailable,"读取跟踪区域失败，请稍后重试。");
        }
    }
}
