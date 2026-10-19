using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
namespace SwiftExport.Core.Interfaces.OrderTracking.Materials;
/// <summary>独立读取物料修订事实，不加载其他跟踪区域。</summary>
public interface IDemandTrackingMaterialRepository
{
    /// <summary>按成品需求稳定身份读取本区域证据，借用当前会话。</summary>
    Task<List<DemandTrackingMaterialDto>> GetMaterialsAsync(Guid grossDemandId,CancellationToken cancellationToken=default);
}
