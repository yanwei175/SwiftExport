using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;
namespace SwiftExport.Core.Interfaces.OrderTracking.Deliveries;
/// <summary>独立读取正式交付历史，不加载其他跟踪区域。</summary>
public interface IDemandTrackingDeliveryRepository
{
    /// <summary>按成品需求稳定身份读取本区域证据，借用当前会话。</summary>
    Task<DemandTrackingDeliverySectionDto> GetDeliveriesAsync(Guid grossDemandId,CancellationToken cancellationToken=default);
}
