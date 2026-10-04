using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products.Catalog;

/// <summary>产品 PostgreSQL 仓储实现，复用通用 CRUD 和外层事务；运行注册仍由完整迁移验收决定。</summary>
public partial class PostgreSqlProductRepository(RepositoryBaseContext context) : PostgreSqlDapperRepository<Product>(context), IProductRepository
{
    /// <summary>普通资料更新不取得启停权限，也不写入已废弃的分类占位字段。</summary>
    protected override IReadOnlyList<string> UpdateProperties => [.. base.UpdateProperties
        .Where(property => !property.Equals(nameof(Product.IsActive), StringComparison.OrdinalIgnoreCase)
                           && !property.Equals("CategoryId", StringComparison.OrdinalIgnoreCase))];
}
