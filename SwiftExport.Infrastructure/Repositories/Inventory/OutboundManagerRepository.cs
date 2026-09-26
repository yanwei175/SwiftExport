using Dapper;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>新出库管理持久化；只写单据，不调用或更新库存余额与流水。</summary>
public sealed partial class OutboundManagerRepository(RepositoryBaseContext context) : IOutboundManagerRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public async Task<PagedResponseBase<OutboundManagerListItemDto>> SearchAsync(OutboundManagerSearchRequest request, CancellationToken token)
    {
        const string filter = """
            FROM dbo.OutboundDocument h WHERE h.IsDeleted=0
              AND (@DocumentType IS NULL OR h.DocumentType=@DocumentType)
              AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus)
              AND (@Keyword IS NULL OR h.DocumentNo LIKE @Keyword ESCAPE '~'
                OR h.WarehouseNameSnapshot LIKE @Keyword ESCAPE '~'
                OR EXISTS(SELECT 1 FROM dbo.OutboundDocumentLine l WHERE l.OutboundDocumentId=h.Uid
                    AND l.IsDeleted=0 AND l.SkuSnapshot LIKE @Keyword ESCAPE '~'))
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(
            "SELECT COUNT(*) " + filter + """
            ; SELECT h.Uid,h.Version,h.DocumentNo,h.DocumentType,h.LifecycleStatus,h.PostingStatus,
                h.WarehouseSiteId,h.WarehouseNameSnapshot,h.ShipmentDate,h.Remark,h.IssueReason,
                (SELECT COUNT(*) FROM dbo.OutboundDocumentLine l WHERE l.OutboundDocumentId=h.Uid AND l.IsDeleted=0) AS LineCount,
                COALESCE((SELECT SUM(l.Quantity) FROM dbo.OutboundDocumentLine l WHERE l.OutboundDocumentId=h.Uid AND l.IsDeleted=0),0) AS TotalQuantity
            """ + "\n" + filter + " ORDER BY h.ShipmentDate DESC,h.Uid DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;",
            new { request.DocumentType, request.PostingStatus, Keyword = Like(request.Keyword), Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize }, token));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(), Items = (await reader.ReadAsync<OutboundManagerListItemDto>()).ToList(), PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc/>
    public async Task<OutboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token)
    {
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command("""
            SELECT Uid,Version,DocumentNo,DocumentType,LifecycleStatus,PostingStatus,
                   WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ShipmentDate,Remark,IssueReason,
                   PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,TriggerPackingListId,CommandId,
                   CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,IsDeleted
            FROM dbo.OutboundDocument WHERE Uid=@Uid AND IsDeleted=0;
            SELECT l.Uid,l.Version,l.OutboundDocumentId,l.LineNumber,l.ProductId,l.SkuSnapshot,l.DescriptionSnapshot,
                   l.UnitSnapshot,l.Quantity,l.CommandItemId,l.OriginalLineId,l.CreatedDate,l.CreatedBy,l.UpdatedDate,l.UpdatedBy,
                   p.LinkSku,p.MainImage AS ImagePath,COALESCE(b.OnHandQty,0) AS OnHandQty
            FROM dbo.OutboundDocumentLine l
            JOIN dbo.OutboundDocument h ON h.Uid=l.OutboundDocumentId
            LEFT JOIN dbo.Product p ON p.Uid=l.ProductId
            LEFT JOIN dbo.InventoryBalance b ON b.WarehouseSiteId=h.WarehouseSiteId AND b.ProductId=l.ProductId AND b.IsDeleted=0
            WHERE l.OutboundDocumentId=@Uid AND l.IsDeleted=0 ORDER BY l.LineNumber,l.Uid;
            """, new { Uid = uid }, token));
        var header = await reader.ReadSingleOrDefaultAsync<OutboundHeaderDetailDto>();
        var lines = (await reader.ReadAsync<OutboundLineDetailDto>()).ToList();
        return header is null ? null : new() { Header = header, Lines = lines };
    }

    /// <summary>从正库存选产品；一次查询固定计数与页数据，其他草稿不扣减余额。</summary>
    public async Task<PagedResponseBase<OutboundItemCandidateDto>> SearchItemsAsync(OutboundItemSearchRequest request, CancellationToken token)
    {
        const string sql = """
            SELECT b.Uid,b.WarehouseSiteId,b.ProductId,p.Sku,p.LinkSku,p.DescriptionZh AS Description,
                   p.MainImage AS ImagePath,p.Unit,b.OnHandQty
            INTO #OutboundItems
            FROM dbo.InventoryBalance b JOIN dbo.Product p ON p.Uid=b.ProductId
            JOIN dbo.WarehouseSite w ON w.Uid=b.WarehouseSiteId
            WHERE b.WarehouseSiteId=@WarehouseSiteId AND b.IsDeleted=0 AND b.OnHandQty>0
                AND p.IsDeleted=0 AND p.IsActive=1 AND w.IsDeleted=0 AND w.IsActive=1
                AND NULLIF(LTRIM(RTRIM(p.Unit)),'') IS NOT NULL
                AND b.ProductId NOT IN @ExcludedProductIds
                AND (@Keyword IS NULL OR p.Sku LIKE @Keyword ESCAPE '~'
                    OR p.LinkSku LIKE @Keyword ESCAPE '~' OR p.DescriptionZh LIKE @Keyword ESCAPE '~');
            SELECT COUNT(*) FROM #OutboundItems;
            SELECT Uid,WarehouseSiteId,ProductId,Sku,LinkSku,Description,ImagePath,Unit,OnHandQty
            FROM #OutboundItems ORDER BY Sku,ProductId OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            DROP TABLE #OutboundItems;
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql,
            new { request.WarehouseSiteId, request.ExcludedProductIds, Keyword = Like(request.Keyword),
                Skip = (long)(request.PageIndex-1)*request.PageSize, Take = request.PageSize }, token));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(), Items = (await reader.ReadAsync<OutboundItemCandidateDto>()).ToList(),
            PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>草稿只验证产品身份和单位，不要求余额仍为正。</summary>
    public async Task<IReadOnlyList<OutboundItemCandidateDto>> GetProductsAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return [];
        return (await context.Session.Connection.QueryAsync<OutboundItemCandidateDto>(Command("""
            SELECT Uid AS ProductId,Sku,LinkSku,DescriptionZh AS Description,MainImage AS ImagePath,Unit
            FROM dbo.Product WHERE Uid IN @Ids AND IsDeleted=0;
            """, new { Ids = ids }, token))).ToList();
    }

    /// <summary>新增草稿行须使用当前可用产品，已有历史快照不被覆盖。</summary>
    public Task<bool> IsProductActiveAsync(Guid productId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("SELECT CONVERT(bit,COUNT(*)) FROM dbo.Product WHERE Uid=@ProductId AND IsActive=1 AND IsDeleted=0", new { ProductId = productId }, token));

    /// <inheritdoc/>
    public Task<int> LockCommandAsync(Guid commandId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            SELECT @Result;
            """, new { Resource = $"SwiftExport:Outbound:{commandId:N}" }, token));

    /// <inheritdoc/>
    public Task<OutboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<OutboundDocument>(Command("SELECT * FROM dbo.OutboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND IsDeleted=0", new { Uid = uid }, token));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OutboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<OutboundDocumentLine>(Command("SELECT * FROM dbo.OutboundDocumentLine WITH(UPDLOCK,HOLDLOCK) WHERE OutboundDocumentId=@Uid AND IsDeleted=0 ORDER BY Uid", new { Uid = uid }, token))).ToList();

    /// <inheritdoc/>
    public Task<bool> HasLedgerAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WHERE SourceDocumentId=@Uid)
            OR EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry e INNER JOIN dbo.OutboundDocumentLine l ON l.Uid=e.SourceLineId WHERE l.OutboundDocumentId=@Uid)
            THEN 1 ELSE 0 END);
            """, new { Uid = uid }, token));

    /// <inheritdoc/>
    public async Task SaveAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, int expectedVersion, CancellationToken token)
    {
        if (expectedVersion == 0)
        {
            context.AuditStamp.StampForInsert(header);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.OutboundDocument(Uid,Version,IsDeleted,DocumentNo,DocumentType,LifecycleStatus,PostingStatus,
                    WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ShipmentDate,Remark,IssueReason,TriggerPackingListId,CommandId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@DocumentNo,@DocumentType,@LifecycleStatus,@PostingStatus,
                    @WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@ShipmentDate,@Remark,@IssueReason,NULL,@CommandId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """, header, token));
            header.Version = 1;
        }
        else
        {
            context.AuditStamp.StampForUpdate(header);
            var changed = await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE dbo.OutboundDocument SET WarehouseSiteId=@WarehouseSiteId,WarehouseCodeSnapshot=@WarehouseCodeSnapshot,
                    WarehouseNameSnapshot=@WarehouseNameSnapshot,ShipmentDate=@ShipmentDate,Remark=@Remark,IssueReason=@IssueReason,
                    Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND DocumentType=22 AND TriggerPackingListId IS NULL AND PostingStatus=0 AND LifecycleStatus=0;
                """, header, token));
            if (changed != 1) throw new System.Data.DBConcurrencyException("出库草稿发生并发冲突。");
            header.Version++;
        }
        // 整单版本已经锁定；先软删省略行，保留稳定明细 Uid，不重建全部明细身份。
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE OutboundDocumentId=@Uid AND IsDeleted=0 AND Uid NOT IN @Ids;
            """, new { header.Uid, header.UpdatedDate, header.UpdatedBy, Ids = lines.Select(x => x.Uid).ToArray() }, token));
        foreach (var line in lines)
        {
            var isNew = line.Version == 0;
            if (isNew) context.AuditStamp.StampForInsert(line); else context.AuditStamp.StampForUpdate(line);
            var count = await context.Session.Connection.ExecuteAsync(Command(isNew ? """
                INSERT dbo.OutboundDocumentLine(Uid,Version,IsDeleted,OutboundDocumentId,LineNumber,ProductId,SkuSnapshot,DescriptionSnapshot,
                    UnitSnapshot,Quantity,CommandItemId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@OutboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,
                    @UnitSnapshot,@Quantity,@CommandItemId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """ : """
                UPDATE dbo.OutboundDocumentLine SET LineNumber=@LineNumber,Quantity=@Quantity,Version=Version+1,
                    UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND OutboundDocumentId=@OutboundDocumentId AND Version=@Version AND IsDeleted=0;
                """, line, token));
            if (count != 1) throw new System.Data.DBConcurrencyException("出库明细版本已变化，保存失败。");
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(OutboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocument SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND DocumentType=22 AND TriggerPackingListId IS NULL AND PostingStatus=0 AND LifecycleStatus=0;
            """, header, token));
        if (count != 1) throw new System.Data.DBConcurrencyException("出库草稿删除发生并发冲突。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.OutboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE OutboundDocumentId=@Uid AND IsDeleted=0;
            """, header, token));
        header.Version++;
    }

    /// <summary>按字面值搜索，转义 SQL LIKE 通配符。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[") + "%";
}
