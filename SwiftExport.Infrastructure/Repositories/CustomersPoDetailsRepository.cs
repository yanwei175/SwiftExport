using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class CustomersPoDetailsRepository : DapperRepository<CustomersPoDetails>, ICustomersPoDetailsRepository
    {
        private readonly IDataTableFactory<CustomersPoDetails> _fac;
        public CustomersPoDetailsRepository(IMappingCache cache,IDbConnectionFactory _connFac, IDataTableFactory<CustomersPoDetails> fac) : base(cache, _connFac)
        {
            _fac = fac;
        }

        public async Task<int> DeleteByCustomerCodeWithSupplierCodeAsync(string CustomerCode, string SupplierCode, IUnitOfWork uow)
        {
            var sql = $"DELETE FROM [{tableName}] Where CustomerCode = @customerCode AND SupplierCode = @ supplierCode";
            return await uow.Connection.ExecuteAsync(sql, new { customerCode = CustomerCode, supplierCode = SupplierCode }, transaction: uow.Transaction);
        }

        public async Task<CustomersPoDetails> GetByCustomerCodeWithSupplierCodeAsync(string CustomerCode, string SupplierCode)
        {
            var sql = $"SELECT * FROM  [{tableName}] Where IsDelete<>1 AND CustomerCode = @customerCode AND SupplierCode = @ supplierCode" ;
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryFirstOrDefaultAsync<CustomersPoDetails>(sql, new { customerCode= CustomerCode, supplierCode = SupplierCode });
        }
    }
 }

