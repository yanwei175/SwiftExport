using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Core.Interfaces;

public interface ISupplierProductRepository : IRepository<SupplierProduct>
{
    Task<SupplierProductDto?> GetDtoByIdAsync(Guid uid);
    Task<PagedResponseBase<SupplierProductDto>> GetPagedAsync(SupplierProductSearchRequest request);
    Task<IReadOnlyList<string>> GetExistingSupplierSkuListAsync(IEnumerable<string> supplierSkus, Guid supplierId);
    Task<IReadOnlyList<string>> GetSupplierSkusUsedByOtherRecordsAsync(
        Guid supplierId,
        IEnumerable<(Guid Uid, string SupplierSku)> items);
    Task<IReadOnlyList<Guid>> GetSupplierIdsWithProductsAsync(IEnumerable<Guid> supplierIds);
}
