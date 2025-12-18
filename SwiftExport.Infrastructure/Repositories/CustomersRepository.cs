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
    public class CustomersRepository : DapperRepository<Customers>, ICustomersRepository
    {
        private readonly IDataTableFactory<Customers> _fac;
        public CustomersRepository(IMappingCache cache,IDbConnectionFactory _connFac, IDataTableFactory<Customers> fac) : base(cache, _connFac)
        {
            _fac = fac;
        }

        public async Task<IEnumerable<string>> 获取表字段列表Async()
        {
            var sql = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @customers";
            using (var _connection = _connFac.CreateConnection())
             return await _connection.QueryAsync<string>(sql, new { customers = "Customers" });
        }

        public async Task<Customers> GetByCustomerCodeAsync(string customerCode)
        {
            var sql = $"SELECT * FROM  [{tableName}] Where IsDelete<>1 AND CustomerCode = @customerCode";
            using (var _connection = _connFac.CreateConnection())
                return await  _connection.QueryFirstOrDefaultAsync<Customers>(sql, new { customerCode });
        }

        public async Task<bool> ExistByCustomerCode(string customerCode)
        {
            // 确保方法是 async
            using (var connection = _connFac.CreateConnection())
            {
                // 1. SQL 修正：查询最小数据（例如数字 1）并使用 = 替代 IN
                var sql = $"SELECT 1 FROM [{tableName}] WHERE CustomerCode = @Code";

                // 2. Dapper 查询：返回 int?。如果存在，返回 1；如果不存在，返回 null。
                // Dapper 自动处理 connection.Open()
                int? result = await connection.QueryFirstOrDefaultAsync<int?>(
                    sql,
                    new { Code = customerCode } // 3. 参数名修正为 @Code
                );

                // 4. 逻辑修正：如果 result 有值（即不是 null），则表示存在。
                return result.HasValue;
            }
        }

        public async Task<IEnumerable<string>> GetExistingCustomerCodesAsync(IEnumerable<string> customerCodes)
        {
            using (var connection = _connFac.CreateConnection())
            {
                // SQL 修正：仅查询 CustomerCode 字段，并使用 IN 语句
                var sql = $"SELECT CustomerCode FROM [{tableName}] WHERE  CustomerCode IN @Codes";

                // Dapper 优势：QueryAsync 自动将 IEnumerable<string> 映射到 IN 语句
                return await connection.QueryAsync<string>(
                    sql,
                    new { Codes = customerCodes } // 将参数名与 SQL 中的 @Codes 匹配
                );
            }
        }



    }
 }

