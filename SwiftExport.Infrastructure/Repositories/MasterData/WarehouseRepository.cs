using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.MasterData;

/// <summary>
/// 仓库仓储实现。
/// </summary>
public class WarehouseRepository(RepositoryBaseContext context)
    : DapperRepository<Warehouse>(context), IWarehouseRepository
{
    /// <summary>
    /// 按仓库代码查询仓库。
    /// </summary>
    public async Task<Warehouse?> GetByWhseCodeAsync(string whseCode)
    {
        if (string.IsNullOrWhiteSpace(whseCode)) return null;

        var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK) WHERE [WhseCode] = @WhseCode AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return await Connection.QueryFirstOrDefaultAsync<Warehouse>(
            sql,
            new { WhseCode = whseCode.Trim() },
            transaction: Transaction);
    }
}
