using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.OrderTracking.DemandDetail;

/// <summary>提供独立成品需求基础明细用例。</summary>
public interface IDemandTrackingHeaderService
{
    /// <summary>校验需求身份并区分不存在、读取失败和有效事实。</summary>
    Task<ServiceResult<DemandTrackingHeaderDto>> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default);
}
