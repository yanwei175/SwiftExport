using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.QualityControl.AfterSales.Lookup;

/// <summary>实现售后七类白名单候选分页及引用核验，借用当前会话和事务，不裁决额外业务资格。</summary>
public sealed class PostgreSqlAfterSalesLookupRepository(RepositoryBaseContext context) : IAfterSalesLookupRepository
{
    /// <summary>按原活动白名单和客户上下文分页读取候选，保留产品快照、空值排序及取消，不创建事务或加锁。</summary>
    public async Task<PagedResponseBase<AfterSalesLookupDto>> LookupAsync(
        string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken)
    {
        var source = ReferenceSql(kind, true);
        var page = Math.Max(1, request.PageIndex);
        var size = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, 200);
        var columns = "q.\"Uid\",q.\"Code\",q.\"Name\"" + (kind == "products"
            ? ",q.\"CustomerSku\",q.\"LinkSku\",q.\"Unit\",q.\"ColorId\",q.\"ColorName\",q.\"Specification\"" : "");
        // 保留原始 %/_ 通配符和反斜杠文本；不套用会改变原查询输入语义的转义模式生成器。
        const string where = """
            WHERE (@Keyword IS NULL OR q."Code" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE ''
                OR q."Name" COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '')
            """;
        var sql = $"""
            SELECT COUNT(*) FROM ({source}) q {where};
            SELECT {columns} FROM ({source}) q {where}
            ORDER BY rtrim(q."Code",' ') COLLATE public.erp_dictionary_ci ASC NULLS FIRST,
                {SqlGuidOrder("q.\"Uid\"")} ASC
            LIMIT @Take OFFSET @Skip;
            """;
        var parameters = new DynamicParameters();
        parameters.Add("CustomerId", request.CustomerId, DbType.Guid);
        parameters.Add("Keyword", string.IsNullOrWhiteSpace(request.Keyword) ? null : "%" + request.Keyword.Trim() + "%", DbType.String);
        parameters.Add("Skip", (page - 1) * size, DbType.Int32);
        parameters.Add("Take", size, DbType.Int32);
        using var reader = await context.Session.Connection.QueryMultipleAsync(new CommandDefinition(
            sql, parameters, context.Session.Transaction, cancellationToken: cancellationToken));
        var count = await reader.ReadSingleAsync<int>();
        return new()
        {
            PageIndex = page,
            PageSize = size,
            TotalCount = count,
            Items = (await reader.ReadAsync<AfterSalesLookupDto>()).ToList()
        };
    }

    /// <summary>按原精确类型和身份读取引用；缺失返回 null，保留历史资格、父事务及单行异常，不创建事务或加锁。</summary>
    public Task<AfterSalesLookupDto?> ReferenceAsync(string kind, Guid uid, Guid? customerId, bool requireActive)
    {
        var source = ReferenceSql(kind, requireActive);
        var columns = "q.\"Uid\",q.\"Code\",q.\"Name\"" + (kind == "products"
            ? ",q.\"CustomerSku\",q.\"LinkSku\",q.\"Unit\",q.\"ColorId\",q.\"ColorName\",q.\"Specification\"" : "");
        return context.Session.Connection.QuerySingleOrDefaultAsync<AfterSalesLookupDto>(
            $"SELECT {columns} FROM ({source}) q WHERE q.\"Uid\"=@uid",
            new ReferenceParameters(uid, customerId), context.Session.Transaction);
    }

    /// <summary>只允许固定实体分支；品质分类从独立树表读取，父级和末级同等准入，停用仅阻断新引用。</summary>
    private static string ReferenceSql(string kind, bool active) => kind switch
    {
        "customers" => $"SELECT c.\"uid\" AS \"Uid\",c.\"customer_code\" AS \"Code\",c.\"customer_name\" AS \"Name\" FROM \"public\".\"customer\" c WHERE c.\"is_deleted\"=FALSE{Active("c", active)}",
        "suppliers" => $"SELECT s.\"uid\" AS \"Uid\",s.\"supplier_code\" AS \"Code\",s.\"supplier_name\" AS \"Name\" FROM \"public\".\"supplier\" s WHERE s.\"is_deleted\"=FALSE{Active("s", active)}",
        "owners" => $"SELECT u.\"uid\" AS \"Uid\",u.\"user_name\" AS \"Code\",COALESCE(u.\"nick_name\",u.\"user_name\") AS \"Name\" FROM \"public\".\"sys_user\" u WHERE u.\"is_deleted\"=FALSE{Active("u", active)}",
        "colors" => $"SELECT c.\"uid\" AS \"Uid\",c.\"color_code\" AS \"Code\",COALESCE(c.\"color_name_en\",c.\"color_name_zh\") AS \"Name\" FROM \"public\".\"product_color\" c WHERE c.\"is_deleted\"=FALSE",
        "quality-categories" => $"""
            SELECT d."uid" AS "Uid",
            COALESCE(NULLIF(btrim(d."category_code"),''),NULLIF(btrim(d."fake_name"),''),d."category_name") AS "Code",
            d."category_name" AS "Name" FROM "public"."after_sales_quality_category" d WHERE d."is_deleted"=FALSE{(active ? $" AND d.\"is_enabled\"=TRUE" : "")}
        """,
        "sales-order-lines" => """
            SELECT l."uid" AS "Uid",
            CONCAT(h."order_no",'/',l."line_number") AS "Code",
            l."sku" AS "Name" FROM "public"."sales_order_line" l INNER JOIN "public"."sales_order_header" h ON h."uid"=l."sales_order_id" WHERE l."is_deleted"=FALSE AND h."is_deleted"=FALSE AND h."customer_id"=@CustomerId
        """,
        "products" => ProductSql(active),
        _ => throw new ArgumentException("不支持的售后候选类型。", nameof(kind))
    };

    /// <summary>当前型号取物料自身 LinkSku，不依赖中性产品；颜色及客户关系的历史显示和首条顺序沿用原规则。</summary>
    private static string ProductSql(bool active) => $"""
        SELECT p."uid" AS "Uid",p."sku" AS "Code",
          COALESCE(p."description_zh",p."description_en",'') AS "Name",
          p."link_sku" AS "LinkSku",p."unit" AS "Unit",
          p."color_id" AS "ColorId",
          COALESCE(c."color_name_en",c."color_name_zh") AS "ColorName",
          COALESCE(p."features_zh",p."features_en") AS "Specification",cp."CustomerSku"
        FROM "public"."product" p LEFT JOIN "public"."product_color" c ON c."uid"=p."color_id"
        LEFT JOIN LATERAL (
          SELECT r."customer_sku" AS "CustomerSku" FROM "public"."customer_product" r
          WHERE r."sku_id"=p."uid" AND r."customer_id"=@CustomerId
            AND r."is_deleted"=FALSE AND r."is_active"=TRUE
          ORDER BY {SqlGuidOrder("r.\"uid\"")} LIMIT 1
        ) cp ON TRUE WHERE p."is_deleted"=FALSE{Active("p", active)}
        """;

    /// <summary>活动条件只用于有 IsActive 的固定实体，不改变 NULL/软删除资格。</summary>
    private static string Active(string alias, bool active) => active ? $" AND {alias}.\"is_active\"=TRUE" : "";


    /// <summary>uuid_send 为网络字节序，按已冻结 SqlGuid 比较字节顺序重排，避免原生 uuid 首条差异。</summary>
    private static string SqlGuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(i => $"get_byte(uuid_send({column}),{i})")) + "]";

    /// <summary>本切片两个标量明确 uuid 类型，包括 NULL 客户；不修改公共参数框架。</summary>
    private sealed class ReferenceParameters(Guid uid, Guid? customerId) : SqlMapper.IDynamicParameters
    {
        /// <summary>每个命令独立拥有参数，NULL 转 DBNull 而不让驱动猜测类型。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            command.Parameters.Add(new NpgsqlParameter("uid", NpgsqlDbType.Uuid) { Value = uid });
            command.Parameters.Add(new NpgsqlParameter("CustomerId", NpgsqlDbType.Uuid) { Value = customerId.HasValue ? customerId.Value : DBNull.Value });
        }
    }
}
