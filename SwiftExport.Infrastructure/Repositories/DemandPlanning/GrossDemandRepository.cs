using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning;

/// <summary>持久化来源需求本体，并提供来源身份、版本和生命周期操作。</summary>
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
        var sourceMode = request.SourceUid.HasValue;
        var pageIndex = sourceMode ? 1 : Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var (where, parameters) = BuildWorkspaceFilters(request);
        if (!sourceMode)
        {
            parameters.Add("Offset", (pageIndex - 1) * pageSize);
            parameters.Add("PageSize", pageSize);
        }

        var whereSql = string.Join(" AND ", where);
        var pagingSql = sourceMode
            ? ";"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        var selectSql = $@"
SELECT
    d.[Uid], d.[Version], d.[LifecycleStatus],
    d.[SourceType], d.[SourceUid], d.[SourceNo], d.[SourceLineId], d.[SourceLineNumber], d.[SourceVersion], d.[CustomerId],
    d.[ProductId], d.[IsNonStandard], d.[SkuSnapshot] AS [Sku], d.[LinkSkuSnapshot] AS [LinkSku],
    d.[CustomerSkuSnapshot] AS [CustomerSku], d.[DescriptionSnapshot] AS [Description], d.[ColorSnapshot] AS [Color],
    d.[SpecificationSnapshot] AS [Specification], d.[ImagePathSnapshot] AS [ImagePath], d.[UnitSnapshot] AS [Unit],
    d.[DemandQty], d.[RequiredDate], d.[Remark],
    d.[CreatedDate], d.[CreatedBy], d.[UpdatedDate], d.[UpdatedBy]
FROM [dbo].[GrossDemand] d
WHERE {whereSql}
ORDER BY CASE WHEN d.[RequiredDate] IS NULL THEN 1 ELSE 0 END, d.[RequiredDate], d.[CreatedDate], d.[Uid]
{pagingSql}";
        var countSql = $"SELECT COUNT(1) FROM [dbo].[GrossDemand] d WHERE {whereSql};";

        var items = await Connection.QueryAsync<GrossDemandListItemDto>(
            new CommandDefinition(selectSql, parameters, Transaction, cancellationToken: cancellationToken));
        var total = await Connection.ExecuteScalarAsync<int>(
            new CommandDefinition(countSql, parameters, Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<GrossDemandListItemDto>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = sourceMode ? Math.Max(1, total) : pageSize
        };
    }

    public async Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(
        GrossDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var where = new List<string> { "d.[IsDeleted] = 0" };
        var parameters = new DynamicParameters();
        AddListFilter(request.LifecycleStatuses, "d.[LifecycleStatus]", "SourceLifecycleStatuses", where, parameters);
        var sql = $@"
SELECT d.[SourceUid],MAX(d.[SourceType]) AS [SourceType],MAX(d.[SourceNo]) AS [SourceNo],COUNT(1) AS [DemandCount]
FROM [dbo].[GrossDemand] d
WHERE {string.Join(" AND ", where)}
GROUP BY d.[SourceUid]
ORDER BY MAX(d.[SourceType]),MAX(d.[SourceNo]),d.[SourceUid];";
        var groups = await Connection.QueryAsync<GrossDemandSourceGroupDto>(
            new CommandDefinition(sql, parameters, Transaction, cancellationToken: cancellationToken));
        return [.. groups];
    }

    public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT d.[Uid], d.[Version], d.[LifecycleStatus],
       d.[SourceType], d.[SourceUid], d.[SourceNo], d.[SourceLineId], d.[SourceLineNumber], d.[SourceVersion], d.[CustomerId],
       d.[ProductId], d.[IsNonStandard], d.[SkuSnapshot] AS [Sku], d.[LinkSkuSnapshot] AS [LinkSku],
       d.[CustomerSkuSnapshot] AS [CustomerSku], d.[DescriptionSnapshot] AS [Description], d.[ColorSnapshot] AS [Color],
       d.[SpecificationSnapshot] AS [Specification], d.[ImagePathSnapshot] AS [ImagePath], d.[UnitSnapshot] AS [Unit],
       d.[DemandQty], d.[RequiredDate], d.[Remark],
       d.[LifecycleChangedAt], d.[LifecycleChangedBy], d.[LifecycleChangedReason],
       d.[CreatedDate], d.[CreatedBy], d.[UpdatedDate], d.[UpdatedBy]
FROM [dbo].[GrossDemand] d
WHERE d.[Uid] = @Uid AND d.[IsDeleted] = 0;";
        return Connection.QuerySingleOrDefaultAsync<GrossDemandDetailDto>(
            new CommandDefinition(sql, new { Uid = uid }, Transaction, cancellationToken: cancellationToken));
    }

    public Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid)
        => Connection.QuerySingleOrDefaultAsync<GrossDemand>(GrossDemandSelect + @"
FROM [dbo].[GrossDemand] d WITH (UPDLOCK, HOLDLOCK)
WHERE d.[Uid] = @Uid AND d.[IsDeleted] = 0;", new { Uid = uid }, Transaction);

    public async Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid)
    {
        var rows = await Connection.QueryAsync<GrossDemand>(GrossDemandSelect + @"
FROM [dbo].[GrossDemand] d WITH (UPDLOCK, ROWLOCK)
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
INSERT INTO [dbo].[GrossDemand]
([Uid],[Version],[IsDeleted],[CreatedDate],[CreatedBy],[UpdatedDate],[UpdatedBy],
 [LifecycleStatus],[DecompositionStatus],[DecompositionRevision],
 [SourceType],[SourceUid],[SourceNo],[SourceLineId],[SourceLineNumber],[SourceVersion],[DemandBasisFingerprint],[CustomerId],[ProductId],[IsNonStandard],
 [SkuSnapshot],[LinkSkuSnapshot],[CustomerSkuSnapshot],[DescriptionSnapshot],[ColorSnapshot],[SpecificationSnapshot],[ImagePathSnapshot],[UnitSnapshot],
 [DemandQty],[RequiredDate],[Remark],[DecomposedAt],[DecomposedBy],
 [LifecycleChangedAt],[LifecycleChangedBy],[LifecycleChangedReason])
VALUES
(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @LifecycleStatus,@DecompositionStatus,@DecompositionRevision,
 @SourceType,@SourceUid,@SourceNo,@SourceLineId,@SourceLineNumber,@SourceVersion,@DemandBasisFingerprint,@CustomerId,@ProductId,@IsNonStandard,
 @SkuSnapshot,@LinkSkuSnapshot,@CustomerSkuSnapshot,@DescriptionSnapshot,@ColorSnapshot,@SpecificationSnapshot,@ImagePathSnapshot,@UnitSnapshot,
 @DemandQty,@RequiredDate,@Remark,@DecomposedAt,@DecomposedBy,
 @LifecycleChangedAt,@LifecycleChangedBy,@LifecycleChangedReason);";
            var rows = await Connection.ExecuteAsync(sql, demand, Transaction);
            results.Add(rows == 1
                ? RepositoryWriteResult.Ok(demand.Uid, null, demand.Version, "新增成功。")
                : RepositoryWriteResult.Fail(demand.Uid, null, "INSERT_FAILED", "新增毛需求失败。"));
        }
        return results;
    }

    /// <summary>按实体 Version 更新同一来源行的当前需求事实，不改变生命周期和拆解历史。</summary>
    public async Task<RepositoryWriteResult> UpdateSourceDemandAsync(GrossDemand demand, int expectedVersion)
    {
        const string sql = @"
UPDATE dbo.GrossDemand
SET SourceNo=@SourceNo,SourceLineNumber=@SourceLineNumber,SourceVersion=@SourceVersion,DemandBasisFingerprint=@DemandBasisFingerprint,CustomerId=@CustomerId,
    ProductId=@ProductId,IsNonStandard=@IsNonStandard,SkuSnapshot=@SkuSnapshot,LinkSkuSnapshot=@LinkSkuSnapshot,
    CustomerSkuSnapshot=@CustomerSkuSnapshot,DescriptionSnapshot=@DescriptionSnapshot,ColorSnapshot=@ColorSnapshot,
    SpecificationSnapshot=@SpecificationSnapshot,ImagePathSnapshot=@ImagePathSnapshot,UnitSnapshot=@UnitSnapshot,
    DemandQty=@DemandQty,RequiredDate=@RequiredDate,Remark=@Remark,
    UpdatedDate=@Now,UpdatedBy=@UserName,Version=Version+1
OUTPUT INSERTED.Version
WHERE Uid=@Uid AND Version=@ExpectedVersion AND LifecycleStatus=@Active AND IsDeleted=0;";
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(sql, new
        {
            demand.Uid, demand.SourceNo, demand.SourceLineNumber, demand.SourceVersion, demand.DemandBasisFingerprint, demand.CustomerId,
            demand.ProductId, demand.IsNonStandard, demand.SkuSnapshot, demand.LinkSkuSnapshot,
            demand.CustomerSkuSnapshot, demand.DescriptionSnapshot, demand.ColorSnapshot,
            demand.SpecificationSnapshot, demand.ImagePathSnapshot, demand.UnitSnapshot,
            demand.DemandQty, demand.RequiredDate, demand.Remark, ExpectedVersion = expectedVersion,
            Active = GrossDemandLifecycleStatusEnum.Active, Now = DateTime.Now,
            UserName = _context.CurrentUser.UserName
        }, Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(demand.Uid, expectedVersion, newVersion, "来源需求已更新。")
            : RepositoryWriteResult.Fail(demand.Uid, expectedVersion, "CONCURRENCY_CONFLICT", "来源需求状态或版本已变化。");
    }

    public async Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int expectedVersion)
    {
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
UPDATE [dbo].[GrossDemand]
SET [ProductId]=@ProductId,[IsNonStandard]=@IsNonStandard,[SkuSnapshot]=@SkuSnapshot,
    [LinkSkuSnapshot]=@LinkSkuSnapshot,[CustomerSkuSnapshot]=@CustomerSkuSnapshot,[DescriptionSnapshot]=@DescriptionSnapshot,
    [ColorSnapshot]=@ColorSnapshot,[SpecificationSnapshot]=@SpecificationSnapshot,[ImagePathSnapshot]=@ImagePathSnapshot,
    [DemandBasisFingerprint]=@DemandBasisFingerprint,[DemandQty]=@DemandQty,[RequiredDate]=@RequiredDate,[Remark]=@Remark,
    [UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
OUTPUT INSERTED.[Version]
WHERE [Uid]=@Uid AND [Version]=@ExpectedVersion AND [SourceType]='MD'
  AND [LifecycleStatus]=0 AND [IsDeleted]=0;",
            new { demand.Uid, demand.ProductId, demand.IsNonStandard, demand.SkuSnapshot, demand.LinkSkuSnapshot,
                demand.CustomerSkuSnapshot, demand.DescriptionSnapshot, demand.ColorSnapshot, demand.SpecificationSnapshot,
                demand.ImagePathSnapshot, demand.DemandBasisFingerprint, demand.DemandQty, demand.RequiredDate, demand.Remark, ExpectedVersion = expectedVersion,
                Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(demand.Uid, expectedVersion, newVersion, "手工毛需求更新成功。")
            : RepositoryWriteResult.Fail(demand.Uid, expectedVersion, "CONCURRENCY_CONFLICT", "手工毛需求状态或版本已变化，请刷新后重试。");
    }

    public async Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int expectedVersion, string reason)
    {
        var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(@"
UPDATE [dbo].[GrossDemand]
SET [LifecycleStatus]=@Closed,[LifecycleChangedAt]=@Now,[LifecycleChangedBy]=@UserName,
    [LifecycleChangedReason]=@Reason,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
OUTPUT INSERTED.[Version]
WHERE [Uid]=@Uid AND [Version]=@ExpectedVersion AND [SourceType]='MD'
  AND [LifecycleStatus]=@Active AND [IsDeleted]=0;",
            new { Uid = uid, ExpectedVersion = expectedVersion, Reason = reason, Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName, Closed = GrossDemandLifecycleStatusEnum.Closed,
                Active = GrossDemandLifecycleStatusEnum.Active }, Transaction);
        return newVersion.HasValue
            ? RepositoryWriteResult.Ok(uid, expectedVersion, newVersion, "手工毛需求已关闭。")
            : RepositoryWriteResult.Fail(uid, expectedVersion, "CONCURRENCY_CONFLICT", "手工毛需求状态或版本已变化，请刷新后重试。");
    }

    public Task<int> HardDeleteNeverDecomposedAsync(IEnumerable<Guid> grossDemandIds)
    {
        var ids = grossDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult(0);
        return Connection.ExecuteAsync(@"
DELETE d
FROM [dbo].[GrossDemand] d
WHERE d.[Uid] IN @GrossDemandIds
  AND d.[LifecycleStatus]=@Active
  AND d.[IsDeleted]=0;",
            new
            {
                GrossDemandIds = ids,
                Active = GrossDemandLifecycleStatusEnum.Active
            }, Transaction);
    }

    public Task<int> VoidRolledBackForSourceCancellationAsync(
        IEnumerable<Guid> grossDemandIds,
        string reason)
    {
        var ids = grossDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return Task.FromResult(0);
        return Connection.ExecuteAsync(@"
UPDATE d
SET d.[LifecycleStatus]=@Voided,
    d.[LifecycleChangedAt]=@Now,
    d.[LifecycleChangedBy]=@UserName,
    d.[LifecycleChangedReason]=@Reason,
    d.[UpdatedDate]=@Now,
    d.[UpdatedBy]=@UserName,
    d.[Version]=d.[Version]+1
FROM [dbo].[GrossDemand] d
WHERE d.[Uid] IN @GrossDemandIds
  AND d.[LifecycleStatus]=@Active
  AND d.[IsDeleted]=0;",
            new
            {
                GrossDemandIds = ids,
                Reason = reason,
                Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName,
                Active = GrossDemandLifecycleStatusEnum.Active,
                Voided = GrossDemandLifecycleStatusEnum.Voided
            }, Transaction);
    }

    public Task<int> VoidPendingBySourceAsync(string sourceType, Guid sourceUid, string reason)
        => Connection.ExecuteAsync(@"
UPDATE [dbo].[GrossDemand]
SET [LifecycleStatus]=@Voided,[LifecycleChangedAt]=@Now,[LifecycleChangedBy]=@UserName,
    [LifecycleChangedReason]=@Reason,[UpdatedDate]=@Now,[UpdatedBy]=@UserName,[Version]=[Version]+1
WHERE [SourceType]=@SourceType AND [SourceUid]=@SourceUid AND [LifecycleStatus]=@Active
  AND [IsDeleted]=0;",
            new { SourceType = sourceType, SourceUid = sourceUid, Reason = reason, Now = DateTime.Now,
                UserName = _context.CurrentUser.UserName, Voided = GrossDemandLifecycleStatusEnum.Voided,
                Active = GrossDemandLifecycleStatusEnum.Active }, Transaction);

    private static (List<string> Where, DynamicParameters Parameters) BuildWorkspaceFilters(
        GrossDemandSearchRequest request)
    {
        var where = new List<string> { "d.[IsDeleted] = 0" };
        var parameters = new DynamicParameters();
        AddKeywordFilter(request.Keyword, where, parameters);
        AddListFilter(request.SourceTypes, "d.[SourceType]", "SourceTypes", where, parameters);
        AddListFilter(request.SourceNos, "d.[SourceNo]", "SourceNos", where, parameters);
        AddListFilter(request.Skus, "d.[SkuSnapshot]", "Skus", where, parameters);
        AddListFilter(request.CustomerSkus, "d.[CustomerSkuSnapshot]", "CustomerSkus", where, parameters);
        AddListFilter(request.LifecycleStatuses, "d.[LifecycleStatus]", "LifecycleStatuses", where, parameters);
        if (request.RequiredDateFrom.HasValue)
        {
            where.Add("d.[RequiredDate] >= @RequiredDateFrom");
            parameters.Add("RequiredDateFrom", request.RequiredDateFrom.Value);
        }
        if (request.RequiredDateTo.HasValue)
        {
            where.Add("d.[RequiredDate] < DATEADD(day, 1, @RequiredDateTo)");
            parameters.Add("RequiredDateTo", request.RequiredDateTo.Value.Date);
        }
        if (request.IsNonStandard.HasValue)
        {
            where.Add("d.[IsNonStandard] = @IsNonStandard");
            parameters.Add("IsNonStandard", request.IsNonStandard.Value);
        }
        if (request.SourceUid.HasValue)
        {
            where.Add("d.[SourceUid] = @SourceUid");
            parameters.Add("SourceUid", request.SourceUid.Value);
        }

        return (where, parameters);
    }

    private static void AddKeywordFilter(string? keyword, ICollection<string> where, DynamicParameters parameters)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        where.Add("(d.[SourceNo] LIKE @Keyword OR d.[SkuSnapshot] LIKE @Keyword OR d.[CustomerSkuSnapshot] LIKE @Keyword OR d.[DescriptionSnapshot] LIKE @Keyword)");
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
       d.[LifecycleStatus],
       d.[SourceType],d.[SourceUid],d.[SourceNo],d.[SourceLineId],d.[SourceLineNumber],d.[SourceVersion],d.[DemandBasisFingerprint],d.[CustomerId],
       d.[ProductId],d.[IsNonStandard],d.[SkuSnapshot],d.[LinkSkuSnapshot],d.[CustomerSkuSnapshot],
       d.[DescriptionSnapshot],d.[ColorSnapshot],d.[SpecificationSnapshot],
       d.[ImagePathSnapshot],d.[UnitSnapshot],d.[DemandQty],d.[RequiredDate],d.[Remark],d.[LifecycleChangedAt],
       d.[LifecycleChangedBy],d.[LifecycleChangedReason] ";

}
