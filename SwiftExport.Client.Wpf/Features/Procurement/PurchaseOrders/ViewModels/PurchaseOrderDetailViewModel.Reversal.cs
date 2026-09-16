using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>采购详情反向操作；持久化删行立即提交，不进入普通商务保存变更集。</summary>
public sealed partial class PurchaseOrderDetailViewModel
{
    private readonly ProcurementReversalSession _reversalSession;
    private readonly IProcurementReversalDialogService? _reversalDialogs;
    private bool _reversalDisposed;
    private string ReversalUser => _currentUser.CurrentUser?.UserId ?? string.Empty;
    /// <summary>未决反向命令存在时冻结本单编辑，但允许刷新和原命令重试。</summary>
    public bool CanInteractWithDocument => !_reversalDisposed && !IsReadOnly && _reversalSession.Find(ReversalUser, PurchaseOrderId) is null;
    private bool CanVoidOrder() => _pendingConfirmation is null && IsNotBusy && CanInteractWithDocument && !IsCreateMode && !HasUnsavedChanges
        && _currentUser.HasPermission(PermissionKeys.ProcurementOrderVoid) && LifecycleStatus == PurchaseOrderLifecycleStatusEnum.Active;
    private bool CanDeleteDraftLines() => IsNotBusy && CanEdit && !HasUnsavedChanges && Lines.Items.Count > 0;
    private bool CanRetryReversal() => IsNotBusy && _reversalSession.Find(ReversalUser, PurchaseOrderId) is not null;

    [RelayCommand(CanExecute = nameof(CanVoidOrder))]
    private Task VoidOrderAsync() => StartReversalAsync(ProcurementReversalOperation.Void);
    [RelayCommand(CanExecute = nameof(CanDeleteDraftLines))]
    private Task DeleteDraftLinesAsync() => StartReversalAsync(ProcurementReversalOperation.DeleteLines);
    [RelayCommand(CanExecute = nameof(CanRetryReversal))]
    private Task RetryReversalAsync() => RunWriteAsync(CompleteReversalAsync, "确认原反向命令结果");

    /// <summary>预览选定行或整单；有未保存修改时只提示，不自动保存或丢弃。</summary>
    private async Task StartReversalAsync(ProcurementReversalOperation operation)
    {
        if (!CanInteractWithDocument || IsBusy || _reversalDialogs is null || IsCreateMode) return;
        if (HasUnsavedChanges) { _messages.ShowWarning("请先处理修改", "请先保存或刷新放弃当前修改，再执行反向操作。"); return; }
        if (operation == ProcurementReversalOperation.CancelConfirmation && !CanCancelCommand()
            || operation == ProcurementReversalOperation.Void && !CanVoidOrder()
            || operation == ProcurementReversalOperation.DeleteLines && !CanDeleteDraftLines()) return;
        var requested = new List<ProcurementLineVersionDto>();
        if (operation == ProcurementReversalOperation.DeleteLines)
        {
            var selected = Lines.Items.Where(x => x.IsChecked).ToList();
            if (selected.Count == 0 && Lines.SelectedItem is not null) selected.Add(Lines.SelectedItem);
            if (selected.Count == 0) { _messages.ShowWarning("请选择采购行", "勾选待删除行或选择当前行。"); return; }
            if (selected.Count >= Lines.Items.Count) { _messages.ShowWarning("不能删除全部行", "请使用整单作废解除全部引用。"); return; }
            requested = selected.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.Uid, Version = x.Version }).ToList();
        }
        await RunWriteAsync(async () =>
        {
            var uid = PurchaseOrderId; var version = Version;
            var request = new ProcurementReversalPreviewRequest { ExpectedVersion = version, Lines = requested };
            ServiceResult<ProcurementReversalPreviewDto> result = operation switch
            {
                ProcurementReversalOperation.CancelConfirmation => await _api.PreviewCancelConfirmationAsync(uid, request),
                ProcurementReversalOperation.Void => await _api.PreviewVoidAsync(uid, request),
                _ => await _api.PreviewDeleteLinesAsync(uid, request)
            };
            if (_reversalDisposed) return;
            if (!result.Success || result.Data is null) { _messages.ShowWarning("预览未通过", result.Message); return; }
            var preview = result.Data;
            if (preview.PurchaseOrderId != uid || preview.Version != version || preview.Operation != operation) { _messages.ShowWarning("预览已失效", "采购身份或版本不匹配，请刷新。"); return; }
            var reason = await _reversalDialogs.ConfirmAsync(preview);
            if (_reversalDisposed || reason is null || HasUnsavedChanges || PurchaseOrderId != uid || Version != version) return;
            if (!_reversalSession.Freeze(ReversalUser, preview, reason)) return;
            await CompleteReversalAsync();
        }, "检查采购反向操作");
    }

    /// <summary>成功后只刷新详情；刷新失败不再提交写命令。</summary>
    private async Task CompleteReversalAsync()
    {
        var uid = PurchaseOrderId;
        try
        {
            var result = await _reversalSession.SendAsync(ReversalUser, uid, _api);
            if (_reversalDisposed) return;
            if (!result.Success) { StatusText = result.Message; _messages.ShowWarning("采购反向结果", result.Message); return; }
            StatusText = "操作已成功，正在重新加载采购。";
            Version = result.Data!.Version; ConfirmationStatus = result.Data.ConfirmationStatus;
            LifecycleStatus = result.Data.LifecycleStatus;
            IsReadOnly = true;
            await LoadAsync(uid, CancellationToken.None);
            _messages.ShowSuccess("采购操作完成", "需求回退仍需在物料需求页面单独操作。");
        }
        catch
        {
            if (!_reversalDisposed) StatusText = _reversalSession.Find(ReversalUser, uid) is null
                ? "操作已成功，详情加载失败，请刷新；不要重复提交。" : "结果未知，请点击重试原操作，保持原命令确认结果。";
            throw;
        }
        finally { if (!_reversalDisposed) NotifyState(); }
    }
    private void OnReversalChanged(object? sender, EventArgs e) { if (!_reversalDisposed) NotifyState(); }
}
