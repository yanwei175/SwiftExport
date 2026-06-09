using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products;

public class ProductCategoryRepository(RepositoryBaseContext context) : DapperRepository<ProductCategory>(context), IProductCategoryRepository
{
    public async Task<IReadOnlyList<ProductCategory>> GetSubById(Guid parentId)
    {
        // 使用 CTE 递归查询包含自身在内的所有子节点,如果要不包含自身,可以把Id = @ParentId改为ParentId = @ParentId
        string sql = $@"
        WITH CategoryTree AS (
            -- 1. 锚点部分：找到直接子节点
            SELECT * 
            FROM [{TableName}] 
            WHERE Uid = @ParentId AND (IsDeleted = 0 OR IsDeleted IS NULL)

            UNION ALL

            -- 2. 递归部分：将当前结果集与原表进行 JOIN
            SELECT c.*
            FROM [{TableName}] c
            INNER JOIN CategoryTree ct ON c.ParentId = ct.Uid
            WHERE (c.IsDeleted = 0 OR c.IsDeleted IS NULL)
        )
        SELECT * FROM CategoryTree;";

        var rst = await Connection.QueryAsync<ProductCategory>(sql, new { ParentId = parentId }, transaction: Transaction);
        return [.. rst];
    }
}
