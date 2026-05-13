using Dapper;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

/// <summary>
/// 采购入库头仓储实现。
/// </summary>
public class ReceiveHeadRepository(RepositoryBaseContext context)
    : DapperRepository<ReceiveHead>(context), IReceiveHeadRepository
{
    /// <summary>
    /// 按入库单号查询。
    /// </summary>
    public async Task<ReceiveHead?> GetByReceiveNoAsync(string receiveNo)
    {
        if (string.IsNullOrWhiteSpace(receiveNo)) return null;

        var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK) WHERE [ReceiveNo] = @ReceiveNo AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return await Connection.QueryFirstOrDefaultAsync<ReceiveHead>(
            sql,
            new { ReceiveNo = receiveNo.Trim() },
            transaction: Transaction);
    }
}
