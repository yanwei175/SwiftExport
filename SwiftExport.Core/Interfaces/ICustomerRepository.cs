using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces
{
    public interface ICustomerRepository : IRepository<Customer>
    {
        Task<bool> ExistByCustomerCode(string CustomerCode);
        Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<string> CustomerCodeList);
        Task<IReadOnlyList<string>> 获取表字段列表Async();
    }
}
