using Dapper;
using {{ProjectName}}.Core.Entities.Sample;
using {{ProjectName}}.Core.Interfaces;
using {{ProjectName}}.Infrastructure.Contexts;
using {{ProjectName}}.Infrastructure.Repositories.Base;

namespace {{ProjectName}}.Infrastructure.Repositories.Sample;

public class SampleItemRepository(RepositoryBaseContext context)
    : DapperRepository<SampleItem>(context), ISampleItemRepository
{
    public Task<SampleItem?> GetByCodeAsync(string code)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [Code] = @Code AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return Connection.QueryFirstOrDefaultAsync<SampleItem>(sql, new { Code = code }, Transaction);
    }
}
