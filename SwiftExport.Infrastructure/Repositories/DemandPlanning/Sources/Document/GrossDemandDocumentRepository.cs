using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;

/// <summary>在调用方事务中锁定并写入来源需求及生命周期事实。</summary>
public sealed class GrossDemandDocumentRepository(RepositoryBaseContext context) : IGrossDemandDocumentRepository
{
    private readonly RepositoryBaseContext _context = context;
    private IDbConnection Connection => _context.Session.Connection;
    private IDbTransaction? Transaction => _context.Session.Transaction;

    /// <inheritdoc/>
    public Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid)
        => Connection.QuerySingleOrDefaultAsync<GrossDemand>(GrossDemandSelect + @"
FROM [dbo].[GrossDemand] d WITH (UPDLOCK, HOLDLOCK)
WHERE d.[Uid] = @Uid AND d.[IsDeleted] = 0;", new { Uid = uid }, Transaction);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid)
    {
        var rows = await Connection.QueryAsync<GrossDemand>(GrossDemandSelect + @"
FROM [dbo].[GrossDemand] d WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
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

    /// <inheritdoc/>
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
 [LifecycleStatus],
 [SourceType],[SourceUid],[SourceNo],[SourceLineId],[SourceLineNumber],[SourceVersion],[DemandBasisFingerprint],[CustomerId],[ProductId],[IsNonStandard],
 [SkuSnapshot],[LinkSkuSnapshot],[CustomerSkuSnapshot],[DescriptionSnapshot],[ColorSnapshot],[SpecificationSnapshot],[ImagePathSnapshot],[UnitSnapshot],
 [DemandQty],[RequiredDate],[Remark],
 [LifecycleChangedAt],[LifecycleChangedBy],[LifecycleChangedReason])
VALUES
(@Uid,@Version,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,
 @LifecycleStatus,
 @SourceType,@SourceUid,@SourceNo,@SourceLineId,@SourceLineNumber,@SourceVersion,@DemandBasisFingerprint,@CustomerId,@ProductId,@IsNonStandard,
 @SkuSnapshot,@LinkSkuSnapshot,@CustomerSkuSnapshot,@DescriptionSnapshot,@ColorSnapshot,@SpecificationSnapshot,@ImagePathSnapshot,@UnitSnapshot,
 @DemandQty,@RequiredDate,@Remark,
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <summary>删除由上层物料历史校验判定为无历史的来源需求；本仓储不反向读取物料表。</summary>
    public Task<int> HardDeleteForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds)
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    private const string GrossDemandSelect = @"
SELECT d.[Uid],d.[Version],d.[IsDeleted],d.[CreatedDate],d.[CreatedBy],d.[UpdatedDate],d.[UpdatedBy],
       d.[LifecycleStatus],
       d.[SourceType],d.[SourceUid],d.[SourceNo],d.[SourceLineId],d.[SourceLineNumber],d.[SourceVersion],d.[DemandBasisFingerprint],d.[CustomerId],
       d.[ProductId],d.[IsNonStandard],d.[SkuSnapshot],d.[LinkSkuSnapshot],d.[CustomerSkuSnapshot],
       d.[DescriptionSnapshot],d.[ColorSnapshot],d.[SpecificationSnapshot],
       d.[ImagePathSnapshot],d.[UnitSnapshot],d.[DemandQty],d.[RequiredDate],d.[Remark],d.[LifecycleChangedAt],
       d.[LifecycleChangedBy],d.[LifecycleChangedReason] ";

}
