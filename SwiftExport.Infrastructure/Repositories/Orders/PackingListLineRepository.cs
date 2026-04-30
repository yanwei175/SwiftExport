using Dapper;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public class PackingListLineRepository(RepositoryBaseContext context)
    : DapperRepository<PackingListLine>(context), IPackingListLineRepository
{
    public async Task<IReadOnlyList<PackingListLine>> GetByPackingListNoAsync(string packingListNo)
    {
        if (string.IsNullOrWhiteSpace(packingListNo)) return [];

        var sql = $@"SELECT *
                     FROM [{TableName}] WITH(NOLOCK)
                     WHERE [PackingListNo] = @PackingListNo
                       AND (IsDeleted = 0 OR IsDeleted IS NULL)
                     ORDER BY [Id]";

        var result = await Connection.QueryAsync<PackingListLine>(
            sql,
            new { PackingListNo = packingListNo.Trim() },
            transaction: Transaction);

        return [.. result];
    }
}
