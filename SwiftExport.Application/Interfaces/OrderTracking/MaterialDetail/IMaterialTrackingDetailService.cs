using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.OrderTracking.MaterialDetail;
/// <summary>单物料只读追踪入口，不加载采购或收货区域。</summary>
public interface IMaterialTrackingDetailService
{
    /// <summary>读取物料快照、来源及回退说明。</summary>
    Task<ServiceResult<MaterialTrackingDetailDto>> GetMaterialDetailAsync(Guid netDemandId,CancellationToken cancellationToken=default);
}
