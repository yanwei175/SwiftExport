using Dapper;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Orders;

public class PackingListHeadRepository(RepositoryBaseContext context)
    : DapperRepository<PackingListHead>(context), IPackingListHeadRepository
{
    public async Task<PackingListHead?> GetByPackingListNoAsync(string packingListNo)
    {
        if (string.IsNullOrWhiteSpace(packingListNo)) return null;

        var sql = $@"SELECT TOP 1 *
                     FROM [{TableName}] WITH(NOLOCK)
                     WHERE [PackingListNo] = @PackingListNo
                       AND (IsDeleted = 0 OR IsDeleted IS NULL)";

        return await Connection.QueryFirstOrDefaultAsync<PackingListHead>(
            sql,
            new { PackingListNo = packingListNo.Trim() },
            transaction: Transaction);
    }

    public Task<PackingListDetailDto?> GetDetailByHeadIdAsync(Guid headId)
    {
        throw new NotImplementedException();
    }
}
