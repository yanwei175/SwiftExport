using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
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
    public bool CanInteractWithDocument => !_reversalDisposed && !IsDeleted && IsNotBusy && !IsReadOnly && !HasPendingWrite && _reversalSession.Find(ReversalUser, PurchaseOrderId) is null;
    private bool CanVoidOrder() => IsNotBusy && CanInteractWithDocument && !IsCreateMode && !HasUnsavedChanges
        && _currentUser.HasPermission(PermissionKeys.ProcurementOrderVoid) && LifecycleStatus == PurchaseOrderLifecycleStatusEnum.Active;
    private bool CanDeleteDraftLines() => IsNotBusy && CanEdit && !HasUnsavedChanges && Lines.Items.Count > 0;
    private bool CanRetryReversal() => !_reversalDisposed && IsNotBusy
        && _reversalSession.Find(ReversalUser, PurchaseOrderId) is { } pending && HasReversalPermission(pending.Operation);

    /// <summary>核实未决请求仍须拥有该原操作权限，不能用其他采购权限代替。</summary>
    private bool HasReversalPermission(ProcurementReversalOperation operation) => _currentUser.HasPermission(operation switch
    {
        ProcurementReversalOperation.CancelConfirmation => PermissionKeys.ProcurementOrderConfirm,
        ProcurementReversalOperation.DeleteDraft or ProcurementReversalOperation.DeleteLines => PermissionKeys.ProcurementOrderEdit,
        ProcurementReversalOperation.Void => PermissionKeys.ProcurementOrderVoid,
        _ => string.Empty
    });
    private bool CanDeleteWholeDraft() => CanInteractWithDocument && !IsCreateMode && !HasUnsavedChanges && ServerCanDeleteDraft
        && _currentUser.HasPermission(PermissionKeys.ProcurementOrderEdit);
    /// <summary>删除整张从未确认草稿，最终头行和下游资格由后端复核。</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteWholeDraft))]
    private Task DeleteDraftAsync() => CanDeleteWholeDraft() ? StartReversalAsync(ProcurementReversalOperation.DeleteDraft) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanVoidOrder))]
    private Task VoidOrderAsync() => StartReversalAsync(ProcurementReversalOperation.Void);
    [RelayCommand(CanExecute = nameof(CanDeleteDraftLines))]
    private Task DeleteDraftLinesAsync() => StartReversalAsync(ProcurementReversalOperation.DeleteLines);
    /// <summary>只核实具有原操作权限的冻结请求，不因旧详情只读而丢失恢复入口。</summary>
    [RelayCommand(CanExecute = nameof(CanRetryReversal))]
    private Task RetryReversalAsync()
    {
        if (!CanRetryReversal()) return Task.CompletedTask;
        var uid = PurchaseOrderId; var user = ReversalUser; var session = _sessionVersion;
        // 会话刷新将旧详情设为只读，但恢复原命令身份不等同于允许创建新的写命令。
        return RunBusyAsync(() => CompleteReversalAsync(uid, user, session), "确认原反向命令结果");
    }

    /// <summary>预览选定行或整单；有未保存修改时只提示，不自动保存或丢弃。</summary>
    private async Task StartReversalAsync(ProcurementReversalOperation operation)
    {
        if (!CanInteractWithDocument || IsBusy || _reversalDialogs is null || IsCreateMode) return;
        if (HasUnsavedChanges) { _messages.ShowWarning("请先处理修改", "请先保存或刷新放弃当前修改，再执行反向操作。"); return; }
        if (operation == ProcurementReversalOperation.CancelConfirmation && !CanCancelCommand()
            || operation == ProcurementReversalOperation.Void && !CanVoidOrder()
            || operation == ProcurementReversalOperation.DeleteLines && !CanDeleteDraftLines()) return;
        if (operation == ProcurementReversalOperation.DeleteDraft && !CanDeleteWholeDraft()) return;
        var requested = new List<ProcurementLineVersionDto>();
        if (operation == ProcurementReversalOperation.DeleteDraft)
            requested = Lines.Items.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.Uid, Version = x.Version }).ToList();
        if (operation == ProcurementReversalOperation.DeleteLines)
        {
            var selected = Lines.Items.Where(x => x.IsChecked).ToList();
            if (selected.Count == 0 && Lines.SelectedItem is not null) selected.Add(Lines.SelectedItem);
            if (selected.Count == 0) { _messages.ShowWarning("请选择采购行", "勾选待删除行或选择当前行。"); return; }
            if (selected.Count >= Lines.Items.Count) { _messages.ShowWarning("不能删除全部行", "请使用整单作废解除全部引用。"); return; }
            requested = selected.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.Uid, Version = x.Version }).ToList();
        }
        var user = ReversalUser; var session = _sessionVersion;
        await RunWriteAsync(async () =>
        {
            var uid = PurchaseOrderId; var version = Version;
            var request = new ProcurementReversalPreviewRequest { ExpectedVersion = version, Lines = requested };
            var result = await _runner.RunAsync(() => operation switch
            {
                ProcurementReversalOperation.CancelConfirmation => _api.PreviewCancelConfirmationAsync(uid, request),
                ProcurementReversalOperation.Void => _api.PreviewVoidAsync(uid, request),
                ProcurementReversalOperation.DeleteDraft => _api.PreviewDeleteDraftAsync(uid, request),
                _ => _api.PreviewDeleteLinesAsync(uid, request)
            }, "预览采购反向操作失败");
            if (!IsCurrentReversal(user, session)) return;
            if (!result.Success || result.Data is null) { StatusText = result.Error?.Message ?? result.Message; _messages.ShowWarning("预览未通过", StatusText); return; }
            var preview = result.Data;
            if (preview.PurchaseOrderId != uid || preview.Version != version || preview.Operation != operation) { _messages.ShowWarning("预览已失效", "采购身份或版本不匹配，请刷新。"); return; }
            if (operation == ProcurementReversalOperation.DeleteDraft && (preview.Lines.Count != requested.Count
                || requested.Any(line => !preview.Lines.Any(x => x.PurchaseOrderLineId == line.PurchaseOrderLineId && x.Version == line.Version))))
            { _messages.ShowWarning("预览已失效", "整张草稿的行版本不完整，请刷新。"); return; }
            var reason = await _reversalDialogs.ConfirmAsync(preview);
            if (!IsCurrentReversal(user, session) || !HasReversalPermission(operation) || reason is null || HasUnsavedChanges || HasPendingWrite || PurchaseOrderId != uid || Version != version) return;
            if (!_reversalSession.Freeze(user, preview, reason)) return;
            await CompleteReversalAsync(uid, user, session);
        }, "检查采购反向操作");
    }

    /// <summary>成功后只刷新详情；刷新失败不再提交写命令。</summary>
    private async Task CompleteReversalAsync(Guid uid, string user, int session)
    {
        var pending = _reversalSession.Find(user, uid);
        try
        {
            var result = await _reversalSession.SendAsync(user, uid, _api);
            if (!IsCurrentReversal(user, session)) return;
            if (!result.Success) { StatusText = result.Message; _messages.ShowWarning("采购反向结果", result.Message); return; }
            StatusText = "操作已成功，正在重新加载采购。";
            Version = result.Data!.Version; ConfirmationStatus = result.Data.ConfirmationStatus;
            LifecycleStatus = result.Data.LifecycleStatus;
            IsReadOnly = true;
            if (pending?.Operation == ProcurementReversalOperation.DeleteDraft)
            { IsDeleted = true; Lines.Clear(); HeaderHasChanges = false; StatusText = "采购草稿已删除，来源草稿占用已释放；该采购不再可编辑。"; return; }
            await LoadAsync(uid, CancellationToken.None);
            _messages.ShowSuccess("采购操作完成", "需求回退仍需在物料需求页面单独操作。");
        }
        catch
        {
            if (IsCurrentReversal(user, session)) StatusText = _reversalSession.Find(user, uid) is null
                ? "操作已成功，详情加载失败，请刷新；不要重复提交。" : "结果未知，请点击重试原操作，保持原命令确认结果。";
            throw;
        }
        finally { if (!_reversalDisposed) NotifyState(); }
    }

    /// <summary>旧预览或旧命令结果不能回写已经刷新身份快照的详情。</summary>
    private bool IsCurrentReversal(string user, int session) => !_reversalDisposed && user == ReversalUser && session == _sessionVersion;
    private void OnReversalChanged(object? sender, EventArgs e) { if (!_reversalDisposed) NotifyState(); }
}
