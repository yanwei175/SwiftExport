using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface ICustomersService : IBaseService<Customers>
    {
        Task<bool> ExistByCustomerCode(string customerCode);
        Task<ServiceResult<IReadOnlyList<string>>> GetNonExistingCustomerCodesAsync(IEnumerable<string> customerCodes);
        Task<ServiceResult<Customers>> GetByCustomerCodeAsync(string customerCode);
        Task<ServiceResult<CUDResult>> BatchCUDBy_UQ_CustomersCodeAsync(IEnumerable<Customers> createList,
            IEnumerable<Customers> updateList, IEnumerable<Customers> deleteList);
    }
}
