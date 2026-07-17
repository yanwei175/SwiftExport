using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

public sealed class GrossDemandRepository(RepositoryBaseContext context) : IGrossDemandRepository
{
    private readonly RepositoryBaseContext _context = context;
    private IDbConnection Connection => _context.Session.Connection;
    private IDbTransaction? Transaction => _context.Session.Transaction;

    public async Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(
        GrossDemandSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var where = new List<string> { "d.[IsDeleted] = 0" };
        var parameters = new DynamicParameters();

        AddKeywordFilter(request.Keyword, where, parameters);
        AddListFilter(request.SourceTypes, "d.[SourceType]", "SourceTypes", where, parameters);
        AddListFilter(request.SourceNos, "d.[SourceNo]", "SourceNos", where, parameters);
        AddListFilter(request.Skus, "d.[Sku]", "Skus", where, parameters);
        AddListFilter(request.CustomerSkus, "d.[SourceSku]", "CustomerSkus", where, parameters);
        AddListFilter(request.LifecycleStatuses, "d.[LifecycleStatus]", "LifecycleStatuses", where, parameters);
        AddListFilter(request.DecompositionStatuses, "d.[DecompositionStatus]", "DecompositionStatuses", where, parameters);
        if (request.ExecutionProgresses is { Count: > 0 })
        {
            where.Add($"({ExecutionProgressSql}) IN @ExecutionProgresses");
            parameters.Add("ExecutionProgresses", request.ExecutionProgresses.Distinct().ToArray());
        }
        if (request.RequiredDateFrom.HasValue)
        {
            where.Add("d.[HopefulDate] >= @RequiredDateFrom");
            parameters.Add("RequiredDateFrom", request.RequiredDateFrom.Value);
        }
        if (request.RequiredDateTo.HasValue)
        {
            where.Add("d.[HopefulDate] < DATEADD(day, 1, @RequiredDateTo)");
            parameters.Add("RequiredDateTo", request.RequiredDateTo.Value.Date);
        }
        if (request.IsNonStandard.HasValue)
        {
            where.Add("d.[IsNonStandard] = @IsNonStandard");
            parameters.Add("IsNonStandard", request.IsNonStandard.Value);
        }

        parameters.Add("Offset", (pageIndex - 1) * pageSize);
        parameters.Add("PageSize", pageSize);
        var whereSql = string.Join(" AND ", where);
        var selectSql = $@"
SELECT
    d.[Uid], d.[Version], d.[LifecycleStatus], d.[DecompositionStatus], d.[DecompositionRevision],
    d.[SourceType], d.[SourceUid], d.[SourceNo], d.[SourceLineId], d.[SourceLineNumber],
    d.[ProductId], d.[IsNonStandard], d.[Sku] AS [Sku], d.[LinkSkuSnapshot] AS [LinkSku],
    d.[SourceSku] AS [CustomerSku], d.[Description] AS [Description], d.[ColorName] AS [Color],
    d.[Features] AS [Specification], d.[ImagePathSnapshot] AS [ImagePath],
    d.[OriginalQty] AS [DemandQty], d.[HopefulDate] AS [RequiredDate], d.[Remark],
    ISNULL(na.[ActiveCount], 0) AS [ActiveNetDemandCount],
    ISNULL(na.[CustomerSuppliedCount], 0) AS [CustomerSuppliedNetDemandCount],
    CASE
      WHEN ISNULL(na.[ActiveCount], 0) = 0 THEN 0
      WHEN ISNULL(la.[ReceivedQty], 0) >= d.[OriginalQty] THEN 4
      WHEN ISNULL(la.[CompletedQty], 0) > 0 THEN 3
      WHEN ISNULL(la.[AllocatedQty], 0) >= d.[OriginalQty] THEN 2
      WHEN ISNULL(la.[AllocatedQty], 0) > 0 THEN 1
      ELSE 0
    END AS [ExecutionProgress],
    CASE WHEN d.[LifecycleStatus] = 0 AND d.[DecompositionStatus] = 0 AND ISNULL(na.[ActiveCount],0)=0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [CanDecompose],
    CASE WHEN d.[LifecycleStatus] = 0 AND d.[DecompositionStatus] = 1 AND ISNULL(na.[ActiveCount],0)>0 AND ISNULL(la.[ActiveLinkCount], 0) = 0 THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [CanRollback],
    CASE WHEN ISNULL(la.[ActiveLinkCount], 0) > 0 THEN N'已有下游执行单，请先取消或反向处理' ELSE NULL END AS [RollbackBlockerSummary],
    d.[DecomposedTime] AS [DecomposedAt], d.[DecomposedBy],
    d.[CreatedDate], d.[CreatedBy], d.[UpdatedDate], d.[UpdatedBy]
FROM [dbo].[IndependentDemand] d
OUTER APPLY (
    SELECT COUNT(1) AS [ActiveCount],
           SUM(CASE WHEN nd.[MaterialTypeSnapshot] = N'客供件' THEN 1 ELSE 0 END) AS [CustomerSuppliedCount]
    FROM [dbo].[DependentDemand] nd
    WHERE nd.[IndependentDemandId] = d.[Uid] AND nd.[LifecycleStatus] = 0 AND nd.[IsDeleted] = 0
) na
OUTER APPLY (
    SELECT COUNT(1) AS [ActiveLinkCount], SUM(l.[AllocatedQty]-l.[ReleasedQty]) AS [AllocatedQty],
           SUM(l.[CompletedQty]) AS [CompletedQty], SUM(l.[ReceivedQty]) AS [ReceivedQty]
    FROM [dbo].[DemandExecutionLink] l
    INNER JOIN [dbo].[DependentDemand] nd ON nd.[Uid] = l.[DependentDemandId]
    WHERE nd.[IndependentDemandId] = d.[Uid] AND nd.[LifecycleStatus] = 0 AND nd.[IsDeleted] = 0
      AND l.[LinkStatus] = 0 AND l.[IsDeleted] = 0
) la
WHERE {whereSql}
ORDER BY CASE WHEN d.[HopefulDate] IS NULL THEN 1 ELSE 0 END, d.[HopefulDate], d.[CreatedDate], d.[Uid]
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var countSql = $"SELECT COUNT(1) FROM [dbo].[IndependentDemand] d WHERE {whereSql};";

        var items = await Connection.QueryAsync<GrossDemandListItemDto>(
            new CommandDefinition(selectSql, parameters, Transaction, cancellationToken: cancellationToken));
        var total = await Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<GrossDemandListItemDto>
        {
            Items = [.. items], TotalCount = total, PageIndex = pageIndex, PageSize = pageSize
        };
    }

    public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT d.[Uid], d.[Version], d.[LifecycleStatus], d.[DecompositionStatus], d.[DecompositionRevision],
       d.[SourceType], d.[SourceUid], d.[SourceNo], d.[SourceLineId], d.[SourceLineNumber],
       d.[ProductId], d.[IsNonStandard], d.[Sku] AS [Sku], d.[LinkSkuSnapshot] AS [LinkSku],
       d.[SourceSku] AS [CustomerSku], d.[Description], d.[ColorName] AS [Color],
       d.[Features] AS [Specification], d.[ImagePathSnapshot] AS [ImagePath],
       d.[OriginalQty] AS [DemandQty], d.[HopefulDate] AS [RequiredDate], d.[Remark],
       d.[DecomposedTime] AS [DecomposedAt], d.[DecomposedBy],
       d.[LifecycleChangedAt], d.[LifecycleChangedBy], d.[LifecycleChangedReason],
       d.[CreatedDate], d.[CreatedBy], d.[UpdatedDate], d.[UpdatedBy]
FROM [dbo].[IndependentDemand] d
WHERE d.[Uid] = @Uid AND d.[IsDeleted] = 0;";
        return Connection.QuerySingleOrDefaultAsync<GrossDemandDetailDto>(
            new CommandDefinition(sql, new { Uid = uid }, Transaction, cancellationToken: cancellationToken));
    }

    public Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid)
        => Connection.QuerySingleOrDefaultAsync<GrossDemand>(GrossDemandSelect + @"
FROM [dbo].[IndependentDemand] d WITH (UPDLOCK, ROWLOCK)
WHERE d.[Uid] = @Uid AND d.[IsDeleted] = 0;", new { Uid = uid }, Transaction);

    public async Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid)
    {
        var rows = await Connection.QueryAsync<GrossDemand>(GrossDemandSelect + @"
FROM [dbo].[IndependentDemand] d WITH (UPDLOCK, ROWLOCK)
WHERE d.[SourceType] = @SourceType AND d.[SourceUid] = @SourceUid
  AND d.[LifecycleStatus] = @Active AND d.[IsDeleted] = 0
ORDER BY d.[SourceLineNumber], d.[Uid];", new
        {
            SourceType = sourceType,
            SourceUid = sourceUid,
            Active = GrossDemandLifecycleStatusEnum.Active
        }, Transaction);
        return [.. rows];
    }

    public async Task<IReadOnlyList<RepositoryWriteResult>> AddSourceDemandsAsync(IEnumerable<GrossDemand> demands)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var demand in demands)
        {
            _context.AuditStamp.StampForInsert(demand);
            if (demand.Uid == Guid.Empty) demand.Uid = SwiftExport.Contracts.Common.GuidSequentialGenerator.NewGuid();
            if (demand.Version <= 0) demand.Version = 1;
            const string sql = @"
INSERT INTO [dbo].[IndependentDemand]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [LifecycleStatus],[DecompositionStatus],[DecompositionRevision],
 [SourceType],[SourceUid],[SourceNo],[SourceLineId],[SourceLineNumber],[ProductId],[IsNonStandard],
 [Sku],[LinkSkuSnapshot],[SourceSku],[Description],[ColorName],[Features],[ImagePathSnapshot],
 [OriginalQty],[HopefulDate],[Remark],[DecomposedTime],[DecomposedBy],
 [LifecycleChangedAt],[LifecycleChangedBy],[LifecycleChangedReason],
 [OrderStatus],[ConfirmationStatus],[FilledQty],[ShippedQty],[IsShipped],[KittingRate],[IsDecomposed])
VALUES
(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @LifecycleStatus,@DecompositionStatus,@DecompositionRevision,
 @SourceType,@SourceUid,@SourceNo,@SourceLineId,@SourceLineNumber,@ProductId,@IsNonStandard,
 @SkuSnapshot,@LinkSkuSnapshot,@CustomerSkuSnapshot,@DescriptionSnapshot,@ColorSnapshot,@SpecificationSnapshot,@ImagePathSnapshot,
 @DemandQty,@RequiredDate,@Remark,@DecomposedAt,@DecomposedBy,
 @LifecycleChangedAt,@LifecycleChangedBy,@LifecycleChangedReason,
 1,1,0,0,0,0,0);";
            var rows = await Connection.ExecuteAsync(sql, demand, Transaction);
            results.Add(rows == 1
                ? RepositoryWriteResult.Ok(demand.Uid, null, demand.Version, "新增成功。")
                : RepositoryWriteResult.Fail(demand.Uid, null, "INSERT_FAILED", "新增毛需求失败。"));
        }
        return results;
    }

    public async Task<RepositoryWriteResult> UpdateDecompositionStateAsync(
        Guid uid, int expectedVersion, GrossDemandDecompositionStatusEnum status,
        int decompositionRevision, DateTime? decomposedAt)
    {
        var now = DateTime.Now;
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
UPDATE [dbo].[IndependentDemand]
SET [DecompositionStatus] = @Status, [DecompositionRevision] = @Revision,
    [DecomposedTime] = @DecomposedAt, [DecomposedBy] = CASE WHEN @DecomposedAt IS NULL THEN NULL ELSE @UserName END,
    [IsDecomposed] = CASE WHEN @Status = 1 THEN 1 ELSE 0 END,
    [UpdatedDate] = @Now, [UpdatedBy] = @UserName, [Version] = [Version] + 1
OUTPUT INSERTED.[Version]
WHERE [Uid] = @Uid AND [Version] = @ExpectedVersion AND [IsDeleted] = 0;",
            new { Uid = uid, ExpectedVersion = expectedVersion, Status = status, Revision = decompositionRevision,
                DecomposedAt = decomposedAt, Now = now, UserName = _context.CurrentUser.UserName }, Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(uid, expectedVersion, newVersion, "状态更新成功。")
            : RepositoryWriteResult.Fail(uid, expectedVersion, "CONCURRENCY_CONFLICT", "毛需求已被其他用户修改，请刷新后重试。");
    }

    public async Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int expectedVersion)
    {
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
UPDATE [dbo].[IndependentDemand]
SET [ProductId]=@ProductId,[IsNonStandard]=@IsNonStandard,[Sku]=@SkuSnapshot,
    [LinkSkuSnapshot]=@LinkSkuSnapshot,[SourceSku]=@CustomerSkuSnapshot,[Description]=@DescriptionSnapshot,
    [ColorName]=@ColorSnapshot,[Features]=@SpecificationSnapshot,[ImagePathSnapshot]=@ImagePathSnapshot,
    [OriginalQty]=@DemandQty,[HopefulDate]=@RequiredDate,[Remark]=@Remark,
    [UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
OUTPUT INSERTED.[Version]
WHERE [Uid]=@Uid AND [Version]=@ExpectedVersion AND [SourceType]='MD'
  AND [LifecycleStatus]=0 AND [DecompositionStatus]=0 AND [IsDeleted]=0;",
            new { demand.Uid, demand.ProductId, demand.IsNonStandard, demand.SkuSnapshot, demand.LinkSkuSnapshot,
                demand.CustomerSkuSnapshot, demand.DescriptionSnapshot, demand.ColorSnapshot, demand.SpecificationSnapshot,
                demand.ImagePathSnapshot, demand.DemandQty, demand.RequiredDate, demand.Remark, ExpectedVersion = expectedVersion,
                Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(demand.Uid, expectedVersion, newVersion, "手工毛需求更新成功。")
            : RepositoryWriteResult.Fail(demand.Uid, expectedVersion, "CONCURRENCY_CONFLICT", "手工毛需求状态或版本已变化，请刷新后重试。");
    }

    public async Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int expectedVersion, string reason)
    {
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
UPDATE [dbo].[IndependentDemand]
SET [LifecycleStatus]=@Closed,[LifecycleChangedAt]=@Now,[LifecycleChangedBy]=@UserName,
    [LifecycleChangedReason]=@Reason,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
OUTPUT INSERTED.[Version]
WHERE [Uid]=@Uid AND [Version]=@ExpectedVersion AND [SourceType]='MD'
  AND [LifecycleStatus]=@Active AND [DecompositionStatus]=@Pending AND [IsDeleted]=0;",
            new { Uid = uid, ExpectedVersion = expectedVersion, Reason = reason, Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName, Closed = GrossDemandLifecycleStatusEnum.Closed,
                Active = GrossDemandLifecycleStatusEnum.Active, Pending = GrossDemandDecompositionStatusEnum.Pending }, Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(uid, expectedVersion, newVersion, "手工毛需求已关闭。")
            : RepositoryWriteResult.Fail(uid, expectedVersion, "CONCURRENCY_CONFLICT", "手工毛需求状态或版本已变化，请刷新后重试。");
    }

    public async Task<bool> HasSourceCancellationBlockerAsync(string sourceType, Guid sourceUid, IEnumerable<Guid> sourceLineIds)
    {
        var ids = sourceLineIds.Distinct().ToArray();
        if (ids.Length == 0) return false;
        const string sql = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM [dbo].[IndependentDemand] d WITH (UPDLOCK, ROWLOCK)
    WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid AND d.[IsDeleted]=0
      AND (d.[LifecycleStatus]<>0 OR d.[DecompositionStatus]<>0 OR d.[IsDecomposed]=1 OR d.[FilledQty]>0 OR d.[ShippedQty]>0))
 OR EXISTS (
    SELECT 1 FROM [dbo].[DependentDemand] nd WITH (UPDLOCK, ROWLOCK)
    INNER JOIN [dbo].[IndependentDemand] d ON d.[Uid]=nd.[IndependentDemandId]
    WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid AND d.[IsDeleted]=0
      AND nd.[LifecycleStatus]=0 AND nd.[IsDeleted]=0)
 OR EXISTS (
    SELECT 1
    FROM [dbo].[DemandExecutionLink] link WITH (UPDLOCK, HOLDLOCK)
    INNER JOIN [dbo].[DependentDemand] nd ON nd.[Uid]=link.[DependentDemandId]
    INNER JOIN [dbo].[IndependentDemand] d ON d.[Uid]=nd.[IndependentDemandId]
    WHERE d.[SourceType]=@SourceType AND d.[SourceUid]=@SourceUid
      AND link.[LinkStatus]=0 AND link.[IsDeleted]=0)
 OR EXISTS (SELECT 1 FROM [dbo].[PackingListLine] WHERE [SoLineId] IN @LineIds AND ([IsDeleted]=0 OR [IsDeleted] IS NULL))
 OR EXISTS (SELECT 1 FROM [dbo].[PurchaseReceiveLine] WHERE [SoLineId] IN @LineIds AND ([IsDeleted]=0 OR [IsDeleted] IS NULL))
THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END;";
        return await Connection.ExecuteScalarAsync<bool>(sql, new { SourceType = sourceType, SourceUid = sourceUid, LineIds = ids }, Transaction);
    }

    public Task<int> SoftDeleteBySourceAsync(string sourceType, Guid sourceUid)
        => Connection.ExecuteAsync(@"
UPDATE [dbo].[IndependentDemand]
SET [IsDeleted]=1,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
WHERE [SourceType]=@SourceType AND [SourceUid]=@SourceUid
  AND [LifecycleStatus]=@Active AND [IsDeleted]=0;",
            new { SourceType = sourceType, SourceUid = sourceUid, Active = GrossDemandLifecycleStatusEnum.Active,
                Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, Transaction);

    public Task<int> VoidPendingBySourceAsync(string sourceType, Guid sourceUid, string reason)
        => Connection.ExecuteAsync(@"
UPDATE [dbo].[IndependentDemand]
SET [LifecycleStatus]=@Voided,[LifecycleChangedAt]=@Now,[LifecycleChangedBy]=@UserName,
    [LifecycleChangedReason]=@Reason,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
WHERE [SourceType]=@SourceType AND [SourceUid]=@SourceUid AND [LifecycleStatus]=@Active
  AND [DecompositionStatus]=@Pending AND [IsDeleted]=0;",
            new { SourceType = sourceType, SourceUid = sourceUid, Reason = reason, Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName, Voided = GrossDemandLifecycleStatusEnum.Voided,
                Active = GrossDemandLifecycleStatusEnum.Active, Pending = GrossDemandDecompositionStatusEnum.Pending }, Transaction);

    private static void AddKeywordFilter(string? keyword, ICollection<string> where, DynamicParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        where.Add("(d.[SourceNo] LIKE @Keyword OR d.[Sku] LIKE @Keyword OR d.[SourceSku] LIKE @Keyword OR d.[Description] LIKE @Keyword)");
        parameters.Add("Keyword", $"%{keyword.Trim()}%");
    }

    private static void AddListFilter<T>(IEnumerable<T>? values, string column, string parameterName,
        ICollection<string> where, DynamicParameters parameters)
    {
        var array = values?.Distinct().ToArray() ?? [];
        if (array.Length == 0) return;
        where.Add($"{column} IN @{parameterName}");
        parameters.Add(parameterName, array);
    }

    private const string GrossDemandSelect = @"
SELECT d.[Uid],d.[Version],d.[IsDeleted],d.[CreatedDate],d.[CreatedBy],d.[UpdatedDate],d.[UpdatedBy],
       d.[LifecycleStatus],d.[DecompositionStatus],d.[DecompositionRevision],
       d.[SourceType],d.[SourceUid],d.[SourceNo],d.[SourceLineId],d.[SourceLineNumber],
       d.[ProductId],d.[IsNonStandard],d.[Sku] AS [SkuSnapshot],d.[LinkSkuSnapshot],
       d.[SourceSku] AS [CustomerSkuSnapshot],d.[Description] AS [DescriptionSnapshot],
       d.[ColorName] AS [ColorSnapshot],d.[Features] AS [SpecificationSnapshot],
       d.[ImagePathSnapshot],d.[OriginalQty] AS [DemandQty],d.[HopefulDate] AS [RequiredDate],d.[Remark],
       d.[DecomposedTime] AS [DecomposedAt],d.[DecomposedBy],d.[LifecycleChangedAt],
       d.[LifecycleChangedBy],d.[LifecycleChangedReason] ";

    private const string ExecutionProgressSql = @"
CASE
 WHEN NOT EXISTS (SELECT 1 FROM [dbo].[DependentDemand] epn WHERE epn.[IndependentDemandId]=d.[Uid] AND epn.[LifecycleStatus]=0 AND epn.[IsDeleted]=0) THEN 0
 WHEN ISNULL((SELECT SUM(epl.[ReceivedQty]) FROM [dbo].[DemandExecutionLink] epl INNER JOIN [dbo].[DependentDemand] epn ON epn.[Uid]=epl.[DependentDemandId] WHERE epn.[IndependentDemandId]=d.[Uid] AND epn.[LifecycleStatus]=0 AND epn.[IsDeleted]=0 AND epl.[LinkStatus]=0 AND epl.[IsDeleted]=0),0)>=d.[OriginalQty] THEN 4
 WHEN ISNULL((SELECT SUM(epl.[CompletedQty]) FROM [dbo].[DemandExecutionLink] epl INNER JOIN [dbo].[DependentDemand] epn ON epn.[Uid]=epl.[DependentDemandId] WHERE epn.[IndependentDemandId]=d.[Uid] AND epn.[LifecycleStatus]=0 AND epn.[IsDeleted]=0 AND epl.[LinkStatus]=0 AND epl.[IsDeleted]=0),0)>0 THEN 3
 WHEN ISNULL((SELECT SUM(epl.[AllocatedQty]-epl.[ReleasedQty]) FROM [dbo].[DemandExecutionLink] epl INNER JOIN [dbo].[DependentDemand] epn ON epn.[Uid]=epl.[DependentDemandId] WHERE epn.[IndependentDemandId]=d.[Uid] AND epn.[LifecycleStatus]=0 AND epn.[IsDeleted]=0 AND epl.[LinkStatus]=0 AND epl.[IsDeleted]=0),0)>=d.[OriginalQty] THEN 2
 WHEN ISNULL((SELECT SUM(epl.[AllocatedQty]-epl.[ReleasedQty]) FROM [dbo].[DemandExecutionLink] epl INNER JOIN [dbo].[DependentDemand] epn ON epn.[Uid]=epl.[DependentDemandId] WHERE epn.[IndependentDemandId]=d.[Uid] AND epn.[LifecycleStatus]=0 AND epn.[IsDeleted]=0 AND epl.[LinkStatus]=0 AND epl.[IsDeleted]=0),0)>0 THEN 1
 ELSE 0 END";
}
