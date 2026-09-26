using Dapper;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Infrastructure.Contexts;
using System.Data;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;

/// <summary>读取采购来源、核对正式收货事实，并在事务内锁定采购头行。</summary>
public sealed class InboundSourceRepository(RepositoryBaseContext context) : IInboundSourceRepository
{
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <inheritdoc/>
    public async Task<PagedResponseBase<InboundSourceCandidateDto>> SearchSourcesAsync(InboundSourceSearchRequest request, CancellationToken token)
    {
        // 先验证历史反向完整性；仅已过账单贡献收货数量，已完整反向和草稿不占额度。
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
        sql = InboundReceiptHistorySql.Guard + """
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
                   Sku,LinkSku,Description,ImagePath,Unit,SupplierName,DefaultWarehouseName,OriginalQty,RemainingReceivableQty,OtherDraftCount,OtherDraftQuantity
            FROM #InboundSources ORDER BY SourceNo DESC,LineNumber,SourceLineId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            DROP TABLE #InboundSources;
            """;
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, new
        {
            Confirmed = (int)ConfirmationStatusEnum.已确认, Active = (int)PurchaseOrderLifecycleStatusEnum.Active,
            KnownTypes = MaterialTypesEnum.MaterialTypes, CustomerType = MaterialTypesEnum.客供件,
            CustomerSupplied = request.PurchaseCategory == InboundPurchaseCategoryEnum.CustomerSupplied,
            request.SupplierId, request.CurrentInboundDocumentId, Keyword = Like(request.Keyword), Skip = (long)(request.PageIndex - 1) * request.PageSize, Take = request.PageSize
        }, token));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(), Items = (await reader.ReadAsync<InboundSourceCandidateDto>()).ToList(), PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InboundSourceCandidateDto>> GetSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token) =>
        (await context.Session.Connection.QueryAsync<InboundSourceCandidateDto>(Command(InboundReceiptHistorySql.Guard + SourceProjection + " AND l.Uid IN @Ids", new { Ids = ids, CurrentInboundDocumentId = (Guid?)null }, token))).ToList();

    // 保存只读取身份和快照，不按数量或版本拒绝草稿；最终采购状态、容量归过账校验。
    private const string SourceProjection = """
        SELECT 10 AS DocumentType,l.Uid AS SourceLineId,l.Version AS SourceVersion,l.PurchaseOrderId,l.ProductId,
               h.OrderNo AS SourceNo,l.LineNumber,h.BoNo,l.Sku,l.LinkSku,l.DescriptionZh AS Description,
               product.MainImage AS ImagePath,l.UnitSnapshot AS Unit,h.SupplierName,
               defaultWarehouse.Name AS DefaultWarehouseName,l.QTY AS OriginalQty,
               l.QTY-COALESCE(received.Qty,0) AS RemainingReceivableQty,drafts.OtherDraftCount,drafts.OtherDraftQuantity
        FROM dbo.PurchaseOrderLine l
        INNER JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
        INNER JOIN dbo.Product product ON product.Uid=l.ProductId
        LEFT JOIN dbo.WarehouseSite defaultWarehouse
            ON defaultWarehouse.Uid=product.DefaultWarehouseSiteId AND defaultWarehouse.IsDeleted=0
        OUTER APPLY(SELECT SUM(CASE WHEN rh.PostingStatus=1 THEN rl.Quantity ELSE 0 END) AS Qty FROM dbo.InboundDocumentLine rl
            INNER JOIN dbo.InboundDocument rh ON rh.Uid=rl.InboundDocumentId
            WHERE rl.SourceLineId=l.Uid AND rh.DocumentType=10 AND rh.PostingStatus IN (1,8)
                AND rl.IsDeleted=0 AND rh.IsDeleted=0) received
        OUTER APPLY(SELECT COUNT(DISTINCT dh.Uid) AS OtherDraftCount,COALESCE(SUM(dl.Quantity),0) AS OtherDraftQuantity
            FROM dbo.InboundDocumentLine dl JOIN dbo.InboundDocument dh ON dh.Uid=dl.InboundDocumentId
            WHERE dl.SourceLineId=l.Uid AND dh.DocumentType=10 AND dh.PostingStatus=0 AND dh.LifecycleStatus=0
                AND dl.IsDeleted=0 AND dh.IsDeleted=0 AND (@CurrentInboundDocumentId IS NULL OR dh.Uid<>@CurrentInboundDocumentId)) drafts
        WHERE l.IsDeleted=0 AND h.IsDeleted=0 AND product.IsDeleted=0 AND l.ProductId IS NOT NULL
            AND NULLIF(LTRIM(RTRIM(l.UnitSnapshot)),'') IS NOT NULL
        """;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InboundPurchasePostingSourceDto>> ReadPostingSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return [];
        return (await context.Session.Connection.QueryAsync<InboundPurchasePostingSourceDto>(Command(InboundReceiptHistorySql.Guard + """
            SELECT l.Uid AS SourceLineId,l.PurchaseOrderId,l.ProductId,h.OrderNo AS SourceNo,
                l.UnitSnapshot AS Unit,p.Unit AS CurrentUnit,l.QTY AS OriginalQty,
                CONVERT(bit,CASE WHEN h.IsDeleted=0 AND l.IsDeleted=0 AND p.IsDeleted=0
                    AND h.ConfirmationStatus=@Confirmed AND h.LifecycleStatus=@Active
                    AND l.MaterialType IN @KnownTypes THEN 1 ELSE 0 END) AS IsReceivable,
                CONVERT(bit,CASE WHEN l.MaterialType=@CustomerType THEN 1 ELSE 0 END) AS IsCustomerSupplied,
                COALESCE((SELECT SUM(CASE WHEN ih.PostingStatus=1 THEN il.Quantity ELSE 0 END) FROM dbo.InboundDocumentLine il
                    JOIN dbo.InboundDocument ih ON ih.Uid=il.InboundDocumentId
                    WHERE il.SourceLineId=l.Uid AND ih.DocumentType=10 AND ih.PostingStatus IN(1,8)
                        AND il.IsDeleted=0 AND ih.IsDeleted=0),0) AS PostedNetQty
            FROM dbo.PurchaseOrderLine l JOIN dbo.PurchaseOrderHead h ON h.Uid=l.PurchaseOrderId
            LEFT JOIN dbo.Product p ON p.Uid=l.ProductId WHERE l.Uid IN @Ids;
            """, new { Ids = ids, Confirmed = (int)ConfirmationStatusEnum.已确认,
                Active = (int)PurchaseOrderLifecycleStatusEnum.Active, KnownTypes = MaterialTypesEnum.MaterialTypes,
                CustomerType = MaterialTypesEnum.客供件 }, token))).ToList();
    }

    /// <inheritdoc/>
    public async Task LockPurchaseSourcesAsync(IReadOnlyList<Guid> ids, CancellationToken token)
    {
        if (ids.Count == 0) return;
        var owners = (await context.Session.Connection.QueryAsync<Guid>(Command(
            "SELECT DISTINCT PurchaseOrderId FROM dbo.PurchaseOrderLine WHERE Uid IN @Ids", new { Ids = ids }, token))).OrderBy(x => x).ToArray();
        // 与采购命令共用物理头行锁，不用入库专属应用锁冒充采购并发保护。
        foreach (var id in owners)
            await context.Session.Connection.QueryAsync<Guid>(Command("SELECT Uid FROM dbo.PurchaseOrderHead WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Id", new { Id = id }, token));
        foreach (var id in ids.Distinct().OrderBy(x => x))
        {
            var owner = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command(
                "SELECT PurchaseOrderId FROM dbo.PurchaseOrderLine WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Id", new { Id = id }, token));
            if (owner.HasValue && !owners.Contains(owner.Value)) throw new DBConcurrencyException("采购来源归属已变化，请重新预览。");
        }
    }

    /// <inheritdoc/>
    public Task<bool> HasUnprovenReceiptAsync(CancellationToken token) => context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
        SELECT CONVERT(bit,CASE WHEN EXISTS(SELECT 1 FROM dbo.InboundDocumentLine il
            JOIN dbo.InboundDocument ih ON ih.Uid=il.InboundDocumentId
            LEFT JOIN dbo.PurchaseOrderLine pl ON pl.Uid=il.SourceLineId
            WHERE ih.IsDeleted=0 AND il.IsDeleted=0 AND ih.PostingStatus IN(1,8)
                AND (ih.DocumentType IN(11,12) OR (ih.DocumentType=10 AND pl.Uid IS NULL))) THEN 1 ELSE 0 END);
        """, null, token));

    /// <summary>按字面值搜索，转义 SQL LIKE 通配符。</summary>
    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        "%" + value.Trim().Replace("~","~~").Replace("%","~%").Replace("_","~_").Replace("[","~[") + "%";}
