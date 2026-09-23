using Dapper;
using System.Data;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;
/// <summary>出库管理查询及草稿产品身份读取，不写单据和库存。</summary>
public sealed class OutboundQueryRepository(RepositoryBaseContext context) : IOutboundQueryRepository
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

    /// <summary>按字面值搜索并转义 SQL LIKE 通配符。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[") + "%";
}
