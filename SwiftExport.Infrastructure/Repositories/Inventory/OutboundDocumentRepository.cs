using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

public sealed class OutboundDocumentRepository(RepositoryBaseContext context) : IOutboundDocumentRepository
{
    public async Task<PagedResponseBase<OutboundListItemDto>> SearchAsync(OutboundSearchRequest request, CancellationToken cancellationToken = default)
    {
        var page = Math.Max(request.PageIndex, 1);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var parameters = new { No = Like(request.DocumentNo), request.DocumentType, request.PostingStatus, request.ShipmentDateFrom, request.ShipmentDateTo, PackingNo = Like(request.PackingListNo), SalesNo = Like(request.SalesOrderNo), Sku = Like(request.Sku), Warehouse = Like(request.WarehouseKeyword), Offset = (page - 1) * size, PageSize = size };
        const string where = "WHERE ISNULL(h.IsDeleted,0)=0 AND (@No IS NULL OR h.DocumentNo LIKE @No) AND (@DocumentType IS NULL OR h.DocumentType=@DocumentType) AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus) AND (@ShipmentDateFrom IS NULL OR h.ShipmentDate>=@ShipmentDateFrom) AND (@ShipmentDateTo IS NULL OR h.ShipmentDate<DATEADD(day,1,@ShipmentDateTo)) AND (@PackingNo IS NULL OR h.PackingListNoSnapshot LIKE @PackingNo) AND (@Warehouse IS NULL OR h.WarehouseCodeSnapshot LIKE @Warehouse OR h.WarehouseNameSnapshot LIKE @Warehouse) AND (@SalesNo IS NULL OR EXISTS(SELECT 1 FROM dbo.OutboundDocumentLine x WHERE x.OutboundDocumentId=h.Uid AND x.SalesOrderNoSnapshot LIKE @SalesNo AND ISNULL(x.IsDeleted,0)=0)) AND (@Sku IS NULL OR EXISTS(SELECT 1 FROM dbo.OutboundDocumentLine x WHERE x.OutboundDocumentId=h.Uid AND x.SkuSnapshot LIKE @Sku AND ISNULL(x.IsDeleted,0)=0))";
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.OutboundDocument h {where};", parameters, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<OutboundListItemDto>(new CommandDefinition($"""
            SELECT h.Uid,h.Version,h.DocumentNo,h.DocumentType,h.PostingStatus,h.ShipmentDate,
                   h.WarehouseNameSnapshot AS WarehouseName,h.PackingListNoSnapshot AS PackingListNo,
                   COUNT(l.Uid) AS LineCount,COALESCE(SUM(l.Quantity),0) AS TotalQuantity,
                   CAST(0 AS bit) AS IsLegacyReadOnly,CAST(NULL AS nvarchar(500)) AS ReadOnlyReason
            FROM dbo.OutboundDocument h LEFT JOIN dbo.OutboundDocumentLine l ON l.OutboundDocumentId=h.Uid AND ISNULL(l.IsDeleted,0)=0
            {where} GROUP BY h.Uid,h.Version,h.DocumentNo,h.DocumentType,h.PostingStatus,h.ShipmentDate,h.WarehouseNameSnapshot,h.PackingListNoSnapshot
            ORDER BY h.ShipmentDate DESC,h.DocumentNo DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<OutboundListItemDto> { Items = rows.ToList(), TotalCount = total, PageIndex = page, PageSize = size };
    }

    public Task<OutboundHeaderDto?> GetHeaderAsync(Guid uid, CancellationToken cancellationToken = default) => context.Session.Connection.QuerySingleOrDefaultAsync<OutboundHeaderDto>(new CommandDefinition("""
        SELECT Uid,Version,DocumentNo,DocumentType,PostingStatus,WarehouseSiteId,WarehouseCodeSnapshot AS WarehouseCode,
               WarehouseNameSnapshot AS WarehouseName,ShipmentDate,SourcePackingListId,PackingListNoSnapshot AS PackingListNo,
               PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,CAST(0 AS bit) AS IsLegacyReadOnly,CAST(NULL AS nvarchar(500)) AS ReadOnlyReason
        FROM dbo.OutboundDocument WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<OutboundLineDto>> GetLinesAsync(Guid uid, CancellationToken cancellationToken = default)
        => (await context.Session.Connection.QueryAsync<OutboundLineDto>(new CommandDefinition("""
            SELECT Uid,LineNumber,ProductId,SkuSnapshot AS Sku,DescriptionSnapshot AS Description,UnitSnapshot AS Unit,Quantity,
                   PackingListContributionId,NetDemandAllocationId,SalesOrderNoSnapshot AS SalesOrderNo,CommandItemId
            FROM dbo.OutboundDocumentLine WHERE OutboundDocumentId=@Uid AND ISNULL(IsDeleted,0)=0 ORDER BY LineNumber;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();

    public async Task<IReadOnlyList<InventoryLedgerItemDto>> GetLedgersAsync(Guid uid, CancellationToken cancellationToken = default)
        => (await context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(new CommandDefinition("""
            SELECT Uid,Sequence,WarehouseSiteId,WarehouseCodeSnapshot AS WarehouseCode,ProductId,SkuSnapshot AS Sku,UnitSnapshot AS Unit,
                   TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,
                   SourceDocumentType AS SourceDocumentNo,ReversalOfLedgerId,Reason,OccurredDate,CreatedBy
            FROM dbo.InventoryLedgerEntry WHERE SourceDocumentId=@Uid ORDER BY Sequence;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();

    public Task<OutboundDocument?> GetByCommandForUpdateAsync(Guid commandId) => context.Session.Connection.QuerySingleOrDefaultAsync<OutboundDocument>("""
        SELECT TOP(1) * FROM dbo.OutboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE CommandId=@CommandId AND ISNULL(IsDeleted,0)=0 ORDER BY Uid;
        """, new { CommandId = commandId }, context.Session.Transaction);

    public async Task<IReadOnlyList<OutboundDocument>> GetByPackingListForUpdateAsync(Guid packingListId)
        => (await context.Session.Connection.QueryAsync<OutboundDocument>("""
            SELECT * FROM dbo.OutboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE SourcePackingListId=@PackingListId AND ISNULL(IsDeleted,0)=0 ORDER BY WarehouseSiteId,DocumentType,Uid;
            """, new { PackingListId = packingListId }, context.Session.Transaction)).ToList();

    public async Task<IReadOnlyList<OutboundDocumentLine>> GetLinesForUpdateAsync(Guid documentId)
        => (await context.Session.Connection.QueryAsync<OutboundDocumentLine>("""
            SELECT * FROM dbo.OutboundDocumentLine WITH(UPDLOCK,HOLDLOCK) WHERE OutboundDocumentId=@DocumentId AND ISNULL(IsDeleted,0)=0 ORDER BY LineNumber;
            """, new { DocumentId = documentId }, context.Session.Transaction)).ToList();

    public async Task<int> InsertAsync(OutboundDocument document, IReadOnlyList<OutboundDocumentLine> lines)
    {
        context.AuditStamp.StampForInsert(document);
        foreach (var line in lines) context.AuditStamp.StampForInsert(line);
        var head = await context.Session.Connection.ExecuteAsync("""
            INSERT dbo.OutboundDocument(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,DocumentNo,DocumentType,LifecycleStatus,PostingStatus,
              WarehouseSiteId,WarehouseCodeSnapshot,WarehouseNameSnapshot,ShipmentDate,SourcePackingListId,PackingListNoSnapshot,PostedDate,PostedBy,CommandId)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@DocumentNo,@DocumentType,@LifecycleStatus,@PostingStatus,
              @WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@ShipmentDate,@SourcePackingListId,@PackingListNoSnapshot,@PostedDate,@PostedBy,@CommandId);
            """, document, context.Session.Transaction);
        var detail = await context.Session.Connection.ExecuteAsync("""
            INSERT dbo.OutboundDocumentLine(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,OutboundDocumentId,LineNumber,ProductId,SkuSnapshot,DescriptionSnapshot,
              UnitSnapshot,Quantity,PackingListContributionId,NetDemandAllocationId,GrossDemandId,SalesOrderNoSnapshot,CommandItemId,OriginalLineId)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@OutboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,
              @UnitSnapshot,@Quantity,@PackingListContributionId,@NetDemandAllocationId,@GrossDemandId,@SalesOrderNoSnapshot,@CommandItemId,@OriginalLineId);
            """, lines, context.Session.Transaction);
        return head + detail;
    }

    public Task<int> SetReversedAsync(Guid uid, int expectedVersion, DateTime reversedDate, string reversedBy, string reason) => context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.OutboundDocument SET PostingStatus=8,ReversedDate=@ReversedDate,ReversedBy=@ReversedBy,ReverseReason=@Reason,
            Version=Version+1,UpdatedDate=@ReversedDate,UpdatedBy=@ReversedBy
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND PostingStatus=1 AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid, ExpectedVersion = expectedVersion, ReversedDate = reversedDate, ReversedBy = reversedBy, Reason = reason }, context.Session.Transaction);

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
}
