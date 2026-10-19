using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;

namespace SwiftExport.Core.Interfaces.OrderTracking.DemandDetail;

/// <summary>读取成品需求跟踪的基础事实，不装载物料、采购和交付区域。</summary>
public interface IDemandTrackingHeaderRepository
{
    /// <summary>按稳定需求身份读取未删除且受支持来源的基础明细。</summary>
    Task<DemandTrackingHeaderDto?> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default);
}
