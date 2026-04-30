using Dapper;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 库存仓储实现。
/// </summary>
public class InventoryRepository(RepositoryBaseContext context)
    : DapperRepository<Inventory>(context), IInventoryRepository
{
    /// <summary>
    /// 按 仓库+库位+SKU 查询库存记录。
    /// </summary>
    public async Task<Inventory?> GetByWhseLocationSkuAsync(string whseCode, string locationCode, string sku)
    {
        if (string.IsNullOrWhiteSpace(whseCode) || string.IsNullOrWhiteSpace(locationCode) || string.IsNullOrWhiteSpace(sku))
            return null;

        var sql = $@"SELECT TOP 1 *
                     FROM [{TableName}] WITH(NOLOCK)
                     WHERE [WhseCode] = @WhseCode
                       AND [LocationCode] = @LocationCode
                       AND [Sku] = @Sku
                       AND (IsDeleted = 0 OR IsDeleted IS NULL)";

        return await Connection.QueryFirstOrDefaultAsync<Inventory>(
            sql,
            new
            {
                WhseCode = whseCode.Trim(),
                LocationCode = locationCode.Trim(),
                Sku = sku.Trim()
            },
            transaction: Transaction);
    }
}
