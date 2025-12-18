using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface ICustomersRepository : IRepository<Customers>
    {
        Task<bool> ExistByCustomerCode(string CustomerCode);
        Task<IEnumerable<string>> GetExistingCustomerCodesAsync(IEnumerable<string> CustomerCodes);
        Task<Customers> GetByCustomerCodeAsync(string customerCode);
        Task<IEnumerable<string>> 获取表字段列表Async();
    }
}
