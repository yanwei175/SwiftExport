using Dapper;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 采购订单行仓储实现。
/// </summary>
public class PurchaseOrderLineRepository(RepositoryBaseContext context)
    : DapperRepository<PurchaseOrderLine>(context), IPurchaseOrderLineRepository
{
    public async Task<List<PurchaseOrderLineDto>> GetReadOnlyLinesByPurchaseOrderIdAsync(int purchaseOrderId)
    {

        var sql = $"SELECT a.*, b.MainImage FROM [PurchaseOrderLine] a LEFT JOIN [Product] b ON a.Sku = b.Sku WHERE (a.IsDeleted = 0 OR a.IsDeleted IS NULL) AND a.PurchaseOrderId = @PurchaseOrderId";

        var rst = await Connection.QueryAsync<PurchaseOrderLineDto>(sql, new { PurchaseOrderId = purchaseOrderId }, transaction: Transaction);
        return [.. rst];
    }
}
