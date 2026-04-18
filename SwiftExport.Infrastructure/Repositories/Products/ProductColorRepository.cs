using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Products
{
    public class ProductColorRepository(RepositoryBaseContext context) : DapperRepository<ProductColor>(context), IProductColorRepository
    {
        public async Task<bool> ExistByColorCode(string productColor)
        {
            // 1. SQL 修正：查询最小数据（例如数字 1）并使用 = 替代 IN
            var sql = $"SELECT 1 FROM [{TableName}] WHERE ColorCodeUnique = @Code";
            int? result = await Connection.QueryFirstOrDefaultAsync<int?>(sql, new { Code = productColor }, transaction: Transaction);

            // 4. 逻辑修正：如果 result 有值（即不是 null），则表示存在。
            return result.HasValue;
        }

        public async Task<IReadOnlyList<string>> GetExistingColorCodesAsync(IEnumerable<string> productColorList)
        {
            var sql = $"SELECT ColorCodeUnique FROM [{TableName}] WHERE  ColorCodeUnique IN @Codes";
            var rst = await Connection.QueryAsync<string>(sql, new { Codes = productColorList }, transaction: Transaction);
            return [.. rst];
        }
    }
}
