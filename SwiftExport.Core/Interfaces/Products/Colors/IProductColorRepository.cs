using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Products.Colors
{
    public interface IProductColorRepository : IRepository<ProductColor>
    {
        Task<bool> ExistByColorCode(string productColor);
        Task<IReadOnlyList<string>> GetExistingColorCodesAsync(IEnumerable<string> productColorList);
        Task<IReadOnlyList<ProductColor>> GetByColorCodesAsync(IEnumerable<string> colorCodes, bool withUpdLock = false);
        Task<IReadOnlyList<Guid>> GetReferencedColorIdsAsync(IEnumerable<Guid> colorIds);

        Task<PagedResponseBase<ProductColor>> GetProductColorPagedAsync(ProductColorSearchRequest request);



    }
}
