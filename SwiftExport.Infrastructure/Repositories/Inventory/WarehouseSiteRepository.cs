using Dapper;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory;

public sealed class WarehouseSiteRepository(RepositoryBaseContext context) : IWarehouseSiteRepository
{
    private readonly RepositoryBaseContext _context = context;

    public async Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(1, request.PageIndex);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var p = new DynamicParameters(new { request.SupplierId, request.IsActive, Keyword = string.IsNullOrWhiteSpace(request.Keyword) ? null : $"%{request.Keyword.Trim()}%", Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
        const string where = "WHERE ISNULL(w.IsDeleted,0)=0 AND (@SupplierId IS NULL OR w.SupplierId=@SupplierId) AND (@IsActive IS NULL OR w.IsActive=@IsActive) AND (@Keyword IS NULL OR w.WhseCode LIKE @Keyword OR w.WhseName LIKE @Keyword OR s.SupplierCode LIKE @Keyword OR s.SupplierName LIKE @Keyword)";
        var total = await _context.Session.Connection.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM dbo.Warehouse w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0 {where};", p, _context.Session.Transaction);
        var sql = $"""
            SELECT w.Uid,w.Version,w.WhseCode AS Code,w.WhseName AS Name,w.SupplierId,
                   COALESCE(s.SupplierCode,w.SupplierCodeSnapshot) AS SupplierCode,
                   COALESCE(s.SupplierName,w.SupplierNameSnapshot) AS SupplierName,w.IsActive,w.UpdatedDate
            FROM dbo.Warehouse w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0
            {where} ORDER BY w.IsActive DESC,w.WhseCode,w.Uid OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        var rows = await _context.Session.Connection.QueryAsync<WarehouseSiteDto>(new CommandDefinition(sql, p, _context.Session.Transaction, cancellationToken: cancellationToken));
        return new PagedResponseBase<WarehouseSiteDto> { Items = rows.ToList(), TotalCount = total, PageIndex = pageIndex, PageSize = pageSize };
    }

    public Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default)
        => _context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSiteDto>(new CommandDefinition("""
            SELECT w.Uid,w.Version,w.WhseCode AS Code,w.WhseName AS Name,w.SupplierId,
                   COALESCE(s.SupplierCode,w.SupplierCodeSnapshot) AS SupplierCode,COALESCE(s.SupplierName,w.SupplierNameSnapshot) AS SupplierName,w.IsActive,w.UpdatedDate
            FROM dbo.Warehouse w LEFT JOIN dbo.Supplier s ON s.Uid=w.SupplierId AND ISNULL(s.IsDeleted,0)=0
            WHERE w.Uid=@Uid AND ISNULL(w.IsDeleted,0)=0;
            """, new { Uid = uid }, _context.Session.Transaction, cancellationToken: cancellationToken));

    public Task<WarehouseSite?> GetForUpdateAsync(Guid uid)
        => _context.Session.Connection.QuerySingleOrDefaultAsync<WarehouseSite>("""
            SELECT Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,WhseCode AS Code,WhseName AS Name,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive
            FROM dbo.Warehouse WITH(UPDLOCK,HOLDLOCK) WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0;
            """, new { Uid = uid }, _context.Session.Transaction);

    public Task<bool> IsActiveSupplierAsync(Guid supplierId) => _context.Session.Connection.ExecuteScalarAsync<bool>("SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Supplier WHERE Uid=@Uid AND ISNULL(IsDeleted,0)=0) THEN 1 ELSE 0 END AS bit);", new { Uid = supplierId }, _context.Session.Transaction);

    public async Task<int> InsertAsync(WarehouseSite site)
    {
        _context.AuditStamp.StampForInsert(site);
        return await _context.Session.Connection.ExecuteAsync("""
            INSERT dbo.Warehouse(Uid,Version,IsDeleted,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy,WhseCode,WhseName,IsVirtual,SiteType,SupplierId,SupplierCodeSnapshot,SupplierNameSnapshot,IsActive)
            VALUES(@Uid,@Version,@IsDeleted,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy,@Code,@Name,1,@SiteType,@SupplierId,@SupplierCodeSnapshot,@SupplierNameSnapshot,@IsActive);
            """, site, _context.Session.Transaction);
    }

    public Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion) => _context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.Warehouse SET WhseName=@Name,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
        """, new { site.Uid, site.Name, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);

    public Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion) => _context.Session.Connection.ExecuteAsync("""
        UPDATE dbo.Warehouse SET IsActive=0,Version=Version+1,UpdatedDate=@Now,UpdatedBy=@UserName
        WHERE Uid=@Uid AND Version=@ExpectedVersion AND ISNULL(IsDeleted,0)=0;
        """, new { site.Uid, ExpectedVersion = expectedVersion, Now = DateTime.Now, UserName = _context.CurrentUser.UserName }, _context.Session.Transaction);

    public Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId) => _context.Session.Connection.ExecuteScalarAsync<bool>("""
        SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Inventory WHERE WarehouseId=@WarehouseSiteId AND ISNULL(IsDeleted,0)=0 AND OnhandQty<>0)
                         OR EXISTS(SELECT 1 FROM dbo.InventoryTransaction WHERE WarehouseId=@WarehouseSiteId AND ISNULL(IsDeleted,0)=0)
                    THEN 1 ELSE 0 END AS bit);
        """, new { WarehouseSiteId = warehouseSiteId }, _context.Session.Transaction);
}
