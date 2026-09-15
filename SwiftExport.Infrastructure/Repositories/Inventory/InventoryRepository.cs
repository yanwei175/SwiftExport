using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>提供库存余额、不可变流水查询及过账锁的数据访问能力。</summary>
public sealed class InventoryRepository(RepositoryBaseContext context) : IInventoryRepository
{
    private readonly RepositoryBaseContext _context = context;

    /// <summary>分页查询库存余额；业务展示字段使用过账快照，图片读取当前有效产品主数据。</summary>
    public async Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = new DynamicParameters(new { request.SupplierId, request.WarehouseSiteId, request.NonZeroOnly, Keyword = Like(request.Keyword), Offset = (index - 1) * size, PageSize = size });
        const string where = """
            WHERE ISNULL(i.IsDeleted,0)=0 AND (@SupplierId IS NULL OR w.SupplierId=@SupplierId)
              AND (@WarehouseSiteId IS NULL OR i.WarehouseSiteId=@WarehouseSiteId)
              AND (ISNULL(@NonZeroOnly,0)=0 OR i.OnHandQty<>0)
              AND (@Keyword IS NULL OR i.SkuSnapshot LIKE @Keyword ESCAPE '\' OR i.ProductNameSnapshot LIKE @Keyword ESCAPE '\' OR w.Code LIKE @Keyword ESCAPE '\' OR w.Name LIKE @Keyword ESCAPE '\')
            """;
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dbo.InventoryBalance i JOIN dbo.WarehouseSite w ON w.Uid=i.WarehouseSiteId {where};", p, _context.Session.Transaction);
        var rows = await _context.Session.Connection.QueryAsync<InventoryBalanceDto>(new CommandDefinition($"""
            SELECT i.Uid,i.Version,i.WarehouseSiteId,w.Code AS WarehouseCode,w.Name AS WarehouseName,w.SupplierId,
                   s.SupplierCode,s.SupplierName,i.ProductId,i.SkuSnapshot AS Sku,i.ProductNameSnapshot AS ProductName,
                   i.UnitSnapshot AS Unit,p.MainImage AS ImagePath,i.OnHandQty,i.OnHandQty AS AvailableQty,i.LastLedgerSequence,i.UpdatedDate
            FROM dbo.InventoryBalance i JOIN dbo.WarehouseSite w ON w.Uid=i.WarehouseSiteId LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId
            LEFT JOIN dbo.Product p ON p.Uid=i.ProductId AND ISNULL(p.IsDeleted,0)=0
            {where} ORDER BY w.Code,i.SkuSnapshot,i.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, p, _context.Session.Transaction, cancellationToken: cancellationToken));
        return Page(rows, total, index, size);
    }

    /// <summary>分页查询库存流水，历史名称和单号不跟随主数据变化。</summary>
    public async Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var p = new DynamicParameters(new { request.SupplierId, request.WarehouseSiteId, request.TransactionType, request.OccurredDateFrom, request.OccurredDateTo, SourceDocumentNo = Like(request.SourceDocumentNo), Keyword = Like(request.Keyword), Offset = (index - 1) * size, PageSize = size });
        const string where = """
            WHERE (@SupplierId IS NULL OR w.SupplierId=@SupplierId) AND (@WarehouseSiteId IS NULL OR t.WarehouseSiteId=@WarehouseSiteId)
              AND (@TransactionType IS NULL OR t.TransactionType=@TransactionType)
              AND (@OccurredDateFrom IS NULL OR t.OccurredDate>=@OccurredDateFrom)
              AND (@OccurredDateTo IS NULL OR t.OccurredDate<DATEADD(day,1,@OccurredDateTo))
              AND (@SourceDocumentNo IS NULL OR t.SourceDocumentNoSnapshot LIKE @SourceDocumentNo ESCAPE '\')
              AND (@Keyword IS NULL OR t.SkuSnapshot LIKE @Keyword ESCAPE '\' OR t.ProductNameSnapshot LIKE @Keyword ESCAPE '\' OR t.SourceDocumentType LIKE @Keyword ESCAPE '\' OR t.WarehouseCodeSnapshot LIKE @Keyword ESCAPE '\')
            """;
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dbo.InventoryLedgerEntry t JOIN dbo.WarehouseSite w ON w.Uid=t.WarehouseSiteId {where};", p, _context.Session.Transaction);
        var rows = await _context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(new CommandDefinition($"{LedgerSelect} JOIN dbo.WarehouseSite w ON w.Uid=t.WarehouseSiteId {where} ORDER BY t.Sequence DESC,t.Uid DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", p, _context.Session.Transaction, cancellationToken: cancellationToken));
        return Page(rows, total, index, size);
    }

    /// <summary>读取余额详情；产品缺失或已删除时仍返回余额，并将图片留空。</summary>
    public Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default) => _context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalanceDto>(new CommandDefinition("""
        SELECT i.Uid,i.Version,i.WarehouseSiteId,w.Code AS WarehouseCode,w.Name AS WarehouseName,w.SupplierId,
               s.SupplierCode,s.SupplierName,i.ProductId,i.SkuSnapshot AS Sku,i.ProductNameSnapshot AS ProductName,
               i.UnitSnapshot AS Unit,p.MainImage AS ImagePath,i.OnHandQty,i.OnHandQty AS AvailableQty,i.LastLedgerSequence,i.UpdatedDate
        FROM dbo.InventoryBalance i JOIN dbo.WarehouseSite w ON w.Uid=i.WarehouseSiteId LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId
        LEFT JOIN dbo.Product p ON p.Uid=i.ProductId AND ISNULL(p.IsDeleted,0)=0
        WHERE i.Uid=@Uid AND ISNULL(i.IsDeleted,0)=0;
        """, new { Uid = uid }, _context.Session.Transaction, cancellationToken: cancellationToken));

    /// <summary>按余额键分页读取 Trace 流水。</summary>
    public async Task<PagedResponseBase<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        var index = Math.Max(1, pageIndex);
        var size = Math.Clamp(pageSize, 1, 200);
        var args = new { BalanceId = balanceId, Offset = (index - 1) * size, PageSize = size };
        const string join = "JOIN dbo.InventoryBalance i ON i.WarehouseSiteId=t.WarehouseSiteId AND i.ProductId=t.ProductId JOIN dbo.WarehouseSite w ON w.Uid=t.WarehouseSiteId WHERE i.Uid=@BalanceId";
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dbo.InventoryLedgerEntry t {join};", args, _context.Session.Transaction);
        var rows = await _context.Session.Connection.QueryAsync<InventoryLedgerItemDto>(new CommandDefinition($"{LedgerSelect} {join} ORDER BY t.Sequence DESC,t.Uid DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", args, _context.Session.Transaction, cancellationToken: cancellationToken));
        return Page(rows, total, index, size);
    }

    /// <summary>计算余额投影与流水净额的一致性。</summary>
    public Task<InventoryReconciliationDto?> GetReconciliationAsync(Guid balanceId, CancellationToken cancellationToken = default) => _context.Session.Connection.QuerySingleOrDefaultAsync<InventoryReconciliationDto>(new CommandDefinition("""
        SELECT i.Uid AS BalanceId,i.OnHandQty AS BalanceQty,COALESCE(SUM(t.QuantityDelta),0) AS LedgerNetQty,
               i.LastLedgerSequence AS BalanceLastLedgerSequence,COALESCE(MAX(t.Sequence),0) AS LedgerLastSequence,
               CONVERT(bit,CASE WHEN i.OnHandQty=COALESCE(SUM(t.QuantityDelta),0) AND i.LastLedgerSequence=COALESCE(MAX(t.Sequence),0) THEN 1 ELSE 0 END) AS IsReconciled
        FROM dbo.InventoryBalance i LEFT JOIN dbo.InventoryLedgerEntry t ON t.WarehouseSiteId=i.WarehouseSiteId AND t.ProductId=i.ProductId
        WHERE i.Uid=@BalanceId AND ISNULL(i.IsDeleted,0)=0 GROUP BY i.Uid,i.OnHandQty,i.LastLedgerSequence;
        """, new { BalanceId = balanceId }, _context.Session.Transaction, cancellationToken: cancellationToken));

    /// <summary>锁定唯一余额投影。</summary>
    public Task<InventoryBalance?> LockBalanceAsync(Guid warehouseSiteId, Guid productId) => _context.Session.Connection.QuerySingleOrDefaultAsync<InventoryBalance>("""
        SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,WarehouseSiteId,ProductId,SkuSnapshot,ProductNameSnapshot,UnitSnapshot,OnHandQty,LastLedgerSequence
        FROM dbo.InventoryBalance WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseSiteId=@WarehouseSiteId AND ProductId=@ProductId;
        """, new { WarehouseSiteId = warehouseSiteId, ProductId = productId }, _context.Session.Transaction);

    /// <summary>按幂等键读取流水。</summary>
    public Task<InventoryLedgerEntry?> FindLedgerByCommandItemAsync(Guid commandItemId) => QueryLedgerAsync("", "CommandItemId=@CommandItemId", new { CommandItemId = commandItemId });
    /// <summary>按幂等键锁定流水。</summary>
    public Task<InventoryLedgerEntry?> LockLedgerByCommandItemAsync(Guid commandItemId) => QueryLedgerAsync("WITH(UPDLOCK,HOLDLOCK)", "CommandItemId=@CommandItemId", new { CommandItemId = commandItemId });
    /// <summary>按流水主键锁定原始事实。</summary>
    public Task<InventoryLedgerEntry?> LockLedgerByIdAsync(Guid ledgerId) => QueryLedgerAsync("WITH(UPDLOCK,HOLDLOCK)", "Uid=@Uid", new { Uid = ledgerId });
    /// <summary>锁定已有反向流水。</summary>
    public Task<InventoryLedgerEntry?> LockReversalByOriginalAsync(Guid originalLedgerId) => QueryLedgerAsync("WITH(UPDLOCK,HOLDLOCK)", "ReversalOfLedgerId=@OriginalLedgerId", new { OriginalLedgerId = originalLedgerId });
    /// <summary>锁定已有纠正替换流水。</summary>
    public Task<InventoryLedgerEntry?> LockReplacementByOriginalAsync(Guid originalLedgerId) => QueryLedgerAsync("WITH(UPDLOCK,HOLDLOCK)", "ReplacementOfLedgerId=@OriginalLedgerId", new { OriginalLedgerId = originalLedgerId });

    /// <summary>锁定纠正组全部事实，冻结纠正命令集合。</summary>
    public async Task<IReadOnlyList<InventoryLedgerEntry>> LockCorrectionGroupAsync(Guid correctionGroupId)
    {
        var rows = await _context.Session.Connection.QueryAsync<InventoryLedgerEntry>($"SELECT {LedgerColumns} FROM dbo.InventoryLedgerEntry WITH(UPDLOCK,HOLDLOCK) WHERE CorrectionGroupId=@CorrectionGroupId ORDER BY Sequence,Uid;", new { CorrectionGroupId = correctionGroupId }, _context.Session.Transaction);
        return rows.ToList();
    }

    /// <summary>读取可用于新过账的产品。</summary>
    public Task<Product?> GetProductForPostingAsync(Guid productId) => _context.Session.Connection.QuerySingleOrDefaultAsync<Product>("SELECT Uid,Sku,Unit,DescriptionZh,DescriptionEn FROM dbo.Product WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;", new { Uid = productId }, _context.Session.Transaction);

    /// <summary>取得事务所有的命令级应用锁。</summary>
    public Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default) => _context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
        DECLARE @Result int; EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000; SELECT @Result;
        """, new { Resource = $"InventoryCommand:{commandId:N}" }, _context.Session.Transaction, cancellationToken: cancellationToken));

    /// <summary>创建余额投影。</summary>
    public async Task<int> InsertBalanceAsync(InventoryBalance balance)
    {
        _context.AuditStamp.StampForInsert(balance);
        return await _context.Session.Connection.ExecuteAsync("""
            INSERT dbo.InventoryBalance(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,WarehouseSiteId,ProductId,SkuSnapshot,ProductNameSnapshot,UnitSnapshot,OnHandQty,LastLedgerSequence)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@WarehouseSiteId,@ProductId,@SkuSnapshot,@ProductNameSnapshot,@UnitSnapshot,@OnHandQty,@LastLedgerSequence);
            """, balance, _context.Session.Transaction);
    }

    /// <summary>以乐观并发版本写回余额最终状态。</summary>
    public Task<int> UpdateBalanceAsync(InventoryBalance balance, int expectedVersion) => _context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.InventoryBalance SET SkuSnapshot=@SkuSnapshot,ProductNameSnapshot=@ProductNameSnapshot,UnitSnapshot=@UnitSnapshot,OnHandQty=@OnHandQty,
            LastLedgerSequence=@LastLedgerSequence,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName WHERE Uid=@Uid AND Version=@ExpectedVersion;
        """, new { balance.Uid, balance.SkuSnapshot, balance.ProductNameSnapshot, balance.UnitSnapshot, balance.OnHandQty, balance.LastLedgerSequence, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);

    /// <summary>取得全局单调流水序号。</summary>
    public Task<long> NextLedgerSequenceAsync() => _context.Session.Connection.ExecuteScalarAsync<long>("SELECT NEXT VALUE FOR dbo.InventoryLedgerSequence;", transaction: _context.Session.Transaction);

    /// <summary>新增不可变库存流水。</summary>
    public async Task<int> InsertLedgerAsync(InventoryLedgerEntry entry)
    {
        _context.AuditStamp.StampForInsert(entry);
        return await _context.Session.Connection.ExecuteAsync("""
            INSERT dbo.InventoryLedgerEntry(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Sequence,WarehouseSiteId,ProductId,TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,SourceDocumentNoSnapshot,ReversalOfLedgerId,ReplacementOfLedgerId,CorrectionGroupId,CommandId,CommandItemId,RequestFingerprint,Reason,OccurredDate,WarehouseCodeSnapshot,WarehouseNameSnapshot,SkuSnapshot,ProductNameSnapshot,UnitSnapshot)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@Sequence,@WarehouseSiteId,@ProductId,@TransactionType,@QuantityDelta,@BalanceAfter,@SourceDocumentType,@SourceDocumentId,@SourceLineId,@SourceDocumentNoSnapshot,@ReversalOfLedgerId,@ReplacementOfLedgerId,@CorrectionGroupId,@CommandId,@CommandItemId,@RequestFingerprint,@Reason,@OccurredDate,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@SkuSnapshot,@ProductNameSnapshot,@UnitSnapshot);
            """, entry, _context.Session.Transaction);
    }

    private Task<InventoryLedgerEntry?> QueryLedgerAsync(string hint, string where, object args) => _context.Session.Connection.QuerySingleOrDefaultAsync<InventoryLedgerEntry>($"SELECT {LedgerColumns} FROM dbo.InventoryLedgerEntry {hint} WHERE {where};", args, _context.Session.Transaction);
    private static string? Like(string? value) { var text = value?.Trim(); return string.IsNullOrEmpty(text) ? null : $"%{text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[")}%"; }
    private static PagedResponseBase<T> Page<T>(IEnumerable<T> rows, int total, int index, int size) => new() { Items = rows.ToList(), TotalCount = total, PageIndex = index, PageSize = size };

    private const string LedgerColumns = "Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Sequence,WarehouseSiteId,ProductId,TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,SourceDocumentNoSnapshot,ReversalOfLedgerId,ReplacementOfLedgerId,CorrectionGroupId,CommandId,CommandItemId,RequestFingerprint,Reason,OccurredDate,WarehouseCodeSnapshot,WarehouseNameSnapshot,SkuSnapshot,ProductNameSnapshot,UnitSnapshot";
    private const string LedgerSelect = """
        SELECT t.Uid,t.Sequence,t.WarehouseSiteId,t.WarehouseCodeSnapshot AS WarehouseCode,t.WarehouseNameSnapshot AS WarehouseName,
               t.ProductId,t.SkuSnapshot AS Sku,t.ProductNameSnapshot AS ProductName,t.UnitSnapshot AS Unit,t.TransactionType,t.QuantityDelta,t.BalanceAfter,
               t.SourceDocumentType,t.SourceDocumentId,t.SourceLineId,t.SourceDocumentNoSnapshot AS SourceDocumentNo,t.ReversalOfLedgerId,
               rv.Uid AS ReversedByLedgerId,t.ReplacementOfLedgerId,rp.Uid AS ReplacedByLedgerId,t.CorrectionGroupId,t.Reason,t.OccurredDate,t.CreatedBy
        FROM dbo.InventoryLedgerEntry t LEFT JOIN dbo.InventoryLedgerEntry rv ON rv.ReversalOfLedgerId=t.Uid LEFT JOIN dbo.InventoryLedgerEntry rp ON rp.ReplacementOfLedgerId=t.Uid
        """;
}
