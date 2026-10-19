using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
namespace SwiftExport.Core.Interfaces.OrderTracking.Receipts;
/// <summary>独立读取采购行收货证据，不加载其他跟踪区域。</summary>
public interface IDemandTrackingReceiptRepository
{
    /// <summary>按单物料读取采购整行进度和证据，缺失物料返回null。</summary>
    Task<DemandTrackingReceiptSectionDto?> GetMaterialReceiptsAsync(Guid netDemandId,CancellationToken cancellationToken=default);
    /// <summary>按成品需求稳定身份读取本区域证据，借用当前会话。</summary>
    Task<DemandTrackingReceiptSectionDto> GetReceiptsAsync(Guid grossDemandId,CancellationToken cancellationToken=default);
}
