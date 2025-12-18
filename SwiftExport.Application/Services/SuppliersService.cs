using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class SuppliersService : BaseService<Suppliers>, ISuppliersService
    {
        //private readonly IRepository _repo;
        public SuppliersService(IRepository<Suppliers> repo, IUintOfWorkFactory uowFac) : base(repo, uowFac)
        {
        }
        

        public Task<Result<CUDResult>> BeachCUDBy_UQ_SuppCodeAsync(IEnumerable<Suppliers> createList, IEnumerable<Suppliers> updateList, IEnumerable<Suppliers> deleteList)
        {
            throw new NotImplementedException();
        }

        public Task<bool> ExistByCustomerCode(string suppCode)
        {
            throw new NotImplementedException();
        }

        public Task<Result<Suppliers>> GetBySupplierCodeAsync(string suppCode)
        {
            throw new NotImplementedException();
        }

        public Task<Result<IReadOnlyList<string>>> GetNonExistingSupplierCodesAsync(IEnumerable<string> suppCodes)
        {
            throw new NotImplementedException();
        }
    }
}
