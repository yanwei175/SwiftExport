using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
/// <summary>管理页与详情共用的采购反向影响确认入口。</summary>
public interface IProcurementReversalDialogService
{
    /// <summary>确认返回原因；取消或资格不符返回空，不发送写命令。</summary>
    Task<string?> ConfirmAsync(ProcurementReversalPreviewDto preview, CancellationToken ct = default);
}
