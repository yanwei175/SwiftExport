using Dapper;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

/// <summary>读取入库单据列表与完整详情；复用当前请求的事务。</summary>
public sealed class InboundQueryRepository(RepositoryBaseContext context) : IInboundQueryRepository
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
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(InboundReceiptHistorySql.Guard + """
            SELECT Uid,Version,DocumentNo,DocumentType,CreationMode,LifecycleStatus,PostingStatus,
                   WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ReceiptDate,Remark,
                   PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,TriggerPackingListId,CommandId,
                   CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,IsDeleted
            FROM dbo.InboundDocument WHERE Uid=@Uid AND IsDeleted=0;
            SELECT l.Uid,l.Version,l.InboundDocumentId,l.LineNumber,l.ProductId,l.SkuSnapshot,l.DescriptionSnapshot,
                   l.UnitSnapshot,l.Quantity,l.SourceLineId,l.OriginalLineId,l.CreatedDate,l.CreatedBy,l.UpdatedDate,l.UpdatedBy,
                   p.Version AS SourceVersion,h.OrderNo AS SourceNo,h.BoNo,p.LinkSku,product.MainImage AS ImagePath,
                   p.QTY AS OriginalQty,drafts.OtherDraftCount,drafts.OtherDraftQuantity,
                   CASE WHEN p.QTY>COALESCE(received.Qty,0) THEN p.QTY-COALESCE(received.Qty,0) ELSE 0 END AS RemainingReceivableQty
            FROM dbo.InboundDocumentLine l
            INNER JOIN dbo.InboundDocument ih ON ih.Uid=l.InboundDocumentId AND ih.IsDeleted=0
            LEFT JOIN dbo.PurchaseOrderLine p ON ih.DocumentType=10 AND p.Uid=l.SourceLineId
            LEFT JOIN dbo.PurchaseOrderHead h ON h.Uid=p.PurchaseOrderId
            LEFT JOIN dbo.Product product ON product.Uid=l.ProductId
            OUTER APPLY(SELECT SUM(CASE WHEN rh.PostingStatus=1 THEN rl.Quantity ELSE 0 END) AS Qty FROM dbo.InboundDocumentLine rl
                INNER JOIN dbo.InboundDocument rh ON rh.Uid=rl.InboundDocumentId
                WHERE rl.SourceLineId=p.Uid AND rh.DocumentType=10 AND rh.PostingStatus IN (1,8)
                  AND rl.IsDeleted=0 AND rh.IsDeleted=0) received
            OUTER APPLY(SELECT COUNT(DISTINCT dh.Uid) AS OtherDraftCount,COALESCE(SUM(dl.Quantity),0) AS OtherDraftQuantity
                FROM dbo.InboundDocumentLine dl JOIN dbo.InboundDocument dh ON dh.Uid=dl.InboundDocumentId
                WHERE dl.SourceLineId=p.Uid AND dh.DocumentType=10 AND dh.PostingStatus=0 AND dh.LifecycleStatus=0
                    AND dl.IsDeleted=0 AND dh.IsDeleted=0 AND dh.Uid<>@Uid) drafts
            WHERE l.InboundDocumentId=@Uid AND l.IsDeleted=0 ORDER BY l.LineNumber,l.Uid;
            """, new { Uid = uid }, token));
        var header = await reader.ReadSingleOrDefaultAsync<InboundHeaderDetailDto>();
        var lines = (await reader.ReadAsync<InboundLineDetailDto>()).ToList();
        return header is null ? null : new() { Header = header, Lines = lines };
    }

    /// <summary>按字面值搜索，转义 SQL LIKE 通配符。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[") + "%";}
