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
FROM [dbo].[DependentDemand] nd WITH (UPDLOCK, ROWLOCK)
WHERE nd.[IndependentDemandId]=@GrossDemandId AND nd.[LifecycleStatus]=@Active AND nd.[IsDeleted]=0
ORDER BY nd.[DecompositionRevision],nd.[LineNumber],nd.[Uid];",
            new { GrossDemandId = grossDemandId, Active = NetDemandLifecycleStatusEnum.Active }, Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<NetDemandTraceItemDto>> GetByGrossDemandForTraceAsync(
        Guid grossDemandId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT nd.[Uid],nd.[Version],nd.[IndependentDemandId] AS [GrossDemandId],nd.[DecompositionRevision],
       nd.[LineNumber],nd.[LifecycleStatus],nd.[ParentNetDemandId],nd.[ParentSku] AS [ParentSku],
       nd.[RootNetDemandId],root.[Sku] AS [RootSku],nd.[DeliveryTargetNetDemandId],target.[Sku] AS [DeliveryTargetSku],
       nd.[DeliveryMode],nd.[ProductId],nd.[BomLineId],nd.[BomVersion],nd.[BomPath],
       nd.[MaterialTypeSnapshot] AS [MaterialType],nd.[SupplyType] AS [SupplyType],nd.[Sku] AS [Sku],
       nd.[SourceSku] AS [CustomerSku],nd.[Description],nd.[ColorName] AS [Color],nd.[Features] AS [Specification],
       nd.[GrossQty],nd.[NetQty],nd.[RequiredDate],nd.[PreferredSupplierId],nd.[PreferredSupplierCode],
       CASE WHEN ISNULL(x.[ReceivedQty],0)>=nd.[NetQty] THEN 4
            WHEN ISNULL(x.[CompletedQty],0)>0 THEN 3
            WHEN ISNULL(x.[AllocatedQty],0)>=nd.[NetQty] THEN 2
            WHEN ISNULL(x.[AllocatedQty],0)>0 THEN 1 ELSE 0 END AS [ExecutionProgress],
       nd.[RolledBackAt],nd.[RolledBackBy],nd.[RolledBackReason]
FROM [dbo].[DependentDemand] nd
LEFT JOIN [dbo].[DependentDemand] root ON root.[Uid]=nd.[RootNetDemandId]
LEFT JOIN [dbo].[DependentDemand] target ON target.[Uid]=nd.[DeliveryTargetNetDemandId]
OUTER APPLY (
    SELECT SUM(l.[AllocatedQty]-l.[ReleasedQty]) AS [AllocatedQty],SUM(l.[CompletedQty]) AS [CompletedQty],SUM(l.[ReceivedQty]) AS [ReceivedQty]
    FROM [dbo].[DemandExecutionLink] l
    WHERE l.[DependentDemandId]=nd.[Uid] AND l.[LinkStatus]=0 AND l.[IsDeleted]=0
) x
WHERE nd.[IndependentDemandId]=@GrossDemandId AND nd.[IsDeleted]=0
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
INSERT INTO [dbo].[DependentDemand]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [IndependentDemandId],[DecompositionRevision],[LineNumber],[LifecycleStatus],
 [ParentNetDemandId],[RootNetDemandId],[DeliveryTargetNetDemandId],[DeliveryMode],
 [ProductId],[BomLineId],[BomVersion],[BomPath],[MaterialTypeSnapshot],
 [SupplyType],[Sku],[ParentSku],[SourceSku],[Description],[ColorName],[Features],
 [GrossQty],[NetQty],[RequiredDate],[PreferredSupplierId],[PreferredSupplierCode],[Remark],
 [RolledBackAt],[RolledBackBy],[RolledBackReason],
 [SourceType],[SourceUid],[SourceNo],[SourceLineId],[OrderStatus],[ConfirmationStatus],
 [ExecutedQty],[ReceivedQty],[ReadyQty],[IsReady],[IsNonStandard])
SELECT
 @Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @GrossDemandId,@DecompositionRevision,@LineNumber,@LifecycleStatus,
 @ParentNetDemandId,@RootNetDemandId,@DeliveryTargetNetDemandId,@DeliveryMode,
 @ProductId,@BomLineId,@BomVersion,@BomPath,@MaterialTypeSnapshot,
 @SupplyTypeSnapshot,@SkuSnapshot,@ParentSkuSnapshot,@CustomerSkuSnapshot,
 @DescriptionSnapshot,@ColorSnapshot,@SpecificationSnapshot,
 @GrossQty,@NetQty,@RequiredDate,@PreferredSupplierId,@PreferredSupplierCode,@Remark,
 @RolledBackAt,@RolledBackBy,@RolledBackReason,
 gd.[SourceType],gd.[SourceUid],gd.[SourceNo],gd.[SourceLineId],0,0,
 0,0,0,0,@IsNonStandard
FROM [dbo].[IndependentDemand] gd
WHERE gd.[Uid]=@GrossDemandId AND gd.[IsDeleted]=0;";
            var rows = await Connection.ExecuteAsync(sql, new
            {
                demand.Uid, demand.Version, demand.CreatedDate, demand.CreatedBy, demand.UpdatedDate, demand.UpdatedBy,
                demand.GrossDemandId, demand.DecompositionRevision, demand.LineNumber, demand.LifecycleStatus,
                demand.ParentNetDemandId, demand.RootNetDemandId, demand.DeliveryTargetNetDemandId, demand.DeliveryMode,
                demand.ProductId, demand.BomLineId, demand.BomVersion, demand.BomPath, demand.MaterialTypeSnapshot,
                demand.SupplyTypeSnapshot, demand.SkuSnapshot, demand.ParentSkuSnapshot, demand.CustomerSkuSnapshot,
                demand.DescriptionSnapshot, demand.ColorSnapshot, demand.SpecificationSnapshot,
                demand.GrossQty, demand.NetQty, demand.RequiredDate, demand.PreferredSupplierId,
                demand.PreferredSupplierCode, demand.Remark, demand.RolledBackAt, demand.RolledBackBy,
                demand.RolledBackReason, IsNonStandard = !demand.ProductId.HasValue
            }, Transaction);
            results.Add(rows == 1
                ? RepositoryWriteResult.Ok(demand.Uid, null, demand.Version, "新增成功。")
                : RepositoryWriteResult.Fail(demand.Uid, null, "INSERT_FAILED", "新增净需求失败。"));
        }
        return results;
    }

    public Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason)
        => Connection.ExecuteAsync(@"
UPDATE [dbo].[DependentDemand]
SET [LifecycleStatus]=@RolledBack,[RolledBackAt]=@Now,[RolledBackBy]=@UserName,
    [RolledBackReason]=@Reason,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
WHERE [IndependentDemandId]=@GrossDemandId AND [DecompositionRevision]=@Revision
  AND [LifecycleStatus]=@Active AND [IsDeleted]=0;",
            new { GrossDemandId = grossDemandId, Revision = revision, Reason = reason, Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName, RolledBack = NetDemandLifecycleStatusEnum.RolledBack,
                Active = NetDemandLifecycleStatusEnum.Active }, Transaction);

    private const string NetDemandSelect = @"
SELECT nd.[Uid],nd.[Version],nd.[IsDeleted],nd.[CreatedDate],nd.[CreatedBy],nd.[UpdatedDate],nd.[UpdatedBy],
       nd.[IndependentDemandId] AS [GrossDemandId],nd.[DecompositionRevision],nd.[LineNumber],nd.[LifecycleStatus],
       nd.[ParentNetDemandId],nd.[RootNetDemandId],nd.[DeliveryTargetNetDemandId],nd.[DeliveryMode],
       nd.[ProductId],nd.[BomLineId],nd.[BomVersion],nd.[BomPath],nd.[MaterialTypeSnapshot],
       nd.[SupplyType] AS [SupplyTypeSnapshot],nd.[Sku] AS [SkuSnapshot],nd.[ParentSku] AS [ParentSkuSnapshot],
       nd.[SourceSku] AS [CustomerSkuSnapshot],nd.[Description] AS [DescriptionSnapshot],
       nd.[ColorName] AS [ColorSnapshot],nd.[Features] AS [SpecificationSnapshot],
       nd.[GrossQty],nd.[NetQty],nd.[RequiredDate],nd.[PreferredSupplierId],nd.[PreferredSupplierCode],nd.[Remark],
       nd.[RolledBackAt],nd.[RolledBackBy],nd.[RolledBackReason] ";
}
