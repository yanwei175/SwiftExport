using Dapper;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.SalesOrders.Document;

/// <summary>借用PG会话持久化销售行，保留请求身份、行号暂存及逐行版本契约；整体事务由应用用例拥有。</summary>
public sealed class PostgreSqlSalesOrderLineRepository(RepositoryBaseContext context)
    : PostgreSqlDapperRepository<SalesOrderLine>(context), ISalesOrderLineRepository
{
    private static readonly string LineTable = PostgreSqlNames.Quote(Metadata.SchemaName) + "." + PostgreSqlNames.Quote(Metadata.TableName);
    private const string LineProjection = """
        "uid" AS "Uid",
        "version" AS "Version",
        "is_deleted" AS "IsDeleted",
        "created_date" AS "CreatedDate",
        "created_by" AS "CreatedBy",
        "updated_date" AS "UpdatedDate",
        "updated_by" AS "UpdatedBy",
        "line_number" AS "LineNumber",
        "sales_order_id" AS "SalesOrderId",
        "order_no" AS "OrderNo",
        "is_non_standard" AS "IsNonStandard",
        "product_id" AS "ProductId",
        "customer_product_id" AS "CustomerProductId",
        "sku" AS "Sku",
        "link_sku" AS "LinkSku",
        "customer_sku" AS "CustomerSku",
        "description_en" AS "DescriptionEn",
        "color_name_en" AS "ColorNameEn",
        "features_en" AS "FeaturesEn",
        "cny_price" AS "CnyPrice",
        "usd_price" AS "UsdPrice",
        "qty" AS "Qty",
        "unit_snapshot" AS "UnitSnapshot",
        "cny_amount" AS "CnyAmount",
        "usd_amount" AS "UsdAmount",
        "pcs_of_carton" AS "PcsOfCarton",
        "nw_of_carton" AS "NwOfCarton",
        "gw_of_carton" AS "GwOfCarton",
        "cbm_of_carton" AS "CbmOfCarton",
        "ctns" AS "Ctns",
        "nw_sub_total" AS "NwSubTotal",
        "gw_sub_total" AS "GwSubTotal",
        "cbm_sub_total" AS "CbmSubTotal",
        "sub_note" AS "SubNote",
        "expected_delivery_date" AS "ExpectedDeliveryDate",
        "main_image" AS "MainImage"
    """;
    private static readonly string UidOrder = "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }
        .Select(offset => $"get_byte(uuid_send(uid),{offset})")) + "]";

    /// <summary>完整读取有效行并按源行号/SqlGuid排序；更新锁要求调用方事务，仅保护现存返回行。</summary>
    public async Task<IReadOnlyList<SalesOrderLine>> GetLinesByOrderIdAsync(Guid orderId, bool withUpdLock = false)
    {
        var lockHint = GetLockHint(withUpdLock);
        var sql = $"SELECT {LineProjection} FROM {LineTable} WHERE sales_order_id=@OrderId AND is_deleted=FALSE ORDER BY line_number,{UidOrder}{lockHint}";
        return (await Connection.QueryAsync<SalesOrderLine>(sql, new { OrderId = orderId }, transaction: Transaction)).ToList();
    }

    /// <summary>保留非空请求Uid及正Version，审计只补缺值；不调用会重置Uid的泛型Add。</summary>
    public override async Task<RepositoryWriteResult> AddAsync(SalesOrderLine entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        AuditStamp.StampForInsert(entity);
        entity.IsDeleted = false;
        if (entity.Uid == Guid.Empty) entity.Uid = GuidSequentialGenerator.NewGuid();
        if (entity.Version <= 0) entity.Version = 1;
        var columns = string.Join(',', InsertProperties.Select(property => PostgreSqlNames.Quote(Metadata.GetProperty(property).ColumnName)));
        var values = string.Join(',', InsertProperties.Select(property => "@" + Metadata.GetProperty(property).ParameterName));
        var rows = await Connection.ExecuteAsync($"INSERT INTO {LineTable} ({columns}) VALUES ({values})",
            PostgreSqlParameters.Entity(entity, Metadata), transaction: Transaction);
        return rows == 1
            ? RepositoryWriteResult.Ok(entity.Uid, null, entity.Version, "新增销售订单行成功。")
            : RepositoryWriteResult.Fail(entity.Uid, null, "INSERT_FAILED", "新增销售订单行失败。");
    }

    /// <summary>先观察MAX再更新当前有效范围，不推进版本或审计；返回真实UPDATE行数供用例核对完整行集合。</summary>
    public async Task<int> StageLineNumbersAsync(Guid orderId, int finalLineCount)
    {
        var currentMax = await Connection.ExecuteScalarAsync<int>($"SELECT COALESCE(MAX(line_number),0) FROM {LineTable} WHERE sales_order_id=@OrderId AND is_deleted=FALSE",
            new { OrderId = orderId }, transaction: Transaction);
        var offset = (long)currentMax + (long)finalLineCount + 1;
        // 翻译源THROW 52720：先做long上界门禁再转int，保留原消息；本地异常不自行使PG事务aborted，外层负责失败回滚。
        if (offset + (long)currentMax > int.MaxValue)
            throw new InvalidOperationException("销售订单行号暂存超出 int 范围。");
        return await Connection.ExecuteAsync($"UPDATE {LineTable} SET line_number=line_number+@Offset WHERE sales_order_id=@OrderId AND is_deleted=FALSE",
            new { OrderId = orderId, Offset = (int)offset }, transaction: Transaction);
    }

    /// <summary>按输入枚举顺序逐条新增并保留每行结果，普通失败继续，技术异常直接传播。</summary>
    public async Task<IReadOnlyList<RepositoryWriteResult>> AddLinesAsync(IEnumerable<SalesOrderLine> lines)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var line in lines) results.Add(await AddAsync(line));
        return results;
    }

    /// <summary>按输入顺序调用既有泛型Update，保留实体预推进Version及现有返回契约，不接管批量事务。</summary>
    public async Task<IReadOnlyList<RepositoryWriteResult>> UpdateLinesAsync(IEnumerable<SalesOrderLine> lines)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var line in lines) results.Add(await UpdateAsync(line));
        return results;
    }

    /// <summary>按Uid及原Version逐行软删并推进版本，与不推进版本的继承Delete保持职责区别。</summary>
    public async Task<IReadOnlyList<RepositoryWriteResult>> DeleteLinesAsync(IEnumerable<SalesOrderDeletedLineDto> lines)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var line in lines)
        {
            var parameters = DeleteParameters();
            parameters.Add("Uid", line.Uid); parameters.Add("Version", line.Version);
            var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>($"""
                UPDATE {LineTable} SET is_deleted=TRUE,updated_date=@Now,updated_by=@UserName,version=version+1
                WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE RETURNING version;
                """, parameters, transaction: Transaction);
            results.Add(newVersion.HasValue
                ? RepositoryWriteResult.Ok(line.Uid, line.Version, newVersion.Value, "删除销售订单行成功。")
                : RepositoryWriteResult.Fail(line.Uid, line.Version, "CONCURRENCY_CONFLICT", "删除销售订单行失败，数据已被其他用户修改。"));
        }
        return results;
    }

    /// <summary>软删订单当前全部有效行并推进各行版本，返回影响行数；不删除来源或接管草稿状态校验。</summary>
    public Task<int> DeleteByOrderIdAsync(Guid orderId)
    {
        var parameters = DeleteParameters(); parameters.Add("OrderId", orderId);
        return Connection.ExecuteAsync($"""
            UPDATE {LineTable} SET is_deleted=TRUE,updated_date=@Now,updated_by=@UserName,version=version+1
            WHERE sales_order_id=@OrderId AND is_deleted=FALSE;
            """, parameters, transaction: Transaction);
    }

    /// <summary>每次删除取一次本地审计时间，显式绑定无时区墙上时间及当前用户名。</summary>
    private PostgreSqlParameters DeleteParameters()
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddTimestamp("Now", DateTime.Now); parameters.Add("UserName", CurrentUser.UserName);
        return parameters;
    }
}
