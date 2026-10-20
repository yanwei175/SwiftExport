using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Views;
using SwiftExportClient.Services.UI;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
/// <summary>仅组装采购反向弹窗，不访问 API 或持有页面状态。</summary>
public sealed class ProcurementReversalDialogService(IDialogService dialogs) : IProcurementReversalDialogService
{
    /// <inheritdoc/>
    public async Task<string?> ConfirmAsync(ProcurementReversalPreviewDto preview, CancellationToken ct = default)
    {
        var title = preview.Operation switch { ProcurementReversalOperation.CancelConfirmation => "取消采购确认", ProcurementReversalOperation.DeleteLines => "删除采购草稿行", ProcurementReversalOperation.DeleteDraft => "删除整张采购草稿", _ => "整单作废" };
        var model = new ProcurementReversalPreviewViewModel(preview);
        var result = await dialogs.ShowAsync(new ProcurementReversalPreviewView { DataContext = model },
            new DialogOptions(title) { PrimaryButtonText = title, CloseButtonText = "取消", Width = 840 }, model, ct);
        return result == DialogResult.Primary && model.IsPrimaryButtonEnabled ? model.Reason.Trim() : null;
    }
}
