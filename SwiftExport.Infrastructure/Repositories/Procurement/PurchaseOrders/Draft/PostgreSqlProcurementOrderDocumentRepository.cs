using Dapper;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;

/// <summary>在调用方 PG 事务内读取、创建并保存采购头行，保留原商务白名单和版本结果，不接管业务事务。</summary>
public sealed class PostgreSqlProcurementOrderDocumentRepository(RepositoryBaseContext context) : IProcurementOrderDocumentRepository
{
    private static readonly EntityMetadata Head = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderHead));
    private static readonly EntityMetadata Line = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderLine));
    private static readonly string[] HeadInsertFields = ["Uid", "Version", "IsDeleted", "CreatedDate", "CreatedBy", "UpdatedDate", "UpdatedBy", "OriginKind", "CustomerId", "CustomerCode", "CustomerNameSnapshot", "SupplierId", "SupplierCode", "SupplierName", "OrderNo", "Currency", "DiscountRate", "DiscountAmount", "OrderDate", "ExpectedDate", "Remark", "PackagingRequirements", "ExecutionRequestId", "DemandSourceType", "DemandSourceUid", "DemandSourceNo", "BoNo", "CustomerPoNo", "ReplyDate", "SoPerson", "PoPerson", "InletrHoseType", "AeratorType", "PalletType", "BoxType", "LabelType", "IMType", "CartonType", "LogoType", "ZheKouBeiZhu", "ConfirmationStatus", "LifecycleStatus"];
    private static readonly string[] LineInsertFields = ["Uid", "Version", "IsDeleted", "CreatedDate", "CreatedBy", "UpdatedDate", "UpdatedBy", "PurchaseOrderId", "LineNumber", "OrderNo", "ProductId", "Sku", "UnitSnapshot", "Quantity", "UnlinkedQty", "SupplyType", "MaterialType", "DeliveryTargetSupplierId", "DeliverySupplierCodeSnapshot", "DeliverySupplierNameSnapshot", "UnitPrice", "TaxPrice", "UnitAmount", "TaxAmount", "PlannedDeliveryDate", "SupplierSku", "DescriptionZh", "SubNote", "CustomerSku", "LinkSku", "FinishZh", "FeaturesZh", "SourceType", "SourceNo"];

    /// <summary>读取未删采购头并锁定已有行；不存在身份的并发创建仍须由实际命令及唯一约束共同验收。</summary>
    public Task<PurchaseOrderHead?> GetHeadAsync(Guid uid, CancellationToken ct) =>
        context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(Command($"""
            SELECT {Projection(Head, "h")} FROM {Table(Head)} h
            WHERE {C(Head, "h", "Uid")}=@uid AND {C(Head, "h", "IsDeleted")}=FALSE FOR UPDATE OF h;
            """, new { uid }, ct));

    /// <summary>先锁有效父头，再读取并锁定已有有效明细；原行号及 SqlGuid 顺序不变，不假称 FOR UPDATE 能锁空范围。</summary>
    public async Task<List<PurchaseOrderLine>> GetLinesAsync(Guid uid, CancellationToken ct)
    {
        // 现用草稿保存和确认先持头锁；重复取得父头只作为同一单据已有明细集合的锚点，不新增全局锁。
        await context.Session.Connection.ExecuteAsync(Command($"""
            SELECT {C(Head, "h", "Uid")} FROM {Table(Head)} h
            WHERE {C(Head, "h", "Uid")}=@uid AND {C(Head, "h", "IsDeleted")}=FALSE FOR UPDATE OF h;
            """, new { uid }, ct));
        var rows = await context.Session.Connection.QueryAsync<PurchaseOrderLine>(Command($"""
            SELECT {Projection(Line, "l")} FROM {Table(Line)} l
            JOIN {Table(Head)} h ON {C(Head, "h", "Uid")}={C(Line, "l", "PurchaseOrderId")} AND {C(Head, "h", "IsDeleted")}=FALSE
            WHERE {C(Line, "l", "PurchaseOrderId")}=@uid AND {C(Line, "l", "IsDeleted")}=FALSE
            ORDER BY {C(Line, "l", "LineNumber")},{UuidOrder(C(Line, "l", "Uid"))} FOR UPDATE OF l;
            """, new { uid }, ct));
        return rows.AsList();
    }

    /// <summary>按源独立采购列清单插入头行，保留调用方身份和版本；来源头三列与行两列仍强制写空。</summary>
    public async Task InsertAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        context.AuditStamp.StampForInsert(head);
        await context.Session.Connection.ExecuteAsync(Command(InsertSql(Head, HeadInsertFields, ["DemandSourceType", "DemandSourceUid", "DemandSourceNo"]), PostgreSqlParameters.Entity(head, Head), ct));
        foreach (var line in lines)
        {
            context.AuditStamp.StampForInsert(line);
            await context.Session.Connection.ExecuteAsync(Command(InsertSql(Line, LineInsertFields, ["SourceType", "SourceNo"]), PostgreSqlParameters.Entity(line, Line), ct));
        }
    }

    /// <summary>仅保存原头行商务字段；任一原版本未命中返回相同失败，由调用方回滚，不自行重试或推进实体版本。</summary>
    public async Task<RepositoryWriteResult> SaveAsync(PurchaseOrderHead head, IReadOnlyList<PurchaseOrderLine> lines, CancellationToken ct)
    {
        context.AuditStamp.StampForUpdate(head);
        var headSql = $"""
            UPDATE {Table(Head)} h SET {Assignments(Head, ["Remark", "PackagingRequirements", "ExpectedDate", "UpdatedDate", "UpdatedBy"])},
                {Q(Head, "Version")}={C(Head, "h", "Version")}+1
            WHERE {C(Head, "h", "Uid")}=@Uid AND {C(Head, "h", "Version")}=@Version
                AND {C(Head, "h", "ConfirmationStatus")}=0 AND {C(Head, "h", "LifecycleStatus")}=0 AND {C(Head, "h", "IsDeleted")}=FALSE;
            """;
        if (await context.Session.Connection.ExecuteAsync(Command(headSql, PostgreSqlParameters.Entity(head, Head), ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, "CONCURRENCY_CONFLICT", "采购头并发冲突。");
        foreach (var line in lines)
        {
            context.AuditStamp.StampForUpdate(line);
            var lineSql = $"""
                UPDATE {Table(Line)} l SET {Assignments(Line, ["SupplierSku", "DescriptionZh", "SubNote", "UnitPrice", "TaxPrice", "UnitAmount", "TaxAmount", "PlannedDeliveryDate", "UpdatedDate", "UpdatedBy"])},
                    {Q(Line, "Version")}={C(Line, "l", "Version")}+1
                FROM {Table(Head)} h WHERE {C(Head, "h", "Uid")}={C(Line, "l", "PurchaseOrderId")}
                    AND {C(Line, "l", "Uid")}=@Uid AND {C(Line, "l", "PurchaseOrderId")}=@PurchaseOrderId
                    AND {C(Line, "l", "Version")}=@Version AND {C(Line, "l", "IsDeleted")}=FALSE
                    AND {C(Head, "h", "IsDeleted")}=FALSE AND {C(Head, "h", "ConfirmationStatus")}=0 AND {C(Head, "h", "LifecycleStatus")}=0;
                """;
            if (await context.Session.Connection.ExecuteAsync(Command(lineSql, PostgreSqlParameters.Entity(line, Line), ct)) != 1)
                return RepositoryWriteResult.Fail(line.Uid, line.Version, "CONCURRENCY_CONFLICT", "采购行并发冲突。");
        }
        return RepositoryWriteResult.Ok(head.Uid, head.Version, head.Version + 1);
    }

    /// <summary>保存原确认状态、批次和审计字段；保持源状态与版本谓词及结构化失败，不补建确认组成。</summary>
    public async Task<RepositoryWriteResult> ConfirmAsync(PurchaseOrderHead head, CancellationToken ct)
    {
        context.AuditStamp.StampForUpdate(head);
        var sql = $"""
            UPDATE {Table(Head)} h SET {Q(Head, "ConfirmationStatus")}=1,
                {Assignments(Head, ["ActiveConfirmationId", "ConfirmedDate", "ConfirmedBy", "UpdatedDate", "UpdatedBy"])},
                {Q(Head, "Version")}={C(Head, "h", "Version")}+1
            WHERE {C(Head, "h", "Uid")}=@Uid AND {C(Head, "h", "Version")}=@Version
                AND {C(Head, "h", "ConfirmationStatus")}=0 AND {C(Head, "h", "LifecycleStatus")}=0 AND {C(Head, "h", "IsDeleted")}=FALSE;
            """;
        if (await context.Session.Connection.ExecuteAsync(Command(sql, PostgreSqlParameters.Entity(head, Head), ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, "CONCURRENCY_CONFLICT", "采购确认并发冲突。");
        return RepositoryWriteResult.Ok(head.Uid, head.Version, head.Version + 1);
    }

    /// <summary>沿原采购持久化边界要求已有事务，透传取消令牌，不创建连接或事务。</summary>
    private CommandDefinition Command(string sql, object args, CancellationToken ct) => new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购持久化需要事务。"), cancellationToken: ct);

    /// <summary>源 SELECT * 的实体范围由同一结构元数据显式投影，Quantity 使用实际 PG 列映射。</summary>
    private static string Projection(EntityMetadata map, string alias) => string.Join(',', map.Columns.Select(column => alias + "." + PostgreSqlNames.Quote(column.ColumnName) + " AS " + PostgreSqlNames.Quote(column.PropertyName)));

    /// <summary>仅原独立采购固定字段可写，强制空列不使用实体内的来源值。</summary>
    private static string InsertSql(EntityMetadata map, string[] fields, string[] nullFields) => $"INSERT INTO {Table(map)} ({string.Join(',', fields.Select(field => Q(map, field)))}) VALUES ({string.Join(',', fields.Select(field => nullFields.Contains(field) ? "NULL" : "@" + map.GetProperty(field).ParameterName))});";

    /// <summary>固定商务白名单使用 CLR 参数名称，不包含身份、源关联或客户端 SQL。</summary>
    private static string Assignments(EntityMetadata map, string[] fields) => string.Join(',', fields.Select(field => Q(map, field) + "=@" + map.GetProperty(field).ParameterName));

    /// <summary>限定结构元数据提供的 schema 和表名。</summary>
    private static string Table(EntityMetadata map) => PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);

    /// <summary>返回固定属性的转义物理列名。</summary>
    private static string Q(EntityMetadata map, string property) => PostgreSqlNames.Quote(map.GetProperty(property).ColumnName);

    /// <summary>固定表别名只用于本类生成 SQL。</summary>
    private static string C(EntityMetadata map, string alias, string property) => alias + "." + Q(map, property);

    /// <summary>维持源 uniqueidentifier 排序权重。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}
