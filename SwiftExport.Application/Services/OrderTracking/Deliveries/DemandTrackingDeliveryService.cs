using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.OrderTracking.Deliveries;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.Deliveries;
using SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;
namespace SwiftExport.Application.Services.OrderTracking.Deliveries;
/// <summary>独立加载本区域；只核对基础需求存在，不调用整包 Trace。</summary>
public sealed class DemandTrackingDeliveryService(ServiceBaseContext context,IDemandTrackingHeaderRepository headers,IDemandTrackingDeliveryRepository repository) : IDemandTrackingDeliveryService
{
    /// <summary>不存在、取消及技术失败保持独立语义，已核实的业务异常由区域证据说明。</summary>
    public async Task<ServiceResult<DemandTrackingDeliverySectionDto>> GetDeliveriesAsync(Guid grossDemandId,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if(grossDemandId==Guid.Empty) return ServiceResult<DemandTrackingDeliverySectionDto>.Fail(ServiceErrorCodes.Validation,"成品需求身份不能为空。","GrossDemandId");
        try
        {
            if(await headers.GetDemandHeaderAsync(grossDemandId,cancellationToken) is null)
                return ServiceResult<DemandTrackingDeliverySectionDto>.Fail(ServiceErrorCodes.NotFound,"未找到成品需求。");
            var businessDate=OrderTrackingBusinessDate.Today();
            return ServiceResult<DemandTrackingDeliverySectionDto>.Ok(await repository.GetDeliveriesAsync(grossDemandId,businessDate,cancellationToken));
        }
        catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
        catch(Exception ex)
        {
            context.LoggerFactory.CreateLogger<DemandTrackingDeliveryService>().LogError(ex,"读取跟踪区域 Deliveries 失败，需求 {GrossDemandId}。",grossDemandId);
            return ServiceResult<DemandTrackingDeliverySectionDto>.Fail(ServiceErrorCodes.ServiceUnavailable,"读取跟踪区域失败，请稍后重试。");
        }
    }
}
