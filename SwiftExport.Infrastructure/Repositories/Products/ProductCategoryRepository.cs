using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products;

public class ProductCategoryRepository(RepositoryBaseContext context) : DapperRepository<ProductCategory>(context), IProductCategoryRepository
{
    public async Task<IReadOnlyList<ProductCategory>> GetSubById(int parentId)
    {
        var sql = $"SELECT * FROM [{TableName}] WHERE ParentId = @ParentId AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<ProductCategory>(sql, new { ParentId = parentId }, transaction: Transaction);
        return [.. rst];
    }
}
