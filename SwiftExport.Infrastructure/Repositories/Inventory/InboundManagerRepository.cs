using Dapper;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>新入库管理持久化；只写单据，不调用或更新库存余额与流水。</summary>
public sealed class InboundManagerRepository(RepositoryBaseContext context) : IInboundManagerRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public async Task<PagedResponseBase<InboundManagerListItemDto>> SearchAsync(InboundManagerSearchRequest request, CancellationToken token)
    {
        const string filter = """
            FROM dbo.InboundDocument h WHERE h.IsDeleted=0
              AND (@DocumentType IS NULL OR h.DocumentType=@DocumentType)
              AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus)
              AND (@Keyword IS NULL OR h.DocumentNo LIKE @Keyword ESCAPE '~'
                OR h.WarehouseNameSnapshot LIKE @Keyword ESCAPE '~'
                OR EXISTS(SELECT 1 FROM dbo.InboundDocumentLine l WHERE l.InboundDocumentId=h.Uid
                    AND l.IsDeleted=0 AND l.SkuSnapshot LIKE @Keyword ESCAPE '~'))
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(
            "SELECT COUNT(*) " + filter + """
            ; SELECT h.Uid,h.Version,h.DocumentNo,h.DocumentType,h.CreationMode,h.LifecycleStatus,h.PostingStatus,
                h.WarehouseSiteId,h.WarehouseNameSnapshot,h.ReceiptDate,h.Remark,
                (SELECT COUNT(*) FROM dbo.InboundDocumentLine l WHERE l.InboundDocumentId=h.Uid AND l.IsDeleted=0) AS LineCount,
                COALESCE((SELECT SUM(l.Quantity) FROM dbo.InboundDocumentLine l WHERE l.InboundDocumentId=h.Uid AND l.IsDeleted=0),0) AS TotalQuantity
            """ + "\n" + filter + " ORDER BY h.ReceiptDate DESC,h.Uid DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;",
            new { request.DocumentType, request.PostingStatus, Keyword = Like(request.Keyword), Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize }, token));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(), Items = (await reader.ReadAsync<InboundManagerListItemDto>()).ToList(), PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc/>
    public async Task<InboundManagerDetailDto?> GetDetailAsync(Guid uid, CancellationToken token)
    {
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command("""
            SELECT Uid,Version,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,
                   WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ReceiptDate,Remark,
                   PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,TriggerPackingListId,CommandId,
                   CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,IsDeleted
            FROM dbo.InboundDocument WHERE Uid=@Uid AND IsDeleted=0;
            SELECT l.Uid,l.Version,l.InboundDocumentId,l.LineNumber,l.ProductId,l.SkuSnapshot,l.DescriptionSnapshot,
                   l.UnitSnapshot,l.Quantity,l.SourceLineId,l.OriginalLineId,l.CreatedDate,l.CreatedBy,l.UpdatedDate,l.UpdatedBy,
                   p.Version AS SourceVersion,h.OrderNo AS SourceNo,h.BoNo,p.LinkSku,product.MainImage AS ImagePath,
                   p.QTY AS OriginalQty,
                   CASE WHEN p.QTY>COALESCE(received.Qty,0) THEN p.QTY-COALESCE(received.Qty,0) ELSE 0 END AS RemainingReceivableQty
            FROM dbo.InboundDocumentLine l
            INNER JOIN dbo.InboundDocument ih ON ih.Uid=l.InboundDocumentId AND ih.IsDeleted=0
            LEFT JOIN dbo.PurchaseOrderLine p ON ih.DocumentType=10 AND p.Uid=l.SourceLineId
            LEFT JOIN dbo.PurchaseOrderHead h ON h.Uid=p.PurchaseOrderId
            LEFT JOIN dbo.Product product ON product.Uid=l.ProductId
            OUTER APPLY(SELECT SUM(rl.Quantity) AS Qty FROM dbo.InboundDocumentLine rl
                INNER JOIN dbo.InboundDocument rh ON rh.Uid=rl.InboundDocumentId
                WHERE rl.SourceLineId=p.Uid AND rh.DocumentType=10 AND rh.PostingStatus IN (1,8)
                  AND rl.IsDeleted=0 AND rh.IsDeleted=0) received
            WHERE l.InboundDocumentId=@Uid AND l.IsDeleted=0 ORDER BY l.LineNumber,l.Uid;
            """, new { Uid = uid }, token));
        var header = await reader.ReadSingleOrDefaultAsync<InboundHeaderDetailDto>();
        var lines = (await reader.ReadAsync<InboundLineDetailDto>()).ToList();
        return header is null ? null : new() { Header = header, Lines = lines };
    }

    /// <inheritdoc/>
    public async Task<PagedResponseBase<InboundSourceCandidateDto>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token)
    {
        // 先按真实采购行汇总有符号正式收货；反向原单及负向单共同计入，草稿从不占额度。
        var sql = SourceProjection + "\n" + """
            AND h.ConfirmationStatus=@Confirmed AND h.LifecycleStatus=@Active
            AND l.MaterialType IN @KnownTypes
            AND ((@CustomerSupplied=1 AND l.MaterialType=@CustomerType) OR (@CustomerSupplied=0 AND l.MaterialType<>@CustomerType))
            AND (@SupplierId IS NULL OR h.SupplierId=@SupplierId)
            AND l.QTY>COALESCE(received.Qty,0)
            AND (@Keyword IS NULL OR h.OrderNo LIKE @Keyword ESCAPE '~' OR h.BoNo LIKE @Keyword ESCAPE '~'
                 OR l.Sku LIKE @Keyword ESCAPE '~' OR l.LinkSku LIKE @Keyword ESCAPE '~')
            """;
        // 临时结果固定同一次请求的计数和页数据，不使用 NOLOCK 或更新锁。
        sql = """
            IF EXISTS(
                SELECT 1 FROM dbo.InboundDocumentLine il
                INNER JOIN dbo.InboundDocument ih ON ih.Uid=il.InboundDocumentId
                LEFT JOIN dbo.PurchaseOrderLine pl ON pl.Uid=il.SourceLineId
                WHERE ih.IsDeleted=0 AND il.IsDeleted=0 AND ih.PostingStatus IN (1,8)
                  AND (ih.DocumentType IN (11,12) OR (ih.DocumentType=10
                    AND (il.SourceLineId IS NULL OR pl.Uid IS NULL))))
                THROW 51001,N'历史入库采购来源尚未核实。',1;
            """ + "\nSELECT * INTO #InboundSources FROM (" + sql + """
            ) candidates;
            SELECT COUNT(*) FROM #InboundSources;
            SELECT DocumentType,SourceLineId,SourceVersion,PurchaseOrderId,ProductId,SourceNo,LineNumber,BoNo,
                   Sku,LinkSku,Description,ImagePath,Unit,SupplierName,OriginalQty,RemainingReceivableQty
            FROM #InboundSources ORDER BY SourceNo DESC,LineNumber,SourceLineId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            DROP TABLE #InboundSources;
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, new
        {
            Confirmed = (int)ConfirmationStatusEnum.已确认, Active = (int)PurchaseOrderLifecycleStatusEnum.Active,
            KnownTypes = MaterialTypesEnum.MaterialTypes, CustomerType = MaterialTypesEnum.客供件,
            CustomerSupplied = request.PurchaseCategory == InboundPurchaseCategoryEnum.CustomerSupplied,
            request.SupplierId, Keyword = Like(request.Keyword), Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize
        }, token));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(), Items = (await reader.ReadAsync<InboundSourceCandidateDto>()).ToList(), PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InboundSourceCandidateDto>> GetSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<InboundSourceCandidateDto>(Command(SourceProjection + " AND l.Uid IN @Ids", new { Ids = ids }, token))).ToList();

    // 保存只读取身份和快照，不按数量或版本拒绝草稿；最终采购状态、容量归过账校验。
    private const string SourceProjection = """
        SELECT 10 AS DocumentType,l.Uid AS SourceLineId,l.Version AS SourceVersion,l.PurchaseOrderId,l.ProductId,
               h.OrderNo AS SourceNo,l.LineNumber,h.BoNo,l.Sku,l.LinkSku,l.DescriptionZh AS Description,
               product.MainImage AS ImagePath,l.UnitSnapshot AS Unit,h.SupplierName,l.QTY AS OriginalQty,
               l.QTY-COALESCE(received.Qty,0) AS RemainingReceivableQty
        FROM dbo.PurchaseOrderLine l
        INNER JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
        INNER JOIN dbo.Product product ON product.Uid=l.ProductId
        OUTER APPLY(SELECT SUM(rl.Quantity) AS Qty FROM dbo.InboundDocumentLine rl
            INNER JOIN dbo.InboundDocument rh ON rh.Uid=rl.InboundDocumentId
            WHERE rl.SourceLineId=l.Uid AND rh.DocumentType=10 AND rh.PostingStatus IN (1,8)
                AND rl.IsDeleted=0 AND rh.IsDeleted=0) received
        WHERE l.IsDeleted=0 AND h.IsDeleted=0 AND product.IsDeleted=0 AND l.ProductId IS NOT NULL
            AND NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),'') IS NOT NULL
        """;

    /// <inheritdoc/>
    public Task<int> LockCommandAsync(Guid commandId, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<int>(Command("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            SELECT @Result;
            """, new { Resource = $"SwiftExport:Inbound:{commandId:N}" }, token));

    /// <inheritdoc/>
    public Task<InboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<InboundDocument>(Command("SELECT * FROM dbo.InboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND IsDeleted=0", new { Uid = uid }, token));

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<InboundDocumentLine>(Command("SELECT * FROM dbo.InboundDocumentLine WITH(UPDLOCK,HOLDLOCK) WHERE InboundDocumentId=@Uid AND IsDeleted=0 ORDER BY Uid", new { Uid = uid }, token))).ToList();

    /// <inheritdoc/>
    public Task<bool> HasLedgerAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WHERE SourceDocumentId=@Uid)
            OR EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry e INNER JOIN dbo.InboundDocumentLine l ON l.Uid=e.SourceLineId WHERE l.InboundDocumentId=@Uid)
            THEN 1 ELSE 0 END);
            """, new { Uid = uid }, token));

    /// <inheritdoc/>
    public async Task SaveAsync(InboundDocument header, IReadOnlyList<InboundDocumentLine> lines, int expectedVersion, CancellationToken token)
    {
        if (expectedVersion == 0)
        {
            context.AuditStamp.StampForInsert(header);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT dbo.InboundDocument(Uid,Version,IsDeleted,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,
                    WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ReceiptDate,Remark,CommandId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@DocumentNo,@DocumentType,@CreationMode,@LifecycleStatus,@PostingStatus,
                    @WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@ReceiptDate,@Remark,@CommandId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """, header, token));
            header.Version = 1;
        }
        else
        {
            context.AuditStamp.StampForUpdate(header);
            var changed = await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE dbo.InboundDocument SET WarehouseSiteId=@WarehouseSiteId,WarehouseCodeSnapshot=@WarehouseCodeSnapshot,
                    WarehouseNameSnapshot=@WarehouseNameSnapshot,ReceiptDate=@ReceiptDate,Remark=@Remark,
                    Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND CreationMode=0 AND PostingStatus=0 AND LifecycleStatus=0;
                """, header, token));
            if (changed != 1) throw new System.Data.DBConcurrencyException("入库草稿发生并发冲突。");
            header.Version++;
        }
        // 整单版本已经锁定；先软删省略行，保留稳定明细 Uid，不重建全部明细身份。
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE InboundDocumentId=@Uid AND IsDeleted=0 AND Uid NOT IN @Ids;
            """, new { header.Uid, header.UpdatedDate, header.UpdatedBy, Ids = lines.Select(x => x.Uid).ToArray() }, token));
        foreach (var line in lines)
        {
            var isNew = line.Version == 0;
            if (isNew) context.AuditStamp.StampForInsert(line); else context.AuditStamp.StampForUpdate(line);
            var count = await context.Session.Connection.ExecuteAsync(Command(isNew ? """
                INSERT dbo.InboundDocumentLine(Uid,Version,IsDeleted,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,DescriptionSnapshot,
                    UnitSnapshot,Quantity,SourceLineId,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)
                VALUES(@Uid,1,0,@InboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,
                    @UnitSnapshot,@Quantity,@SourceLineId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """ : """
                UPDATE dbo.InboundDocumentLine SET LineNumber=@LineNumber,Quantity=@Quantity,Version=Version+1,
                    UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
                WHERE Uid=@Uid AND InboundDocumentId=@InboundDocumentId AND Version=@Version AND IsDeleted=0;
                """, line, token));
            if (count != 1) throw new System.Data.DBConcurrencyException("入库明细版本已变化，保存失败。");
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(InboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocument SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE Uid=@Uid AND Version=@Version AND IsDeleted=0 AND CreationMode=0 AND PostingStatus=0 AND LifecycleStatus=0;
            """, header, token));
        if (count != 1) throw new System.Data.DBConcurrencyException("入库草稿删除发生并发冲突。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE dbo.InboundDocumentLine SET IsDeleted=1,Version=Version+1,UpdatedDate=@UpdatedDate,UpdatedBy=@UpdatedBy
            WHERE InboundDocumentId=@Uid AND IsDeleted=0;
            """, header, token));
        header.Version++;
    }

    /// <summary>按字面值搜索，转义 SQL LIKE 通配符。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[") + "%";
}
