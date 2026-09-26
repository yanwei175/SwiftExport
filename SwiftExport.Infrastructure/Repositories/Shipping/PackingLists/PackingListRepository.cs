using Dapper;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>使用 PackingList 头行表实现管理查询、草稿持久化和模块专属来源补充，不处理库存动作。</summary>
public sealed partial class PackingListRepository(RepositoryBaseContext context) :
    IPackingListQueryRepository, IPackingListDocumentRepository, IPackingListSourceExitRepository
{
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public async Task<bool> HasForeignPackingGroupsAsync(Guid headId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default)
    {
        // 组身份的应用锁先于查询，防止两个箱单同时首次使用同一个空组标识。
        foreach (var id in groupIds.Distinct().Order())
        {
            var result = await context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@Key,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; SELECT @r;",
                new { Key = "PackingList.Group:" + id.ToString("N") }, context.Session.Transaction, cancellationToken: cancellationToken));
            if (result < 0) throw new DBConcurrencyException("拼箱标识正在被其他操作使用，请重试。");
        }
        if (groupIds.Count == 0) return false;
        return await context.Session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.PackingListLine WITH(READCOMMITTED) WHERE PackingGroupId IN @Ids AND PackingListHeadId<>@HeadId) THEN 1 ELSE 0 END);",
            new { Ids = groupIds, HeadId = headId }, context.Session.Transaction, cancellationToken: cancellationToken));
    }

    /// <inheritdoc/>
    public Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.PackingListHead',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.PackingListLine',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.OutboundDocument',N'U') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListHead',N'CreateCommandId') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'GrossDemandId') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'PackingGroupId') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListHead',N'ShippingLocationSnapshot') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'IsNonStandardSnapshot') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'SourceTypeSnapshot') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'SourceDocumentNoSnapshot') IS NOT NULL
              AND COL_LENGTH(N'dbo.OutboundDocument',N'TriggerPackingListId') IS NOT NULL
              AND EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.PackingListHead')
                         AND name=N'UX_PackingListHead_CreateCommand')
              THEN 1 ELSE 0 END);
            """, null, cancellationToken));

    /// <inheritdoc/>
    public Task<PackingListHead?> LockHeaderAsync(Guid uid, bool includeDeleted, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<PackingListHead>(Command("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PackingListNo,CustomerId,
                   CustomerNameSnapshot,CustomerAddressSnapshot,AttentionSnapshot,InvoiceDate,ContainerNo,SealNo,ShippingLocationSnapshot,
                   Note,Etd,Eta,LifecycleStatus,PostingStatus,PostingDate,PostedDate,PostedBy,
                   ReversedDate,ReversedBy,ReverseReason,CreateCommandId,PostCommandId,ReverseCommandId
            FROM dbo.PackingListHead WITH(UPDLOCK,HOLDLOCK)
            WHERE Uid=@Uid AND (@IncludeDeleted=1 OR IsDeleted=0);
            """, new { Uid = uid, IncludeDeleted = includeDeleted }, cancellationToken));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PackingListLine>> LockLinesAsync(Guid uid, CancellationToken cancellationToken = default) =>
        (await context.Session.Connection.QueryAsync<PackingListLine>(Command("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,PackingListHeadId,LineNumber,
                   GrossDemandId,ProductId,IsNonStandardSnapshot,SourceTypeSnapshot,SourceDocumentNoSnapshot,
                   SourceLineNumberSnapshot,SkuSnapshot,LinkSkuSnapshot,CustomerSkuSnapshot,DescriptionEnSnapshot,
                   ColorNameEnSnapshot,PackingGroupId,UnitSnapshot,Qty,PcsPerPackage,PackageCount,NwPerPackage,GwPerPackage,
                   CbmPerPackage,SubTotalNw,SubTotalGw,SubTotalCbm,CustomerPoNo,InvoiceNo,Marks,Note
            FROM dbo.PackingListLine WITH(UPDLOCK,HOLDLOCK)
            WHERE PackingListHeadId=@Uid AND IsDeleted=0 ORDER BY Uid;
            """, new { Uid = uid }, cancellationToken))).ToList();

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, Guid>> LockLineOwnersAsync(
        IReadOnlyCollection<Guid> lineIds, CancellationToken cancellationToken = default)
    {
        if (lineIds.Count == 0) return new Dictionary<Guid, Guid>();
        var rows = await context.Session.Connection.QueryAsync<LineOwnerRow>(Command("""
            SELECT Uid,PackingListHeadId FROM dbo.PackingListLine WITH(UPDLOCK,HOLDLOCK) WHERE Uid IN @Ids;
            """, new { Ids = lineIds }, cancellationToken));
        return rows.ToDictionary(row => row.Uid, row => row.PackingListHeadId);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(PackingListHead header, IReadOnlyList<PackingListLine> activeLines,
        IReadOnlyList<PackingListLine> deletedLines, int expectedVersion, CancellationToken cancellationToken = default)
    {
        if (expectedVersion == 0)
        {
            context.AuditStamp.StampForInsert(header);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.PackingListHead(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
                  PackingListNo,CustomerId,CustomerNameSnapshot,CustomerAddressSnapshot,AttentionSnapshot,InvoiceDate,
                  ContainerNo,SealNo,ShippingLocationSnapshot,Note,Etd,Eta,LifecycleStatus,PostingStatus,PostingDate,PostedDate,
                  PostedBy,ReversedDate,ReversedBy,ReverseReason,CreateCommandId,PostCommandId,ReverseCommandId)
                VALUES(@Uid,1,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PackingListNo,@CustomerId,
                  @CustomerNameSnapshot,@CustomerAddressSnapshot,@AttentionSnapshot,@InvoiceDate,@ContainerNo,@SealNo,@ShippingLocationSnapshot,
                  @Note,@Etd,@Eta,@LifecycleStatus,@PostingStatus,@PostingDate,@PostedDate,@PostedBy,
                  @ReversedDate,@ReversedBy,@ReverseReason,@CreateCommandId,@PostCommandId,@ReverseCommandId);
                """, header, cancellationToken));
            header.Version = 1;
        }
        else
        {
            context.AuditStamp.StampForUpdate(header);
            var count = await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE dbo.PackingListHead SET CustomerId=@CustomerId,CustomerNameSnapshot=@CustomerNameSnapshot,
                  CustomerAddressSnapshot=@CustomerAddressSnapshot,AttentionSnapshot=@AttentionSnapshot,
                  InvoiceDate=@InvoiceDate,ContainerNo=@ContainerNo,SealNo=@SealNo,ShippingLocationSnapshot=@ShippingLocationSnapshot,
                  Note=@Note,Etd=@Etd,Eta=@Eta,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND LifecycleStatus=0 AND PostingStatus=0;
                """, header, cancellationToken));
            if (count != 1) throw new DBConcurrencyException("装箱单头版本或状态已变化。");
            header.Version++;
            // 有效行号有唯一索引。先移到不与旧/新行号重叠的临时正整数，再写最终顺序；
            // 临时变更不增加版本，失败随调用方事务回滚，删除行在软删除时恢复原行号。
            var currentOrder = (await context.Session.Connection.QueryAsync<PackingListLine>(Command(
                "SELECT Uid,Version,LineNumber FROM dbo.PackingListLine WITH(UPDLOCK,HOLDLOCK) WHERE PackingListHeadId=@Uid AND IsDeleted=0 ORDER BY Uid;",
                new { header.Uid }, cancellationToken))).ToArray();
            var occupied = currentOrder.Select(x => x.LineNumber).Concat(activeLines.Select(x => x.LineNumber)).ToHashSet();
            var temporary = 1;
            foreach (var row in currentOrder)
            {
                while (occupied.Contains(temporary)) temporary = checked(temporary + 1);
                occupied.Add(temporary);
                var moved = await context.Session.Connection.ExecuteAsync(Command(
                    "UPDATE dbo.PackingListLine SET LineNumber=@Temporary WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0;",
                    new { row.Uid, row.Version, Temporary = temporary }, cancellationToken));
                if (moved != 1) throw new DBConcurrencyException("装箱单行排序版本已变化。");
            }
        }

        foreach (var line in activeLines)
        {
            var insert = line.Version == 0;
            if (insert) context.AuditStamp.StampForInsert(line); else context.AuditStamp.StampForUpdate(line);
            var count = await context.Session.Connection.ExecuteAsync(Command(insert ? InsertLineSql : UpdateLineSql, line, cancellationToken));
            if (count != 1) throw new DBConcurrencyException($"装箱单行 {line.Uid} 版本已变化。");
            if (insert) line.Version = 1; else line.Version++;
        }

        foreach (var line in deletedLines)
        {
            context.AuditStamp.StampForUpdate(line);
            var count = await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE dbo.PackingListLine SET IsDeleted=1,LineNumber=@LineNumber,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND PackingListHeadId=@PackingListHeadId AND Version=@Version AND IsDeleted=0;
                """, line, cancellationToken));
            if (count != 1) throw new DBConcurrencyException($"待删除装箱单行 {line.Uid} 版本已变化。");
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(PackingListHead header, CancellationToken cancellationToken = default)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.PackingListHead SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND LifecycleStatus=0 AND PostingStatus=0;
            """, header, cancellationToken));
        if (count != 1) throw new DBConcurrencyException("装箱单版本或状态已变化。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.PackingListLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE PackingListHeadId=@Uid AND IsDeleted=0;
            """, new { header.Uid, header.UpdatedDate, header.UpdatedBy }, cancellationToken));
        header.Version++;
    }

    private const string HeaderListColumns = "h.Uid,h.Version,h.IsDeleted,h.CreatedDate,h.CreatedBy,h.UpdatedDate,h.UpdatedBy,h.PackingListNo,h.CustomerId,h.CustomerNameSnapshot,h.InvoiceDate,h.ContainerNo,h.SealNo,h.ShippingLocationSnapshot,h.Etd,h.Eta,h.LifecycleStatus,h.PostingStatus";
    private const string HeaderDetailColumns = "h.Uid,h.Version,h.IsDeleted,h.CreatedDate,h.CreatedBy,h.UpdatedDate,h.UpdatedBy,h.PackingListNo,h.CustomerId,h.CustomerNameSnapshot,h.CustomerAddressSnapshot,h.AttentionSnapshot,h.InvoiceDate,h.ContainerNo,h.SealNo,h.ShippingLocationSnapshot,h.Note,h.Etd,h.Eta,h.LifecycleStatus,h.PostingStatus,h.PostingDate,h.PostedDate,h.PostedBy,h.ReversedDate,h.ReversedBy,h.ReverseReason,h.CreateCommandId,h.PostCommandId,h.ReverseCommandId";
    private const string InvoiceSummary = "COALESCE((SELECT STRING_AGG(x.InvoiceNo,N', ') WITHIN GROUP (ORDER BY x.FirstLine,x.FirstUid) FROM (SELECT LTRIM(RTRIM(il.InvoiceNo)) COLLATE Latin1_General_100_BIN2 InvoiceNo,MIN(il.LineNumber) FirstLine,MIN(il.Uid) FirstUid FROM dbo.PackingListLine il WHERE il.PackingListHeadId=h.Uid AND il.IsDeleted=0 AND NULLIF(LTRIM(RTRIM(il.InvoiceNo)),N'') IS NOT NULL GROUP BY LTRIM(RTRIM(il.InvoiceNo)) COLLATE Latin1_General_100_BIN2) x),N'')";

    /// <summary>承载锁定查询返回的行归属，避免依赖 ValueTuple 的 Dapper 构造映射。</summary>
    private sealed class LineOwnerRow
    {
        /// <summary>装箱单行的稳定标识。</summary>
        public Guid Uid { get; set; }
        /// <summary>占用该行标识的装箱单头标识。</summary>
        public Guid PackingListHeadId { get; set; }
    }

    private const string InsertLineSql = """
        INSERT dbo.PackingListLine(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,
          PackingListHeadId,LineNumber,GrossDemandId,ProductId,IsNonStandardSnapshot,SourceTypeSnapshot,
          SourceDocumentNoSnapshot,SourceLineNumberSnapshot,SkuSnapshot,LinkSkuSnapshot,CustomerSkuSnapshot,
          DescriptionEnSnapshot,ColorNameEnSnapshot,PackingGroupId,UnitSnapshot,Qty,PcsPerPackage,PackageCount,NwPerPackage,
          GwPerPackage,CbmPerPackage,SubTotalNw,SubTotalGw,SubTotalCbm,CustomerPoNo,InvoiceNo,Marks,Note)
        VALUES(@Uid,1,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PackingListHeadId,@LineNumber,
          @GrossDemandId,@ProductId,@IsNonStandardSnapshot,@SourceTypeSnapshot,@SourceDocumentNoSnapshot,
          @SourceLineNumberSnapshot,@SkuSnapshot,@LinkSkuSnapshot,@CustomerSkuSnapshot,@DescriptionEnSnapshot,
          @ColorNameEnSnapshot,@PackingGroupId,@UnitSnapshot,@Qty,@PcsPerPackage,@PackageCount,@NwPerPackage,@GwPerPackage,
          @CbmPerPackage,@SubTotalNw,@SubTotalGw,@SubTotalCbm,@CustomerPoNo,@InvoiceNo,@Marks,@Note);
        """;

    private const string UpdateLineSql = """
        UPDATE dbo.PackingListLine SET PackingGroupId=@PackingGroupId,LineNumber=@LineNumber,Qty=@Qty,PcsPerPackage=@PcsPerPackage,
          PackageCount=@PackageCount,NwPerPackage=@NwPerPackage,GwPerPackage=@GwPerPackage,CbmPerPackage=@CbmPerPackage,
          SubTotalNw=@SubTotalNw,SubTotalGw=@SubTotalGw,SubTotalCbm=@SubTotalCbm,InvoiceNo=@InvoiceNo,
          Marks=@Marks,Note=@Note,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
        WHERE Uid=@Uid AND PackingListHeadId=@PackingListHeadId AND Version=@Version AND IsDeleted=0;
        """;

    /// <summary>按 SQL Server LIKE ESCAPE 规则转义用户输入，使通配符按字面匹配。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~", "~~", StringComparison.Ordinal)
            .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal)
            .Replace("[", "~[", StringComparison.Ordinal) + "%";
}
