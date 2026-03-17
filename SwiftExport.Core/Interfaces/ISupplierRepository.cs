using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces
{
    public interface ISupplierRepository : IRepository<Supplier>
    {
        Task<bool> ExistBySupplierCode(string SupplierCode);
        Task<IReadOnlyList<string>> GetExistingSupplierCodesAsync(IEnumerable<string> SupplierCodeList);

    }
}
