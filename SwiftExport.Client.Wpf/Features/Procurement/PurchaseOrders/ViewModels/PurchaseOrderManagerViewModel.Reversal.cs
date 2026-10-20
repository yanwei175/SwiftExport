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
    private bool CanDeleteDraft() => !_reversalDisposed && IsNotBusy && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit)
        && SelectedOrder?.CanDeleteDraft == true && !HasPendingConfirmation(SelectedOrder.PurchaseOrderId);
    /// <summary>删除整张从未确认草稿，释放来源占用，曾确认历史必须走作废。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteDraft))]
    private Task DeleteDraftAsync() => CanDeleteDraft() ? StartReversalAsync(ProcurementReversalOperation.DeleteDraft) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanVoidOrder))]
    private Task VoidOrderAsync() => StartReversalAsync(ProcurementReversalOperation.Void);
    [RelayCommand(CanExecute = nameof(CanRetryReversal))]
    private Task RetryReversalAsync() => RunWriteAsync(async () => { if (SelectedOrder is not null) await CompleteReversalAsync(SelectedOrder.PurchaseOrderId); }, "重试原采购操作");

    /// <summary>只预览当前采购，用户同意前不发送任何写请求。</summary>
    private async Task StartReversalAsync(ProcurementReversalOperation operation)
    {
        if (SelectedOrder is null || _reversalDialogs is null || _reversalDisposed) return;
        if (operation == ProcurementReversalOperation.DeleteDraft ? !CanDeleteDraft() : operation == ProcurementReversalOperation.CancelConfirmation ? !CanCancelOrderConfirmation() : !CanVoidOrder()) return;
        var row = SelectedOrder;
        await RunWriteAsync(async () =>
        {
            var request = new ProcurementReversalPreviewRequest { ExpectedVersion = row.Version };
            if (operation == ProcurementReversalOperation.DeleteDraft)
            {
                var lines = await _apiCallRunner.RunAsync(() => _managerApi.GetLinesAsync(row.PurchaseOrderId), "读取整张草稿失败");
                if (!lines.Success || lines.Data is null) { _messages.ShowWarning("不能删除草稿", lines.Message); return; }
                request.Lines = lines.Data.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.PurchaseOrderLineId, Version = x.Version }).ToList();
            }
            var result = operation switch
            {
                ProcurementReversalOperation.CancelConfirmation => await _orderApi.PreviewCancelConfirmationAsync(row.PurchaseOrderId, request),
                ProcurementReversalOperation.DeleteDraft => await _orderApi.PreviewDeleteDraftAsync(row.PurchaseOrderId, request),
                _ => await _orderApi.PreviewVoidAsync(row.PurchaseOrderId, request)
            };
            if (_reversalDisposed) return;
            if (!result.Success || result.Data is null) { ResultSummary = result.Message; _messages.ShowWarning("预览未通过", result.Message); return; }
            var preview = result.Data;
            if (preview.PurchaseOrderId != row.PurchaseOrderId || preview.Version != request.ExpectedVersion || preview.Operation != operation)
            { ResultSummary = "预览身份或版本不匹配，请刷新采购。"; return; }
            if (operation == ProcurementReversalOperation.DeleteDraft && (preview.Lines.Count != request.Lines.Count
                || request.Lines.Any(line => !preview.Lines.Any(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId && x.Version == line.Version))))
            { ResultSummary = "整张草稿的行版本不完整，请刷新采购。"; return; }
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
                if (pending?.Operation == ProcurementReversalOperation.DeleteDraft)
                { row.PropertyChanged -= OnOrderPropertyChanged; Orders.Remove(row); if (ReferenceEquals(SelectedOrder, row)) SelectedOrder = Orders.FirstOrDefault(); }
                row.Version = result.Data!.Version; row.ConfirmationStatus = result.Data.ConfirmationStatus;
                if (pending?.Operation == ProcurementReversalOperation.Void) row.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
            }
            var completedSummary = pending?.Operation == ProcurementReversalOperation.DeleteDraft
                ? "采购草稿已删除，来源草稿占用已释放。" : "操作已成功；物料需求回退需在物料需求页面单独执行。";
            ResultSummary = await LoadPageCoreAsync(uid) ? completedSummary : "操作已成功，列表刷新失败，请刷新；不要重复提交。";
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
        DeleteDraftCommand.NotifyCanExecuteChanged();
    }
}
