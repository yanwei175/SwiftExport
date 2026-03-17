using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class CustomerProductRepository : DapperRepository<CustomerProducts>, ICustomerProductRepository
    {

        private readonly IDataTableFactory<CustomerProducts> _fac;

        public CustomerProductRepository( IMappingCache cache, IDbConnectionFactory _connFac, IDataTableFactory<CustomerProducts> fac) : base( cache, _connFac)
        {
            _fac = fac;
        }

        public async Task<int> SyncCustomerProductsAsync(IEnumerable<CustomerProducts> products,IUnitOfWork uow)
        {
            var dt = await _fac.CreatDtByEntityPropertysAsync(products);
                return await uow.Connection.ExecuteAsync(
                    "SyncCustomerProducts",
                    new { Products = dt.AsTableValuedParameter("CustomerProductsType") },
                    transaction: uow.Transaction,
                    commandType: CommandType.StoredProcedure
                );
        }

    }
}
