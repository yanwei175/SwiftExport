using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

public sealed class InventoryRepository(RepositoryBaseContext context) : IInventoryRepository
{
    private readonly RepositoryBaseContext _context = context;

    public async Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex); var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = new DynamicParameters(new { request.SupplierId, request.WarehouseSiteId, request.NonZeroOnly, Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{request.Keyword.Trim()}%", Offset = (index - 1) * size, PageSize = size });
        const string where = "WHERE ISNULL(i.IsDeleted,0)=0 AND (@SupplierId IS NULL OR w.SupplierId=@SupplierId) AND (@WarehouseSiteId IS NULL OR i.WarehouseId=@WarehouseSiteId) AND (@NonZeroOnly IS NULL OR @NonZeroOnly=0 OR i.OnhandQty<>0) AND (@Keyword IS NULL OR i.Sku LIKE @Keyword OR p.Sku LIKE @Keyword OR p.DescriptionZh LIKE @Keyword OR p.DescriptionEn LIKE @Keyword OR w.WhseCode LIKE @Keyword)";
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dbo.Inventory i JOIN dbo.Warehouse w ON w.Uid=i.WarehouseId LEFT JOIN dbo.Product p ON p.Uid=i.ProductId AND ISNULL(p.IsDeleted,0)=0 {where};", p, _context.Session.Transaction);
        var sql = $"""
            SELECT i.Uid,i.Version,i.WarehouseId AS WarehouseSiteId,w.WhseCode AS WarehouseCode,w.WhseName AS WarehouseName,i.ProductId,
                   COALESCE(p.Sku,i.Sku) AS Sku,COALESCE(p.DescriptionZh,p.DescriptionEn,i.Sku) AS ProductName,COALESCE(i.UnitSnapshot,i.Unit) AS Unit,
                   i.OnhandQty, i.OnhandQty AS AvailableQty,i.UpdatedDate
            FROM dbo.Inventory i JOIN dbo.Warehouse w ON w.Uid=i.WarehouseId LEFT JOIN dbo.Product p ON p.Uid=i.ProductId AND ISNULL(p.IsDeleted,0)=0
            {where} ORDER BY w.WhseCode,COALESCE(p.Sku,i.Sku),i.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        var rows = await _context.Session.Connection.QueryAsync<InventoryBalanceDto>(new CommandDefinition(sql, p, _context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<InventoryBalanceDto> { Items = rows.ToList(), TotalCount = total, PageIndex = index, PageSize = size };
    }

    public async Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex); var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = new DynamicParameters(new { request.SupplierId, request.WarehouseSiteId, request.TransactionType, request.OccurredDateFrom, request.OccurredDateTo, SourceDocumentNo = string.IsNullOrWhiteSpace(request.SourceDocumentNo) ? null : $"%{request.SourceDocumentNo.Trim()}%", Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{request.Keyword.Trim()}%", Offset = (index - 1) * size, PageSize = size });
        const string where = "WHERE ISNULL(t.IsDeleted,0)=0 AND (@SupplierId IS NULL OR w.SupplierId=@SupplierId) AND (@WarehouseSiteId IS NULL OR t.WarehouseId=@WarehouseSiteId) AND (@TransactionType IS NULL OR t.TransactionType=@TransactionType) AND (@OccurredDateFrom IS NULL OR t.OccurredDate>=@OccurredDateFrom) AND (@OccurredDateTo IS NULL OR t.OccurredDate<DATEADD(day,1,@OccurredDateTo)) AND (@SourceDocumentNo IS NULL OR t.RefNo LIKE @SourceDocumentNo) AND (@Keyword IS NULL OR t.Sku LIKE @Keyword OR p.Sku LIKE @Keyword OR t.RefNo LIKE @Keyword OR w.WhseCode LIKE @Keyword)";
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dbo.InventoryTransaction t JOIN dbo.Warehouse w ON w.Uid=t.WarehouseId LEFT JOIN dbo.Product p ON p.Uid=t.ProductId AND ISNULL(p.IsDeleted,0)=0 {where};", p, _context.Session.Transaction);
        var sql = $"""
            SELECT t.Uid,t.Sequence,t.WarehouseId AS WarehouseSiteId,w.WhseCode AS WarehouseCode,t.ProductId,COALESCE(p.Sku,t.Sku) AS Sku,
                   COALESCE(t.UnitSnapshot,t.Unit) AS Unit,t.TransactionType,t.QuantityDelta,t.BalanceAfter,t.SourceDocumentType,t.SourceDocumentId,t.SourceLineId,
                   t.RefNo AS SourceDocumentNo,t.ReversalOfLedgerId,t.Reason,t.OccurredDate,t.CreatedBy
            FROM dbo.InventoryTransaction t JOIN dbo.Warehouse w ON w.Uid=t.WarehouseId LEFT JOIN dbo.Product p ON p.Uid=t.ProductId AND ISNULL(p.IsDeleted,0)=0
            {where} ORDER BY t.Sequence DESC,t.Uid DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        var rows = await _context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(new CommandDefinition(sql, p, _context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<InventoryLedgerItemDto> { Items = rows.ToList(), TotalCount = total, PageIndex = index, PageSize = size };
    }

    public Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default) => _context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalanceDto>(new CommandDefinition("""
        SELECT i.Uid,i.Version,i.WarehouseId AS WarehouseSiteId,w.WhseCode AS WarehouseCode,w.WhseName AS WarehouseName,i.ProductId,COALESCE(p.Sku,i.Sku) AS Sku,
               COALESCE(p.DescriptionZh,p.DescriptionEn,i.Sku) AS ProductName,COALESCE(i.UnitSnapshot,i.Unit) AS Unit,i.OnhandQty,i.OnhandQty AS AvailableQty,i.UpdatedDate
        FROM dbo.Inventory i JOIN dbo.Warehouse w ON w.Uid=i.WarehouseId LEFT JOIN dbo.Product p ON p.Uid=i.ProductId AND ISNULL(p.IsDeleted,0)=0 WHERE i.Uid=@Uid AND ISNULL(i.IsDeleted,0)=0;
        """, new { Uid = uid }, _context.Session.Transaction, cancellationToken: cancellationToken));

    public async Task<IReadOnlyList<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, CancellationToken cancellationToken = default)
    {
        const string sql = """SELECT t.Uid,t.Sequence,t.WarehouseId AS WarehouseSiteId,w.WhseCode AS WarehouseCode,t.ProductId,COALESCE(p.Sku,t.Sku) AS Sku,COALESCE(t.UnitSnapshot,t.Unit) AS Unit,t.TransactionType,t.QuantityDelta,t.BalanceAfter,t.SourceDocumentType,t.SourceDocumentId,t.SourceLineId,t.RefNo AS SourceDocumentNo,t.ReversalOfLedgerId,t.Reason,t.OccurredDate,t.CreatedBy FROM dbo.InventoryTransaction t JOIN dbo.Inventory i ON i.WarehouseId=t.WarehouseId AND i.ProductId=t.ProductId JOIN dbo.Warehouse w ON w.Uid=t.WarehouseId LEFT JOIN dbo.Product p ON p.Uid=t.ProductId AND ISNULL(p.IsDeleted,0)=0 WHERE i.Uid=@BalanceId AND ISNULL(t.IsDeleted,0)=0 ORDER BY t.Sequence DESC,t.Uid DESC;""";
        var rows = await _context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(new CommandDefinition(sql, new { BalanceId = balanceId }, _context.Session.Transaction, cancellationToken: cancellationToken)); return rows.ToList();
    }

    public Task<InventoryBalance?> LockBalanceAsync(Guid warehouseSiteId, Guid productId) => _context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalance>("""
        SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,WarehouseId AS WarehouseSiteId,ProductId,Sku AS SkuSnapshot,UnitSnapshot,OnhandQty,LastLedgerSequence
        FROM dbo.Inventory WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseId=@WarehouseSiteId AND ProductId=@ProductId AND ISNULL(IsDeleted,0)=0;
        """, new { WarehouseSiteId = warehouseSiteId, ProductId = productId }, _context.Session.Transaction);
    public Task<InventoryLedgerEntry?> FindLedgerByCommandItemAsync(Guid commandItemId) => _context.Session.Connection.QuerySingleOrDefaultAsync<InventoryLedgerEntry>("SELECT Uid,Sequence,WarehouseId AS WarehouseSiteId,ProductId,TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,ReversalOfLedgerId,CommandId,CommandItemId,Reason,OccurredDate,WarehouseCodeSnapshot,SkuSnapshot,UnitSnapshot FROM dbo.InventoryTransaction WHERE CommandItemId=@CommandItemId AND ISNULL(IsDeleted,0)=0;", new { CommandItemId = commandItemId }, _context.Session.Transaction);
    public Task<bool> IsActiveProductAsync(Guid productId) => _context.Session.Connection.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Product WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit);", new { Uid = productId }, _context.Session.Transaction);
    public async Task<int> InsertBalanceAsync(InventoryBalance balance) { _context.AuditStamp.StampForInsert(balance); return await _context.Session.Connection.ExecuteAsync("INSERT dbo.Inventory(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,WarehouseId,ProductId,Sku,UnitSnapshot,OnhandQty,LastLedgerSequence) VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@WarehouseSiteId,@ProductId,@SkuSnapshot,@UnitSnapshot,@OnHandQty,@LastLedgerSequence);", balance, _context.Session.Transaction); }
    public Task<int> UpdateBalanceAsync(InventoryBalance balance, int expectedVersion) => _context.Session.Connection.ExecuteAsync("UPDATE dbo.Inventory SET OnhandQty=@OnHandQty,LastLedgerSequence=@LastLedgerSequence,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName WHERE Uid=@Uid AND Version=@ExpectedVersion;", new { balance.Uid, balance.OnHandQty, balance.LastLedgerSequence, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);
    public Task<long> NextLedgerSequenceAsync() => _context.Session.Connection.ExecuteScalarAsync<long>("SELECT NEXT VALUE FOR dbo.InventoryLedgerSequence;", transaction: _context.Session.Transaction);
    public async Task<int> InsertLedgerAsync(InventoryLedgerEntry entry) { _context.AuditStamp.StampForInsert(entry); return await _context.Session.Connection.ExecuteAsync("""INSERT dbo.InventoryTransaction(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Sequence,WarehouseId,ProductId,TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,ReversalOfLedgerId,CommandId,CommandItemId,Reason,OccurredDate,WarehouseCodeSnapshot,SkuSnapshot,UnitSnapshot) VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@Sequence,@WarehouseSiteId,@ProductId,@TransactionType,@QuantityDelta,@BalanceAfter,@SourceDocumentType,@SourceDocumentId,@SourceLineId,@ReversalOfLedgerId,@CommandId,@CommandItemId,@Reason,@OccurredDate,@WarehouseCodeSnapshot,@SkuSnapshot,@UnitSnapshot);""", entry, _context.Session.Transaction); }
}
