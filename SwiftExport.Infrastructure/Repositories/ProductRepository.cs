using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Factory;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Infrastructure.Repositories
{
    public class ProductRepository : DapperRepository<Product>, IProductRepository

    {
        private readonly IDataTableFactory<Product> _fac;
        public ProductRepository(IDbConnectionFactory _connFac, IMappingCache cache, IDataTableFactory<Product> fac) : base(cache, _connFac)
        {
            _fac=fac;
        }

        public async Task<int> Delete单产品BySku(string sku,IUnitOfWork uow)
        {
            var sql = $"DELETE FROM [{tableName}] WHERE [SKU] = @Sku";
            // 修正：参数名保持 PascalCase/CamelCase 一致性
            return await uow.Connection.ExecuteAsync(sql, new { Sku = sku }, transaction: uow.Transaction);
        }

        public async Task<int> Disable单产品BySku(string sku, IUnitOfWork uow)
        {
            var sql = $"UPDATE [{tableName}] SET IsDelete=1 WHERE [SKU] = @sku";

                return await uow.Connection.ExecuteAsync(sql, new {sku}, transaction: uow.Transaction);
        }

        public async  Task<Product> Get单产品BySku(string sku)
        {
            // 注意：参数名保持一致 { Sku }
            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [SKU] = @Sku";

            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryFirstOrDefaultAsync<Product>(sql, new { Sku = sku });

        }

        public async Task<Product> Get单产品By客户代码和型号(string CustomerCode, string cSKU)
        {

            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [CustomerCode] = @CustomerCode AND [CSKU] = @cSKU";

            // Dapper 可以自动匹配匿名对象的属性名
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryFirstOrDefaultAsync<Product>(sql, new { CustomerCode, cSKU } );

        }

        public async Task<IEnumerable<Product>> Get多产品BySkus(IEnumerable<string> Skus)
        {
            // Dapper 自动处理 IN (@Skus)
            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [SKU] IN @Skus";
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryAsync<Product>(sql, new { Skus } );
        }

        public async Task<IEnumerable<Product>> Get多产品By单客户代码多客户型号(string CustomerCode, IEnumerable<string> cSKUs)
        {
            string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [CustomerCode] = @CustomerCode AND [CSKU] IN @cSKUs";
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryAsync<Product>(sql, new { CustomerCode, cSKUs } );

        }

        public async Task<IEnumerable<Product>> Get多产品By多客户代码多客户型号(Dictionary<string, string> cSkuAndCustomerCode)
        {
            // 检查输入是否为空
            if (cSkuAndCustomerCode == null || !cSkuAndCustomerCode.Any())
            {
                return Enumerable.Empty<Product>();
            }


            // 基础 WHERE 子句
            StringBuilder sqlBuilder = new StringBuilder($"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND (");
            DynamicParameters parameters = new DynamicParameters();
            List<string> conditions = new List<string>();
            int index = 0;


            foreach (var kvp in cSkuAndCustomerCode)
            {
                string paramCSKU = $"@cSKU{index}";
                string paramCustomerCode = $"@CustomerCode{index}";

                // 【关键修正】使用方括号包裹字段名，并确保拼接格式为 OR 条件组
                conditions.Add($"([CSKU] = {paramCSKU} AND [CustomerCode] = {paramCustomerCode})");

                parameters.Add(paramCSKU, kvp.Key);   // kvp.Key 是 cSKU
                parameters.Add(paramCustomerCode, kvp.Value); // kvp.Value 是 CustomerCode

                index++;
            }
            // SELECT* FROM Product
            //WHERE IsDelete<>1
            //AND(
            //(SKU = @cSKU0 AND CustomerCode = @customerCode0)
            //OR(SKU = @cSKU1 AND CustomerCode = @customerCode1)
            // OR(SKU = @cSKU2 AND CustomerCode = @customerCode2)
            //)
            // 将所有 OR 条件组连接起来，并用外层括号包裹
            sqlBuilder.Append(string.Join(" OR ", conditions));
            sqlBuilder.Append(")"); // 闭合外层括号

            string sql = sqlBuilder.ToString();

            // SQL 结构将是：SELECT * FROM [Table] WHERE IsDelete<>1 AND ((CSKU=...) AND (CSKU=...))
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryAsync<Product>(sql, parameters );

        }

        public async Task<int> SyncProductsAsync(IEnumerable<Product> products,IUnitOfWork uow)
        {
               var dt= await _fac.CreatDtByEntityPropertysAsync(products);

                        return await uow.Connection.ExecuteAsync(
                            "SyncProducts",
                            new { Products = dt.AsTableValuedParameter("ProductType") },
                            transaction: uow.Transaction,
                            commandType: CommandType.StoredProcedure
                        );
            }





        }

    }