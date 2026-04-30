using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Orders
{
    /// <summary>
    /// 销售订单行数据源仓储实现。
    /// 基于 CustomerProduct 与 Product 联查，为订单行编辑提供候选数据。
    /// </summary>
    public partial class SalesOrderLineSourceRepository(RepositoryBaseContext context)
        : DapperRepository<CustomerProduct>(context), ISalesOrderLineSourceRepository
    {
        /// <summary>
        /// 联查分页查询。
        /// 支持 QueryCriteria 动态条件，返回可直接用于 SalesOrderLineDto 的数据结构。
        /// </summary>
        public async Task<PagedResult<SalesOrderLineDto>> GetPagedSalesOrderLineSourceDtoAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "cp.Id DESC")
        {
            if (string.IsNullOrWhiteSpace(orderBy))
            {
                orderBy = "cp.Id DESC";
            }
            else
            {
                orderBy = OrderByRegex().Replace(orderBy, string.Empty);
                if (string.IsNullOrWhiteSpace(orderBy))
                {
                    orderBy = "cp.Id DESC";
                }
            }

            var parameters = new DynamicParameters();

            // 中文注释：以 cp 为主表，固定过滤“未删除 + 启用”的客户产品。
            var sb = new StringBuilder("WHERE (cp.IsDeleted = 0 OR cp.IsDeleted IS NULL) AND cp.IsActive = 1");

            // 中文注释：动态条件统一以 cp 别名解析；需要 product 字段时，服务层通过 cp 可映射字段传入。
            var whereClause = ParseQueryCriteria(criteria, parameters, "cp.");
            if (!string.IsNullOrWhiteSpace(whereClause))
            {
                sb.Append(whereClause);
            }

            if (pageIndex < 1) pageIndex = 1;
            if (pageSize <= 0) pageSize = 20;

            int offset = (pageIndex - 1) * pageSize;
            parameters.Add("@Offset", offset);
            parameters.Add("@Limit", pageSize);

            const string joinSql = @"
                        FROM [CustomerProduct] cp WITH(NOLOCK)
                        LEFT JOIN [Product] p WITH(NOLOCK) ON cp.SkuId = p.Id
                        LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Id";
            string whereSql = sb.ToString();

            // 中文注释：映射字段按 SalesOrderLineDto 语义组织，前端可直接绑定/回填。
            string sql = $@"
                        SELECT COUNT(1)
                        {joinSql}
                        {whereSql};

                        SELECT
                            cp.Id                                                  AS CustomerProductId,
                            cp.SkuId                                               AS ProductId,
                            cp.Sku                                                 AS Sku,
                            cp.LinkSku                                             AS LinkSku,
                            cp.CustomerSku                                         AS CustomerSKU,
                            -- 核心逻辑：客户产品表字段优先，产品主表字段保底
                            COALESCE(cp.CustomerPdDescription, p.DescriptionEn)    AS DescriptionEn,
                            pc.ColorNameEn                                         AS ColorNameEn,
                            COALESCE(cp.CustomerPdFeatures, p.FeaturesEn)          AS FeaturesEn,
                            cp.CNYPrice                                            AS CNYPrice,
                            cp.USDPrice                                            AS USDPrice,

                            -- 包装逻辑：通过 OUTER APPLY 获取唯一默认包装规格
                            pkg.PcsPerPackage                                      AS PcsOfCarton,
                            pkg.Nw_KG                                              AS NwOfCarton,
                            pkg.Gw_KG                                              AS GwOfCarton,
                            pkg.Cbm                                                AS CbmOfCarton,
        
                            p.MainImage                                            AS MainImage
                        {joinSql}
                        -- OUTER APPLY
                        OUTER APPLY (
                            SELECT TOP 1 
                                pp.PcsPerPackage,
                                pp.Nw_KG,
                                pp.Gw_KG,
                                pp.Cbm
                            FROM [ProductPackage] pp
                            WHERE pp.ProductId = p.Id AND pp.IsDeleted = 0
                            ORDER BY pp.IsDefault DESC, pp.PcsPerPackage DESC -- 优先取默认，次选大包装
                        ) pkg
                        {whereSql}
                        ORDER BY {orderBy}
                        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

            using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
            var total = await multi.ReadFirstAsync<int>();
            var items = await multi.ReadAsync<SalesOrderLineDto>();

            return new PagedResult<SalesOrderLineDto>
            {
                Items = [.. items],
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9_,\s\.\[\]]")]
        private static partial System.Text.RegularExpressions.Regex OrderByRegex();
    }
}
