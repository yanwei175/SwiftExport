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
        Task<Result<IReadOnlyList<string>>> GetNonExistingCustomerCodesAsync(IEnumerable<string> customerCodes);
        Task<Result<Customers>> GetByCustomerCodeAsync(string customerCode);
        Task<Result<CUDResult>> 批量操作客户Async(IEnumerable<Customers> createList,
            IEnumerable<Customers> updateList, IEnumerable<Customers> deleteList);
    }
}
