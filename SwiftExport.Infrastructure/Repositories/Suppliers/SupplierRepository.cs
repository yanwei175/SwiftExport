using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Suppliers
{
    public class SupplierRepository(RepositoryBaseContext context) : DapperRepository<Supplier>(context), ISupplierRepository
    {
        /// <summary>
        /// 按供应商编码查询单条供应商档案。
        /// </summary>
        public async Task<Supplier?> GetByCodeAsync(string supplierCode)
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                return null;

            var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK) WHERE SupplierCode = @Code AND (IsDeleted = 0 OR IsDeleted IS NULL)";
            return await Connection.QueryFirstOrDefaultAsync<Supplier>(sql, new { Code = supplierCode.Trim() }, transaction: Transaction);
        }

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

        /// <summary>
        /// 按供应商编码列表批量查询供应商。
        /// </summary>
        public async Task<IReadOnlyList<Supplier>> GetBySupplierCodeListAsync(IEnumerable<string> supplierCodeList)
        {
            if (supplierCodeList == null) return [];

            var normalized = supplierCodeList
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0) return [];

            var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE SupplierCode IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)";
            var rst = await Connection.QueryAsync<Supplier>(sql, new { Codes = normalized }, transaction: Transaction);
            return [.. rst];
        }


    }
}
