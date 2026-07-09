using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces
{
    public interface ICustomerRepository : IRepository<Customer>
    {
        Task<Customer?> GetByCustomerCodeAsync(string customerCode);
        Task<bool> ExistByCustomerCode(string CustomerCode);
        Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<string> CustomerCodeList);

        Task<PagedResponseBase<CustomerDto>> GetPagedAsync(CustomerSearchRequest request);
        Task<List<string>> GetCustomerCodesUsedByOtherCustomersAsync(IEnumerable<(Guid Uid, string CustomerCode)> items);

    }
}
