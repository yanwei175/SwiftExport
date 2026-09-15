using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
/// <summary>管理页单张采购反向操作，复用详情相同的 API、对话框和未决会话。</summary>
public sealed partial class PurchaseOrderManagerViewModel
{
    private readonly ProcurementReversalSession _reversalSession;
    private readonly IProcurementReversalDialogService? _reversalDialogs;
    private bool _reversalDisposed;
    private string ReversalUser => _currentUser.CurrentUser?.UserId ?? string.Empty;
    private bool CanVoidOrder() => IsNotBusy && _currentUser.HasPermission(PermissionKeys.ProcurementOrderVoid)
        && SelectedOrder is { LifecycleStatus: PurchaseOrderLifecycleStatusEnum.Active } && !HasPendingConfirmation(SelectedOrder.PurchaseOrderId);
    private bool CanRetryReversal() => IsNotBusy && SelectedOrder is not null && _reversalSession.Find(ReversalUser, SelectedOrder.PurchaseOrderId) is not null;
    [RelayCommand(CanExecute = nameof(CanVoidOrder))]
    private Task VoidOrderAsync() => StartReversalAsync(ProcurementReversalOperation.Void);
    [RelayCommand(CanExecute = nameof(CanRetryReversal))]
    private Task RetryReversalAsync() => RunWriteAsync(async () => { if (SelectedOrder is not null) await CompleteReversalAsync(SelectedOrder.PurchaseOrderId); }, "重试原采购操作");

    /// <summary>只预览当前采购，用户同意前不发送任何写请求。</summary>
    private async Task StartReversalAsync(ProcurementReversalOperation operation)
    {
        if (SelectedOrder is null || _reversalDialogs is null || _reversalDisposed) return;
        if (operation == ProcurementReversalOperation.CancelConfirmation ? !CanCancelOrderConfirmation() : !CanVoidOrder()) return;
        var row = SelectedOrder;
        await RunWriteAsync(async () =>
        {
            var request = new ProcurementReversalPreviewRequest { ExpectedVersion = row.Version };
            var result = operation == ProcurementReversalOperation.CancelConfirmation
                ? await _orderApi.PreviewCancelConfirmationAsync(row.PurchaseOrderId, request)
                : await _orderApi.PreviewVoidAsync(row.PurchaseOrderId, request);
            if (_reversalDisposed) return;
            if (!result.Success || result.Data is null) { ResultSummary = result.Message; _messages.ShowWarning("预览未通过", result.Message); return; }
            var preview = result.Data;
            if (preview.PurchaseOrderId != row.PurchaseOrderId || preview.Version != request.ExpectedVersion || preview.Operation != operation)
            { ResultSummary = "预览身份或版本不匹配，请刷新采购。"; return; }
            var reason = await _reversalDialogs.ConfirmAsync(preview);
            if (_reversalDisposed || reason is null || row.Version != request.ExpectedVersion || HasPendingConfirmation(row.PurchaseOrderId)) return;
            if (!_reversalSession.Freeze(ReversalUser, preview, reason)) return;
            await CompleteReversalAsync(row.PurchaseOrderId);
        }, "检查采购反向操作");
    }

    /// <summary>冻结命令返回成功后只刷新查询，查询失败不重新写入。</summary>
    private async Task CompleteReversalAsync(Guid uid)
    {
        var pending = _reversalSession.Find(ReversalUser, uid);
        try
        {
            var result = await _reversalSession.SendAsync(ReversalUser, uid, _orderApi);
            if (_reversalDisposed) return;
            if (!result.Success) { ResultSummary = result.Message; _messages.ShowWarning("采购反向结果", result.Message); return; }
            var row = Orders.FirstOrDefault(x => x.PurchaseOrderId == uid);
            if (row is not null)
            {
                row.Version = result.Data!.Version; row.ConfirmationStatus = result.Data.ConfirmationStatus;
                if (pending?.Operation == ProcurementReversalOperation.Void) row.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
            }
            ResultSummary = "操作已成功；物料需求回退需在物料需求页面单独执行。";
            if (!await LoadPageCoreAsync(uid)) ResultSummary = "操作已成功，列表刷新失败，请刷新；不要重复提交。";
        }
        catch
        {
            if (!_reversalDisposed) ResultSummary = _reversalSession.Find(ReversalUser, uid) is null
                ? "操作已成功，请刷新采购查询。" : "结果未知，请点击重试原操作；刷新、翻页不会丢失原命令。";
            throw;
        }
        finally { if (!_reversalDisposed) OnReversalChanged(this, EventArgs.Empty); }
    }
    private void OnReversalChanged(object? sender, EventArgs e)
    {
        if (_reversalDisposed) return;
        ConfirmCommand.NotifyCanExecuteChanged(); BatchConfirmCommand.NotifyCanExecuteChanged(); CancelConfirmationCommand.NotifyCanExecuteChanged();
        VoidOrderCommand.NotifyCanExecuteChanged(); RetryReversalCommand.NotifyCanExecuteChanged();
    }
}
