using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
namespace SwiftExport.Core.Interfaces.OrderTracking.MaterialDetail;
/// <summary>读取单个物料采用时快照，历史及软删除修订仍可定位。</summary>
public interface IMaterialTrackingDetailRepository
{
    /// <summary>按物料身份返回快照与来源，缺失时返回null。</summary>
    Task<MaterialTrackingDetailDto?> GetMaterialDetailAsync(Guid netDemandId,CancellationToken cancellationToken=default);
}
