using Dapper;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 采购订单头仓储实现。
/// </summary>
public class PurchaseOrderRepository(RepositoryBaseContext context)
    : DapperRepository<PurchaseOrderHead>(context), IPurchaseOrderRepository
{
    public async Task<PurchaseOrderHead?> GetByBoNoAsync(string boNo, bool withUpdLock = false)
    {
        if (string.IsNullOrWhiteSpace(boNo)) throw new ArgumentNullException(nameof(boNo));
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";

        var sql = $"SELECT * FROM [PurchaseOrderHead] {lockHint} WHERE BoNo = @BoNo AND (IsDelete = 0 OR IsDelete IS NULL)";

        return await Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(sql, new { BoNo = boNo }, transaction: Transaction);
    }
    public async Task<PurchaseOrderHead?> GetByOrderNoAsync(string orderNo, bool withUpdLock = false)
    {
        if (string.IsNullOrWhiteSpace(orderNo)) throw new ArgumentNullException(nameof(orderNo));
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";

        var sql = $"SELECT * FROM [PurchaseOrderHead] {lockHint} WHERE OrderNo = @OrderNo AND (IsDeleted = 0 OR IsDeleted IS NULL)";

        return await Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(sql, new { OrderNo = orderNo }, transaction: Transaction);
    }


}
