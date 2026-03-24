using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Interfaces
{
    public interface ICustomerService : IBaseService<Customer, CustomerDto>
    {
        Task<bool> ExistByCustomerCode(string customerCode);
        Task<ServiceResult<IReadOnlyList<string>>> GetNonExistingCustomerCodesAsync(IEnumerable<string> customerCodes);
        Task<ServiceResult<CustomerDto>> GetByCustomerCodeAsync(string customerCode);
    }
}
