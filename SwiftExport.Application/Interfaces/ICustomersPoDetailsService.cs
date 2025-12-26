using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface ICustomersPoDetailsService : IBaseService<CustomersPoDetails>
    {
        Task <ServiceResult<CustomersPoDetails>> GetByCustomerCodeWithSupplierCodeAsync(string customerCode, string supplierCode);
        //新增或者修改
        Task<ServiceResult<CustomersPoDetails>> AddOrUpdateAsync(CustomersPoDetails ety);

        //根据customerCode和supplierCode删除
        Task<ServiceResult<bool>> DeleteByCustomerCodeAndSupplierCodeAsync(string customerCode, string supplierCode);

    }
}
