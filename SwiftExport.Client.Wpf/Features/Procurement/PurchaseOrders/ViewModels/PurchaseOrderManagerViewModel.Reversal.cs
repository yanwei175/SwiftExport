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
    private bool CanVoidOrder() => !_reversalDisposed && IsNotBusy && _currentUser.HasPermission(PermissionKeys.ProcurementOrderVoid)
        && SelectedOrder is { LifecycleStatus: PurchaseOrderLifecycleStatusEnum.Active, RequiresRefresh: false } && !HasPendingConfirmation(SelectedOrder.PurchaseOrderId);
    private bool CanRetryReversal() => !_reversalDisposed && IsNotBusy && SelectedOrder is not null
        && _reversalSession.Find(ReversalUser, SelectedOrder.PurchaseOrderId) is { } pending && HasReversalPermission(pending.Operation);

    /// <summary>原命令重试只消费对应操作权限，确认、编辑与作废权限不能互相替代。</summary>
    private bool HasReversalPermission(ProcurementReversalOperation operation) => _currentUser.HasPermission(operation switch
    {
        ProcurementReversalOperation.CancelConfirmation => PermissionKeys.ProcurementOrderConfirm,
        ProcurementReversalOperation.DeleteDraft or ProcurementReversalOperation.DeleteLines => PermissionKeys.ProcurementOrderEdit,
        ProcurementReversalOperation.Void => PermissionKeys.ProcurementOrderVoid,
        _ => string.Empty
    });
    private bool CanDeleteDraft() => !_reversalDisposed && IsNotBusy && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit)
        && SelectedOrder?.CanDeleteDraft == true && !HasPendingConfirmation(SelectedOrder.PurchaseOrderId);
    /// <summary>删除整张从未确认草稿，释放来源占用，曾确认历史必须走作废。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteDraft))]
    private Task DeleteDraftAsync() => CanDeleteDraft() ? StartReversalAsync(ProcurementReversalOperation.DeleteDraft) : Task.CompletedTask;
    [RelayCommand(CanExecute = nameof(CanVoidOrder))]
    private Task VoidOrderAsync() => StartReversalAsync(ProcurementReversalOperation.Void);
    /// <summary>重试前再次检查原操作权限，并冻结本次核实所归属的用户和会话快照。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryReversal))]
    private Task RetryReversalAsync()
    {
        if (!CanRetryReversal()) return Task.CompletedTask;
        var uid = SelectedOrder!.PurchaseOrderId; var user = ReversalUser; var session = _sessionVersion;
        return RunWriteAsync(() => CompleteReversalAsync(uid, user, session), "重试原采购操作");
    }

    /// <summary>只预览当前采购，用户同意前不发送任何写请求。</summary>
    private async Task StartReversalAsync(ProcurementReversalOperation operation)
    {
        if (SelectedOrder is null || _reversalDialogs is null || _reversalDisposed) return;
        if (operation == ProcurementReversalOperation.DeleteDraft ? !CanDeleteDraft() : operation == ProcurementReversalOperation.CancelConfirmation ? !CanCancelOrderConfirmation() : !CanVoidOrder()) return;
        var row = SelectedOrder; var user = ReversalUser; var session = _sessionVersion;
        await RunWriteAsync(async () =>
        {
            var request = new ProcurementReversalPreviewRequest { ExpectedVersion = row.Version };
            if (operation == ProcurementReversalOperation.DeleteDraft)
            {
                var lines = await _apiCallRunner.RunAsync(() => _managerApi.GetLinesAsync(row.PurchaseOrderId), "读取整张草稿失败");
                if (!IsCurrentReversal(user, session)) return;
                if (!lines.Success || lines.Data is null) { _messages.ShowWarning("不能删除草稿", lines.Message); return; }
                request.Lines = lines.Data.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.PurchaseOrderLineId, Version = x.Version }).ToList();
            }
            var result = await _apiCallRunner.RunAsync(() => operation switch
            {
                ProcurementReversalOperation.CancelConfirmation => _orderApi.PreviewCancelConfirmationAsync(row.PurchaseOrderId, request),
                ProcurementReversalOperation.DeleteDraft => _orderApi.PreviewDeleteDraftAsync(row.PurchaseOrderId, request),
                _ => _orderApi.PreviewVoidAsync(row.PurchaseOrderId, request)
            }, "预览采购反向操作失败");
            if (!IsCurrentReversal(user, session)) return;
            if (!result.Success || result.Data is null) { ResultSummary = GetFailureText(result, "预览采购反向操作失败。"); _messages.ShowWarning("预览未通过", ResultSummary); return; }
            var preview = result.Data;
            if (preview.PurchaseOrderId != row.PurchaseOrderId || preview.Version != request.ExpectedVersion || preview.Operation != operation)
            { ResultSummary = "预览身份或版本不匹配，请刷新采购。"; return; }
            if (operation == ProcurementReversalOperation.DeleteDraft && (preview.Lines.Count != request.Lines.Count
                || request.Lines.Any(line => !preview.Lines.Any(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId && x.Version == line.Version))))
            { ResultSummary = "整张草稿的行版本不完整，请刷新采购。"; return; }
            var reason = await _reversalDialogs.ConfirmAsync(preview);
            if (!IsCurrentReversal(user, session) || !HasReversalPermission(operation) || reason is null || row.Version != request.ExpectedVersion || HasPendingConfirmation(row.PurchaseOrderId)) return;
            if (!_reversalSession.Freeze(user, preview, reason)) return;
            await CompleteReversalAsync(row.PurchaseOrderId, user, session);
        }, "检查采购反向操作");
    }

    /// <summary>成功回执立即废弃旧行资格，再刷新查询；查询失败保持冻结，不重新写入。</summary>
    private async Task CompleteReversalAsync(Guid uid, string user, int session)
    {
        var pending = _reversalSession.Find(user, uid);
        try
        {
            var result = await _reversalSession.SendAsync(user, uid, _orderApi);
            if (!IsCurrentReversal(user, session)) return;
            if (!result.Success) { ResultSummary = result.Message; _messages.ShowWarning("采购反向结果", result.Message); return; }
            var row = Orders.FirstOrDefault(x => x.PurchaseOrderId == uid);
            if (row is not null)
            {
                if (pending?.Operation == ProcurementReversalOperation.DeleteDraft)
                { row.PropertyChanged -= OnOrderPropertyChanged; Orders.Remove(row); if (ReferenceEquals(SelectedOrder, row)) SelectedOrder = Orders.FirstOrDefault(); }
                row.AcceptReversalResult(result.Data!);
            }
            var completedSummary = pending?.Operation == ProcurementReversalOperation.DeleteDraft
                ? "采购草稿已删除，来源草稿占用已释放。" : "操作已成功；物料需求回退需在物料需求页面单独执行。";
            ResultSummary = await LoadPageCoreAsync(uid) ? completedSummary : "操作已成功，列表刷新失败，请刷新；不要重复提交。";
        }
        catch
        {
            if (IsCurrentReversal(user, session)) ResultSummary = _reversalSession.Find(user, uid) is null
                ? "操作已成功，请刷新采购查询。" : "结果未知，请点击重试原操作；刷新、翻页不会丢失原命令。";
            throw;
        }
        finally { if (!_reversalDisposed) OnReversalChanged(this, EventArgs.Empty); }
    }

    /// <summary>同账号权限刷新也会使旧预览失效，异步结果不能重新归属到后续身份快照。</summary>
    private bool IsCurrentReversal(string user, int session) => !_reversalDisposed && user == ReversalUser && session == _sessionVersion;
    private void OnReversalChanged(object? sender, EventArgs e)
    {
        if (_reversalDisposed) return;
        ConfirmCommand.NotifyCanExecuteChanged(); BatchConfirmCommand.NotifyCanExecuteChanged(); CancelConfirmationCommand.NotifyCanExecuteChanged();
        VoidOrderCommand.NotifyCanExecuteChanged(); RetryReversalCommand.NotifyCanExecuteChanged();
        DeleteDraftCommand.NotifyCanExecuteChanged();
    }
}
