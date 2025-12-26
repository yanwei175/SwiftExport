using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{
    public interface  ICustomersPoDetailsRepository : IRepository<CustomersPoDetails>
    {
       Task<CustomersPoDetails>  GetByCustomerCodeWithSupplierCodeAsync (string CustomerCode, string SupplierCode);
        Task<int> DeleteByCustomerCodeWithSupplierCodeAsync(string CustomerCode, string SupplierCode, IUnitOfWork uow);
    }
}
