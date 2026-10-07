using System.Collections.Concurrent;
using System.Text.Json;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>按登录用户和采购身份保存未决命令，刷新、分页和切换页面不会丢失原命令。</summary>
public sealed class ProcurementReversalSession
{
    private readonly ConcurrentDictionary<(string User, Guid Order), PendingProcurementReversal> _pending = new();
    private readonly ConcurrentDictionary<(string User, Guid Order), byte> _sending = new();
    /// <summary>通知页面更新写入禁用与重试状态。</summary>
    public event EventHandler? Changed;
    /// <summary>取得当前用户该采购的原命令，不跨账号重放。</summary>
    public PendingProcurementReversal? Find(string user, Guid uid) => _pending.GetValueOrDefault((user, uid));
    /// <summary>冻结服务端预览中的版本及选定行，只接纳一个未决操作。</summary>
    public bool Freeze(string user, ProcurementReversalPreviewDto preview, string reason)
    {
        reason = reason?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(user) || !preview.CanExecute || preview.Blockers.Count > 0
            || preview.PurchaseOrderId == Guid.Empty || preview.Version <= 0 || reason.Length is < 1 or > 500) return false;
        var commandId = Guid.NewGuid();
        var json = preview.Operation switch
        {
            ProcurementReversalOperation.CancelConfirmation => JsonSerializer.Serialize(new CancelProcurementOrderConfirmationCommand { CommandId = commandId, ExpectedVersion = preview.Version, Reason = reason }),
            ProcurementReversalOperation.Void => JsonSerializer.Serialize(new VoidProcurementOrderCommand { CommandId = commandId, ExpectedVersion = preview.Version, Reason = reason }),
            ProcurementReversalOperation.DeleteLines => JsonSerializer.Serialize(new DeleteProcurementDraftLinesCommand { CommandId = commandId, ExpectedVersion = preview.Version, Reason = reason,
                Lines = preview.Lines.Select(x => new ProcurementLineVersionDto { PurchaseOrderLineId = x.PurchaseOrderLineId, Version = x.Version }).ToList() }),
            _ => throw new InvalidOperationException("未知采购反向操作。")
        };
        var added = _pending.TryAdd((user, preview.PurchaseOrderId), new(preview.PurchaseOrderId, preview.Operation, preview.Version, json));
        if (added) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }
    /// <summary>只发送冻结负载；明确业务拒绝或有效成功才解除未决状态，未知响应保留。</summary>
    public async Task<ServiceResult<ProcurementOrderCommandResult>> SendAsync(string user, Guid uid, IProcurementOrderApi api)
    {
        var key = (user, uid);
        if (!_pending.TryGetValue(key, out var pending)) return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.Validation, "没有待重试命令。");
        if (!_sending.TryAdd(key, 0)) return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.CommandBusy, "原命令正在处理中。");
        try
        {
            var result = pending.Operation switch
            {
                ProcurementReversalOperation.CancelConfirmation => await api.CancelConfirmationAsync(uid, JsonSerializer.Deserialize<CancelProcurementOrderConfirmationCommand>(pending.CommandJson)!),
                ProcurementReversalOperation.Void => await api.VoidAsync(uid, JsonSerializer.Deserialize<VoidProcurementOrderCommand>(pending.CommandJson)!),
                _ => await api.DeleteLinesAsync(uid, JsonSerializer.Deserialize<DeleteProcurementDraftLinesCommand>(pending.CommandJson)!)
            };
            if (result.Success)
            {
                if (result.Data is null || result.Data.PurchaseOrderId != uid || result.Data.Version != pending.ExpectedVersion + 1
                    || result.Data.Lines is null || result.Data.Lines.Count == 0
                    || result.Data.Lines.Any(x => x is null || x.PurchaseOrderLineId == Guid.Empty || x.Version <= 0)
                    || result.Data.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != result.Data.Lines.Count
                    || result.Data.LifecycleStatus != (pending.Operation == ProcurementReversalOperation.Void ? PurchaseOrderLifecycleStatusEnum.Voided : PurchaseOrderLifecycleStatusEnum.Active)
                    || (pending.Operation == ProcurementReversalOperation.DeleteLines && result.Data.ConfirmationStatus != ConfirmationStatusEnum.待确认)
                    || (pending.Operation == ProcurementReversalOperation.CancelConfirmation && (result.Data.ConfirmationStatus != ConfirmationStatusEnum.待确认 || result.Data.ConfirmationId.HasValue)))
                    return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "响应不完整，请重试原命令确认结果。");
                if (pending.Operation == ProcurementReversalOperation.DeleteLines)
                {
                    var removed = JsonSerializer.Deserialize<DeleteProcurementDraftLinesCommand>(pending.CommandJson)!.Lines.Select(x => x.PurchaseOrderLineId).ToHashSet();
                    if (result.Data.Lines.Any(x => removed.Contains(x.PurchaseOrderLineId)))
                        return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "回执仍包含被删行，请重试原命令确认结果。");
                }
                _pending.TryRemove(key, out _);
            }
            else if (result.Error?.Code is ServiceErrorCodes.Validation or ServiceErrorCodes.VersionConflict or ServiceErrorCodes.BusinessBlocked
                or ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated or ServiceErrorCodes.CommandConflict)
                _pending.TryRemove(key, out _);
            return result;
        }
        // 传输异常由现有 API/页面异常设施记录；保持原负载供显式重试。
        finally { _sending.TryRemove(key, out _); Changed?.Invoke(this, EventArgs.Empty); }
    }
}
