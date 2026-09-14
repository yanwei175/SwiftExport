using Dapper;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

public sealed class NetDemandAllocationRepository(RepositoryBaseContext context) : INetDemandAllocationRepository
{
    private readonly RepositoryBaseContext _context = context;
    private IDbConnection Connection => _context.Session.Connection;
    private IDbTransaction? Transaction => _context.Session.Transaction;

    public Task<NetDemandAllocation?> GetByIdAsync(Guid allocationId)
        => Connection.QuerySingleOrDefaultAsync<NetDemandAllocation>(AllocationSelect + @"
FROM [dbo].[NetDemandAllocation] a WITH (NOLOCK)
WHERE a.[Uid]=@AllocationId AND a.[IsDeleted]=0;",
            new { AllocationId = allocationId }, Transaction);

    public Task<NetDemandAllocation?> GetByIdForUpdateAsync(Guid allocationId)
        => Connection.QuerySingleOrDefaultAsync<NetDemandAllocation>(AllocationSelect + @"
FROM [dbo].[NetDemandAllocation] a WITH (UPDLOCK,HOLDLOCK)
WHERE a.[Uid]=@AllocationId AND a.[IsDeleted]=0;",
            new { AllocationId = allocationId }, Transaction);

    public async Task<IReadOnlyList<NetDemandAllocation>> GetActiveByNetDemandIdsForUpdateAsync(
        IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await Connection.QueryAsync<NetDemandAllocation>(AllocationSelect + @"
FROM [dbo].[NetDemandAllocation] a WITH (UPDLOCK,HOLDLOCK)
WHERE a.[NetDemandId] IN @Ids AND a.[LifecycleStatus]=@Active AND a.[IsDeleted]=0
ORDER BY a.[NetDemandId],a.[Sequence],a.[Uid];",
            new { Ids = ids, Active = NetDemandAllocationLifecycleStatusEnum.Active }, Transaction);
        return [.. rows];
    }

    /// <summary>读取指定物料需求当前有效的计划分配，不再提供 Trace 历史查询。</summary>
    public async Task<NetDemandAllocationDto?> GetActiveAsync(
        Guid netDemandId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT a.[Uid],a.[Version],a.[NetDemandId],a.[PlanRevision],a.[Sequence],a.[LifecycleStatus],
       a.[ExecutionType],a.[AssignmentSource],a.[PlannedQty],a.[DemandRequiredDate],a.[PlannedDeliveryDate],
       a.[PurchaseSupplierId],a.[SupplierCodeSnapshot] AS [PurchaseSupplierCode],
       a.[SupplierNameSnapshot] AS [PurchaseSupplierName],a.[DeliveryTargetAllocationId],a.[DeliveryTargetSupplierId],
       a.[DeliverySupplierCodeSnapshot] AS [DeliveryTargetSupplierCode],
       a.[DeliverySupplierNameSnapshot] AS [DeliveryTargetSupplierName],a.[ReplacesAllocationId],a.[ChangeReason],
       a.[CancelReasonCode],a.[CancelledAt],a.[CancelledBy],a.[CancelledReason],a.[CreatedDate],a.[CreatedBy]
FROM [dbo].[NetDemandAllocation] a
WHERE a.[NetDemandId]=@NetDemandId AND a.[LifecycleStatus]=@Active AND a.[IsDeleted]=0;";
        return await Connection.QuerySingleOrDefaultAsync<NetDemandAllocationDto>(new CommandDefinition(
            sql,
            new { NetDemandId = netDemandId, Active = NetDemandAllocationLifecycleStatusEnum.Active },
            Transaction,
            cancellationToken: cancellationToken));
    }

    public async Task<RepositoryWriteResult> SupersedeAsync(Guid allocationId, int expectedVersion, string reason)
    {
        _ = reason;
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
UPDATE [dbo].[NetDemandAllocation]
SET [LifecycleStatus]=@Superseded,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
OUTPUT INSERTED.[Version]
WHERE [Uid]=@AllocationId AND [Version]=@ExpectedVersion
  AND [LifecycleStatus]=@Active AND [IsDeleted]=0;",
            new
            {
                AllocationId = allocationId,
                ExpectedVersion = expectedVersion,
                Superseded = NetDemandAllocationLifecycleStatusEnum.Superseded,
                Active = NetDemandAllocationLifecycleStatusEnum.Active,
                Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName
            }, Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(allocationId, expectedVersion, newVersion.Value, "旧计划已替代。")
            : RepositoryWriteResult.Fail(allocationId, expectedVersion, "CONCURRENCY_CONFLICT", "计划版本已变化，请刷新后重试。");
    }

    public Task<RepositoryWriteResult> AddReplacementAsync(NetDemandAllocation allocation)
        => InsertOneAsync(allocation);

    public async Task<IReadOnlyList<NetDemandAllocation>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId)
    {
        const string sql = @"
SELECT a.[Uid],a.[Version],a.[IsDeleted],a.[CreatedDate],a.[CreatedBy],a.[UpdatedDate],a.[UpdatedBy],
       a.[NetDemandId],a.[PlanRevision],a.[Sequence],a.[LifecycleStatus],a.[ExecutionType],a.[PlannedQty],
       a.[DemandRequiredDate],a.[PlannedDeliveryDate],a.[PurchaseSupplierId],a.[SupplierCodeSnapshot],
       a.[SupplierNameSnapshot],a.[DeliveryTargetAllocationId],a.[DeliveryTargetSupplierId],
       a.[DeliverySupplierCodeSnapshot],a.[DeliverySupplierNameSnapshot],a.[AssignmentSource],
       a.[ReplacesAllocationId],a.[ChangeReason],a.[CancelReasonCode],a.[CancelledAt],a.[CancelledBy],a.[CancelledReason]
FROM [dbo].[NetDemandAllocation] a WITH (UPDLOCK, HOLDLOCK)
INNER JOIN [dbo].[NetDemand] nd ON nd.[Uid]=a.[NetDemandId]
WHERE nd.[GrossDemandId]=@GrossDemandId
  AND nd.[LifecycleStatus]=@NetDemandActive AND nd.[IsDeleted]=0
  AND a.[LifecycleStatus]=@AllocationActive AND a.[IsDeleted]=0
ORDER BY nd.[LineNumber],a.[Sequence],a.[Uid];";
        var rows = await Connection.QueryAsync<NetDemandAllocation>(sql, new
        {
            GrossDemandId = grossDemandId,
            NetDemandActive = NetDemandLifecycleStatusEnum.Active,
            AllocationActive = NetDemandAllocationLifecycleStatusEnum.Active
        }, Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<RepositoryWriteResult>> AddInitialAsync(IEnumerable<NetDemandAllocation> allocations)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var allocation in allocations)
        {
            var result = await InsertOneAsync(allocation);
            results.Add(result);
            if (!result.Success) break;
        }
        return results;
    }

    private async Task<RepositoryWriteResult> InsertOneAsync(NetDemandAllocation allocation)
    {
        _context.AuditStamp.StampForInsert(allocation);
        if (allocation.Uid == Guid.Empty)
            return RepositoryWriteResult.Fail(Guid.Empty, null, "UID_REQUIRED", "计划分配必须由 Application 预生成 Uid。");
        if (allocation.Version <= 0) allocation.Version = 1;
        const string sql = @"
INSERT INTO [dbo].[NetDemandAllocation]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [NetDemandId],[PlanRevision],[Sequence],[LifecycleStatus],[ExecutionType],[PlannedQty],
 [DemandRequiredDate],[PlannedDeliveryDate],[PurchaseSupplierId],[SupplierCodeSnapshot],[SupplierNameSnapshot],
 [DeliveryTargetAllocationId],[DeliveryTargetSupplierId],[DeliverySupplierCodeSnapshot],[DeliverySupplierNameSnapshot],
 [AssignmentSource],[ReplacesAllocationId],[ChangeReason],[CancelReasonCode],[CancelledAt],[CancelledBy],[CancelledReason])
VALUES
(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @NetDemandId,@PlanRevision,@Sequence,@LifecycleStatus,@ExecutionType,@PlannedQty,
 @DemandRequiredDate,@PlannedDeliveryDate,@PurchaseSupplierId,@SupplierCodeSnapshot,@SupplierNameSnapshot,
 @DeliveryTargetAllocationId,@DeliveryTargetSupplierId,@DeliverySupplierCodeSnapshot,@DeliverySupplierNameSnapshot,
 @AssignmentSource,@ReplacesAllocationId,@ChangeReason,@CancelReasonCode,@CancelledAt,@CancelledBy,@CancelledReason);";
        var rows = await Connection.ExecuteAsync(sql, allocation, Transaction);
        return rows == 1
            ? RepositoryWriteResult.Ok(allocation.Uid, null, allocation.Version, "新增计划分配成功。")
            : RepositoryWriteResult.Fail(allocation.Uid, null, "INSERT_FAILED", "新增计划分配失败。");
    }

    public Task<int> CancelRevisionAsync(Guid grossDemandId, int decompositionRevision, string reasonCode, string reason)
        => Connection.ExecuteAsync(@"
UPDATE a
SET a.[LifecycleStatus]=@Cancelled,a.[CancelReasonCode]=@ReasonCode,a.[CancelledAt]=@Now,
    a.[CancelledBy]=@UserName,a.[CancelledReason]=@Reason,a.[UpdatedDate]=@Now,
    a.[UpdatedBy]=@UserName,a.[Version]=a.[Version]+1
FROM [dbo].[NetDemandAllocation] a
INNER JOIN [dbo].[NetDemand] nd ON nd.[Uid]=a.[NetDemandId]
WHERE nd.[GrossDemandId]=@GrossDemandId AND nd.[DecompositionRevision]=@DecompositionRevision
  AND nd.[LifecycleStatus]=@NetDemandActive AND nd.[IsDeleted]=0
  AND a.[LifecycleStatus]=@AllocationActive AND a.[IsDeleted]=0;",
            new
            {
                GrossDemandId = grossDemandId,
                DecompositionRevision = decompositionRevision,
                ReasonCode = reasonCode,
                Reason = reason,
                Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName,
                Cancelled = NetDemandAllocationLifecycleStatusEnum.Cancelled,
                AllocationActive = NetDemandAllocationLifecycleStatusEnum.Active,
                NetDemandActive = NetDemandLifecycleStatusEnum.Active
            }, Transaction);

    private const string AllocationSelect = @"
SELECT a.[Uid],a.[Version],a.[IsDeleted],a.[CreatedDate],a.[CreatedBy],a.[UpdatedDate],a.[UpdatedBy],
       a.[NetDemandId],a.[PlanRevision],a.[Sequence],a.[LifecycleStatus],a.[ExecutionType],a.[PlannedQty],
       a.[DemandRequiredDate],a.[PlannedDeliveryDate],a.[PurchaseSupplierId],a.[SupplierCodeSnapshot],
       a.[SupplierNameSnapshot],a.[DeliveryTargetAllocationId],a.[DeliveryTargetSupplierId],
       a.[DeliverySupplierCodeSnapshot],a.[DeliverySupplierNameSnapshot],a.[AssignmentSource],
       a.[ReplacesAllocationId],a.[ChangeReason],a.[CancelReasonCode],a.[CancelledAt],a.[CancelledBy],a.[CancelledReason] ";
}
