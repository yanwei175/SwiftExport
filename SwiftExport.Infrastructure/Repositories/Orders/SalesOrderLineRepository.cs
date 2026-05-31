using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Core.Common;
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


        public async Task<List<SalesOrderLine>> GetLinesByOrderIdAsync(int orderId, bool withUpdLock = false)
        {
            // 根据参数决定用 NOLOCK 还是 UPDLOCK
            string lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";

            // 1. 基础 SQL 已经包含了 WHERE (IsDeleted = 0 OR IsDeleted IS NULL)
            var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE (IsDeleted = 0 OR IsDeleted IS NULL) AND SalesOrderId = @OrderId ORDER BY LineNumber ASC";

            var param = new { OrderId = orderId };

            // 4. 执行查询
            var rst = await Connection.QueryAsync<SalesOrderLine>(sql, param, transaction: Transaction);
            return rst.AsList(); // 或者 return [.. rst]; (C# 12写法)
        }

        public async Task<PagedResult<SalesProductDto>> GetSalesProductPagedListAsync(SalesProductSearchDto request, string orderBy = "p.Sku DESC")
        {
            request ??= new SalesProductSearchDto();

            if (string.IsNullOrWhiteSpace(orderBy))
            {
                orderBy = "p.Sku DESC";
            }
            else
            {
                orderBy = OrderByRegex().Replace(orderBy, string.Empty);
                if (string.IsNullOrWhiteSpace(orderBy))
                {
                    orderBy = "p.Sku DESC";
                }
            }

            var parameters = new DynamicParameters();

            // 中文注释：CustomerId/CustomerCode 用于决定是否走 CustomerProduct 联查。
            bool useCustomerProduct = request.CustomerId > 0 || !string.IsNullOrWhiteSpace(request.CustomerCode);

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

                if (request.CustomerId > 0)
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

            if (request.ColorId > 0)
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
                        LEFT JOIN [Product] p WITH(NOLOCK) ON cp.SkuId = p.Id
                        LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Id"
                : @"
                        FROM [Product] p WITH(NOLOCK)
                        LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Id";
            string whereSql = sb.ToString();

            // 中文注释：映射字段按 SalesOrderLineDto 语义组织，前端可直接绑定/回填。
            string sql = $@"
                        SELECT COUNT(1)
                        {joinSql}
                        {whereSql};

                        SELECT
                            {(useCustomerProduct ? "cp.Id" : "NULL")}                               AS CustomerProductId,
                            {(useCustomerProduct ? "cp.CustomerCode" : "p.DefaultCustomerCode")} AS CustomerCode,
                            {(useCustomerProduct ? "cp.CustomerSku" : "p.DefaultCustomerSku")}      AS CustomerSku,
                            {(useCustomerProduct ? "cp.LinkSku" : "p.LinkSku")}                     AS LinkSku,
                            {(useCustomerProduct ? "cp.SkuId" : "p.Id")}                            AS ProductId,
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
                            WHERE pp.ProductId = p.Id AND pp.IsDeleted = 0
                            ORDER BY pp.IsDefault DESC, pp.PcsPerPackage DESC -- 优先取默认，次选大包装
                        ) pkg
                        {whereSql}
                        ORDER BY {orderBy}
                        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

            using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
            var total = await multi.ReadFirstAsync<int>();
            var items = await multi.ReadAsync<SalesProductDto>();

            return new PagedResult<SalesProductDto>
            {
                Items = [.. items],
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }


        #region 备份
        /// <summary>
        /// 联查分页查询。
        /// 支持 QueryCriteria 动态条件，返回可直接用于 SalesOrderLineDto 的数据结构。
        /// </summary>
        //public async Task<PagedResult<SalesOrderLineDto>> GetPagedSalesOrderLineSourceDtoAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "cp.Id DESC")
        //{
        //    if (string.IsNullOrWhiteSpace(orderBy))
        //    {
        //        orderBy = "cp.Id DESC";
        //    }
        //    else
        //    {
        //        orderBy = OrderByRegex().Replace(orderBy, string.Empty);
        //        if (string.IsNullOrWhiteSpace(orderBy))
        //        {
        //            orderBy = "cp.Id DESC";
        //        }
        //    }

        //    var parameters = new DynamicParameters();

        //    // 中文注释：根据 CustomerId 是否存在切换主查询表。
        //    // CustomerId > 0：以 CustomerProduct 为主；
        //    // CustomerId <= 0：以 Product 为主，不联 CustomerProduct。
        //    bool useCustomerProduct = TryGetCustomerId(criteria, out var customerId) && customerId > 0;

        //    var sb = new StringBuilder();
        //    if (useCustomerProduct)
        //    {
        //        sb.Append("WHERE (cp.IsDeleted = 0 OR cp.IsDeleted IS NULL) AND cp.IsActive = 1");
        //    }
        //    else
        //    {
        //        sb.Append("WHERE (p.IsDeleted = 0 OR p.IsDeleted IS NULL) AND p.IsActive = 1");
        //    }

        //    // 中文注释：改为仓储内专用手写条件拼装，移除通用 ParseQueryCriteria 依赖，
        //    // 并按不同查询主表切换字段映射策略。
        //    var whereClause = BuildWhereClause(criteria, parameters, useCustomerProduct);
        //    if (!string.IsNullOrWhiteSpace(whereClause))
        //    {
        //        sb.Append(whereClause);
        //    }

        //    if (pageIndex < 1) pageIndex = 1;
        //    if (pageSize <= 0) pageSize = 20;

        //    int offset = (pageIndex - 1) * pageSize;
        //    parameters.Add("@Offset", offset);
        //    parameters.Add("@Limit", pageSize);

        //    var joinSql = useCustomerProduct
        //        ? @"
        //                FROM [CustomerProduct] cp WITH(NOLOCK)
        //                LEFT JOIN [Product] p WITH(NOLOCK) ON cp.SkuId = p.Id
        //                LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Id"
        //        : @"
        //                FROM [Product] p WITH(NOLOCK)
        //                LEFT JOIN [ProductColor] pc WITH(NOLOCK) ON p.ColorId = pc.Id";
        //    string whereSql = sb.ToString();

        //    // 中文注释：映射字段按 SalesOrderLineDto 语义组织，前端可直接绑定/回填。
        //    string sql = $@"
        //                SELECT COUNT(1)
        //                {joinSql}
        //                {whereSql};

        //                SELECT
        //                    {(useCustomerProduct ? "cp.Id" : "NULL")}                               AS CustomerProductId,
        //                    {(useCustomerProduct ? "cp.SkuId" : "p.Id")}                            AS ProductId,
        //                    {(useCustomerProduct ? "cp.Sku" : "p.Sku")}                             AS Sku,
        //                    {(useCustomerProduct ? "cp.LinkSku" : "p.LinkSku")}                     AS LinkSku,
        //                    {(useCustomerProduct ? "cp.CustomerSku" : "p.DefaultCustomerSku")}      AS CustomerSKU,
        //                    -- 核心逻辑：有客户时优先客户产品字段；无客户时仅使用产品主数据。
        //                    {(useCustomerProduct ? "COALESCE(cp.CustomerPdDescription, p.DescriptionEn)" : "p.DescriptionEn")} AS DescriptionEn,
        //                    pc.ColorNameEn                                         AS ColorNameEn,
        //                    {(useCustomerProduct ? "COALESCE(cp.CustomerPdFeatures, p.FeaturesEn)" : "p.FeaturesEn")} AS FeaturesEn,
        //                    {(useCustomerProduct ? "cp.CNYPrice" : "p.CNYPrice")}                  AS CNYPrice,
        //                    {(useCustomerProduct ? "cp.USDPrice" : "p.USDPrice")}                  AS USDPrice,

        //                    -- 包装逻辑：通过 OUTER APPLY 获取唯一默认包装规格
        //                    pkg.PcsPerPackage                                      AS PcsOfCarton,
        //                    pkg.Nw_KG                                              AS NwOfCarton,
        //                    pkg.Gw_KG                                              AS GwOfCarton,
        //                    pkg.Cbm                                                AS CbmOfCarton,

        //                    p.MainImage                                            AS MainImage
        //                {joinSql}
        //                -- OUTER APPLY
        //                OUTER APPLY (
        //                    SELECT TOP 1 
        //                        pp.PcsPerPackage,
        //                        pp.Nw_KG,
        //                        pp.Gw_KG,
        //                        pp.Cbm
        //                    FROM [ProductPackage] pp
        //                    WHERE pp.ProductId = p.Id AND pp.IsDeleted = 0
        //                    ORDER BY pp.IsDefault DESC, pp.PcsPerPackage DESC -- 优先取默认，次选大包装
        //                ) pkg
        //                {whereSql}
        //                ORDER BY {orderBy}
        //                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        //    using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        //    var total = await multi.ReadFirstAsync<int>();
        //    var items = await multi.ReadAsync<SalesOrderLineDto>();

        //    return new PagedResult<SalesOrderLineDto>
        //    {
        //        Items = [.. items],
        //        TotalCount = total,
        //        PageIndex = pageIndex,
        //        PageSize = pageSize
        //    };
        //}

        ///// <summary>
        ///// 中文注释：将 QueryCriteria 转换为仓储专用 WHERE 片段（不含 WHERE 关键字）。
        ///// 说明：
        ///// 1. 仅允许白名单字段，避免字段名注入风险；
        ///// 2. 统一使用参数化，避免 SQL 注入；
        ///// 3. 兼容 AND 条件与 OR 分组条件。
        ///// </summary>
        //private static string BuildWhereClause(QueryCriteria criteria, DynamicParameters parameters, bool useCustomerProduct)
        //{
        //    if (criteria == null || (criteria.Items.Count == 0 && criteria.OrGroups.Count == 0))
        //    {
        //        return string.Empty;
        //    }

        //    var sb = new StringBuilder();
        //    int andIndex = 0;

        //    foreach (var item in criteria.Items)
        //    {
        //        if (item == null || item.Condition == null) continue;
        //        var fieldSql = ResolveFieldSql(item.Field, useCustomerProduct);
        //        if (string.IsNullOrWhiteSpace(fieldSql)) continue;

        //        var conditionSql = BuildConditionSql(item.Condition, fieldSql, parameters, $"And_{andIndex}");
        //        if (string.IsNullOrWhiteSpace(conditionSql)) continue;

        //        sb.Append(" AND ").Append(conditionSql);
        //        andIndex++;
        //    }

        //    int groupIndex = 0;
        //    foreach (var group in criteria.OrGroups)
        //    {
        //        if (group == null || group.Count == 0)
        //        {
        //            groupIndex++;
        //            continue;
        //        }

        //        var orParts = new List<string>();
        //        int itemIndex = 0;

        //        foreach (var item in group)
        //        {
        //            if (item == null || item.Condition == null)
        //            {
        //                itemIndex++;
        //                continue;
        //            }

        //            var fieldSql = ResolveFieldSql(item.Field, useCustomerProduct);
        //            if (string.IsNullOrWhiteSpace(fieldSql))
        //            {
        //                itemIndex++;
        //                continue;
        //            }

        //            var conditionSql = BuildConditionSql(item.Condition, fieldSql, parameters, $"Or_{groupIndex}_{itemIndex}");
        //            if (!string.IsNullOrWhiteSpace(conditionSql))
        //            {
        //                orParts.Add(conditionSql);
        //            }

        //            itemIndex++;
        //        }

        //        if (orParts.Count > 0)
        //        {
        //            sb.Append(" AND (").Append(string.Join(" OR ", orParts)).Append(")");
        //        }

        //        groupIndex++;
        //    }

        //    return sb.ToString();
        //}

        ///// <summary>
        ///// 中文注释：字段白名单映射，避免任意字段拼接到 SQL。
        ///// </summary>
        //private static string ResolveFieldSql(string field, bool useCustomerProduct)
        //{
        //    if (string.IsNullOrWhiteSpace(field)) return string.Empty;

        //    if (useCustomerProduct)
        //    {
        //        return field.Trim() switch
        //        {
        //            "CustomerId" => "cp.CustomerId",
        //            "CustomerCode" => "cp.CustomerCode",
        //            "Sku" => "cp.Sku",
        //            "CustomerSku" => "cp.CustomerSku",
        //            "LinkSku" => "cp.LinkSku",
        //            "p.DescriptionEn" => "p.DescriptionEn",
        //            "p.FeaturesEn" => "p.FeaturesEn",
        //            "pc.ColorNameEn" => "pc.ColorNameEn",
        //            _ => string.Empty,
        //        };
        //    }

        //    // 中文注释：无客户场景以 Product 为主，CustomerProduct 字段不存在时映射到产品默认字段。
        //    return field.Trim() switch
        //    {
        //        "CustomerId" => string.Empty,
        //        "CustomerCode" => "p.DefaultCustomerCode",
        //        "Sku" => "p.Sku",
        //        "CustomerSku" => "p.DefaultCustomerSku",
        //        "LinkSku" => "p.LinkSku",
        //        "p.DescriptionEn" => "p.DescriptionEn",
        //        "p.FeaturesEn" => "p.FeaturesEn",
        //        "pc.ColorNameEn" => "pc.ColorNameEn",
        //        _ => string.Empty,
        //    };
        //}

        ///// <summary>
        ///// 中文注释：从 QueryCriteria 的 AND 条件中提取 CustomerId，用于决定查询主表。
        ///// </summary>
        //private static bool TryGetCustomerId(QueryCriteria criteria, out int customerId)
        //{
        //    customerId = 0;
        //    if (criteria?.Items == null || criteria.Items.Count == 0) return false;

        //    foreach (var item in criteria.Items)
        //    {
        //        if (item?.Condition == null) continue;
        //        if (!string.Equals(item.Field, "CustomerId", StringComparison.OrdinalIgnoreCase)) continue;
        //        if (item.Condition.操作符 != 查询操作符.等于) continue;

        //        if (item.Condition.值1 == null) return false;

        //        try
        //        {
        //            customerId = Convert.ToInt32(item.Condition.值1);
        //            return true;
        //        }
        //        catch
        //        {
        //            return false;
        //        }
        //    }

        //    return false;
        //}

        ///// <summary>
        ///// 中文注释：构建单个条件 SQL，统一参数化。
        ///// </summary>
        //private static string BuildConditionSql(QueryCondition cond, string fieldSql, DynamicParameters parameters, string paramToken)
        //{
        //    if (cond == null || string.IsNullOrWhiteSpace(fieldSql)) return string.Empty;

        //    var paramName = $"@{paramToken}";
        //    switch (cond.操作符)
        //    {
        //        case 查询操作符.等于:
        //            parameters.Add(paramName, cond.值1);
        //            return $"{fieldSql} = {paramName}";

        //        case 查询操作符.模糊匹配:
        //            parameters.Add(paramName, $"%{cond.值1}%");
        //            return $"{fieldSql} LIKE {paramName}";

        //        case 查询操作符.包含某值:
        //            parameters.Add(paramName, cond.值1);
        //            return $"{fieldSql} IN {paramName}";

        //        case 查询操作符.大于:
        //            parameters.Add(paramName, cond.值1);
        //            return $"{fieldSql} > {paramName}";

        //        case 查询操作符.小于:
        //            parameters.Add(paramName, cond.值1);
        //            return $"{fieldSql} < {paramName}";

        //        case 查询操作符.大于等于:
        //            parameters.Add(paramName, cond.值1);
        //            return $"{fieldSql} >= {paramName}";

        //        case 查询操作符.小于等于:
        //            parameters.Add(paramName, cond.值1);
        //            return $"{fieldSql} <= {paramName}";

        //        case 查询操作符.区间:
        //            var startName = $"@{paramToken}_Start";
        //            var endName = $"@{paramToken}_End";
        //            parameters.Add(startName, cond.值1);
        //            parameters.Add(endName, cond.值2);
        //            return $"{fieldSql} BETWEEN {startName} AND {endName}";

        //        default:
        //            return string.Empty;
        //    }
        //}
        #endregion
    }
}
