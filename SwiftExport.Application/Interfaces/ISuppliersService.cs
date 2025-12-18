using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface ISuppliersService : IBaseService<Suppliers>
    {
        Task<bool> ExistByCustomerCode(string suppCode);
        Task<Result<IReadOnlyList<string>>> GetNonExistingSupplierCodesAsync(IEnumerable<string> suppCodes);
        Task<Result<Suppliers>> GetBySupplierCodeAsync(string suppCode);
        Task<Result<CUDResult>> BeachCUDBy_UQ_SuppCodeAsync(IEnumerable<Suppliers> createList,
            IEnumerable<Suppliers> updateList, IEnumerable<Suppliers> deleteList);
    }
}
