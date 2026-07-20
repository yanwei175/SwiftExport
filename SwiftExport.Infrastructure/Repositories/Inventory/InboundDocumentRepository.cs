using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

public sealed class InboundDocumentRepository(RepositoryBaseContext context) : IInboundDocumentRepository
{
    public async Task<PagedResponseBase<InboundListItemDto>> SearchAsync(InboundSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var parameters = new DynamicParameters(new
        {
            DocumentNo = Like(request.DocumentNo), request.DocumentType, request.PostingStatus,
            request.ReceiptDateFrom, request.ReceiptDateTo, SupplierKeyword = Like(request.SupplierKeyword),
            WarehouseKeyword = Like(request.WarehouseKeyword), PurchaseOrderNo = Like(request.PurchaseOrderNo),
            SalesOrderNo = Like(request.SalesOrderNo), Sku = Like(request.Sku),
            Offset = (index - 1) * size, PageSize = size
        });
        const string where = """
            WHERE ISNULL(h.IsDeleted,0)=0
              AND (@DocumentNo IS NULL OR h.DocumentNo LIKE @DocumentNo)
              AND (@DocumentType IS NULL OR h.DocumentType=@DocumentType)
              AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus)
              AND (@ReceiptDateFrom IS NULL OR h.ReceiptDate>=@ReceiptDateFrom)
              AND (@ReceiptDateTo IS NULL OR h.ReceiptDate<DATEADD(day,1,@ReceiptDateTo))
              AND (@SupplierKeyword IS NULL OR h.SupplierNameSnapshot LIKE @SupplierKeyword OR h.SupplierCodeSnapshot LIKE @SupplierKeyword)
              AND (@WarehouseKeyword IS NULL OR h.WarehouseNameSnapshot LIKE @WarehouseKeyword OR h.WarehouseCodeSnapshot LIKE @WarehouseKeyword)
              AND (@PurchaseOrderNo IS NULL OR EXISTS(
                    SELECT 1 FROM dbo.InboundDocumentLine sourceLine
                    JOIN dbo.PurchaseOrderLine poLine ON poLine.Uid=sourceLine.SourceUid
                    JOIN dbo.PurchaseOrderHead poHead ON poHead.Uid=poLine.PurchaseOrderId
                    WHERE sourceLine.InboundDocumentId=h.Uid AND sourceLine.SourceType=10
                      AND ISNULL(sourceLine.IsDeleted,0)=0 AND poHead.OrderNo LIKE @PurchaseOrderNo))
              AND (@SalesOrderNo IS NULL OR EXISTS(SELECT 1 FROM dbo.InboundDocumentLine sourceLine WHERE sourceLine.InboundDocumentId=h.Uid AND sourceLine.SalesOrderNoSnapshot LIKE @SalesOrderNo AND ISNULL(sourceLine.IsDeleted,0)=0))
              AND (@Sku IS NULL OR EXISTS(SELECT 1 FROM dbo.InboundDocumentLine sourceLine WHERE sourceLine.InboundDocumentId=h.Uid AND sourceLine.SkuSnapshot LIKE @Sku AND ISNULL(sourceLine.IsDeleted,0)=0))
            """;
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dbo.InboundDocument h {where};", parameters, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<InboundListItemDto>(new CommandDefinition($"""
            SELECT h.Uid,h.Version,h.DocumentNo,h.DocumentType,h.PostingStatus,h.ReceiptDate,
                   h.SupplierNameSnapshot AS SupplierName,h.WarehouseNameSnapshot AS WarehouseName,
                   COUNT(l.Uid) AS LineCount,ISNULL(SUM(l.Quantity),0) AS TotalQuantity,
                   MAX(poHead.OrderNo) AS SourceSummary,CAST(0 AS bit) AS IsLegacyReadOnly,
                   CAST(NULL AS nvarchar(500)) AS ReadOnlyReason,
                   CASE h.PostingStatus WHEN 0 THEN N'草稿' WHEN 1 THEN N'已过账' WHEN 8 THEN N'已反向' ELSE N'未知' END AS PostingStatusText
            FROM dbo.InboundDocument h
            LEFT JOIN dbo.InboundDocumentLine l ON l.InboundDocumentId=h.Uid AND ISNULL(l.IsDeleted,0)=0
            LEFT JOIN dbo.PurchaseOrderLine poLine ON poLine.Uid=l.SourceUid AND l.SourceType=10
            LEFT JOIN dbo.PurchaseOrderHead poHead ON poHead.Uid=poLine.PurchaseOrderId
            {where}
            GROUP BY h.Uid,h.Version,h.DocumentNo,h.DocumentType,h.PostingStatus,h.ReceiptDate,h.SupplierNameSnapshot,h.WarehouseNameSnapshot
            ORDER BY h.ReceiptDate DESC,h.DocumentNo DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<InboundListItemDto> { Items = rows.ToList(), TotalCount = total, PageIndex = index, PageSize = size };
    }

    public Task<InboundHeaderDto?> GetHeaderAsync(Guid uid, CancellationToken cancellationToken = default) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<InboundHeaderDto>(new CommandDefinition("""
            SELECT Uid,Version,DocumentNo,DocumentType,PostingStatus,WarehouseSiteId,
                   WarehouseCodeSnapshot AS WarehouseCode,WarehouseNameSnapshot AS WarehouseName,
                   SupplierId,SupplierCodeSnapshot AS SupplierCode,SupplierNameSnapshot AS SupplierName,
                   ReceiptDate,Remark,PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,SourcePackingListId,
                   CAST(0 AS bit) AS IsLegacyReadOnly,CAST(NULL AS nvarchar(500)) AS ReadOnlyReason
            FROM dbo.InboundDocument WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<InboundLineDto>> GetLinesAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        var rows = await context.Session.Connection.QueryAsync<InboundLineDto>(new CommandDefinition("""
            SELECT l.Uid,l.Version,l.LineNumber,l.ProductId,l.SkuSnapshot AS Sku,l.DescriptionSnapshot AS Description,
                   l.UnitSnapshot AS Unit,l.Quantity,l.SourceType,l.SourceUid,l.DemandExecutionLinkId,l.NetDemandAllocationId,
                   poHead.OrderNo AS PurchaseOrderNo,l.SalesOrderNoSnapshot AS SalesOrderNo,l.OriginalLineId,
                   CAST(0 AS decimal(18,4)) AS RemainingReceivableQty
            FROM dbo.InboundDocumentLine l
            LEFT JOIN dbo.PurchaseOrderLine poLine ON poLine.Uid=l.SourceUid AND l.SourceType=10
            LEFT JOIN dbo.PurchaseOrderHead poHead ON poHead.Uid=poLine.PurchaseOrderId
            WHERE l.InboundDocumentId=@Uid AND ISNULL(l.IsDeleted,0)=0 ORDER BY l.LineNumber;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));
        return [.. rows];
    }

    public Task<InboundDocument?> GetForUpdateAsync(Guid uid) => context.Session.Connection.QuerySingleOrDefaultAsync<InboundDocument>("""
        SELECT * FROM dbo.InboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid }, context.Session.Transaction);

    public async Task<IReadOnlyList<InboundDocumentLine>> GetLinesForUpdateAsync(Guid uid)
        => (await context.Session.Connection.QueryAsync<InboundDocumentLine>("""
            SELECT * FROM dbo.InboundDocumentLine WITH(UPDLOCK,HOLDLOCK)
            WHERE InboundDocumentId=@Uid AND ISNULL(IsDeleted,0)=0 ORDER BY LineNumber;
            """, new { Uid = uid }, context.Session.Transaction)).ToList();

    public Task<InboundDocument?> GetByCommandForUpdateAsync(Guid commandId) => context.Session.Connection.QuerySingleOrDefaultAsync<InboundDocument>("""
        SELECT TOP(1) * FROM dbo.InboundDocument WITH(UPDLOCK,HOLDLOCK)
        WHERE CommandId=@CommandId AND ISNULL(IsDeleted,0)=0 ORDER BY CreatedDate DESC;
        """, new { CommandId = commandId }, context.Session.Transaction);

    public async Task<IReadOnlyList<InboundDocument>> GetByPackingListForUpdateAsync(Guid packingListId)
        => (await context.Session.Connection.QueryAsync<InboundDocument>("""
            SELECT * FROM dbo.InboundDocument WITH(UPDLOCK,HOLDLOCK)
            WHERE SourcePackingListId=@PackingListId AND DocumentType=@DocumentType AND ISNULL(IsDeleted,0)=0
            ORDER BY WarehouseSiteId,Uid;
            """, new { PackingListId = packingListId, DocumentType = InboundDocumentTypeEnum.PackingListAutoReceipt }, context.Session.Transaction)).ToList();

    public async Task<int> InsertAsync(InboundDocument document, IReadOnlyList<InboundDocumentLine> lines)
    {
        context.AuditStamp.StampForInsert(document);
        foreach (var line in lines) context.AuditStamp.StampForInsert(line);
        const string headSql = """INSERT dbo.InboundDocument(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,DocumentNo,DocumentType,LifecycleStatus,PostingStatus,WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,ReceiptDate,Remark,SourcePackingListId,CommandId) VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@DocumentNo,@DocumentType,@LifecycleStatus,@PostingStatus,@WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@SupplierId,@SupplierCodeSnapshot,@SupplierNameSnapshot,@ReceiptDate,@Remark,@SourcePackingListId,@CommandId);""";
        const string lineSql = """INSERT dbo.InboundDocumentLine(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,DescriptionSnapshot,UnitSnapshot,Quantity,SourceType,SourceUid,DemandExecutionLinkId,NetDemandAllocationId,GrossDemandId,SalesOrderNoSnapshot,SalesOrderLineNumberSnapshot,OriginalLineId) VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@InboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,@UnitSnapshot,@Quantity,@SourceType,@SourceUid,@DemandExecutionLinkId,@NetDemandAllocationId,@GrossDemandId,@SalesOrderNoSnapshot,@SalesOrderLineNumberSnapshot,@OriginalLineId);""";
        return await context.Session.Connection.ExecuteAsync(headSql, document, context.Session.Transaction)
             + await context.Session.Connection.ExecuteAsync(lineSql, lines, context.Session.Transaction);
    }

    public async Task<int> UpdateDraftAsync(InboundDocument document, IReadOnlyList<InboundDocumentLine> lines, int expectedVersion)
    {
        var affected = await context.Session.Connection.ExecuteAsync("""UPDATE dbo.InboundDocument SET ReceiptDate=@ReceiptDate,Remark=@Remark,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName WHERE Uid=@Uid AND Version=@ExpectedVersion AND PostingStatus=@Unposted AND ISNULL(IsDeleted,0)=0;""", new { document.Uid, document.ReceiptDate, document.Remark, ExpectedVersion = expectedVersion, Unposted = InboundPostingStatusEnum.Unposted, Now = DateTime.Now, UserName = context.CurrentUser.UserName }, context.Session.Transaction);
        if (affected != 1) return affected;
        await context.Session.Connection.ExecuteAsync("DELETE FROM dbo.InboundDocumentLine WHERE InboundDocumentId=@Uid AND ISNULL(IsDeleted,0)=0;", new { document.Uid }, context.Session.Transaction);
        foreach (var line in lines) { line.InboundDocumentId = document.Uid; context.AuditStamp.StampForInsert(line); }
        const string lineSql = """INSERT dbo.InboundDocumentLine(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,InboundDocumentId,LineNumber,ProductId,SkuSnapshot,DescriptionSnapshot,UnitSnapshot,Quantity,SourceType,SourceUid,DemandExecutionLinkId,NetDemandAllocationId,GrossDemandId,SalesOrderNoSnapshot,SalesOrderLineNumberSnapshot,OriginalLineId) VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@InboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,@UnitSnapshot,@Quantity,@SourceType,@SourceUid,@DemandExecutionLinkId,@NetDemandAllocationId,@GrossDemandId,@SalesOrderNoSnapshot,@SalesOrderLineNumberSnapshot,@OriginalLineId);""";
        return affected + await context.Session.Connection.ExecuteAsync(lineSql, lines, context.Session.Transaction);
    }

    public Task<int> SetPostedAsync(Guid uid, int expectedVersion, DateTime postedDate, string postedBy, Guid commandId) => context.Session.Connection.ExecuteAsync("""UPDATE dbo.InboundDocument SET PostingStatus=@Posted,PostedDate=@PostedDate,PostedBy=@PostedBy,CommandId=@CommandId,Version=Version+1,UpdatedDate=@PostedDate,UpdatedBy=@PostedBy WHERE Uid=@Uid AND Version=@ExpectedVersion AND PostingStatus=@Unposted AND ISNULL(IsDeleted,0)=0;""", new { Uid = uid, ExpectedVersion = expectedVersion, Posted = InboundPostingStatusEnum.Posted, Unposted = InboundPostingStatusEnum.Unposted, PostedDate = postedDate, PostedBy = postedBy, CommandId = commandId }, context.Session.Transaction);
    public Task<int> SetReversedAsync(Guid uid, int expectedVersion, DateTime reversedDate, string reversedBy, string reason, Guid commandId) => context.Session.Connection.ExecuteAsync("""UPDATE dbo.InboundDocument SET PostingStatus=@Reversed,ReversedDate=@ReversedDate,ReversedBy=@ReversedBy,ReverseReason=@Reason,CommandId=@CommandId,Version=Version+1,UpdatedDate=@ReversedDate,UpdatedBy=@ReversedBy WHERE Uid=@Uid AND Version=@ExpectedVersion AND PostingStatus=@Posted AND ISNULL(IsDeleted,0)=0;""", new { Uid = uid, ExpectedVersion = expectedVersion, Reversed = InboundPostingStatusEnum.Reversed, Posted = InboundPostingStatusEnum.Posted, ReversedDate = reversedDate, ReversedBy = reversedBy, Reason = reason, CommandId = commandId }, context.Session.Transaction);

    public async Task<IReadOnlyList<InventoryLedgerItemDto>> GetLedgersAsync(Guid uid, CancellationToken cancellationToken = default)
        => (await context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(new CommandDefinition("""
            SELECT Uid,Sequence,WarehouseSiteId,WarehouseCodeSnapshot AS WarehouseCode,ProductId,SkuSnapshot AS Sku,UnitSnapshot AS Unit,
                   TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,
                   SourceDocumentType AS SourceDocumentNo,ReversalOfLedgerId,Reason,OccurredDate,CreatedBy
            FROM dbo.InventoryLedgerEntry WHERE SourceDocumentId=@Uid ORDER BY Sequence;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
}
