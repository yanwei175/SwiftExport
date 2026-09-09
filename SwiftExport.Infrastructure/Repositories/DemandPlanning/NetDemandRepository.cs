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
    /// 在物料领域按来源需求事实联表计算候选；不读取或回写来源表上的兼容拆解状态。
    /// </summary>
    public async Task<PagedResponseBase<SourceDemandCandidateDto>> SearchSourceDemandCandidatesAsync(
        SourceDemandCandidateSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new SourceDemandCandidateSearchRequest();
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{request.Keyword.Trim()}%";
        const string facts = @"
FROM [dbo].[GrossDemand] d
OUTER APPLY (
    SELECT COUNT(1) AS [ActiveCount], MIN(nd.[SourceDemandVersionSnapshot]) AS [MinDemandVersion],
           MAX(nd.[SourceDemandVersionSnapshot]) AS [MaxDemandVersion],
           MIN(nd.[SourceVersionSnapshot]) AS [MinSourceVersion], MAX(nd.[SourceVersionSnapshot]) AS [MaxSourceVersion],
           MIN(nd.[SourceDemandBasisFingerprintSnapshot]) AS [MinBasis], MAX(nd.[SourceDemandBasisFingerprintSnapshot]) AS [MaxBasis]
    FROM [dbo].[NetDemand] nd
    WHERE nd.[GrossDemandId]=d.[Uid] AND nd.[LifecycleStatus]=0 AND nd.[IsDeleted]=0
) activeFacts
OUTER APPLY (
    SELECT ISNULL(MAX(nd.[DecompositionRevision]),0) AS [LatestRevision]
    FROM [dbo].[NetDemand] nd
    WHERE nd.[GrossDemandId]=d.[Uid]
) historyFacts
WHERE d.[LifecycleStatus]=0 AND d.[IsDeleted]=0
  AND (@Keyword IS NULL OR d.[SourceNo] LIKE @Keyword OR d.[SkuSnapshot] LIKE @Keyword
       OR d.[CustomerSkuSnapshot] LIKE @Keyword OR d.[DescriptionSnapshot] LIKE @Keyword)
  AND (@IncludeUnavailable=1 OR ISNULL(activeFacts.[ActiveCount],0)=0)";
        var selectSql = @"
SELECT d.[Uid] AS [GrossDemandId],d.[Version] AS [SourceDemandVersion],d.[SourceVersion],
       d.[SourceType],d.[SourceUid],d.[SourceNo],d.[SourceLineId],d.[SourceLineNumber],
       d.[SkuSnapshot] AS [Sku],d.[LinkSkuSnapshot] AS [LinkSku],d.[CustomerSkuSnapshot] AS [CustomerSku],
       d.[DescriptionSnapshot] AS [Description],d.[ImagePathSnapshot] AS [ImagePath],d.[DemandQty],d.[RequiredDate],
       historyFacts.[LatestRevision] AS [LatestDecompositionRevision],
       ISNULL(activeFacts.[ActiveCount],0) AS [ActiveMaterialDemandCount],
       CASE
         WHEN ISNULL(activeFacts.[ActiveCount],0)=0 AND historyFacts.[LatestRevision]=0 THEN N'UNESTABLISHED'
         WHEN ISNULL(activeFacts.[ActiveCount],0)=0 THEN N'ROLLED_BACK'
         WHEN d.[DemandBasisFingerprint] IS NOT NULL
          AND activeFacts.[MinBasis]=d.[DemandBasisFingerprint] AND activeFacts.[MaxBasis]=d.[DemandBasisFingerprint] THEN N'ESTABLISHED'
         ELSE N'SOURCE_CHANGED'
       END AS [EstablishmentState],
       CASE WHEN ISNULL(activeFacts.[ActiveCount],0)=0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [CanEstablish],
       CASE WHEN ISNULL(activeFacts.[ActiveCount],0)>0 AND (
          d.[DemandBasisFingerprint] IS NULL OR activeFacts.[MinBasis] IS NULL
          OR activeFacts.[MinBasis]<>d.[DemandBasisFingerprint] OR activeFacts.[MaxBasis]<>d.[DemandBasisFingerprint])
         THEN N'SOURCE_CHANGE_REQUIRES_DISPOSITION'
         WHEN ISNULL(activeFacts.[ActiveCount],0)>0 THEN N'ALREADY_ESTABLISHED' ELSE NULL END AS [BlockerCode],
       CASE WHEN ISNULL(activeFacts.[ActiveCount],0)>0 AND (
          d.[DemandBasisFingerprint] IS NULL OR activeFacts.[MinBasis] IS NULL
          OR activeFacts.[MinBasis]<>d.[DemandBasisFingerprint] OR activeFacts.[MaxBasis]<>d.[DemandBasisFingerprint])
         THEN N'来源事实已变化，必须先处置当前物料需求。'
         WHEN ISNULL(activeFacts.[ActiveCount],0)>0 THEN N'已存在有效物料需求。' ELSE NULL END AS [BlockerMessage] " + facts + @"
ORDER BY CASE WHEN d.[RequiredDate] IS NULL THEN 1 ELSE 0 END,d.[RequiredDate],d.[SourceNo],d.[SourceLineNumber],d.[Uid]
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var countSql = "SELECT COUNT(1) " + facts + ";";
        var parameters = new
        {
            Keyword = keyword,
            IncludeUnavailable = request.IncludeUnavailable,
            Offset = (pageIndex - 1) * pageSize,
            PageSize = pageSize
        };
        var items = await Connection.QueryAsync<SourceDemandCandidateDto>(new CommandDefinition(
            selectSql, parameters, Transaction, cancellationToken: cancellationToken));
        var total = await Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            countSql, parameters, Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<SourceDemandCandidateDto>
        {
            Items = [.. items], TotalCount = total, PageIndex = pageIndex, PageSize = pageSize
        };
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
       d.[DescriptionSnapshot] AS [Description],d.[ImagePathSnapshot] AS [ImagePath],d.[DemandQty],d.[RequiredDate],
       historyFacts.[LatestRevision] AS [LatestDecompositionRevision],
       CAST(0 AS int) AS [ActiveMaterialDemandCount],
       CASE WHEN historyFacts.[LatestRevision]=0 THEN N'UNESTABLISHED' ELSE N'ROLLED_BACK' END AS [EstablishmentState],
       CAST(1 AS bit) AS [CanEstablish],NULL AS [BlockerCode],NULL AS [BlockerMessage]
FROM [dbo].[GrossDemand] d
OUTER APPLY (
    SELECT ISNULL(MAX(nd.[DecompositionRevision]),0) AS [LatestRevision]
    FROM [dbo].[NetDemand] nd
    WHERE nd.[GrossDemandId]=d.[Uid]
) historyFacts
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

    /// <summary>按来源身份锁定物料和执行事实；正式收发事实同样阻断来源退出。</summary>
    public Task<bool> HasSourceExitBlockerAsync(string sourceType, Guid sourceUid, IEnumerable<Guid> sourceLineIds)
    {
        var ids = sourceLineIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult(false);
        const string sql = @"
SELECT CASE WHEN EXISTS (
     SELECT 1 FROM [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
     INNER JOIN [dbo].[GrossDemand] d ON d.[Uid]=nd.[GrossDemandId]
     WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid AND d.[IsDeleted]=0
       AND nd.[LifecycleStatus]=0 AND nd.[IsDeleted]=0)
 OR EXISTS (
     SELECT 1 FROM [dbo].[DemandExecutionLink] link WITH (UPDLOCK,HOLDLOCK)
     INNER JOIN [dbo].[NetDemand] nd ON nd.[Uid]=link.[NetDemandId]
     INNER JOIN [dbo].[GrossDemand] d ON d.[Uid]=nd.[GrossDemandId]
     WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid
       AND link.[LinkStatus]=0 AND link.[IsDeleted]=0)
 OR EXISTS (SELECT 1 FROM [dbo].[PackingListContributionLine] WHERE [SalesOrderLineId] IN @LineIds AND [IsDeleted]=0)
 OR EXISTS (
     SELECT 1 FROM [dbo].[InboundDocumentLine] il
     LEFT JOIN [dbo].[DemandExecutionLink] link ON link.[Uid]=il.[DemandExecutionLinkId]
     LEFT JOIN [dbo].[NetDemandAllocation] allocation ON allocation.[Uid]=COALESCE(il.[NetDemandAllocationId],link.[NetDemandAllocationId])
     LEFT JOIN [dbo].[NetDemand] nd ON nd.[Uid]=COALESCE(link.[NetDemandId],allocation.[NetDemandId])
     LEFT JOIN [dbo].[GrossDemand] d ON d.[Uid]=COALESCE(il.[GrossDemandId],nd.[GrossDemandId])
     WHERE d.[SourceLineId] IN @LineIds AND il.[IsDeleted]=0)
 OR EXISTS (
     SELECT 1 FROM [dbo].[OutboundDocumentLine] ol
     LEFT JOIN [dbo].[NetDemandAllocation] allocation ON allocation.[Uid]=ol.[NetDemandAllocationId]
     LEFT JOIN [dbo].[NetDemand] nd ON nd.[Uid]=allocation.[NetDemandId]
     LEFT JOIN [dbo].[GrossDemand] d ON d.[Uid]=COALESCE(ol.[GrossDemandId],nd.[GrossDemandId])
     WHERE d.[SourceLineId] IN @LineIds AND ol.[IsDeleted]=0)
 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
        return Connection.ExecuteScalarAsync<bool>(sql, new { SourceType = sourceType, SourceUid = sourceUid, LineIds = ids }, Transaction);
    }

    /// <summary>锁定并统计物料历史，修订事实只由 NetDemand 历史决定。</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetHistoryCountsForUpdateAsync(IEnumerable<Guid> grossDemandIds)
    {
        var ids = grossDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, int>();
        var rows = await Connection.QueryAsync<HistoryCountRow>(@"
SELECT ids.[GrossDemandId],COUNT(nd.[Uid]) AS [HistoryCount]
FROM (SELECT [Uid] AS [GrossDemandId] FROM [dbo].[GrossDemand] WITH (UPDLOCK,HOLDLOCK) WHERE [Uid] IN @Ids) ids
LEFT JOIN [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK) ON nd.[GrossDemandId]=ids.[GrossDemandId]
GROUP BY ids.[GrossDemandId];", new { Ids = ids }, Transaction);
        return rows.ToDictionary(row => row.GrossDemandId, row => row.HistoryCount);
    }

    /// <summary>不读取来源兼容修订号或旧计划分配，仅以物料历史判断退出前是否完整安全回退。</summary>
    public Task<bool> HasInvalidExitHistoryAsync(IEnumerable<Guid> grossDemandIds)
    {
        var ids = grossDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult(false);
        const string sql = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM [dbo].[NetDemand] nd WITH (UPDLOCK,HOLDLOCK)
    WHERE nd.[GrossDemandId] IN @Ids
      AND (nd.[IsDeleted]<>0 OR nd.[LifecycleStatus] NOT IN (@RolledBack,@Voided)
           OR nd.[DecompositionRevision]<1)
) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
        return Connection.ExecuteScalarAsync<bool>(sql, new
        {
            Ids = ids,
            RolledBack = NetDemandLifecycleStatusEnum.RolledBack,
            Voided = NetDemandLifecycleStatusEnum.Voided
        }, Transaction);
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

    private sealed class HistoryCountRow
    {
        public Guid GrossDemandId { get; set; }
        public int HistoryCount { get; set; }
    }
}
