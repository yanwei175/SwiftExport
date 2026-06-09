using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces;

public interface IProductCategoryRepository : IRepository<ProductCategory>
{
    Task<IReadOnlyList<ProductCategory>> GetSubById(Guid parentId);

}
