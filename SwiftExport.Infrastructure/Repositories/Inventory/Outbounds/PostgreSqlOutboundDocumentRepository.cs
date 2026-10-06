using Dapper;
using System.Data;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Inventory.Outbounds;

/// <summary>在调用方会话中持久化出库头行及状态；保持原列和对象回写，不写库存事实或接管联合锁协议。</summary>
public sealed class PostgreSqlOutboundDocumentRepository(RepositoryBaseContext context) : IOutboundDocumentRepository
{
    private const string HeaderColumns = """
        uid AS "Uid", version AS "Version", is_deleted AS "IsDeleted", created_date AS "CreatedDate", created_by AS "CreatedBy", updated_date AS "UpdatedDate", updated_by AS "UpdatedBy", document_no AS "DocumentNo", document_type AS "DocumentType", lifecycle_status AS "LifecycleStatus", posting_status AS "PostingStatus", warehouse_site_id AS "WarehouseSiteId", warehouse_code_snapshot AS "WarehouseCodeSnapshot", warehouse_name_snapshot AS "WarehouseNameSnapshot", shipment_date AS "ShipmentDate", remark AS "Remark", issue_reason AS "IssueReason", trigger_packing_list_id AS "TriggerPackingListId", packing_list_no_snapshot AS "PackingListNoSnapshot", posted_date AS "PostedDate", posted_by AS "PostedBy", reversed_date AS "ReversedDate", reversed_by AS "ReversedBy", reverse_reason AS "ReverseReason", command_id AS "CommandId"
        """;
    private const string LineColumns = """
        uid AS "Uid", version AS "Version", is_deleted AS "IsDeleted", created_date AS "CreatedDate", created_by AS "CreatedBy", updated_date AS "UpdatedDate", updated_by AS "UpdatedBy", outbound_document_id AS "OutboundDocumentId", line_number AS "LineNumber", product_id AS "ProductId", sku_snapshot AS "SkuSnapshot", description_snapshot AS "DescriptionSnapshot", unit_snapshot AS "UnitSnapshot", quantity AS "Quantity", command_item_id AS "CommandItemId", original_line_id AS "OriginalLineId"
        """;

    /// <summary>透传当前会话事务及取消令牌，数据库异常由调用方处理。</summary>
    private CommandDefinition Command(string sql, object? args, CancellationToken token) =>
        new(sql, args, context.Session.Transaction, cancellationToken: token);

    /// <summary>在外层事务中锁定未删除的现有单头；不存在身份不提供范围保护。</summary>
    public Task<OutboundDocument?> LockHeaderAsync(Guid uid, CancellationToken token)
    {
        return context.Session.Connection.QuerySingleOrDefaultAsync<OutboundDocument>(
            Command($"SELECT {HeaderColumns} FROM public.outbound_document WHERE uid=@Uid AND is_deleted=FALSE FOR UPDATE", new { Uid = uid }, token));
    }

    /// <summary>先锁实际父头作为现有集合锚点，再读取并锁定有效明细；缺头不伪造空范围保护。</summary>
    public async Task<IReadOnlyList<OutboundDocumentLine>> LockLinesAsync(Guid uid, CancellationToken token)
    {
        // 父头不加软删过滤，保留源明细查询的独立筛选；缺头时仍读已有行，但没有集合锚点。
        await context.Session.Connection.ExecuteScalarAsync<Guid?>(
            Command("SELECT uid FROM public.outbound_document WHERE uid=@Uid FOR UPDATE", new { Uid = uid }, token));
        return (await context.Session.Connection.QueryAsync<OutboundDocumentLine>(
            Command($"SELECT {LineColumns} FROM public.outbound_document_line WHERE outbound_document_id=@Uid AND is_deleted=FALSE ORDER BY {UuidOrder("uid")} FOR UPDATE", new { Uid = uid }, token))).ToList();
    }

    /// <summary>检查单头或任意所属明细关联的正式流水，保留原查询不排除软删除行或流水的语义。</summary>
    public Task<bool> HasLedgerAsync(Guid uid, CancellationToken token) =>
        context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT EXISTS(SELECT 1 FROM public.inventory_ledger_entry WHERE source_document_id=@Uid)
                OR EXISTS(SELECT 1 FROM public.inventory_ledger_entry e
                    INNER JOIN public.outbound_document_line l ON l.uid=e.source_line_id WHERE l.outbound_document_id=@Uid);
            """, new { Uid = uid }, token));

    /// <summary>保存已校验的头行；expectedVersion 只选择新增或更新，更新谓词仍使用 header.Version，成功仅回写单头版本。</summary>
    public async Task SaveAsync(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines, int expectedVersion, CancellationToken token)
    {
        // 保留源判断：expectedVersion 不替代 header.Version，也不补充新的版本校验。
        if (expectedVersion == 0)
        {
            context.AuditStamp.StampForInsert(header);
            await context.Session.Connection.ExecuteAsync(Command("""
                INSERT INTO public.outbound_document(uid,version,is_deleted,document_no,document_type,lifecycle_status,posting_status,
                    warehouse_site_id,warehouse_code_snapshot,warehouse_name_snapshot,shipment_date,remark,issue_reason,trigger_packing_list_id,command_id,created_date,created_by,updated_date,updated_by)
                VALUES(@Uid,1,FALSE,@DocumentNo,@DocumentType,@LifecycleStatus,@PostingStatus,
                    @WarehouseSiteId,@WarehouseCodeSnapshot,@WarehouseNameSnapshot,@ShipmentDate,@Remark,@IssueReason,NULL,@CommandId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """, Parameters(header, "Uid", "DocumentNo", "DocumentType", "LifecycleStatus", "PostingStatus", "WarehouseSiteId", "WarehouseCodeSnapshot", "WarehouseNameSnapshot", "ShipmentDate", "Remark", "IssueReason", "CommandId", "CreatedDate", "CreatedBy", "UpdatedDate", "UpdatedBy"), token));
            header.Version = 1;
        }
        else
        {
            context.AuditStamp.StampForUpdate(header);
            var changed = await context.Session.Connection.ExecuteAsync(Command("""
                UPDATE public.outbound_document SET warehouse_site_id=@WarehouseSiteId,warehouse_code_snapshot=@WarehouseCodeSnapshot,
                    warehouse_name_snapshot=@WarehouseNameSnapshot,shipment_date=@ShipmentDate,remark=@Remark,issue_reason=@IssueReason,
                    version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
                WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE AND document_type=22 AND trigger_packing_list_id IS NULL AND posting_status=0 AND lifecycle_status=0;
                """, Parameters(header, "WarehouseSiteId", "WarehouseCodeSnapshot", "WarehouseNameSnapshot", "ShipmentDate", "Remark", "IssueReason", "UpdatedDate", "UpdatedBy", "Uid", "Version"), token));
            if (changed != 1) throw new System.Data.DBConcurrencyException("出库草稿发生并发冲突。");
            header.Version++;
        }
        // 整单版本已经锁定；先软删省略行，保留稳定明细 Uid，不重建全部明细身份。
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.outbound_document_line SET is_deleted=TRUE,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
            WHERE outbound_document_id=@Uid AND is_deleted=FALSE AND uid <> ALL(@Ids);
            """, OmittedLineParameters(header, lines), token));
        foreach (var line in lines)
        {
            var isNew = line.Version == 0;
            if (isNew) context.AuditStamp.StampForInsert(line); else context.AuditStamp.StampForUpdate(line);
            var count = await context.Session.Connection.ExecuteAsync(Command(isNew ? """
                INSERT INTO public.outbound_document_line(uid,version,is_deleted,outbound_document_id,line_number,product_id,sku_snapshot,description_snapshot,
                    unit_snapshot,quantity,command_item_id,created_date,created_by,updated_date,updated_by)
                VALUES(@Uid,1,FALSE,@OutboundDocumentId,@LineNumber,@ProductId,@SkuSnapshot,@DescriptionSnapshot,
                    @UnitSnapshot,@Quantity,@CommandItemId,@CreatedDate,@CreatedBy,@UpdatedDate,@UpdatedBy);
                """ : """
                UPDATE public.outbound_document_line SET line_number=@LineNumber,quantity=@Quantity,version=version+1,
                    updated_date=@UpdatedDate,updated_by=@UpdatedBy
                WHERE uid=@Uid AND outbound_document_id=@OutboundDocumentId AND version=@Version AND is_deleted=FALSE;
                """, Parameters(line, isNew ? ["Uid", "OutboundDocumentId", "LineNumber", "ProductId", "SkuSnapshot", "DescriptionSnapshot", "UnitSnapshot", "Quantity", "CommandItemId", "CreatedDate", "CreatedBy", "UpdatedDate", "UpdatedBy"] : ["LineNumber", "Quantity", "UpdatedDate", "UpdatedBy", "Uid", "OutboundDocumentId", "Version"]), token));
            // 源仓储不回写明细 Version/IsDeleted，迁移不改变调用方对象状态。
            if (count != 1) throw new System.Data.DBConcurrencyException("出库明细版本已变化，保存失败。");
        }
    }

    /// <summary>按原资格软删头行并递增数据库版本；完成所有行更新后才递增内存单头版本。</summary>
    public async Task DeleteAsync(OutboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.outbound_document SET is_deleted=TRUE,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
            WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE AND document_type=22 AND trigger_packing_list_id IS NULL AND posting_status=0 AND lifecycle_status=0;
            """, Parameters(header, "UpdatedDate", "UpdatedBy", "Uid", "Version"), token));
        if (count != 1) throw new System.Data.DBConcurrencyException("出库草稿删除发生并发冲突。");
        await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.outbound_document_line SET is_deleted=TRUE,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
            WHERE outbound_document_id=@Uid AND is_deleted=FALSE;
            """, Parameters(header, "UpdatedDate", "UpdatedBy", "Uid"), token));
        header.Version++;
    }

    /// <summary>按原资格写入过账状态及审计；失败抛原并发异常，成功只回写单头版本。</summary>
    public async Task MarkPostedAsync(OutboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.outbound_document SET posting_status=1,posted_date=@PostedDate,posted_by=@PostedBy,
                updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@Uid AND version=@Version AND posting_status=0 AND lifecycle_status=0
                AND document_type=22 AND trigger_packing_list_id IS NULL AND is_deleted=FALSE;
            """, Parameters(header, "PostedDate", "PostedBy", "UpdatedDate", "UpdatedBy", "Uid", "Version"), token));
        if (count != 1) throw new DBConcurrencyException("出库状态或版本已变化。");
        header.Version++;
    }
    /// <summary>按原资格写入反过账状态、原因和审计；不改数量，成功只回写单头版本。</summary>
    public async Task MarkReversedAsync(OutboundDocument header, CancellationToken token)
    {
        context.AuditStamp.StampForUpdate(header);
        var count = await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.outbound_document SET posting_status=8,reversed_date=@ReversedDate,reversed_by=@ReversedBy,
                reverse_reason=@ReverseReason,updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@Uid AND version=@Version AND posting_status=1 AND lifecycle_status=0 AND is_deleted=FALSE
                AND document_type=22 AND trigger_packing_list_id IS NULL;
            """, Parameters(header, "ReversedDate", "ReversedBy", "ReverseReason", "UpdatedDate", "UpdatedBy", "Uid", "Version"), token));
        if (count != 1) throw new DBConcurrencyException("出库状态或版本已变化，反过账未完成。");
        header.Version++;
    }

    /// <summary>只绑定当前 SQL 的实体参数；日期保留墙上时间，枚举按现有整数存储，不修改实体值。</summary>
    private static PostgreSqlParameters Parameters(BaseEntity entity, params string[] properties)
    {
        var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
        var parameters = new PostgreSqlParameters();
        foreach (var property in properties)
        {
            var column = metadata.GetProperty(property);
            var value = column.Property.GetValue(entity);
            var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
            if (type == typeof(DateTime)) parameters.AddTimestamp(property, (DateTime?)value);
            else parameters.Add(property, type.IsEnum && value is not null ? Convert.ToInt32(value) : value);
        }
        return parameters;
    }

    /// <summary>显式绑定 UUID 排除数组；空数组保持省略全部已有有效行的源行为。</summary>
    private static PostgreSqlParameters OmittedLineParameters(OutboundDocument header, IReadOnlyList<OutboundDocumentLine> lines)
    {
        var parameters = Parameters(header, "Uid", "UpdatedDate", "UpdatedBy");
        parameters.AddIds("Ids", lines.Select(line => line.Uid));
        return parameters;
    }

    /// <summary>明细顺序保留 SQL Server uniqueidentifier 字节权重，不使用 PG UUID 原生顺序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";

}

