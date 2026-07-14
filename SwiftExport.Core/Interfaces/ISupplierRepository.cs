using SwiftExport.Core.Entities.MasterData;

using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces
{
    public interface ISupplierRepository : IRepository<Supplier>
    {
        Task<bool> ExistBySupplierCode(string SupplierCode);
        /// <summary>
        /// 按供应商编码查询单条供应商档案。
        /// </summary>
        Task<Supplier?> GetByCodeAsync(string supplierCode);
        Task<IReadOnlyList<string>> GetExistingSupplierCodesAsync(IEnumerable<string> SupplierCodeList);
        Task<IReadOnlyList<string>> GetSupplierCodesUsedByOtherSuppliersAsync(
            IEnumerable<(Guid Uid, string SupplierCode)> items);
        /// <summary>
        /// 按供应商编码列表批量查询供应商。
        /// </summary>
        Task<IReadOnlyList<Supplier>> GetBySupplierCodeListAsync(IEnumerable<string> supplierCodeList);
        Task<PagedResponseBase<SupplierDto>> GetPagedAsync(SupplierSearchRequest request);

    }
}
