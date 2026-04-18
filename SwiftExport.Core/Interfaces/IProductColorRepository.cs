using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces
{
    public interface IProductColorRepository : IRepository<ProductColor>
    {
        Task<bool> ExistByColorCode(string productColor);
        Task<IReadOnlyList<string>> GetExistingColorCodesAsync(IEnumerable<string> productColorList);
    }
}
