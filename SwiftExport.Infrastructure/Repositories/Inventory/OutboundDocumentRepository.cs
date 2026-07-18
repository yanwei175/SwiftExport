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
        var p = new
        {
            No = Like(request.DocumentNo), request.DocumentType, request.PostingStatus, request.ShipmentDateFrom, request.ShipmentDateTo,
            PackingNo = Like(request.PackingListNo), SalesNo = Like(request.SalesOrderNo), Sku = Like(request.Sku), Warehouse = Like(request.WarehouseKeyword),
            Offset = (page - 1) * size, PageSize = size
        };
        const string where = "WHERE ISNULL(h.IsDeleted,0)=0 AND (@No IS NULL OR h.OutboundNo LIKE @No) AND (@DocumentType IS NULL OR h.DocumentType=@DocumentType) AND (@PostingStatus IS NULL OR h.PostingStatus=@PostingStatus) AND (@ShipmentDateFrom IS NULL OR h.OutboundDate>=@ShipmentDateFrom) AND (@ShipmentDateTo IS NULL OR h.OutboundDate<DATEADD(day,1,@ShipmentDateTo)) AND (@PackingNo IS NULL OR h.PackingListNoSnapshot LIKE @PackingNo) AND (@Warehouse IS NULL OR h.WarehouseCode LIKE @Warehouse OR h.WarehouseNameSnapshot LIKE @Warehouse) AND (@SalesNo IS NULL OR EXISTS(SELECT 1 FROM dbo.OutboundLine x WHERE x.OutboundDocumentId=h.Uid AND x.SalesOrderNoSnapshot LIKE @SalesNo AND ISNULL(x.IsDeleted,0)=0)) AND (@Sku IS NULL OR EXISTS(SELECT 1 FROM dbo.OutboundLine x WHERE x.OutboundDocumentId=h.Uid AND x.Sku LIKE @Sku AND ISNULL(x.IsDeleted,0)=0))";
        var total = await context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM dbo.OutboundHead h {where};", p, context.Session.Transaction);
        var rows = await context.Session.Connection.QueryAsync<OutboundListItemDto>(new CommandDefinition($"""
            SELECT COALESCE(h.Uid,'00000000-0000-0000-0000-000000000000') AS Uid,COALESCE(h.Version,0) AS Version,h.OutboundNo AS DocumentNo,h.DocumentType,h.PostingStatus,h.OutboundDate AS ShipmentDate,
                   h.WarehouseNameSnapshot AS WarehouseName,h.PackingListNoSnapshot AS PackingListNo,
                   COUNT(l.Uid) AS LineCount,COALESCE(SUM(l.Quantity),0) AS TotalQuantity,
                   CAST(CASE WHEN h.Uid IS NULL OR h.DocumentType IS NULL OR h.PostingStatus IS NULL THEN 1 ELSE 0 END AS bit) AS IsLegacyReadOnly,
                   CASE WHEN h.Uid IS NULL THEN N'遗留出库单缺少新链路 Uid' WHEN h.DocumentType IS NULL OR h.PostingStatus IS NULL THEN N'遗留出库单缺少类型或过账状态' END AS ReadOnlyReason
            FROM dbo.OutboundHead h LEFT JOIN dbo.OutboundLine l ON l.OutboundDocumentId=h.Uid AND ISNULL(l.IsDeleted,0)=0
            {where} GROUP BY h.Uid,h.Version,h.OutboundNo,h.DocumentType,h.PostingStatus,h.OutboundDate,h.WarehouseNameSnapshot,h.PackingListNoSnapshot
            ORDER BY h.OutboundDate DESC,h.OutboundNo DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, p, context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<OutboundListItemDto> { Items = rows.ToList(), TotalCount = total, PageIndex = page, PageSize = size };
    }

    public Task<OutboundHeaderDto?> GetHeaderAsync(Guid uid, CancellationToken cancellationToken = default)
        => context.Session.Connection.QuerySingleOrDefaultAsync<OutboundHeaderDto>(new CommandDefinition("""
            SELECT h.Uid,h.Version,h.OutboundNo AS DocumentNo,h.DocumentType,h.PostingStatus,h.WarehouseSiteId,h.WarehouseCode,
                   h.WarehouseNameSnapshot AS WarehouseName,h.OutboundDate AS ShipmentDate,h.SourcePackingListId,h.PackingListNoSnapshot AS PackingListNo,
                   h.PostedDate,h.PostedBy,h.ReversedDate,h.ReversedBy,h.ReverseReason,CAST(0 AS bit) AS IsLegacyReadOnly,CAST(NULL AS nvarchar(500)) AS ReadOnlyReason
            FROM dbo.OutboundHead h WHERE h.Uid=@Uid AND ISNULL(h.IsDeleted,0)=0;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<OutboundLineDto>> GetLinesAsync(Guid uid, CancellationToken cancellationToken = default)
        => (await context.Session.Connection.QueryAsync<OutboundLineDto>(new CommandDefinition("""
            SELECT Uid,LineNumber,ProductId,Sku,DescriptionSnapshot AS Description,UnitSnapshot AS Unit,Quantity,
                   PackingListContributionId,NetDemandAllocationId,SalesOrderNoSnapshot AS SalesOrderNo,CommandItemId
            FROM dbo.OutboundLine WHERE OutboundDocumentId=@Uid AND ISNULL(IsDeleted,0)=0 ORDER BY LineNumber;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();

    public async Task<IReadOnlyList<InventoryLedgerItemDto>> GetLedgersAsync(Guid uid, CancellationToken cancellationToken = default)
        => (await context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(new CommandDefinition("""
            SELECT Uid,Sequence,WarehouseId AS WarehouseSiteId,WarehouseCodeSnapshot AS WarehouseCode,ProductId,SkuSnapshot AS Sku,UnitSnapshot AS Unit,
                   TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,RefNo AS SourceDocumentNo,
                   ReversalOfLedgerId,Reason,OccurredDate,CreatedBy FROM dbo.InventoryTransaction
            WHERE SourceDocumentId=@Uid AND ISNULL(IsDeleted,0)=0 ORDER BY Sequence;
            """, new { Uid = uid }, context.Session.Transaction, cancellationToken: cancellationToken))).ToList();

    public Task<OutboundDocument?> GetByCommandForUpdateAsync(Guid commandId) => context.Session.Connection.QuerySingleOrDefaultAsync<OutboundDocument>("""
        SELECT TOP(1) Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,OutboundNo AS DocumentNo,DocumentType,LifecycleStatus,PostingStatus,
               WarehouseSiteId,WarehouseCode AS WarehouseCodeSnapshot,WarehouseNameSnapshot,OutboundDate AS ShipmentDate,SourcePackingListId,PackingListNoSnapshot,
               PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,CommandId
        FROM dbo.OutboundHead WITH(UPDLOCK,HOLDLOCK) WHERE CommandId=@CommandId AND ISNULL(IsDeleted,0)=0 ORDER BY Uid;
        """, new { CommandId = commandId }, context.Session.Transaction);

    public async Task<IReadOnlyList<OutboundDocument>> GetByPackingListForUpdateAsync(Guid packingListId)
        => (await context.Session.Connection.QueryAsync<OutboundDocument>("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,OutboundNo AS DocumentNo,DocumentType,LifecycleStatus,PostingStatus,
                   WarehouseSiteId,WarehouseCode AS WarehouseCodeSnapshot,WarehouseNameSnapshot,OutboundDate AS ShipmentDate,SourcePackingListId,PackingListNoSnapshot,
                   PostedDate,PostedBy,ReversedDate,ReversedBy,ReverseReason,CommandId
            FROM dbo.OutboundHead WITH(UPDLOCK,HOLDLOCK) WHERE SourcePackingListId=@PackingListId AND ISNULL(IsDeleted,0)=0 ORDER BY WarehouseSiteId,DocumentType,Uid;
            """, new { PackingListId = packingListId }, context.Session.Transaction)).ToList();

    public async Task<IReadOnlyList<OutboundDocumentLine>> GetLinesForUpdateAsync(Guid documentId)
        => (await context.Session.Connection.QueryAsync<OutboundDocumentLine>("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,OutboundDocumentId,LineNumber,ProductId,Sku AS SkuSnapshot,
                   DescriptionSnapshot,UnitSnapshot,Quantity,PackingListContributionId,NetDemandAllocationId,GrossDemandId,SalesOrderNoSnapshot,CommandItemId,OriginalLineId
            FROM dbo.OutboundLine WITH(UPDLOCK,HOLDLOCK) WHERE OutboundDocumentId=@DocumentId AND ISNULL(IsDeleted,0)=0 ORDER BY LineNumber;
            """, new { DocumentId = documentId }, context.Session.Transaction)).ToList();

    public async Task<int> InsertAsync(OutboundDocument document, IReadOnlyList<OutboundDocumentLine> lines)
    {
        context.AuditStamp.StampForInsert(document);
        foreach (var line in lines) context.AuditStamp.StampForInsert(line);
        var head = await context.Session.Connection.ExecuteAsync("""
            INSERT dbo.OutboundHead(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,OutboundNo,DocumentType,LifecycleStatus,PostingStatus,
              WarehouseSiteId,WarehouseCode,WarehouseNameSnapshot,OutboundDate,SourcePackingListId,PackingListNoSnapshot,PostedDate,PostedBy,CommandId)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@DocumentNo,@DocumentType,@LifecycleStatus,@PostingStatus,
              @WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@ShipmentDate,@SourcePackingListId,@PackingListNoSnapshot,@PostedDate,@PostedBy,@CommandId);
            """, document, context.Session.Transaction);
        var detail = await context.Session.Connection.ExecuteAsync("""
            INSERT dbo.OutboundLine(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,OutboundDocumentId,LineNumber,ProductId,Sku,DescriptionSnapshot,
              UnitSnapshot,Quantity,Qty,PackingListContributionId,NetDemandAllocationId,GrossDemandId,SalesOrderNoSnapshot,CommandItemId,OriginalLineId)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@OutboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,
              @UnitSnapshot,@Quantity,@Quantity,@PackingListContributionId,@NetDemandAllocationId,@GrossDemandId,@SalesOrderNoSnapshot,@CommandItemId,@OriginalLineId);
            """, lines, context.Session.Transaction);
        return head + detail;
    }

    public Task<int> SetReversedAsync(Guid uid, int expectedVersion, DateTime reversedDate, string reversedBy, string reason)
        => context.Session.Connection.ExecuteAsync("""
            UPDATE dbo.OutboundHead SET PostingStatus=8,ReversedDate=@ReversedDate,ReversedBy=@ReversedBy,ReverseReason=@Reason,
                Version=Version+1,UpdatedDate=@ReversedDate,UpdatedBy=@ReversedBy
            WHERE Uid=@Uid AND Version=@ExpectedVersion AND PostingStatus=1 AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid, ExpectedVersion = expectedVersion, ReversedDate = reversedDate, ReversedBy = reversedBy, Reason = reason }, context.Session.Transaction);

    private static string? Like(string? value) => string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";
}
