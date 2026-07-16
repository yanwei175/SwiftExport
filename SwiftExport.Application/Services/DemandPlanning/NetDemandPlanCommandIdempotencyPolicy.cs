using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>计划命令指纹与成功重放的纯策略。</summary>
public sealed class NetDemandPlanCommandIdempotencyPolicy
{
    public string BuildFingerprint(
        string commandType,
        Guid allocationId,
        int expectedVersion,
        DemandExecutionTypeEnum? executionType,
        Guid? supplierId,
        DateTime? plannedDeliveryDate,
        string reason)
    {
        var canonical = string.Join("\n",
            commandType,
            allocationId.ToString("D"),
            expectedVersion.ToString(CultureInfo.InvariantCulture),
            executionType.HasValue ? ((int)executionType.Value).ToString(CultureInfo.InvariantCulture) : string.Empty,
            supplierId?.ToString("D") ?? string.Empty,
            plannedDeliveryDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            reason.Length.ToString(CultureInfo.InvariantCulture) + ":" + reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public NetDemandPlanCommandReplay CreateReplay(
        NetDemandPlanCommandRecord record,
        string expectedFingerprint,
        string? clientKey,
        int rowIndex)
    {
        if (!string.Equals(record.RequestFingerprint, expectedFingerprint, StringComparison.Ordinal))
            return NetDemandPlanCommandReplay.Fail("IDEMPOTENCY_KEY_REUSED", "同一命令标识已用于不同请求，请重新生成命令标识。");
        var affected = string.IsNullOrWhiteSpace(record.AffectedAllocationIds)
            ? []
            : record.AffectedAllocationIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();
        return NetDemandPlanCommandReplay.Ok(new NetDemandPlanCommandItemResult
        {
            AllocationId = record.OriginalAllocationId,
            NewAllocationId = record.NewAllocationId,
            NewAllocationVersion = record.NewAllocationVersion,
            ClientKey = clientKey,
            RowIndex = rowIndex,
            Outcome = NetDemandCommandOutcomeEnum.AlreadyApplied,
            Message = record.ResultMessage ?? "该命令已成功应用。",
            AffectedCustomerSuppliedAllocationIds = affected
        });
    }
}

public sealed class NetDemandPlanCommandReplay
{
    private NetDemandPlanCommandReplay(bool success, string? errorCode, string? errorMessage, NetDemandPlanCommandItemResult? result)
    {
        Success = success;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Result = result;
    }

    public bool Success { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public NetDemandPlanCommandItemResult? Result { get; }

    public static NetDemandPlanCommandReplay Ok(NetDemandPlanCommandItemResult result) => new(true, null, null, result);
    public static NetDemandPlanCommandReplay Fail(string code, string message) => new(false, code, message, null);
}
