using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.OrderTracking.Materials;
/// <summary>独立跟踪区域只读用例，失败不遮挡基础事实或其他区域。</summary>
public interface IDemandTrackingMaterialService
{
    /// <summary>校验需求身份并返回本区域证据与核实说明。</summary>
    Task<ServiceResult<List<DemandTrackingMaterialDto>>> GetMaterialsAsync(Guid grossDemandId,CancellationToken cancellationToken=default);
}
