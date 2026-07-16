using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>无数据库副作用的计划替代计算器。</summary>
public sealed class NetDemandPlanReplacementPlanner
{
    public NetDemandPlanReplacement Build(
        NetDemand targetDemand,
        NetDemandAllocation targetAllocation,
        IReadOnlyList<NetDemand> familyDemands,
        IReadOnlyList<NetDemandAllocation> activeAllocations,
        DemandExecutionTypeEnum executionType,
        Supplier? purchaseSupplier,
        DateTime? plannedDeliveryDate,
        SupplierAssignmentSourceEnum assignmentSource,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(targetDemand);
        ArgumentNullException.ThrowIfNull(targetAllocation);
        if (targetDemand.DeliveryMode == DemandDeliveryModeEnum.ParentExecutionSupplier
            && executionType != DemandExecutionTypeEnum.Purchase)
            return NetDemandPlanReplacement.Fail("CUSTOMER_SUPPLIED_PURCHASE_REQUIRED", "客供件 B 只能保持采购执行方式。");
        var allocationGroups = activeAllocations.GroupBy(x => x.NetDemandId).ToDictionary(x => x.Key, x => x.ToArray());
        if (!allocationGroups.TryGetValue(targetDemand.Uid, out var targetRows)
            || targetRows.Length != 1
            || targetRows[0].Uid != targetAllocation.Uid)
            return NetDemandPlanReplacement.Fail("CORRUPTED_STATE", "目标净需求没有且仅有一条活动计划。");

        var replacement = CopyForReplacement(targetAllocation, reason);
        replacement.ExecutionType = executionType;
        replacement.PlannedDeliveryDate = plannedDeliveryDate;
        replacement.PurchaseSupplierId = executionType == DemandExecutionTypeEnum.Purchase ? purchaseSupplier?.Uid : null;
        replacement.SupplierCodeSnapshot = executionType == DemandExecutionTypeEnum.Purchase ? purchaseSupplier?.SupplierCode : null;
        replacement.SupplierNameSnapshot = executionType == DemandExecutionTypeEnum.Purchase ? purchaseSupplier?.SupplierName : null;
        replacement.AssignmentSource = assignmentSource;

        var replacements = new List<NetDemandAllocation> { replacement };
        var affectedCustomerSupplied = new List<Guid>();
        // A 每次被替代后 Uid 都变化；所有活动 B 必须改指向新的活动 A Allocation，
        // 即使本次只调整 A 的计划交期，也不能继续指向已 Superseded 的历史计划。
        if (targetDemand.DeliveryMode == DemandDeliveryModeEnum.Standard)
        {
            foreach (var childDemand in familyDemands
                         .Where(x => x.DeliveryMode == DemandDeliveryModeEnum.ParentExecutionSupplier
                                     && x.DeliveryTargetNetDemandId == targetDemand.Uid)
                         .OrderBy(x => x.LineNumber)
                         .ThenBy(x => x.Uid))
            {
                if (!allocationGroups.TryGetValue(childDemand.Uid, out var childRows) || childRows.Length != 1)
                    return NetDemandPlanReplacement.Fail("CORRUPTED_STATE", $"客供件 {childDemand.SkuSnapshot} 没有且仅有一条活动计划。");
                var childReplacement = CopyForReplacement(childRows[0], $"父件计划联动：{reason}");
                childReplacement.DeliveryTargetAllocationId = replacement.Uid;
                childReplacement.DeliveryTargetSupplierId = replacement.PurchaseSupplierId;
                childReplacement.DeliverySupplierCodeSnapshot = replacement.SupplierCodeSnapshot;
                childReplacement.DeliverySupplierNameSnapshot = replacement.SupplierNameSnapshot;
                replacements.Add(childReplacement);
                affectedCustomerSupplied.Add(childRows[0].Uid);
            }
        }

        return NetDemandPlanReplacement.Ok(replacements, affectedCustomerSupplied);
    }

    private static NetDemandAllocation CopyForReplacement(NetDemandAllocation source, string reason)
        => new()
        {
            Uid = GuidSequentialGenerator.NewGuid(),
            NetDemandId = source.NetDemandId,
            PlanRevision = source.PlanRevision + 1,
            Sequence = source.Sequence,
            LifecycleStatus = NetDemandAllocationLifecycleStatusEnum.Active,
            ExecutionType = source.ExecutionType,
            PlannedQty = source.PlannedQty,
            DemandRequiredDate = source.DemandRequiredDate,
            PlannedDeliveryDate = source.PlannedDeliveryDate,
            PurchaseSupplierId = source.PurchaseSupplierId,
            SupplierCodeSnapshot = source.SupplierCodeSnapshot,
            SupplierNameSnapshot = source.SupplierNameSnapshot,
            DeliveryTargetAllocationId = source.DeliveryTargetAllocationId,
            DeliveryTargetSupplierId = source.DeliveryTargetSupplierId,
            DeliverySupplierCodeSnapshot = source.DeliverySupplierCodeSnapshot,
            DeliverySupplierNameSnapshot = source.DeliverySupplierNameSnapshot,
            AssignmentSource = source.AssignmentSource,
            ReplacesAllocationId = source.Uid,
            ChangeReason = reason
        };
}

public sealed class NetDemandPlanReplacement
{
    private NetDemandPlanReplacement(
        bool isValid,
        string? errorCode,
        string? errorMessage,
        IReadOnlyList<NetDemandAllocation> replacements,
        IReadOnlyList<Guid> affectedCustomerSuppliedAllocationIds)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Replacements = replacements;
        AffectedCustomerSuppliedAllocationIds = affectedCustomerSuppliedAllocationIds;
    }

    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<NetDemandAllocation> Replacements { get; }
    public IReadOnlyList<Guid> AffectedCustomerSuppliedAllocationIds { get; }

    public static NetDemandPlanReplacement Ok(
        IReadOnlyList<NetDemandAllocation> replacements,
        IReadOnlyList<Guid> affectedCustomerSuppliedAllocationIds)
        => new(true, null, null, replacements, affectedCustomerSuppliedAllocationIds);

    public static NetDemandPlanReplacement Fail(string code, string message)
        => new(false, code, message, [], []);
}
