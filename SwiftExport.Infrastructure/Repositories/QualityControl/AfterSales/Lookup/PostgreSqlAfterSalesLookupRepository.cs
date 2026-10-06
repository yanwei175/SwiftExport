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
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
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
        return new() { PageIndex = page, PageSize = size, TotalCount = count,
            Items = (await reader.ReadAsync<AfterSalesLookupDto>()).ToList() };
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

    /// <summary>只允许固定实体分支；活动条件仅按原类型规则拼接，不修剪 kind 或裁决额外业务资格。</summary>
    private static string ReferenceSql(string kind, bool active) => kind switch
    {
        "customers" => $"SELECT {C<Customer>("c", "Uid")} AS \"Uid\",{C<Customer>("c", "CustomerCode")} AS \"Code\",{C<Customer>("c", "CustomerName")} AS \"Name\" FROM {Table<Customer>()} c WHERE {C<Customer>("c", "IsDeleted")}=FALSE{Active<Customer>("c", active)}",
        "suppliers" => $"SELECT {C<Supplier>("s", "Uid")} AS \"Uid\",{C<Supplier>("s", "SupplierCode")} AS \"Code\",{C<Supplier>("s", "SupplierName")} AS \"Name\" FROM {Table<Supplier>()} s WHERE {C<Supplier>("s", "IsDeleted")}=FALSE{Active<Supplier>("s", active)}",
        "owners" => $"SELECT {C<SysUser>("u", "Uid")} AS \"Uid\",{C<SysUser>("u", "UserName")} AS \"Code\",COALESCE({C<SysUser>("u", "NickName")},{C<SysUser>("u", "UserName")}) AS \"Name\" FROM {Table<SysUser>()} u WHERE {C<SysUser>("u", "IsDeleted")}=FALSE{Active<SysUser>("u", active)}",
        "colors" => $"SELECT {C<ProductColor>("c", "Uid")} AS \"Uid\",{C<ProductColor>("c", "ColorCode")} AS \"Code\",COALESCE({C<ProductColor>("c", "ColorNameEn")},{C<ProductColor>("c", "ColorNameZh")}) AS \"Name\" FROM {Table<ProductColor>()} c WHERE {C<ProductColor>("c", "IsDeleted")}=FALSE",
        "quality-categories" => $"SELECT {C<SysDictData>("d", "Uid")} AS \"Uid\",COALESCE({C<SysDictData>("d", "DataNameEn")},{C<SysDictData>("d", "DataNameZh")}) AS \"Code\",COALESCE({C<SysDictData>("d", "DataNameZh")},{C<SysDictData>("d", "DataNameEn")}) AS \"Name\" FROM {Table<SysDictData>()} d WHERE {C<SysDictData>("d", "IsDeleted")}=FALSE AND rtrim({C<SysDictData>("d", "DictCode")},' ') COLLATE public.erp_dictionary_ci = 'AfterSalesQualityCategory' COLLATE public.erp_dictionary_ci{(active ? $" AND {C<SysDictData>("d", "IsEnabled")}=TRUE" : "")}",
        "sales-order-lines" => $"SELECT {C<SalesOrderLine>("l", "Uid")} AS \"Uid\",CONCAT({C<SalesOrderHeader>("h", "OrderNo")},'/',{C<SalesOrderLine>("l", "LineNumber")}) AS \"Code\",{C<SalesOrderLine>("l", "Sku")} AS \"Name\" FROM {Table<SalesOrderLine>()} l INNER JOIN {Table<SalesOrderHeader>()} h ON {C<SalesOrderHeader>("h", "Uid")}={C<SalesOrderLine>("l", "SalesOrderId")} WHERE {C<SalesOrderLine>("l", "IsDeleted")}=FALSE AND {C<SalesOrderHeader>("h", "IsDeleted")}=FALSE AND {C<SalesOrderHeader>("h", "CustomerId")}=@CustomerId",
        "products" => ProductSql(active),
        _ => throw new ArgumentException("不支持的售后候选类型。", nameof(kind))
    };

    /// <summary>颜色资格不影响历史显示；客户关系始终活动，LATERAL 保留缺关系及 SQL Server Guid 顺序的首条。</summary>
    private static string ProductSql(bool active) => $"""
        SELECT {C<Product>("p", "Uid")} AS "Uid",{C<Product>("p", "Sku")} AS "Code",
          COALESCE({C<Product>("p", "DescriptionZh")},{C<Product>("p", "DescriptionEn")},'') AS "Name",
          {C<Product>("p", "LinkSku")} AS "LinkSku",{C<Product>("p", "Unit")} AS "Unit",
          {C<Product>("p", "ColorId")} AS "ColorId",
          COALESCE({C<ProductColor>("c", "ColorNameEn")},{C<ProductColor>("c", "ColorNameZh")}) AS "ColorName",
          COALESCE({C<Product>("p", "FeaturesZh")},{C<Product>("p", "FeaturesEn")}) AS "Specification",cp."CustomerSku"
        FROM {Table<Product>()} p LEFT JOIN {Table<ProductColor>()} c ON {C<ProductColor>("c", "Uid")}={C<Product>("p", "ColorId")}
        LEFT JOIN LATERAL (
          SELECT {C<CustomerProduct>("r", "CustomerSku")} AS "CustomerSku" FROM {Table<CustomerProduct>()} r
          WHERE {C<CustomerProduct>("r", "SkuId")}={C<Product>("p", "Uid")} AND {C<CustomerProduct>("r", "CustomerId")}=@CustomerId
            AND {C<CustomerProduct>("r", "IsDeleted")}=FALSE AND {C<CustomerProduct>("r", "IsActive")}=TRUE
          ORDER BY {SqlGuidOrder(C<CustomerProduct>("r", "Uid"))} LIMIT 1
        ) cp ON TRUE WHERE {C<Product>("p", "IsDeleted")}=FALSE{Active<Product>("p", active)}
        """;

    /// <summary>活动条件只用于有 IsActive 的固定实体，不改变 NULL/软删除资格。</summary>
    private static string Active<T>(string alias, bool active) => active ? $" AND {C<T>(alias, "IsActive")}=TRUE" : "";

    /// <summary>固定 CLR 实体通过共享映射取得物理表名，不使用用户输入作为标识符。</summary>
    private static string Table<T>()
    {
        var map = PostgreSqlEntityMetadata.GetMetadata(typeof(T));
        return PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    }

    /// <summary>列标识只来自固定实体属性，查询别名继续使用 DTO 属性名。</summary>
    private static string C<T>(string alias, string property) => alias + "." +
        PostgreSqlNames.Quote(PostgreSqlEntityMetadata.GetMetadata(typeof(T)).GetProperty(property).ColumnName);

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
