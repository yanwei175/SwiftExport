using Dapper;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Entities.OtherEntities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class ProductRepository : DapperRepository<Product>, IProductRepository
    {
        private readonly IDataTableFactory<Product> _dataTableFac;

        public ProductRepository(IDbConnectionFactory connFac, IMappingCache cache, IDataTableFactory<Product> dataTableFac)
            : base(cache, connFac)
        {
            _dataTableFac = dataTableFac;
        }

        // ---------------------------------------------------------
        // Delete / Update Operations
        // ---------------------------------------------------------

        /// <summary>
        /// Physically delete a product by SKU (Use with caution)
        /// </summary>
        public async Task<int> DeleteBySkuAsync(string sku, IUnitOfWork uow)
        {
            var sql = $"DELETE FROM [{tableName}] WHERE [SKU] = @sku";
            return await uow.Connection.ExecuteAsync(sql, new { sku }, transaction: uow.Transaction);
        }

        /// <summary>
        /// Soft delete a product by SKU (Sets IsDelete = 1)
        /// </summary>
        public async Task<int> DisableBySkuAsync(string sku, IUnitOfWork uow)
        {
            var sql = $"UPDATE [{tableName}] SET IsDelete=1 WHERE [SKU] = @sku";
            return await uow.Connection.ExecuteAsync(sql, new { sku }, transaction: uow.Transaction);
        }

        /// <summary>
        /// Bulk sync products using Table-Valued Parameters (TVP)
        /// </summary>
        public async Task<int> SyncProductsAsync(IEnumerable<Product> products, IUnitOfWork uow)
        {
            // Convert entities to DataTable for TVP
            var dt = await _dataTableFac.CreatDtByEntityPropertysAsync(products);

            return await uow.Connection.ExecuteAsync(
                "SyncProducts",
                new { Products = dt.AsTableValuedParameter("ProductType") },
                transaction: uow.Transaction,
                commandType: CommandType.StoredProcedure
            );
        }

        // ---------------------------------------------------------
        // Single Read Operations
        // ---------------------------------------------------------

        public async Task<Product> GetBySkuAsync(string sku)
        {
            // Standardized parameter name to @sku
            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [SKU] = @sku";

            using (var conn = _connFac.CreateConnection())
            {
                return await conn.QueryFirstOrDefaultAsync<Product>(sql, new { sku });
            }
        }

        public async Task<Product> GetByCustomerInfoAsync(string customerCode, string cSku)
        {
            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [CustomerCode] = @customerCode AND [CSKU] = @cSku";

            using (var conn = _connFac.CreateConnection())
            {
                return await conn.QueryFirstOrDefaultAsync<Product>(sql, new { customerCode, cSku });
            }
        }

        // ---------------------------------------------------------
        // List/Batch Read Operations
        // ---------------------------------------------------------

        public async Task<IReadOnlyList<Product>> GetListBySkuListAsync(IEnumerable<string> cSkuList)
        {
            // Dapper automatically handles IN clauses with IEnumerable
            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [SKU] IN @cSkuList";

            using (var conn = _connFac.CreateConnection())
            {
                var result = await conn.QueryAsync<Product>(sql, new { cSkuList });
                return result.ToList(); // Safety: Execute immediate
            }
        }

        public async Task<IReadOnlyList<Product>> GetListByCustomerCodeAndcSkuListAsync(string customerCode, IEnumerable<string> cSkuList)
        {
            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [CustomerCode] = @customerCode AND [CSKU] IN @cSkuList";

            using (var conn = _connFac.CreateConnection())
            {
                var result = await conn.QueryAsync<Product>(sql, new { customerCode, cSkuList });
                return result.ToList();
            }
        }

        /// <summary>
        /// Get products by a combination of CustomerSKU and CustomerCode
        /// </summary>
        /// <param name="cSkuAndCustomerCode">Dictionary Key: CustomerSKU, Value: CustomerCode</param>
        public async Task<IReadOnlyList<Product>> GetProductListByCustomerSkuAndCodeAsync(Dictionary<string, string> cSkuAndCustomerCode)
        {
            // 1. Check for empty input
            if (cSkuAndCustomerCode == null || cSkuAndCustomerCode.Count == 0)
            {
                return new List<Product>();
            }

            // 2. Safety check: SQL Server parameter limit (approx 2100)
            if (cSkuAndCustomerCode.Count > 1000)
            {
                throw new ArgumentException("Too many query combinations. Max 1000 allowed.", nameof(cSkuAndCustomerCode));
            }

            // 3. Build SQL
            StringBuilder sqlBuilder = new StringBuilder($"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND (");
            DynamicParameters parameters = new DynamicParameters();
            List<string> orClauses = new List<string>();

            int index = 0;
            foreach (var kvp in cSkuAndCustomerCode)
            {
                // Validate data
                if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                    continue;

                // Use short parameter names
                string pSku = $"@s{index}";
                string pCode = $"@c{index}";

                // Combine conditions: (CSKU=x AND CustomerCode=y)
                orClauses.Add($"([CSKU] = {pSku} AND [CustomerCode] = {pCode})");

                parameters.Add(pSku, kvp.Key);
                parameters.Add(pCode, kvp.Value);

                index++;
            }

            // If no valid conditions remained
            if (orClauses.Count == 0) return new List<Product>();

            // 4. Join clauses with OR
            sqlBuilder.Append(string.Join(" OR ", orClauses));
            sqlBuilder.Append(")"); // Close the outer parentheses

            string sql = sqlBuilder.ToString();

            // 5. Execute
            using (var conn = _connFac.CreateConnection())
            {
                var result = await conn.QueryAsync<Product>(sql, parameters);
                return result.ToList();
            }
        }

        // ---------------------------------------------------------
        // Memory Search Helpers
        // ---------------------------------------------------------

        public async Task<List<ProductSearchMeta>> GetAllSearchMetaAsync()
        {
            using (var conn = _connFac.CreateConnection())
            {
                // Assuming database column is 'IsDelete' based on consistency with other methods
                // Pre-calculate the search string in SQL
                string sql = @"
                    SELECT SKU, 
                           (SKU + ' | ' + ISNULL(Description,'') + ' | ' + ISNULL(CSku,'')) AS SearchText
                    FROM Product 
                    WHERE IsDelete<>1";

                var result = await conn.QueryAsync<ProductSearchMeta>(sql);
                return result.ToList();
            }
        }

        public async Task<List<string>> GetAllCustomerCodeListAsync()
        {
            using (var conn = _connFac.CreateConnection())
            {
                // 只需要查一列，并去重
                string sql = "SELECT DISTINCT [CustomerCode] FROM [Product] WHERE [CustomerCode] IS NOT NULL AND [CustomerCode] <> '' ORDER BY [CustomerCode]";
                var result = await conn.QueryAsync<string>(sql);
                return result.ToList();
            }
        }
    }
}