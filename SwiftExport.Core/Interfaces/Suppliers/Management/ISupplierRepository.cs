using SwiftExport.Core.Interfaces.Suppliers.Management;
using SwiftExport.Core.Entities.MasterData;

using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.Suppliers.Management
{
    /// <summary>提供供应商资料读取、管理投影及编码占用校验。</summary>
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

        /// <summary>读取与管理分页相同投影和可见范围的全部供应商，保留并发版本并支持取消。</summary>
        Task<IReadOnlyList<SupplierDto>> GetAllForManagementAsync(CancellationToken cancellationToken = default);

    }
}
