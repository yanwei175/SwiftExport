using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

public sealed partial class NetDemandRepository(RepositoryBaseContext context) : INetDemandRepository
{
    private readonly RepositoryBaseContext _context = context;
    private IDbConnection Connection => _context.Session.Connection;
    private IDbTransaction? Transaction => _context.Session.Transaction;

    public async Task<IReadOnlyList<NetDemand>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId)
    {
        var rows = await Connection.QueryAsync<NetDemand>(NetDemandSelect + @"
FROM [dbo].[NetDemand] nd WITH (UPDLOCK, ROWLOCK)
WHERE nd.[GrossDemandId]=@GrossDemandId AND nd.[LifecycleStatus]=@Active AND nd.[IsDeleted]=0
ORDER BY nd.[DecompositionRevision],nd.[LineNumber],nd.[Uid];",
            new { GrossDemandId = grossDemandId, Active = NetDemandLifecycleStatusEnum.Active }, Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<NetDemandTraceItemDto>> GetByGrossDemandForTraceAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT nd.[Uid],nd.[Version],nd.[GrossDemandId],nd.[DecompositionRevision],
       nd.[LineNumber],nd.[LifecycleStatus],nd.[ParentNetDemandId],nd.[ParentSkuSnapshot] AS [ParentSku],
       nd.[RootNetDemandId],root.[SkuSnapshot] AS [RootSku],nd.[DeliveryTargetNetDemandId],target.[SkuSnapshot] AS [DeliveryTargetSku],
       nd.[DeliveryMode],nd.[ProductId],nd.[BomLineId],nd.[BomVersion],nd.[BomPath],
       nd.[MaterialTypeSnapshot] AS [MaterialType],nd.[SupplyTypeSnapshot] AS [SupplyType],nd.[SkuSnapshot] AS [Sku],
       nd.[CustomerSkuSnapshot] AS [CustomerSku],nd.[DescriptionSnapshot] AS [DefaultDescriptionEn],
       nd.[ColorSnapshot] AS [Color],nd.[SpecificationSnapshot] AS [Specification],
       nd.[GrossQty],nd.[NetQty],nd.[RequiredDate],allocation.[PurchaseSupplierId] AS [PreferredSupplierId],
       allocation.[SupplierCodeSnapshot] AS [PreferredSupplierCode],
       CASE WHEN ISNULL(x.[ReceivedQty],0)>=nd.[NetQty] THEN 4
            WHEN ISNULL(x.[ReceivedQty],0)>0 THEN 3
            WHEN ISNULL(x.[AllocatedQty],0)>=nd.[NetQty] THEN 2
            WHEN ISNULL(x.[AllocatedQty],0)>0 THEN 1 ELSE 0 END AS [ExecutionProgress],
       nd.[RolledBackAt],nd.[RolledBackBy],nd.[RolledBackReason]
FROM [dbo].[NetDemand] nd
LEFT JOIN [dbo].[NetDemand] root ON root.[Uid]=nd.[RootNetDemandId]
LEFT JOIN [dbo].[NetDemand] target ON target.[Uid]=nd.[DeliveryTargetNetDemandId]
OUTER APPLY (
    SELECT TOP(1) a.[PurchaseSupplierId],a.[SupplierCodeSnapshot]
    FROM [dbo].[NetDemandAllocation] a
    WHERE a.[NetDemandId]=nd.[Uid] AND a.[LifecycleStatus]=0 AND a.[IsDeleted]=0
    ORDER BY a.[PlanRevision] DESC,a.[Sequence],a.[Uid]
) allocation
OUTER APPLY (
    SELECT SUM(l.[AllocatedQty]-l.[ReleasedQty]) AS [AllocatedQty],
           SUM(ISNULL(receipts.[ReceivedQty],0)) AS [ReceivedQty]
    FROM [dbo].[DemandExecutionLink] l
    OUTER APPLY (
        SELECT SUM(il.[Quantity]) AS [ReceivedQty]
        FROM [dbo].[InboundDocumentLine] il
        INNER JOIN [dbo].[InboundDocument] ih ON ih.[Uid]=il.[InboundDocumentId]
        WHERE il.[DemandExecutionLinkId]=l.[Uid] AND ih.[PostingStatus]=1
          AND il.[IsDeleted]=0 AND ih.[IsDeleted]=0
    ) receipts
    WHERE l.[NetDemandId]=nd.[Uid] AND l.[LinkStatus]=0 AND l.[IsDeleted]=0
) x
WHERE nd.[GrossDemandId]=@GrossDemandId AND nd.[IsDeleted]=0
ORDER BY nd.[DecompositionRevision],nd.[LineNumber],nd.[Uid];";
        var rows = await Connection.QueryAsync<NetDemandTraceItemDto>(new CommandDefinition(
            sql, new { GrossDemandId = grossDemandId }, Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public async Task<IReadOnlyList<RepositoryWriteResult>> AddDecompositionAsync(IEnumerable<NetDemand> demands)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var demand in demands)
        {
            _context.AuditStamp.StampForInsert(demand);
            if (demand.Uid == Guid.Empty) demand.Uid = SwiftExport.Contracts.Common.GuidSequentialGenerator.NewGuid();
            if (demand.Version <= 0) demand.Version = 1;
            const string sql = @"
INSERT INTO [dbo].[NetDemand]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [GrossDemandId],[DecompositionRevision],[LineNumber],[LifecycleStatus],
 [ParentNetDemandId],[RootNetDemandId],[DeliveryTargetNetDemandId],[DeliveryMode],
 [ProductId],[BomLineId],[BomVersion],[BomPath],[MaterialTypeSnapshot],
 [SupplyTypeSnapshot],[SkuSnapshot],[ParentSkuSnapshot],[CustomerSkuSnapshot],
 [DescriptionSnapshot],[ColorSnapshot],[SpecificationSnapshot],
 [GrossQty],[NetQty],[RequiredDate],[Remark],[RolledBackAt],[RolledBackBy],[RolledBackReason])
VALUES
(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @GrossDemandId,@DecompositionRevision,@LineNumber,@LifecycleStatus,
 @ParentNetDemandId,@RootNetDemandId,@DeliveryTargetNetDemandId,@DeliveryMode,
 @ProductId,@BomLineId,@BomVersion,@BomPath,@MaterialTypeSnapshot,
 @SupplyTypeSnapshot,@SkuSnapshot,@ParentSkuSnapshot,@CustomerSkuSnapshot,
 @DescriptionSnapshot,@ColorSnapshot,@SpecificationSnapshot,
 @GrossQty,@NetQty,@RequiredDate,@Remark,@RolledBackAt,@RolledBackBy,@RolledBackReason);";
            var rows = await Connection.ExecuteAsync(sql, new
            {
                demand.Uid, demand.Version, demand.CreatedDate, demand.CreatedBy, demand.UpdatedDate, demand.UpdatedBy,
                demand.GrossDemandId, demand.DecompositionRevision, demand.LineNumber, demand.LifecycleStatus,
                demand.ParentNetDemandId, demand.RootNetDemandId, demand.DeliveryTargetNetDemandId, demand.DeliveryMode,
                demand.ProductId, demand.BomLineId, demand.BomVersion, demand.BomPath, demand.MaterialTypeSnapshot,
                demand.SupplyTypeSnapshot, demand.SkuSnapshot, demand.ParentSkuSnapshot, demand.CustomerSkuSnapshot,
                demand.DescriptionSnapshot, demand.ColorSnapshot, demand.SpecificationSnapshot,
                demand.GrossQty, demand.NetQty, demand.RequiredDate, demand.Remark,
                demand.RolledBackAt, demand.RolledBackBy, demand.RolledBackReason
            }, Transaction);
            results.Add(rows == 1
                ? RepositoryWriteResult.Ok(demand.Uid, null, demand.Version, "新增成功。")
                : RepositoryWriteResult.Fail(demand.Uid, null, "INSERT_FAILED", "新增净需求失败。"));
        }
        return results;
    }

    public Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason)
        => Connection.ExecuteAsync(@"
UPDATE [dbo].[NetDemand]
SET [LifecycleStatus]=@RolledBack,[RolledBackAt]=@Now,[RolledBackBy]=@UserName,
    [RolledBackReason]=@Reason,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
WHERE [GrossDemandId]=@GrossDemandId AND [DecompositionRevision]=@Revision
  AND [LifecycleStatus]=@Active AND [IsDeleted]=0;",
            new { GrossDemandId = grossDemandId, Revision = revision, Reason = reason, Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName, RolledBack = NetDemandLifecycleStatusEnum.RolledBack,
                Active = NetDemandLifecycleStatusEnum.Active }, Transaction);

    private const string NetDemandSelect = @"
SELECT nd.[Uid],nd.[Version],nd.[IsDeleted],nd.[CreatedDate],nd.[CreatedBy],nd.[UpdatedDate],nd.[UpdatedBy],
       nd.[GrossDemandId],nd.[DecompositionRevision],nd.[LineNumber],nd.[LifecycleStatus],
       nd.[ParentNetDemandId],nd.[RootNetDemandId],nd.[DeliveryTargetNetDemandId],nd.[DeliveryMode],
       nd.[ProductId],nd.[BomLineId],nd.[BomVersion],nd.[BomPath],nd.[MaterialTypeSnapshot],
       nd.[SupplyTypeSnapshot],nd.[SkuSnapshot],nd.[ParentSkuSnapshot],nd.[CustomerSkuSnapshot],
       nd.[DescriptionSnapshot],nd.[ColorSnapshot],nd.[SpecificationSnapshot],
       nd.[GrossQty],nd.[NetQty],nd.[RequiredDate],nd.[Remark],
       nd.[RolledBackAt],nd.[RolledBackBy],nd.[RolledBackReason] ";
}
