using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.OrderTracking.Receipts;
/// <summary>独立跟踪区域只读用例，失败不遮挡基础事实或其他区域。</summary>
public interface IDemandTrackingReceiptService
{
    /// <summary>校验需求身份并返回本区域证据与核实说明。</summary>
    Task<ServiceResult<DemandTrackingReceiptSectionDto>> GetReceiptsAsync(Guid grossDemandId,CancellationToken cancellationToken=default);
}
