using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
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

    /// <summary>
    /// 按来源稳定身份聚合全部待建立来源行；不从分页候选结果二次去重，也不依赖来源兼容状态。
    /// </summary>
    public async Task<IReadOnlyList<SourceDemandCandidateGroupDto>> GetSourceDemandCandidateGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT d.[SourceType],d.[SourceUid],MAX(d.[SourceNo]) AS [SourceNo],COUNT(1) AS [CandidateCount]
FROM [dbo].[GrossDemand] d
WHERE d.[LifecycleStatus]=0 AND d.[IsDeleted]=0
  AND NOT EXISTS (
      SELECT 1
      FROM [dbo].[NetDemand] nd
      WHERE nd.[GrossDemandId]=d.[Uid] AND nd.[LifecycleStatus]=0 AND nd.[IsDeleted]=0)
GROUP BY d.[SourceType],d.[SourceUid]
ORDER BY MAX(d.[SourceNo]),d.[SourceType],d.[SourceUid];";
        var rows = await Connection.QueryAsync<SourceDemandCandidateGroupDto>(new CommandDefinition(
            sql, transaction: Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>
    /// 使用来源类型与来源 Uid 双重边界完整返回一张来源单的待建立行，避免同号来源被误合并。
    /// </summary>
    public async Task<IReadOnlyList<SourceDemandCandidateDto>> GetSourceDemandCandidatesBySourceAsync(
        string sourceType,
        Guid sourceUid,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT d.[Uid] AS [GrossDemandId],d.[Version] AS [SourceDemandVersion],d.[SourceVersion],
       d.[SourceType],d.[SourceUid],d.[SourceNo],d.[SourceLineId],d.[SourceLineNumber],
       d.[SkuSnapshot] AS [Sku],d.[LinkSkuSnapshot] AS [LinkSku],d.[CustomerSkuSnapshot] AS [CustomerSku],
       d.[DescriptionSnapshot] AS [Description],d.[ImagePathSnapshot] AS [ImagePath],d.[DemandQty],d.[RequiredDate]
FROM [dbo].[GrossDemand] d
WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid
  AND d.[LifecycleStatus]=0 AND d.[IsDeleted]=0
  AND NOT EXISTS (
      SELECT 1
      FROM [dbo].[NetDemand] nd
      WHERE nd.[GrossDemandId]=d.[Uid] AND nd.[LifecycleStatus]=0 AND nd.[IsDeleted]=0)
ORDER BY CASE WHEN d.[RequiredDate] IS NULL THEN 1 ELSE 0 END,d.[RequiredDate],d.[SourceLineNumber],d.[Uid];";
        var rows = await Connection.QueryAsync<SourceDemandCandidateDto>(new CommandDefinition(
            sql, new { SourceType = sourceType, SourceUid = sourceUid }, Transaction,
            cancellationToken: cancellationToken));
        return [.. rows];
    }

    /// <summary>锁定该来源的全部物料历史，保证并发建立只取得一个新修订号。</summary>
    public async Task<int> GetNextDecompositionRevisionForUpdateAsync(Guid grossDemandId)
    {
        var latest = await Connection.ExecuteScalarAsync<int>(@"
SELECT ISNULL(MAX([DecompositionRevision]),0)
FROM [dbo].[NetDemand] WITH (UPDLOCK,HOLDLOCK)
WHERE [GrossDemandId]=@GrossDemandId;",
            new { GrossDemandId = grossDemandId }, Transaction);
        return latest + 1;
    }

    /// <summary>
    /// 锁定并批量评估来源需求的全部 NetDemand 历史；来源退出不再从本仓储跨域扫描 Link、采购或入库表。
    /// </summary>
    public async Task<IReadOnlyList<MaterialDemandSourceExitAssessment>> AssessSourceExitForUpdateAsync(
        IEnumerable<Guid> grossDemandIds)
    {
        var ids = grossDemandIds.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return [];
        const string sql = @"
SELECT sourceIds.[GrossDemandId],
       COUNT(nd.[Uid]) AS [HistoryCount],
       CONVERT(bit,CASE WHEN SUM(CASE WHEN nd.[Uid] IS NOT NULL
                                      AND nd.[IsDeleted]=0
                                      AND nd.[LifecycleStatus]=@Active THEN 1 ELSE 0 END)>0
                        THEN 1 ELSE 0 END) AS [HasActiveRevision],
       CONVERT(bit,CASE WHEN SUM(CASE WHEN nd.[Uid] IS NOT NULL
                                      AND (nd.[IsDeleted]<>0
                                           OR nd.[LifecycleStatus] NOT IN (@RolledBack,@Voided)
                                           OR nd.[DecompositionRevision]<1)
                                      THEN 1 ELSE 0 END)=0
                        THEN 1 ELSE 0 END) AS [IsHistoryComplete]
FROM (SELECT [Uid] AS [GrossDemandId]
      FROM [dbo].[GrossDemand] WITH (UPDLOCK,HOLDLOCK)
      WHERE [Uid] IN @Ids) sourceIds
LEFT JOIN [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
       ON nd.[GrossDemandId]=sourceIds.[GrossDemandId]
GROUP BY sourceIds.[GrossDemandId]
ORDER BY sourceIds.[GrossDemandId];";
        var rows = await Connection.QueryAsync<MaterialDemandSourceExitAssessment>(sql, new
        {
            Ids = ids,
            Active = NetDemandLifecycleStatusEnum.Active,
            RolledBack = NetDemandLifecycleStatusEnum.RolledBack,
            Voided = NetDemandLifecycleStatusEnum.Voided
        }, Transaction);
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
 [GrossDemandId],[SourceDemandVersionSnapshot],[SourceVersionSnapshot],[SourceDemandBasisFingerprintSnapshot],[DecompositionRevision],[LineNumber],[LifecycleStatus],
 [ParentNetDemandId],[RootNetDemandId],[DeliveryTargetNetDemandId],[DeliveryMode],
 [ProductId],[SuggestedPurchaseSupplierId],[BomLineId],[BomVersion],[BomPath],[MaterialTypeSnapshot],
 [SupplyTypeSnapshot],[SkuSnapshot],[ParentSkuSnapshot],[CustomerSkuSnapshot],
 [DescriptionSnapshot],[ColorSnapshot],[SpecificationSnapshot],
 [GrossQty],[NetQty],[RequiredDate],[Remark],[RolledBackAt],[RolledBackBy],[RolledBackReason])
VALUES
(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @GrossDemandId,@SourceDemandVersionSnapshot,@SourceVersionSnapshot,@SourceDemandBasisFingerprintSnapshot,@DecompositionRevision,@LineNumber,@LifecycleStatus,
 @ParentNetDemandId,@RootNetDemandId,@DeliveryTargetNetDemandId,@DeliveryMode,
 @ProductId,@SuggestedPurchaseSupplierId,@BomLineId,@BomVersion,@BomPath,@MaterialTypeSnapshot,
 @SupplyTypeSnapshot,@SkuSnapshot,@ParentSkuSnapshot,@CustomerSkuSnapshot,
 @DescriptionSnapshot,@ColorSnapshot,@SpecificationSnapshot,
 @GrossQty,@NetQty,@RequiredDate,@Remark,@RolledBackAt,@RolledBackBy,@RolledBackReason);";
            var rows = await Connection.ExecuteAsync(sql, new
            {
                demand.Uid, demand.Version, demand.CreatedDate, demand.CreatedBy, demand.UpdatedDate, demand.UpdatedBy,
                demand.GrossDemandId, demand.SourceDemandVersionSnapshot, demand.SourceVersionSnapshot, demand.SourceDemandBasisFingerprintSnapshot,
                demand.DecompositionRevision, demand.LineNumber, demand.LifecycleStatus,
                demand.ParentNetDemandId, demand.RootNetDemandId, demand.DeliveryTargetNetDemandId, demand.DeliveryMode,
                demand.ProductId, demand.SuggestedPurchaseSupplierId, demand.BomLineId, demand.BomVersion, demand.BomPath, demand.MaterialTypeSnapshot,
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

    /// <summary>只更新活动物料需求的推荐采购供应商，并以 Version 防止覆盖并发修改。</summary>
    public Task<int> UpdateSuggestedPurchaseSupplierAsync(
        Guid netDemandId,
        int expectedVersion,
        Guid? suggestedPurchaseSupplierId)
        => Connection.ExecuteAsync(@"
UPDATE [dbo].[NetDemand]
SET [SuggestedPurchaseSupplierId]=@SuggestedPurchaseSupplierId,
    [UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
WHERE [Uid]=@NetDemandId AND [Version]=@ExpectedVersion
  AND [LifecycleStatus]=@Active AND [IsDeleted]=0;",
            new
            {
                NetDemandId = netDemandId,
                ExpectedVersion = expectedVersion,
                SuggestedPurchaseSupplierId = suggestedPurchaseSupplierId,
                Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName,
                Active = NetDemandLifecycleStatusEnum.Active
            }, Transaction);

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
       nd.[GrossDemandId],nd.[SourceDemandVersionSnapshot],nd.[SourceVersionSnapshot],nd.[SourceDemandBasisFingerprintSnapshot],
       nd.[DecompositionRevision],nd.[LineNumber],nd.[LifecycleStatus],
       nd.[ParentNetDemandId],nd.[RootNetDemandId],nd.[DeliveryTargetNetDemandId],nd.[DeliveryMode],
       nd.[ProductId],nd.[SuggestedPurchaseSupplierId],nd.[BomLineId],nd.[BomVersion],nd.[BomPath],nd.[MaterialTypeSnapshot],
       nd.[SupplyTypeSnapshot],nd.[SkuSnapshot],nd.[ParentSkuSnapshot],nd.[CustomerSkuSnapshot],
       nd.[DescriptionSnapshot],nd.[ColorSnapshot],nd.[SpecificationSnapshot],
       nd.[GrossQty],nd.[NetQty],nd.[RequiredDate],nd.[Remark],
       nd.[RolledBackAt],nd.[RolledBackBy],nd.[RolledBackReason] ";

}
