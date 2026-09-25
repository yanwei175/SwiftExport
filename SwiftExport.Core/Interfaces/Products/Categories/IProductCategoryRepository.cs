using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Products.Categories;

public interface IProductCategoryRepository : IRepository<ProductCategory>
{
    Task<IReadOnlyList<ProductCategory>> GetSubById(Guid parentId);
    Task<IReadOnlyList<ProductCategory>> GetAllForUpdateAsync();
    Task<IReadOnlyList<Guid>> GetReferencedCategoryIdsAsync(IEnumerable<Guid> categoryIds);
    Task<PagedResponseBase<ProductCategory>> GetPagedAsync(ProductCategorySearchRequest request);
}
