using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
namespace SwiftExport.Core.Interfaces.OrderTracking.Purchases;
/// <summary>独立读取采购关联贡献，不加载其他跟踪区域。</summary>
public interface IDemandTrackingPurchaseRepository
{
    /// <summary>按成品需求稳定身份读取本区域证据，借用当前会话。</summary>
    Task<List<DemandTrackingPurchaseDto>> GetPurchasesAsync(Guid grossDemandId,CancellationToken cancellationToken=default);
}
