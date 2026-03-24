using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Customers
{
    public class CustomerRepository(RepositoryBaseContext context) : DapperRepository<Customer>(context), ICustomerRepository
    {

        public async Task<IReadOnlyList<string>> 获取表字段列表Async()
        {
            var sql = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @customer";
            var rst = await Connection.QueryAsync<string>(sql, new { customers = "Customer" }, transaction: Transaction);
            return [.. rst];
        }

        public async Task<bool> ExistByCustomerCode(string customerCode)
        {

            // 1. SQL 修正：查询最小数据（例如数字 1）并使用 = 替代 IN
            var sql = $"SELECT 1 FROM [{TableName}] WHERE CustomerCode = @Code";
            int? result = await Connection.QueryFirstOrDefaultAsync<int?>(sql, new { Code = customerCode }, transaction: Transaction);

            // 4. 逻辑修正：如果 result 有值（即不是 null），则表示存在。
            return result.HasValue;
        }

        public async Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<string> customerCodes)
        {
            var sql = $"SELECT CustomerCode FROM [{TableName}] WHERE  CustomerCode IN @Codes";
            var rst = await Connection.QueryAsync<string>(sql, new { Codes = customerCodes }, transaction: Transaction);
            return [.. rst];
        }



    }
}

