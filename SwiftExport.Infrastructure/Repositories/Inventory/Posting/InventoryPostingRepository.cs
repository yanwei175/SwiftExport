using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Inventory.Posting;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Posting;

/// <summary>在调用方事务内锁定库存事实、维护余额并追加不可变流水。</summary>
public sealed class InventoryPostingRepository(RepositoryBaseContext context) : IInventoryPostingRepository
{
    private readonly RepositoryBaseContext _context = context;

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
    private const string LedgerColumns = "Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Sequence,WarehouseSiteId,ProductId,TransactionType,QuantityDelta,BalanceAfter,SourceDocumentType,SourceDocumentId,SourceLineId,SourceDocumentNoSnapshot,ReversalOfLedgerId,ReplacementOfLedgerId,CorrectionGroupId,CommandId,CommandItemId,RequestFingerprint,Reason,OccurredDate,WarehouseCodeSnapshot,WarehouseNameSnapshot,SkuSnapshot,ProductNameSnapshot,UnitSnapshot";
}
