using Dapper;
using Microsoft.Data.SqlClient;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Contexts;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;

/// <summary>使用共享 DbSession 读取虚拟仓与供应商选择候选。</summary>
public sealed class WarehouseQueryRepository(RepositoryBaseContext context) : IWarehouseQueryRepository
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

    /// <summary>按 SQL Server LIKE ESCAPE 规则转义用户关键词，避免通配符扩大候选范围。</summary>
    private static string EscapeLike(string value) => value
        .Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal)
        .Replace("_", "~_", StringComparison.Ordinal)
        .Replace("[", "~[", StringComparison.Ordinal);
}
