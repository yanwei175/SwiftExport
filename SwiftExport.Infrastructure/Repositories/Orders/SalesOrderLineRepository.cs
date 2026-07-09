using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
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
    public partial class SalesOrderLineRepository(RepositoryBaseContext context) : DapperRepository<SalesOrderLine>(context), ISalesOrderLineRepository
    {

        public async Task<IReadOnlyList<SalesOrderLine>> GetLinesByOrderIdAsync(Guid orderId, bool withUpdLock = false)
        {
            // 根据参数决定用 NOLOCK 还是 UPDLOCK
            string lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";

            // 1. 基础 SQL 已经包含了 WHERE (IsDeleted = 0 OR IsDeleted IS NULL)
            var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE (IsDeleted = 0 OR IsDeleted IS NULL) AND SalesOrderId = @OrderId ORDER BY LineNumber ASC";

            var param = new { OrderId = orderId };

            // 4. 执行查询
            var rst = await Connection.QueryAsync<SalesOrderLine>(sql, param, transaction: Transaction);
            return [.. rst];
        }

        public async Task<IReadOnlyList<SalesOrderLineManagerDto>> GetSalesOrderLineManagerAsync(Guid orderId)
        {
            var sql = $@"
                        SELECT 
                            l.{nameof(SalesOrderLine.LineNumber)} AS {nameof(SalesOrderLineManagerDto.LineNumber)},
                            l.{nameof(SalesOrderLine.Uid)} AS {nameof(SalesOrderLineManagerDto.Uid)},
                            l.{nameof(SalesOrderLine.Version)} AS {nameof(SalesOrderLineManagerDto.Version)},
                            l.{nameof(SalesOrderLine.CreatedBy)} AS {nameof(SalesOrderLineManagerDto.CreatedBy)},
                            l.{nameof(SalesOrderLine.CreatedDate)} AS {nameof(SalesOrderLineManagerDto.CreatedDate)},
                            l.{nameof(SalesOrderLine.UpdatedBy)} AS {nameof(SalesOrderLineManagerDto.UpdatedBy)},
                            l.{nameof(SalesOrderLine.UpdatedDate)} AS {nameof(SalesOrderLineManagerDto.UpdatedDate)},

                            l.{nameof(SalesOrderLine.MainImage)} AS {nameof(SalesOrderLineManagerDto.MainImage)},
                            l.{nameof(SalesOrderLine.CustomerSku)} AS {nameof(SalesOrderLineManagerDto.CustomerSku)},
                            l.{nameof(SalesOrderLine.Sku)} AS {nameof(SalesOrderLineManagerDto.Sku)},
                            l.{nameof(SalesOrderLine.DescriptionEn)} AS {nameof(SalesOrderLineManagerDto.DescriptionEn)},
                            l.{nameof(SalesOrderLine.ColorNameEn)} AS {nameof(SalesOrderLineManagerDto.ColorNameEn)},
                            l.{nameof(SalesOrderLine.Qty)} AS {nameof(SalesOrderLineManagerDto.Qty)},

                            ISNULL(d.ShippedQty, 0) AS {nameof(SalesOrderLineManagerDto.ShippedQty)},
                            ISNULL(l.{nameof(SalesOrderLine.Qty)}, 0) - ISNULL(d.ShippedQty, 0) AS {nameof(SalesOrderLineManagerDto.UnshippedQty)},

                            l.{nameof(SalesOrderLine.UsdPrice)} AS {nameof(SalesOrderLineManagerDto.UsdPrice)},
                            l.{nameof(SalesOrderLine.CnyPrice)} AS {nameof(SalesOrderLineManagerDto.CnyPrice)},
                            l.{nameof(SalesOrderLine.UsdAmount)} AS {nameof(SalesOrderLineManagerDto.UsdAmount)},
                            l.{nameof(SalesOrderLine.CnyAmount)} AS {nameof(SalesOrderLineManagerDto.CnyAmount)},
                            l.{nameof(SalesOrderLine.SubNote)} AS {nameof(SalesOrderLineManagerDto.SubNote)}
                        FROM [SalesOrderLine] l WITH(NOLOCK)
                        OUTER APPLY
                        (
                            SELECT 
                                SUM(ISNULL(d.{nameof(IndependentDemand.ShippedQty)}, 0)) AS ShippedQty
                            FROM [IndependentDemand] d WITH(NOLOCK)
                            WHERE d.{nameof(IndependentDemand.SourceType)} = @SourceType
                              AND d.{nameof(IndependentDemand.SourceLineId)} = l.{nameof(SalesOrderLine.Uid)}
                        ) d
                        WHERE l.{nameof(SalesOrderLine.SalesOrderId)} = @OrderId
                          AND (l.{nameof(SalesOrderLine.IsDeleted)} = 0 OR l.{nameof(SalesOrderLine.IsDeleted)} IS NULL)
                        ORDER BY l.{nameof(SalesOrderLine.LineNumber)} ASC;";

            var parameters = new
            {
                OrderId = orderId,
                SourceType = OrderTypesEnum.销售订单
            };

            var rst = await Connection.QueryAsync<SalesOrderLineManagerDto>(
                sql, parameters, transaction: Transaction);

            return [.. rst];
        }

        public async Task<PagedResponseBase<SalesProductDto>> GetSalesProductPagedListAsync(SalesProductSearchRequest request, string orderBy = "p.Sku DESC")
        {
            request ??= new SalesProductSearchRequest();

            if (string.IsNullOrWhiteSpace(orderBy))
            {
                orderBy = "p.Sku DESC";
            }
            else
            {
                orderBy = NormalizeOrderBy(orderBy, "p.Sku DESC");
                if (string.IsNullOrWhiteSpace(orderBy))
                {
                    orderBy = "p.Sku DESC";
                }
            }

            var parameters = new DynamicParameters();

            // 中文注释：CustomerId/CustomerCode 用于决定是否走 CustomerProduct 联查。
            bool useCustomerProduct = request.CustomerId != Guid.Empty || !string.IsNullOrWhiteSpace(request.CustomerCode);

            // 中文注释：无客户产品联查时，orderBy 中若有 cp. 前缀字段会导致 SQL 报错，
            // 统一将 cp. 替换为 p. 作为防御性处理。
            if (!useCustomerProduct && orderBy.Contains("cp.", StringComparison.OrdinalIgnoreCase))
            {
                orderBy = System.Text.RegularExpressions.Regex.Replace(orderBy, @"cp\.", "p.", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            var sb = new StringBuilder();
            if (useCustomerProduct)
            {
                sb.Append("WHERE (cp.IsDeleted = 0 OR cp.IsDeleted IS NULL) AND cp.IsActive = 1");

                if (request.CustomerId != Guid.Empty)
                {
                    parameters.Add("@CustomerId", request.CustomerId);
                    sb.Append(" AND cp.CustomerId = @CustomerId");
                }

                if (!string.IsNullOrWhiteSpace(request.CustomerCode))
                {
                    parameters.Add("@CustomerCode", request.CustomerCode.Trim());
                    sb.Append(" AND cp.CustomerCode = @CustomerCode");
                }

            }
            else
            {
                sb.Append("WHERE (p.IsDeleted = 0 OR p.IsDeleted IS NULL)");
            }

            if (request.ColorId != Guid.Empty)
            {
                parameters.Add("@ColorId", request.ColorId);
                sb.Append(" AND p.ColorId = @ColorId");
            }

            // 中文注释：客户SKU精确匹配（支持多个）
            if (request.BatchCustomerSkuList != null && request.BatchCustomerSkuList.Count > 0)
            {
                parameters.Add("@BatchCustomerSkuList", request.BatchCustomerSkuList);
                sb.Append(useCustomerProduct
                    ? " AND cp.CustomerSku IN @BatchCustomerSkuList"
                    : " AND p.DefaultCustomerSku IN @BatchCustomerSkuList");
            }

            // 中文注释：批量关键词模糊匹配（Sku/DescriptionEn/FeaturesEn/CustomerSku）
            if (request.BatchKeywords != null && request.BatchKeywords.Count > 0)
            {
                var keywordGroupSql = new List<string>();
                for (int i = 0; i < request.BatchKeywords.Count; i++)
                {
                    var kw = request.BatchKeywords[i] ?? string.Empty;
                    var paramName = $"@Keyword_{i}";
                    parameters.Add(paramName, $"%{kw}%");

                    var customerSkuField = useCustomerProduct ? "cp.CustomerSku" : "p.DefaultCustomerSku";
                    var skuField = useCustomerProduct ? "cp.Sku" : "p.Sku";
                    var linkSkuField = useCustomerProduct ? "cp.LinkSku" : "p.LinkSku";

                    keywordGroupSql.Add($"({skuField} LIKE {paramName} OR {customerSkuField} LIKE {paramName} OR p.DescriptionEn LIKE {paramName} OR p.FeaturesEn LIKE {paramName} OR {linkSkuField} LIKE {paramName})");
                }

                if (keywordGroupSql.Count > 0)
                {
                    sb.Append(" AND (").Append(string.Join(" OR ", keywordGroupSql)).Append(')');
                }
            }

            int pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
            int pageSize = request.PageSize > 0 ? request.PageSize : 50;

            int offset = (pageIndex - 1) * pageSize;
            parameters.Add("@Offset", offset);
            parameters.Add("@Limit", pageSize);

            var joinSql = useCustomerProduct
                ? @"
                        FROM [CustomerProduct] cp WITH(NOLOCK)
                        LEFT JOIN [Product] p WITH(NOLOCK) ON cp.SkuId = p.Uid
                        LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Uid"
                : @"
                        FROM [Product] p WITH(NOLOCK)
                        LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Uid";
            string whereSql = sb.ToString();

            // 中文注释：映射字段按 SalesOrderLineDto 语义组织，前端可直接绑定/回填。
            string sql = $@"
                        SELECT COUNT(1)
                        {joinSql}
                        {whereSql};

                        SELECT
                            {(useCustomerProduct ? "cp.Uid" : "NULL")}                               AS CustomerProductId,
                            {(useCustomerProduct ? "cp.CustomerCode" : "p.DefaultCustomerCode")} AS CustomerCode,
                            {(useCustomerProduct ? "cp.CustomerSku" : "p.DefaultCustomerSku")}      AS CustomerSku,
                            {(useCustomerProduct ? "cp.LinkSku" : "p.LinkSku")}                     AS LinkSku,
                            {(useCustomerProduct ? "cp.SkuId" : "p.Uid")}                            AS ProductId,
                            {(useCustomerProduct ? "cp.Sku" : "p.Sku")}                             AS Sku,
                            {(useCustomerProduct ? "cp.CNYPrice" : "p.CNYPrice")}                  AS CNYPrice,
                            {(useCustomerProduct ? "cp.USDPrice" : "p.USDPrice")}                  AS USDPrice,
                            {(useCustomerProduct ? "cp.Currency" : "NULL")}                       AS Currency,
                            {(useCustomerProduct ? "cp.MOQ" : "NULL")}                            AS MOQ,
                            {(useCustomerProduct ? "cp.Note" : "p.Note")}                            AS SubNote,
                            {(useCustomerProduct ? "cp.DescriptionEn" : "p.DescriptionEn")}      AS DescriptionEn,
                            {(useCustomerProduct ? "cp.FeaturesEn" : "p.FeaturesEn")}            AS FeaturesEn,
                            p.MaterialType                                        AS MaterialType,
                            -- 颜色名称
                            pc.ColorNameEn                                         AS ColorNameEn,
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
                            WHERE pp.ProductId = p.Uid AND pp.IsDeleted = 0
                            ORDER BY pp.IsDefault DESC, pp.PcsPerPackage DESC -- 优先取默认，次选大包装
                        ) pkg
                        {whereSql}
                        ORDER BY {orderBy}
                        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

            using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
            var total = await multi.ReadFirstAsync<int>();
            var items = await multi.ReadAsync<SalesProductDto>();

            return new PagedResponseBase<SalesProductDto>
            {
                Items = [.. items],
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }



        private static string NormalizeOrderBy(string? orderBy, string defaultOrderBy)
        {
            if (string.IsNullOrWhiteSpace(orderBy)) return defaultOrderBy;

            var safeOrderBy = System.Text.RegularExpressions.Regex.Replace(orderBy, @"[^a-zA-Z0-9_,\s\.\[\]]", string.Empty);
            return string.IsNullOrWhiteSpace(safeOrderBy) ? defaultOrderBy : safeOrderBy;
        }


    }
}
