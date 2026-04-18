using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products;

public class ProductCategoryRepository(RepositoryBaseContext context) : DapperRepository<ProductCategory>(context), IProductCategoryRepository
{

}
