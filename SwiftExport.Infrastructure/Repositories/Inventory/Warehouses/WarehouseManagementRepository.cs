using Dapper;
using Microsoft.Data.SqlClient;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

/// <summary>使用共享 DbSession 锁定虚拟仓并执行资格检查与版本写入。</summary>
public sealed class WarehouseManagementRepository(RepositoryBaseContext context) : IWarehouseManagementRepository
{
    private readonly RepositoryBaseContext _context = context;

    public Task<WarehouseSite?> GetForUpdateAsync(Guid uid)
        => _context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSite>("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Code,Name,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive
            FROM dbo.WarehouseSite WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid }, _context.Session.Transaction);

    /// <inheritdoc/>
    public Task<WarehouseSupplierLookupDto?> GetActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default)
        => _context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSupplierLookupDto>(new CommandDefinition("""
            SELECT Uid,SupplierCode,SupplierName FROM dbo.Supplier WITH(UPDLOCK,HOLDLOCK)
            WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0 AND IsActive=1;
            """, new { Uid = supplierId }, _context.Session.Transaction, cancellationToken: cancellationToken));

    /// <inheritdoc/>
    public Task<WarehouseSite?> FindByCodeForUpdateAsync(string code)
        => _context.Session.Connection.QueryFirstOrDefaultAsync<WarehouseSite>("""
            SELECT TOP(1) Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Code,Name,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive
            FROM dbo.WarehouseSite WITH(UPDLOCK,HOLDLOCK) WHERE Code=@Code AND ISNULL(IsDeleted,0)=0;
            """, new { Code = code }, _context.Session.Transaction);

    /// <inheritdoc/>
    public Task<WarehouseSite?> FindActiveSupplierWarehouseForUpdateAsync(Guid supplierId)
        => _context.Session.Connection.QueryFirstOrDefaultAsync<WarehouseSite>("""
            SELECT TOP(1) Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Code,Name,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive
            FROM dbo.WarehouseSite WITH(UPDLOCK,HOLDLOCK)
            WHERE SupplierId=@SupplierId AND SiteType=0 AND IsActive=1 AND ISNULL(IsDeleted,0)=0;
            """, new { SupplierId = supplierId }, _context.Session.Transaction);

    public async Task<int> InsertAsync(WarehouseSite site)
    {
        _context.AuditStamp.StampForInsert(site);
        try
        {
            return await _context.Session.Connection.ExecuteAsync("""
                INSERT dbo.WarehouseSite(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Code,Name,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive)
                VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@Code,@Name,@SiteType,@SupplierId,@SupplierCodeSnapshot,@SupplierNameSnapshot,@IsActive);
                """, site, _context.Session.Transaction);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            // 数据库唯一约束是并发兜底；0 由 Application 转换为稳定业务冲突。
            return 0;
        }
    }

    public Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion) => _context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.WarehouseSite SET Name=@Name,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
        """, new { site.Uid, site.Name, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);

    public Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion) => _context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.WarehouseSite SET IsActive=0,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
        """, new { site.Uid, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);

    /// <inheritdoc/>
    public async Task<int> ActivateAsync(WarehouseSite site, int expectedVersion)
    {
        try
        {
            return await _context.Session.Connection.ExecuteAsync("""
                UPDATE dbo.WarehouseSite SET IsActive=1,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
                WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
                """, new { site.Uid, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            // 活动仓唯一索引是并发兜底；0 由 Application 转换为稳定业务冲突。
            return 0;
        }
    }

    public Task<int> DeleteAsync(Guid uid, int expectedVersion) => _context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.WarehouseSite SET IsDeleted=1,IsActive=0,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT N'库存余额非零' AS Blocker
            WHERE EXISTS(SELECT 1 FROM dbo.InventoryBalance WITH(UPDLOCK,HOLDLOCK)
                WHERE WarehouseSiteId=@WarehouseSiteId AND IsDeleted=0 AND OnHandQty<>0);
            """;
        var rows = await _context.Session.Connection.QueryAsync<string>(new CommandDefinition(
            sql, new { WarehouseSiteId = warehouseSiteId }, _context.Session.Transaction, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId) => _context.Session.Connection.ExecuteScalarAsync<bool>("""
        SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.InventoryBalance WHERE WarehouseSiteId=@WarehouseSiteId AND ISNULL(IsDeleted,0)=0 AND OnHandQty<>0)
                         OR EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WHERE WarehouseSiteId=@WarehouseSiteId)
                    THEN 1 ELSE 0 END AS bit);
        """, new { WarehouseSiteId = warehouseSiteId }, _context.Session.Transaction);

}