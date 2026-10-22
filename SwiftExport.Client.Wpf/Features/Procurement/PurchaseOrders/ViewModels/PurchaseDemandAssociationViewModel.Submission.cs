using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

/// <summary>关联预览与写入协议；完整冻结负载后才提交，未知结果仅重放原命令。</summary>
public sealed partial class PurchaseDemandAssociationViewModel
{
    private bool CanPreview() => CanEditPlan && Plan.Count > 0;
    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        if (!CanPreview()) return;
        if (ValidatePlan(Plan) is { } error) { StatusText = error; return; }
        var context = Context!; var revision = _planRevision;
        var items = Plan.Select(x => x.ToItem()).ToList();
        Preview = null;
        await RunBusyAsync(async () =>
        {
            var result = await _runner.RunAsync(() => _api.PreviewDemandAssociationAsync(context.PurchaseOrderId,
                new() { ExpectedVersion = context.Version, Items = items }), "校验关联分配失败");
            if (_disposed || !ReferenceEquals(Context, context) || revision != _planRevision) return;
            if (!result.Success || result.Data is null)
            {
                StatusText = result.Message;
                if (result.Error?.Code == ServiceErrorCodes.VersionConflict) NeedsReload = true;
                return;
            }
            var preview = result.Data;
            if (preview.PurchaseOrderId != context.PurchaseOrderId || preview.Version != context.Version || string.IsNullOrWhiteSpace(preview.Token)
                || preview.Items.Count != items.Count || preview.Items.Select(x => x.CommandItemId).Distinct().Count() != items.Count
                || items.Any(item => !preview.Items.Any(x => x.CommandItemId == item.CommandItemId && x.PurchaseOrderLineId == item.PurchaseOrderLineId
                    && x.NetDemandId == item.NetDemandId && x.Quantity == item.Quantity)))
            { StatusText = "校验预览不完整，请重新校验。"; return; }
            Preview = preview; ReviewTabIndex = 1; StatusText = "校验通过，请核对服务端预览、填写原因后确认提交。";
        }, "校验完整暂存清单...");
    }
    private bool CanSubmit() => CanEditPlan && Preview is not null && Reason.Trim().Length is > 0 and <= 500;
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        if (!CanSubmit()) return;
        if (ValidatePlan(Plan) is { } error) { StatusText = error; return; }
        var context = Context!; var preview = Preview!; var revision = _planRevision;
        var command = new AssociateProcurementDemandsCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = context.Version, PreviewToken = preview.Token,
            Reason = Reason.Trim(), Items = Plan.Select(x => x.ToItem()).ToList()
        };
        if (!await _dialogs.ConfirmAsync("确认关联物料需求", $"采购单 {context.OrderNo}，本次 {command.Items.Count} 条分配。\n关联立即生效；请核对预览中的逐行数量变化。", "确认关联")) return;
        // 弹窗期间若意图或依据发生变化，不能把确认套用到另一份请求。
        if (!CanSubmit() || revision != _planRevision || !ReferenceEquals(Preview, preview) || !ReferenceEquals(Context, context)
            || command.Reason != Reason.Trim()) return;
        _pending = new PendingProcurementDemandAssociation(context.PurchaseOrderId, command);
        NotifyCommands();
        await RunBusyAsync(SendFrozenCoreAsync, "提交关联...");
    }
    private bool CanRetry() => !_disposed && HasPermission && !IsBusy && HasPendingCommand;
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryAsync() => CanRetry() ? RunBusyAsync(SendFrozenCoreAsync, "重试原关联命令...") : Task.CompletedTask;
    /// <summary>每次还原相同冻结命令；曾经未知后仅完整成功消解，后续拒绝不否认上次可能已经写入。</summary>
    private async Task SendFrozenCoreAsync()
    {
        if (_pending is not { } pending) return;
        var command = pending.CreateRequest();
        ServiceResult<ProcurementOrderCommandResult> result;
        try { result = await _runner.RunAsync(() => _api.AssociateDemandsAsync(pending.OrderId, command), "提交关联失败"); }
        catch { pending.MarkUnknown(); throw; }
        if (_disposed) return;
        if (result.Success)
        {
            var receipt = result.Data;
            if (receipt is null || receipt.PurchaseOrderId != pending.OrderId || receipt.Version != command.ExpectedVersion + 1
                || receipt.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active
                || receipt.ConfirmationStatus != Context?.ConfirmationStatus
                || receipt.Lines is null || receipt.Lines.Count == 0 || receipt.Lines.Any(x => x is null || x.PurchaseOrderLineId == Guid.Empty || x.Version <= 0)
                || receipt.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != receipt.Lines.Count
                || command.Items.Any(item => !receipt.Lines.Any(x => x.PurchaseOrderLineId == item.PurchaseOrderLineId && x.Version == item.ExpectedLineVersion + 1)))
            { pending.MarkUnknown(); StatusText = "回执不完整，提交结果尚未明确；请重试原命令。"; return; }
            var order = pending.OrderId;
            _pending = null; ClearPlan(); Reason = string.Empty; _minimumReadVersion = receipt.Version; RefreshRequired = true; NotifyCommands();
            await LoadContextCoreAsync(order, replaceOrder: false);
            if (_disposed) return;
            StatusText = RefreshRequired ? "关联已成功，但刷新失败；请重读关联依据，勿重新提交。"
                : "关联已成功，已刷新剩余数量；可继续追加关联。";
            return;
        }
        var code = result.Error?.Code;
        // 授权和请求格式等拒绝可能发生在历史回执读取前；未知后保守冻结，不能据本次拒绝解锁。
        if (!pending.WasUnknown && code is (ServiceErrorCodes.Validation or ServiceErrorCodes.BusinessBlocked or ServiceErrorCodes.VersionConflict
            or ServiceErrorCodes.CommandConflict or ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated
            or ServiceErrorCodes.NotFound or ServiceErrorCodes.StateBlocked))
        {
            _pending = null; InvalidatePreview();
            NeedsReload = code is ServiceErrorCodes.VersionConflict or ServiceErrorCodes.CommandConflict or ServiceErrorCodes.NotFound or ServiceErrorCodes.StateBlocked;
            StatusText = NeedsReload ? $"{result.Message} 暂存意图已保留，请重读并逐条复核。" : result.Message;
        }
        else { pending.MarkUnknown(); StatusText = "提交结果尚未明确，请重试原命令；当前分配和原因已冻结。"; }
        NotifyCommands();
    }
}
