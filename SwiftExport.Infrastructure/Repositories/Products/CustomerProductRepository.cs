using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories.Products;

public partial class CustomerProductRepository(RepositoryBaseContext context) : DapperRepository<CustomerProduct>(context), ICustomerProductRepository
{
    public async Task<IReadOnlyList<int>> GetAllCustomerIdListAsync()
    {
        var sql = $"SELECT CustomerId FROM [{TableName}] WHERE CustomerId IS NOT NULL AND IsDeleted = 0 GROUP BY CustomerId";
        var rst = await Connection.QueryAsync<int>(sql, transaction: Transaction);
        return [.. rst];
    }

    public async Task<CustomerProduct?> GetByCustomerSkuAsync(string customerSku,int customerId)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [CustomerSku] = @Sku AND [CustomerId] = @CustomerId AND IsDeleted = 0";
        return await Connection.QueryFirstOrDefaultAsync<CustomerProduct>(sql, new { Sku = customerSku, CustomerId = customerId }, transaction: Transaction);
    }

    public async Task<IReadOnlyList<string>> GetExistingCustomerSkuListAsync(IEnumerable<string> skuList,int customerId)
    {
        var sql = $"SELECT CustomerSku FROM [{TableName}] WHERE  CustomerSku IN @SkuList AND CustomerId = @CustomerId AND IsDeleted = 0";
        var rst = await Connection.QueryAsync<string>(sql, new { SkuList = skuList, CustomerId = customerId }, transaction: Transaction);
        return [.. rst];
    }



    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9_,\s\.\[\]]")]
    private static partial System.Text.RegularExpressions.Regex OrderByRegex();


}
