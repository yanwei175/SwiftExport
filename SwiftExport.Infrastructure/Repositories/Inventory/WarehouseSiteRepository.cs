using Dapper;
using Microsoft.Data.SqlClient;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

/// <summary>使用共享 DbSession 持久化虚拟仓，并提供事务锁、唯一性预检和完整删除引用检查。</summary>
public sealed class WarehouseSiteRepository(RepositoryBaseContext context) : IWarehouseSiteRepository
{
    private readonly RepositoryBaseContext _context = context;

    /// <inheritdoc/>
    public async Task<PagedResponseBase<WarehouseSitePickerDto>> GetPickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{EscapeLike(request.Keyword.Trim())}%";
        var parameters = new { Keyword = keyword, Offset = (pageIndex - 1) * pageSize, PageSize = pageSize };
        const string filter = "WHERE ISNULL(w.IsDeleted,0)=0 AND w.IsActive=1 AND (@Keyword IS NULL OR w.Code LIKE @Keyword ESCAPE N'~' OR w.Name LIKE @Keyword ESCAPE N'~' OR COALESCE(s.SupplierCode,w.SupplierCodeSnapshot,N'') LIKE @Keyword ESCAPE N'~' OR COALESCE(s.SupplierName,w.SupplierNameSnapshot,N'') LIKE @Keyword ESCAPE N'~')";
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM dbo.WarehouseSite w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0 {filter};",
            parameters, _context.Session.Transaction, cancellationToken: cancellationToken));
        var rows = await _context.Session.Connection.QueryAsync<WarehouseSitePickerDto>(new CommandDefinition($"""
            SELECT w.Uid,w.Version,w.Code,w.Name,w.SupplierId,
                   COALESCE(s.SupplierCode,w.SupplierCodeSnapshot,N'') AS SupplierCode,
                   COALESCE(s.SupplierName,w.SupplierNameSnapshot,N'') AS SupplierName,w.SiteType,w.IsActive
            FROM dbo.WarehouseSite w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0
            {filter} ORDER BY w.Code,w.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, parameters, _context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<WarehouseSitePickerDto>
        {
            Items = rows.ToList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize
        };
    }

    /// <inheritdoc/>
    public async Task<PagedResponseBase<WarehouseSupplierLookupDto>> SearchSupplierLookupAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 100);
        var keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{EscapeLike(request.Keyword.Trim())}%";
        const string filter = "WHERE ISNULL(s.IsDeleted,0)=0 AND s.IsActive=1 AND (@Keyword IS NULL OR s.SupplierCode LIKE @Keyword ESCAPE N'~' OR s.SupplierName LIKE @Keyword ESCAPE N'~')";
        var parameters = new { Keyword = keyword, Offset = (pageIndex - 1) * pageSize, PageSize = pageSize };
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM dbo.Supplier s {filter};", parameters, _context.Session.Transaction, cancellationToken: cancellationToken));
        var rows = await _context.Session.Connection.QueryAsync<WarehouseSupplierLookupDto>(new CommandDefinition($"""
            SELECT s.Uid,s.SupplierCode,s.SupplierName
            FROM dbo.Supplier s {filter}
            ORDER BY s.SupplierCode,s.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """, parameters, _context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<WarehouseSupplierLookupDto>
        {
            Items = rows.ToList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize
        };
    }

    public async Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var p = new DynamicParameters(new { request.SupplierId, request.IsActive, Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{EscapeLike(request.Keyword.Trim())}%", Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
        const string where = "WHERE ISNULL(w.IsDeleted,0)=0 AND (@SupplierId IS NULL OR w.SupplierId=@SupplierId) AND (@IsActive IS NULL OR w.IsActive=@IsActive) AND (@Keyword IS NULL OR w.Code LIKE @Keyword ESCAPE N'~' OR w.Name LIKE @Keyword ESCAPE N'~' OR s.SupplierCode LIKE @Keyword ESCAPE N'~' OR s.SupplierName LIKE @Keyword ESCAPE N'~')";
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM dbo.WarehouseSite w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0 {where};",
            p, _context.Session.Transaction, cancellationToken: cancellationToken));
        var sql = $"""
            SELECT w.Uid,w.Version,w.Code,w.Name,w.SupplierId,
                   COALESCE(s.SupplierCode,w.SupplierCodeSnapshot) AS SupplierCode,
                   COALESCE(s.SupplierName,w.SupplierNameSnapshot) AS SupplierName,w.IsActive,w.UpdatedDate
            FROM dbo.WarehouseSite w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0
            {where} ORDER BY w.IsActive DESC,w.Code,w.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        var rows = await _context.Session.Connection.QueryAsync<WarehouseSiteDto>(new CommandDefinition(sql, p, _context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<WarehouseSiteDto> { Items = rows.ToList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    public Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default)
        => _context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSiteDto>(new CommandDefinition("""
            SELECT w.Uid,w.Version,w.Code,w.Name,w.SupplierId,
                   COALESCE(s.SupplierCode,w.SupplierCodeSnapshot) AS SupplierCode,COALESCE(s.SupplierName,w.SupplierNameSnapshot) AS SupplierName,w.IsActive,w.UpdatedDate
            FROM dbo.WarehouseSite w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0
            WHERE w.Uid=@Uid AND ISNULL(w.IsDeleted,0)=0;
            """, new { Uid = uid }, _context.Session.Transaction, cancellationToken: cancellationToken));

    public Task<WarehouseSite?> GetForUpdateAsync(Guid uid)
        => _context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSite>("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,Code,Name,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive
            FROM dbo.WarehouseSite WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid }, _context.Session.Transaction);

    /// <inheritdoc/>
    public Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default)
        => _context.Session.Connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
            SELECT @Result;
            """, new { Resource = $"SwiftExport:Inventory:Warehouse:{commandId:N}" }, _context.Session.Transaction, cancellationToken: cancellationToken));

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
    public Task<int> DeleteAsync(Guid uid, int expectedVersion) => _context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.WarehouseSite SET IsDeleted=1,IsActive=0,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
        """, new { Uid = uid, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT Blocker FROM (
                SELECT N'库存余额' Blocker WHERE EXISTS(SELECT 1 FROM dbo.InventoryBalance WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseSiteId=@WarehouseSiteId)
                UNION ALL SELECT N'库存流水' WHERE EXISTS(SELECT 1 FROM dbo.InventoryLedgerEntry WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseSiteId=@WarehouseSiteId)
                UNION ALL SELECT N'采购订单行' WHERE EXISTS(SELECT 1 FROM dbo.PurchaseOrderLine WITH(UPDLOCK,HOLDLOCK) WHERE DeliveryWarehouseSiteId=@WarehouseSiteId)
                UNION ALL SELECT N'采购确认节点' WHERE EXISTS(SELECT 1 FROM dbo.PurchaseConfirmationNode WITH(UPDLOCK,HOLDLOCK) WHERE ConsumptionWarehouseSiteId=@WarehouseSiteId)
                UNION ALL SELECT N'正式入库单' WHERE EXISTS(SELECT 1 FROM dbo.InboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseSiteId=@WarehouseSiteId)
                UNION ALL SELECT N'正式出库单' WHERE EXISTS(SELECT 1 FROM dbo.OutboundDocument WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseSiteId=@WarehouseSiteId)
                UNION ALL SELECT N'PackingList明细' WHERE EXISTS(SELECT 1 FROM dbo.PackingListLine WITH(UPDLOCK,HOLDLOCK) WHERE WarehouseSiteId=@WarehouseSiteId)
            ) b;
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

    /// <summary>按 SQL Server LIKE ESCAPE 规则转义用户关键词，避免通配符扩大候选范围。</summary>
    private static string EscapeLike(string value) => value
        .Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal)
        .Replace("_", "~_", StringComparison.Ordinal)
        .Replace("[", "~[", StringComparison.Ordinal);
}
