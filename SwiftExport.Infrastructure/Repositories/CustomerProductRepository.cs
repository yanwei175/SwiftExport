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
        private readonly IDbConnectionFactory _connFactory;
        private readonly IMappingCache _cache;
        private readonly string tableName;
        private readonly IDataTableFactory<CustomerProducts> _fac;

        public CustomerProductRepository(IDbConnectionFactory connFactory, IMappingCache cache,
            IDataTableFactory<CustomerProducts> fac) : base(connFactory, cache)
        {
            _connFactory = connFactory;
            _cache = cache;
            tableName = _cache.GetTableNameByEntity<CustomerProducts>() ?? typeof(CustomerProducts).Name;
            _fac = fac;
        }



        public async Task<int> 同步产品(IEnumerable<CustomerProducts> products)
        {
            var dt = await _fac.CreatDtByEntityPropertysAsync(products);
            using (var conn = _connFactory.CreateConnection())
            {
                return await conn.ExecuteAsync(
                    "SyncCustomerProducts",
                    new { Products = dt.AsTableValuedParameter("CustomerProductsType") },
                    commandType: CommandType.StoredProcedure
                );
            }
        }

    }
}
