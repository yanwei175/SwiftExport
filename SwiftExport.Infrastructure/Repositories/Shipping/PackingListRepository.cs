using Dapper;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Infrastructure.Contexts;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

/// <summary>使用 PackingList 头行表实现管理查询、草稿持久化和模块专属来源补充，不处理库存动作。</summary>
public sealed partial class PackingListRepository(RepositoryBaseContext context) : IPackingListRepository
{
    private CommandDefinition Command(string sql, object? parameters, CancellationToken token) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public Task<bool> IsSchemaReadyAsync(CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN OBJECT_ID(N'dbo.PackingListHead',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.PackingListLine',N'U') IS NOT NULL
              AND OBJECT_ID(N'dbo.OutboundDocument',N'U') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListHead',N'CreateCommandId') IS NOT NULL
              AND COL_LENGTH(N'dbo.PackingListLine',N'GrossDemandId') IS NOT NULL
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
    public async Task<PagedResponseBase<PackingListManagerListItemDto>> SearchAsync(
        PackingListManagerSearchRequest request, CancellationToken cancellationToken = default)
    {
        const string filter = """
            FROM dbo.PackingListHead h
            WHERE h.IsDeleted=0
              AND (@CustomerId IS NULL OR h.CustomerId=@CustomerId)
              AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus)
              AND (@DateFrom IS NULL OR h.InvoiceDate>=@DateFrom)
              AND (@DateTo IS NULL OR h.InvoiceDate<DATEADD(day,1,@DateTo))
              AND (@Keyword IS NULL OR h.PackingListNo LIKE @Keyword ESCAPE '~'
                OR h.CustomerNameSnapshot LIKE @Keyword ESCAPE '~'
                OR h.ContainerNo LIKE @Keyword ESCAPE '~'
                OR h.SealNo LIKE @Keyword ESCAPE '~'
                OR EXISTS(SELECT 1 FROM dbo.PackingListLine kl
                    WHERE kl.PackingListHeadId=h.Uid AND kl.IsDeleted=0
                      AND kl.InvoiceNo LIKE @Keyword ESCAPE '~'))
            """;
        var sql = "SELECT COUNT(1) " + filter + "; SELECT " + HeaderListColumns + "," + InvoiceSummary + " AS InvoiceNosSummary\n"
            + filter + " ORDER BY h.InvoiceDate DESC,h.Uid DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;";
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, new
        {
            request.CustomerId,
            request.PostingStatus,
            DateFrom = request.InvoiceDateFrom?.Date,
            DateTo = request.InvoiceDateTo?.Date,
            Keyword = Like(request.Keyword),
            Skip = (long)(request.PageIndex - 1) * request.PageSize,
            Take = request.PageSize
        }, cancellationToken));
        return new PagedResponseBase<PackingListManagerListItemDto>
        {
            TotalCount = await reader.ReadSingleAsync<int>(),
            Items = (await reader.ReadAsync<PackingListManagerListItemDto>()).ToList(),
            PageIndex = request.PageIndex,
            PageSize = request.PageSize
        };
    }

    /// <inheritdoc/>
    public async Task<PackingListDetailDto?> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var sql = "SELECT " + HeaderDetailColumns + "," + InvoiceSummary + " AS InvoiceNosSummary FROM dbo.PackingListHead h WHERE h.Uid=@Uid AND h.IsDeleted=0;\n"
            + """
            SELECT l.Uid,l.Version,l.IsDeleted,l.CreatedDate,l.CreatedBy,l.UpdatedDate,l.UpdatedBy,
                   l.PackingListHeadId,l.LineNumber,l.GrossDemandId,l.ProductId,l.IsNonStandardSnapshot,
                   l.SourceTypeSnapshot,l.SourceDocumentNoSnapshot,l.SourceLineNumberSnapshot,l.SkuSnapshot,
                   l.LinkSkuSnapshot,l.CustomerSkuSnapshot,l.DescriptionEnSnapshot,l.ColorNameEnSnapshot,
                   l.UnitSnapshot,l.Qty,l.PcsPerPackage,l.PackageCount,l.NwPerPackage,l.GwPerPackage,
                   l.CbmPerPackage,l.SubTotalNw,l.SubTotalGw,l.SubTotalCbm,l.CustomerPoNo,l.InvoiceNo,
                   l.Marks,l.Note,
                   g.ImagePathSnapshot AS ImagePath
            FROM dbo.PackingListLine l
            LEFT JOIN dbo.GrossDemand g ON g.Uid=l.GrossDemandId AND g.IsDeleted=0
            WHERE l.PackingListHeadId=@Uid AND l.IsDeleted=0
            ORDER BY l.LineNumber,l.Uid;
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, new { Uid = uid }, cancellationToken));
        var header = await reader.ReadSingleOrDefaultAsync<PackingListHeaderDto>();
        var lines = (await reader.ReadAsync<PackingListLineDto>()).ToList();
        return header is null ? null : new PackingListDetailDto { Header = header, Lines = lines };
    }

    /// <inheritdoc/>
    public async Task<SourceDeliveryExitAssessment> AssessSourceExitForUpdateAsync(
        IReadOnlyCollection<Guid> grossDemandIds,
        CancellationToken cancellationToken = default)
    {
        var ids = grossDemandIds.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return new SourceDeliveryExitAssessment();
        const string sql = """
            ;WITH Related AS (
                SELECT l.Uid AS LineUid,l.IsDeleted AS LineIsDeleted,l.PackingListHeadId,
                       h.Uid AS HeadUid,h.IsDeleted AS HeadIsDeleted,h.LifecycleStatus,h.PostingStatus,
                       h.ReversedDate
                FROM dbo.PackingListLine l WITH (UPDLOCK,HOLDLOCK)
                LEFT JOIN dbo.PackingListHead h WITH (UPDLOCK,HOLDLOCK) ON h.Uid=l.PackingListHeadId
                WHERE l.GrossDemandId IN @Ids
            ), RelatedHeads AS (
                SELECT DISTINCT HeadUid FROM Related WHERE HeadUid IS NOT NULL
            )
            SELECT
              (SELECT COUNT(DISTINCT PackingListHeadId) FROM Related
               WHERE LineIsDeleted=0 AND HeadIsDeleted=0
                 AND LifecycleStatus=@ActiveLifecycle AND PostingStatus=@Unposted) AS ActiveDraftCount,
              (SELECT COUNT(DISTINCT PackingListHeadId) FROM Related
               WHERE LineIsDeleted=0 AND HeadIsDeleted=0 AND PostingStatus=@Posted) AS PostedPackingListCount,
              (SELECT COUNT(1) FROM dbo.OutboundDocument o WITH (UPDLOCK,HOLDLOCK)
               INNER JOIN RelatedHeads rh ON rh.HeadUid=o.TriggerPackingListId
               WHERE o.IsDeleted=0 AND o.DocumentType=@FinalCustomerShipment
                 AND o.PostingStatus=@OutboundPosted) AS PostedCustomerOutboundCount,
              ((SELECT COUNT(1) FROM Related
                WHERE LineIsDeleted=0 AND (
                    HeadUid IS NULL OR HeadIsDeleted<>0
                    OR PostingStatus NOT IN (@Unposted,@Posted,@Reversed)
                    OR LifecycleStatus NOT IN (@ActiveLifecycle,@ClosedLifecycle,@VoidedLifecycle)
                    OR (PostingStatus=@Unposted AND LifecycleStatus=@ClosedLifecycle)
                    OR (PostingStatus=@Reversed AND ReversedDate IS NULL)))
               +(SELECT COUNT(1) FROM dbo.OutboundDocument o WITH (UPDLOCK,HOLDLOCK)
                 INNER JOIN RelatedHeads rh ON rh.HeadUid=o.TriggerPackingListId
                 WHERE o.IsDeleted=0 AND o.DocumentType=@FinalCustomerShipment
                   AND o.PostingStatus NOT IN (@OutboundPosted,@OutboundReversed))) AS InvalidHistoryCount;
            """;
        return await context.Session.Connection.QuerySingleAsync<SourceDeliveryExitAssessment>(Command(sql, new
        {
            Ids = ids,
            ActiveLifecycle = PackingListLifecycleStatusEnum.Active,
            ClosedLifecycle = PackingListLifecycleStatusEnum.Closed,
            VoidedLifecycle = PackingListLifecycleStatusEnum.Voided,
            Unposted = PackingListPostingStatusEnum.Unposted,
            Posted = PackingListPostingStatusEnum.Posted,
            Reversed = PackingListPostingStatusEnum.Reversed,
            FinalCustomerShipment = OutboundDocumentTypeEnum.FinalCustomerShipment,
            OutboundPosted = OutboundPostingStatusEnum.Posted,
            OutboundReversed = OutboundPostingStatusEnum.Reversed
        }, cancellationToken));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PackingListSourceEnrichment>> GetSourceEnrichmentsAsync(
        IReadOnlyCollection<Guid> grossDemandIds, CancellationToken cancellationToken = default)
    {
        if (grossDemandIds.Count == 0) return Array.Empty<PackingListSourceEnrichment>();
        var rows = await context.Session.Connection.QueryAsync<PackingListSourceEnrichment>(Command("""
            SELECT g.Uid AS GrossDemandId,
                   CASE WHEN pkg.PackageCount=1 THEN CONVERT(decimal(18,4),pkg.PcsPerPackage) END AS PcsPerPackage,
                   CASE WHEN pkg.PackageCount=1 THEN pkg.NwPerPackage END AS NwPerPackage,
                   CASE WHEN pkg.PackageCount=1 THEN pkg.GwPerPackage END AS GwPerPackage,
                   CASE WHEN pkg.PackageCount=1 THEN pkg.CbmPerPackage END AS CbmPerPackage,
                   CASE WHEN g.SourceType=N'SO' AND soh.Uid IS NOT NULL AND sol.Uid IS NOT NULL
                        THEN soh.CustomerPoNo END AS CustomerPoNo
            FROM dbo.GrossDemand g
            LEFT JOIN dbo.SalesOrderHeader soh ON g.SourceType=N'SO' AND soh.Uid=g.SourceUid
                AND soh.CustomerId=g.CustomerId AND soh.IsDeleted=0
            LEFT JOIN dbo.SalesOrderLine sol ON g.SourceType=N'SO' AND sol.Uid=g.SourceLineId
                AND sol.SalesOrderId=soh.Uid AND sol.ProductId=g.ProductId
                AND sol.IsNonStandard=g.IsNonStandard AND sol.IsDeleted=0
            OUTER APPLY(
                SELECT COUNT(1) AS PackageCount,MAX(pp.PcsPerPackage) AS PcsPerPackage,
                       MAX(pp.Nw_KG) AS NwPerPackage,MAX(pp.Gw_KG) AS GwPerPackage,MAX(pp.Cbm) AS CbmPerPackage
                FROM dbo.ProductPackage pp
                WHERE g.IsNonStandard=0 AND pp.ProductId=g.ProductId AND pp.IsDeleted=0 AND pp.IsDefault=1
            ) pkg
            WHERE g.Uid IN @Ids AND g.IsDeleted=0;
            """, new { Ids = grossDemandIds }, cancellationToken));
        return rows.ToList();
    }

    /// <inheritdoc/>
    public Task<int> LockCommandAsync(Guid commandId, CancellationToken cancellationToken = default) =>
        context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            SELECT @Result;
            """, new { Resource = $"SwiftExport:PackingList:{commandId:N}" }, cancellationToken));

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
                   ColorNameEnSnapshot,UnitSnapshot,Qty,PcsPerPackage,PackageCount,NwPerPackage,GwPerPackage,
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
                UPDATE dbo.PackingListLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
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
          DescriptionEnSnapshot,ColorNameEnSnapshot,UnitSnapshot,Qty,PcsPerPackage,PackageCount,NwPerPackage,
          GwPerPackage,CbmPerPackage,SubTotalNw,SubTotalGw,SubTotalCbm,CustomerPoNo,InvoiceNo,Marks,Note)
        VALUES(@Uid,1,0,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@PackingListHeadId,@LineNumber,
          @GrossDemandId,@ProductId,@IsNonStandardSnapshot,@SourceTypeSnapshot,@SourceDocumentNoSnapshot,
          @SourceLineNumberSnapshot,@SkuSnapshot,@LinkSkuSnapshot,@CustomerSkuSnapshot,@DescriptionEnSnapshot,
          @ColorNameEnSnapshot,@UnitSnapshot,@Qty,@PcsPerPackage,@PackageCount,@NwPerPackage,@GwPerPackage,
          @CbmPerPackage,@SubTotalNw,@SubTotalGw,@SubTotalCbm,@CustomerPoNo,@InvoiceNo,@Marks,@Note);
        """;

    private const string UpdateLineSql = """
        UPDATE dbo.PackingListLine SET LineNumber=@LineNumber,Qty=@Qty,PcsPerPackage=@PcsPerPackage,
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
