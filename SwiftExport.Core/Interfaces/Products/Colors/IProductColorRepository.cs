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
        /// <summary>查询有效产品或中性产品引用的颜色；调用方须在同一事务先锁待删除颜色父行。</summary>
        Task<IReadOnlyList<Guid>> GetReferencedColorIdsAsync(IEnumerable<Guid> colorIds);

        Task<PagedResponseBase<ProductColor>> GetProductColorPagedAsync(ProductColorSearchRequest request);



    }
}
