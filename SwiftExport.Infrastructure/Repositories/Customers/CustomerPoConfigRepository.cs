using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Customers
{
    public class CustomerPoConfigRepository(DbSession _session, ILogger<CustomerPoConfigRepository> logger) : DapperRepository<CustomerPoConfig>(_session, logger), ICustomerPoConfigRepository
    {
        public async Task<IReadOnlyList<PoConfigCheck>> GetNonUniqueByPoConfigCheckList(IEnumerable<PoConfigCheck> poConfigCheckList)
        {
            // 防御性编程：如果没有数据，直接返回
            if (poConfigCheckList == null || !poConfigCheckList.Any())
            {
                return [];
            }
            var customerCodes = poConfigCheckList.Select(x => x.CustomerCode).Distinct().ToArray();
            var supplierCodes = poConfigCheckList.Select(x => x.SupplierCode).Distinct().ToArray();
            var sql = $"SELECT CustomerCode, SupplierCode FROM [{TableName}] WHERE  CustomerCode IN @CustomerCodes AND SupplierCode IN @SupplierCodes";
            //var rst = await Connection.QueryAsync<PoConfigCheck>(sql, new { Codes = customerCodes }, transaction: Transaction);
            //return [.. rst];

            var potentialConflicts = await Connection.QueryAsync<PoConfigCheck>(sql, new { CustomerCodes = customerCodes, SupplierCodes = supplierCodes }, transaction: Transaction);

            var conflictList = potentialConflicts.AsList(); // 转换为 List 避免多次枚举
            if (conflictList.Count == 0)
            {
                // 连嫌疑犯都没有，绝对安全
                return [];
            }

            // ==========================================
            // 第四步：内存极速精准锁定（O(1) 哈希表匹配）
            // ==========================================
            var inputSet = new HashSet<string>(
                poConfigCheckList.Select(x => $"{x.CustomerCode}|{x.SupplierCode}")
            );

            var actualDuplicates = conflictList
                .Where(x => inputSet.Contains($"{x.CustomerCode}|{x.SupplierCode}"))
                .ToList();

            return actualDuplicates;

        }
    }
}

