using System.Collections.Concurrent;
using System.Text.Json;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Procurement.PurchaseOrders.Services;

/// <summary>在应用内保留反馈原命令；实例绑定 API 环境，记录按用户及采购单隔离，不落盘。</summary>
public sealed class PurchaseOrderProgressCommandSession(string environment)
{
    private readonly ConcurrentDictionary<(string Environment, string User, Guid Order), Entry> _pending = new();
    private readonly ConcurrentDictionary<(string Environment, string User, Guid Order), byte> _sending = new();
    private sealed record Entry(string Json, bool WasUnknown = false);
    private (string, string, Guid) Key(string user, Guid order) => (environment.TrimEnd('/'), user, order);

    /// <summary>反馈未决或发送状态变化时通知页面刷新门禁。</summary>
    public event EventHandler? Changed;

    /// <summary>返回原命令的独立副本，调用方不能改变冻结负载。</summary>
    public ReportPurchaseOrderProgressCommand? Find(string user, Guid order)
        => _pending.TryGetValue(Key(user, order), out var entry)
            ? JsonSerializer.Deserialize<ReportPurchaseOrderProgressCommand>(entry.Json) : null;

    /// <summary>冻结首次完整命令，未决期间拒绝替换身份或负载。</summary>
    public bool Freeze(string user, Guid order, ReportPurchaseOrderProgressCommand command)
    {
        if (string.IsNullOrWhiteSpace(user) || order == Guid.Empty || command.CommandId == Guid.Empty) return false;
        var added = _pending.TryAdd(Key(user, order), new(JsonSerializer.Serialize(command)));
        if (added) Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>只发送冻结副本；未知、命令忙及未知后的认证失败保留原命令。</summary>
    public async Task<ServiceResult<PurchaseOrderProgressReportResult>> SendAsync(string user, Guid order,
        IPurchaseOrderProgressApi api, IApiCallRunner runner)
    {
        var key = Key(user, order);
        if (!_pending.TryGetValue(key, out var entry))
            return ServiceResult<PurchaseOrderProgressReportResult>.Fail(ServiceErrorCodes.Validation, "没有待重试的反馈。");
        if (!_sending.TryAdd(key, 0))
            return ServiceResult<PurchaseOrderProgressReportResult>.Fail(ServiceErrorCodes.CommandBusy, "原反馈正在提交。");
        try
        {
            var command = JsonSerializer.Deserialize<ReportPurchaseOrderProgressCommand>(entry.Json)!;
            var result = await runner.RunAsync(() => api.ReportAsync(order, command), "登记供应商反馈失败");
            // 此 API 仅追加反馈，不更新采购头；回执必须保留提交时的采购头版本。
            if (result.Success && result.Data is { } receipt && receipt.PurchaseOrderId == order
                && receipt.SupplierProgressReportId != Guid.Empty && receipt.Version == command.ExpectedPurchaseOrderVersion
                && receipt.Outcome is ProcurementCommandOutcomeEnum.Succeeded or ProcurementCommandOutcomeEnum.AlreadyApplied)
            {
                _pending.TryRemove(key, out _);
                return result;
            }

            var code = result.Error?.Code;
            var definitive = !result.Success && (code is ServiceErrorCodes.Validation or ServiceErrorCodes.InvalidCommand
                or ServiceErrorCodes.VersionConflict or ServiceErrorCodes.ConcurrencyConflict or ServiceErrorCodes.NotFound
                or ServiceErrorCodes.BusinessBlocked or ServiceErrorCodes.StateBlocked or ServiceErrorCodes.CommandConflict
                || (!entry.WasUnknown && code is ServiceErrorCodes.Forbidden or ServiceErrorCodes.Unauthenticated));
            if (definitive) { _pending.TryRemove(key, out _); return result; }
            _pending[key] = entry with { WasUnknown = true };
            return ServiceResult<PurchaseOrderProgressReportResult>.Fail(ServiceErrorCodes.OutcomeUnknown,
                "反馈结果尚未确认，请重试原反馈；不要重新登记。");
        }
        catch
        {
            // 客户端异常不能证明后台未提交，原请求仍是唯一安全的重试依据。
            _pending[key] = entry with { WasUnknown = true };
            return ServiceResult<PurchaseOrderProgressReportResult>.Fail(ServiceErrorCodes.OutcomeUnknown,
                "反馈结果未知，请重试原反馈确认结果。");
        }
        finally { _sending.TryRemove(key, out _); Changed?.Invoke(this, EventArgs.Empty); }
    }
}
