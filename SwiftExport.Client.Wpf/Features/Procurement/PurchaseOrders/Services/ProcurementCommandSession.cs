using System.Collections.Concurrent;
using System.Text.Json;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>按用户及采购身份协调管理页和详情的确认、保存与创建；会话内不丢失未知命令。</summary>
public sealed class ProcurementCommandSession
{
    private readonly ConcurrentDictionary<(string User, Guid Order), PendingProcurementCommand> _pending = new();
    private readonly ConcurrentDictionary<(string User, Guid Order), byte> _sending = new();
    /// <summary>通知所有消费页面立即更新编辑及重试门禁。</summary>
    public event EventHandler? Changed;
    /// <summary>查找原账号在该采购上的冻结请求。</summary>
    public PendingProcurementCommand? Find(string user, Guid order) => _pending.GetValueOrDefault((user, order));
    /// <summary>确认是否已有页面正在发送该采购原命令。</summary>
    public bool IsSending(string user, Guid order) => _sending.ContainsKey((user, order));
    /// <summary>在任何提交前冻结负载；另一个页面或操作已经占用时拒绝替换。</summary>
    public bool Freeze<T>(string user, Guid order, ProcurementCommandKind kind, T command)
    {
        if (string.IsNullOrWhiteSpace(user) || order == Guid.Empty) return false;
        var added = _pending.TryAdd((user, order), new(order, kind, JsonSerializer.Serialize(command)));
        if (added) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }
    /// <summary>只允许权威已确认查询消解未决确认；保存结果不能由列表状态猜测。</summary>
    public void ObserveConfirmation(string user, Guid order, ConfirmationStatusEnum status)
    {
        if (status == ConfirmationStatusEnum.已确认 && !IsSending(user, order)
            && Find(user, order) is { Kind: ProcurementCommandKind.Confirm })
        {
            _pending.TryRemove((user, order), out _); Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    /// <summary>以不可变请求执行原命令；结果未知、命令忙或重试认证失败均保留原身份和负载。</summary>
    public async Task<ServiceResult<ProcurementOrderCommandResult>> SendAsync(string user, Guid order,
        IProcurementOrderApi api, IApiCallRunner runner)
    {
        var key = (user, order);
        if (!_pending.TryGetValue(key, out var pending)) return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.Validation, "没有待重试采购命令。");
        if (!_sending.TryAdd(key, 0)) return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.CommandBusy, "原采购命令正在处理中。");
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await runner.RunAsync(() => pending.Kind switch
            {
                ProcurementCommandKind.Confirm => api.ConfirmAsync(order, JsonSerializer.Deserialize<ConfirmProcurementOrderCommand>(pending.CommandJson)!),
                ProcurementCommandKind.Save => api.SaveAsync(order, JsonSerializer.Deserialize<UpdatePurchaseOrderDraftCommand>(pending.CommandJson)!),
                _ => api.CreateAsync(JsonSerializer.Deserialize<CreateIndependentPurchaseOrderCommand>(pending.CommandJson)!)
            }, "采购命令提交失败");
            if (result.Success && result.Data is not null
                && result.Data.PurchaseOrderId != Guid.Empty
                && (pending.Kind == ProcurementCommandKind.Create || result.Data.PurchaseOrderId == order)
                && IsCompleteReceipt(pending, result.Data))
            { _pending.TryRemove(key, out _); return result; }
            var code = result.Error?.Code;
            var definitive = !result.Success && (code is ServiceErrorCodes.Validation or ServiceErrorCodes.VersionConflict
                or ServiceErrorCodes.BusinessBlocked or ServiceErrorCodes.CommandConflict or ServiceErrorCodes.StateBlocked
                or ProcurementErrorCodes.BomBasisChanged
                || (!pending.WasUnknown && code is ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated));
            if (definitive) { _pending.TryRemove(key, out _); return result; }
            _pending[key] = pending with { WasUnknown = true };
            return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown,
                $"{result.Message} 结果尚未确认，请核实或重试原命令；不要重新创建采购操作。");
        }
        catch
        {
            // 请求边界的非预期异常也不能证明未提交，不丢失原负载或向用户暴露异常正文。
            _pending[key] = pending with { WasUnknown = true };
            return ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "采购结果未知，请保留页面并核实或重试原命令。");
        }
        finally { _sending.TryRemove(key, out _); Changed?.Invoke(this, EventArgs.Empty); }
    }

    /// <summary>不完整成功回执不能消解未知结果；核对真实协议中的头版本、行身份和确认状态。</summary>
    private static bool IsCompleteReceipt(PendingProcurementCommand pending, ProcurementOrderCommandResult result)
    {
        if (result.Version <= 0 || result.Lines is null || result.Lines.Count == 0
            || result.Lines.Any(x => x.PurchaseOrderLineId == Guid.Empty || x.Version <= 0)
            || result.Lines.Select(x => x.PurchaseOrderLineId).Distinct().Count() != result.Lines.Count) return false;
        if (pending.Kind == ProcurementCommandKind.Confirm)
        {
            var command = JsonSerializer.Deserialize<ConfirmProcurementOrderCommand>(pending.CommandJson)!;
            return result.Version == command.ExpectedVersion + 1 && result.ConfirmationStatus == ConfirmationStatusEnum.已确认
                && result.ConfirmationId is { } confirmation && confirmation != Guid.Empty;
        }
        if (pending.Kind == ProcurementCommandKind.Save)
            return result.Version == JsonSerializer.Deserialize<UpdatePurchaseOrderDraftCommand>(pending.CommandJson)!.ExpectedVersion + 1;
        return true;
    }
}
