using SwiftExport.Core.Entities.MasterData;

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
        /// <summary>
        /// 按供应商编码列表批量查询供应商。
        /// </summary>
        Task<IReadOnlyList<Supplier>> GetBySupplierCodeListAsync(IEnumerable<string> supplierCodeList);

    }
}
