using Dapper;
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
        private readonly IDbConnectionFactory _connFactory;
        private readonly IMappingCache _cache;
        private readonly string tableName;
        private readonly IDataTableFactory<Product> _fac;
        public ProductRepository(IDbConnectionFactory connFactory, IMappingCache cache,IDataTableFactory<Product> fac) : base(connFactory, cache)
        {
            _connFactory = connFactory;
            _cache = cache;
            tableName = _cache.GetTableNameByEntity<Product>() ?? typeof(Product).Name;
            _fac=fac;
        }

        public async Task<int> Delete单产品BySku(string sku)
        {
            var sql = $"DELETE FROM [{tableName}] WHERE [SKU] = @sku";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, new { sku });
            }
        }

        public async Task<int> Disable单产品BySku(string sku)
        {
            var sql = $"UPDATE [{tableName}] SET IsDelete=1 WHERE [SKU] = @sku";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, new {sku});
            }
        }

        public async  Task<Product> Get单产品BySku(string sku)
        {
                using (var connection = _connFactory.CreateConnection())
                {
                    string sql =$"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND sku = @sku";
                    var product = connection.QueryFirstOrDefaultAsync<Product>(sql, new { sku });
                    return await product;
                }
        }

        public async Task<Product> Get单产品By客户代码和型号(string CustomerCode, string cSKU)
        {
                using (var connection = _connFactory.CreateConnection())
                {
                    string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND CustomerCode = @CustomerCode AND CSKU = @cSKU";
                    var product = connection.QueryFirstOrDefaultAsync<Product>(sql, new { CustomerCode, cSKU });
                    return await product;
                }
        }

        public async Task<IEnumerable<Product>> Get多产品BySkus(IEnumerable<string> Skus)
        {
                using (var connection = _connFactory.CreateConnection())
                {
                    string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND SKU IN @Skus";
                    return await connection.QueryAsync<Product>(sql, new { Skus });
                }
        }

        public async Task<IEnumerable<Product>> Get多产品By动态条件(Dictionary<string, string> 条件字典)
        {
                using (var connection = _connFactory.CreateConnection())
                {
                    StringBuilder sqlBuilder = new StringBuilder($"SELECT * FROM [{tableName}] WHERE IsDelete<>1");
                    DynamicParameters parameters = new DynamicParameters();
                    foreach (var kvp in 条件字典)
                    {
                        sqlBuilder.Append($" AND {kvp.Key} = @{kvp.Key}");
                        parameters.Add($"@{kvp.Key}", kvp.Value);
                    }
                    string sql = sqlBuilder.ToString();
                    return await connection.QueryAsync<Product>(sql, parameters);
                }
        }

        public async Task<IEnumerable<Product>> Get多产品By单客户代码多客户型号(string CustomerCode, IEnumerable<string> cSKUs)
        {

                using (var connection = _connFactory.CreateConnection())
                {
                    string sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND CustomerCode = @CustomerCode AND CSKU IN @cSKUs";
                    return await connection.QueryAsync<Product>(sql, new { CustomerCode, cSKUs });
                }
        }

        public async Task<IEnumerable<Product>> Get多产品By多客户代码多客户型号(Dictionary<string, string> cSkuAndCustomerCode)
        {
                using (var connection = _connFactory.CreateConnection())
                {
                    StringBuilder sqlBuilder = new StringBuilder($"SELECT * FROM [{tableName}] WHERE IsDelete<>1");
                    DynamicParameters parameters = new DynamicParameters();
                    List<string> conditions = new List<string>();
                    int index = 0;
                    foreach (var kvp in cSkuAndCustomerCode)
                    {
                        //建立@cSKU0, @CustomerCode0,@cSKU1, @CustomerCode1...等等
                        string paramCSKU = $"@cSKU{index}";
                        string paramCustomerCode = $"@CustomerCode{index}";
                        conditions.Add($"(CSKU = {paramCSKU} AND CustomerCode = {paramCustomerCode})");
                        //
                        //建立对应值@cSKU0= kvp.Key, @CustomerCode0 = kvp.Value
                        parameters.Add(paramCSKU, kvp.Key);
                        parameters.Add(paramCustomerCode, kvp.Value);
                        index++;
                    }
                      // SELECT* FROM Product
                      //WHERE IsDelete<>1
                      //AND(
                      //(SKU = @cSKU0 AND CustomerCode = @customerCode0)
                      //OR(SKU = @cSKU1 AND CustomerCode = @customerCode1)
                      // OR(SKU = @cSKU2 AND CustomerCode = @customerCode2)
                      //)
                    sqlBuilder.Append(string.Join(" OR ", conditions));
                    string sql = sqlBuilder.ToString();
                    return await connection.QueryAsync<Product>(sql, parameters);
                }
        }

        public async Task<int> 同步产品(IEnumerable<Product> products)
        {
               var dt= await _fac.CreatDtByEntityPropertysAsync(products);
                    using (var conn = _connFactory.CreateConnection())
                    {
                        return await conn.ExecuteAsync(
                            "SyncProducts",
                            new { Products = dt.AsTableValuedParameter("ProductType") },
                            commandType: CommandType.StoredProcedure
                        );
                      }
            }





        }

    }