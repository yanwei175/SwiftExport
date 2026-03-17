using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Suppliers
{
    public class SupplierRepository(DbSession _session, ILogger<SupplierRepository> logger) : DapperRepository<Supplier>(_session, logger), ISupplierRepository
    {
        public async Task<bool> ExistBySupplierCode(string supplierCode)
        {
            // 查询最小数据（例如数字 1）并使用 = 替代 IN
            var sql = $"SELECT 1 FROM [{TableName}] WHERE SupplierCode = @Code";
            int? result = await Connection.QueryFirstOrDefaultAsync<int?>(sql, new { Code = supplierCode }, transaction: Transaction);

            // 如果 result 有值（即不是 null），则表示存在。
            return result.HasValue;
        }

        public async Task<IReadOnlyList<string>> GetExistingSupplierCodesAsync(IEnumerable<string> supplierCodeList)
        {
            var sql = $"SELECT SupplierCode FROM [{TableName}] WHERE  SupplierCode IN @Codes";
            var rst = await Connection.QueryAsync<string>(sql, new { Codes = supplierCodeList }, transaction: Transaction);
            return [.. rst];
        }


    }
}
