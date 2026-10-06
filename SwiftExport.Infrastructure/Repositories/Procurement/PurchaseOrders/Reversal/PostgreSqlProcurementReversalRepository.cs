using Dapper;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Reversal;

/// <summary>采购确认、反向和补关联共用的 PostgreSQL 事实锁及反向持久化；保持原锁序和副作用，不接管联合准备或事务。</summary>
public sealed class PostgreSqlProcurementReversalRepository(RepositoryBaseContext context) : IProcurementReversalRepository
{
    /// <summary>保持原明确事务门禁，透传取消；不自行开启事务或修改隔离级别。</summary>
    private CommandDefinition Command(string sql, object? args, CancellationToken ct) =>
        new(sql, args, context.Session.Transaction ?? throw new InvalidOperationException("采购事实锁定需要事务。"), cancellationToken: ct);

    /// <summary>按原来源到下游顺序锁定完整事实，集合冲突不暴露部分结果。</summary>
    public Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockAsync(Guid uid, CancellationToken ct) =>
        LockCoreAsync(uid, [], true, ct);

    /// <summary>合并待选物料来源后复用相同锁序，补关联不读取收货、库存或供应商报告。</summary>
    public Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockForAssociationAsync(Guid uid, IReadOnlyCollection<Guid> netDemandIds, CancellationToken ct) =>
        LockCoreAsync(uid, netDemandIds, false, ct);

    /// <summary>先发现来源，再锁来源、需求、Link、采购头行及可选下游；原归属及集合重验不补锁新来源。</summary>
    private async Task<(ProcurementReversalFacts? Facts, ServiceErrorDto? Error)> LockCoreAsync(Guid uid, IReadOnlyCollection<Guid> netDemandIds, bool includeDownstream, CancellationToken ct)
    {
        var discoverParameters = Ids("netDemandIds", netDemandIds);
        discoverParameters.Add("uid", uid);
        // PG 高隔离无法将 SQL Server READCOMMITTED 提示等价翻译；沿当前事务观察，联合准备由主控补验。
        const string discover = """
            SELECT DISTINCT n.gross_demand_id FROM public.demand_execution_link l
            JOIN public.net_demand n ON n.uid=l.net_demand_id
            WHERE rtrim(l.target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                AND (l.target_head_id=@uid OR l.target_line_id IN (SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@uid))
            UNION SELECT gross_demand_id FROM public.net_demand WHERE uid=ANY(@netDemandIds);
            """;
        var sourceIds = (await context.Session.Connection.QueryAsync<Guid>(Command(discover, discoverParameters, ct))).OrderBy(id => id).ToArray();
        var facts = new ProcurementReversalFacts();
        foreach (var id in sourceIds)
        {
            var source = await context.Session.Connection.QuerySingleOrDefaultAsync<GrossDemand>(Command(
                $"SELECT {Projection<GrossDemand>()} FROM public.gross_demand WHERE uid=@id FOR UPDATE", new { id }, ct));
            if (source is null) throw new InvalidOperationException("采购来源需求缺失，不能完成操作。");
            facts.Sources.Add(source);
        }
        foreach (var id in sourceIds)
            facts.Demands.AddRange(await context.Session.Connection.QueryAsync<NetDemand>(Command(
                $"SELECT {Projection<NetDemand>()} FROM public.net_demand WHERE gross_demand_id=@id ORDER BY {UuidOrder("uid")} FOR UPDATE", new { id }, ct)));
        var demandIds = facts.Demands.Select(demand => demand.Uid).ToArray();
        if (demandIds.Length > 0)
            facts.Links.AddRange(await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(
                $"SELECT {Projection<DemandExecutionLink>()} FROM public.demand_execution_link WHERE net_demand_id=ANY(@demandIds) ORDER BY {UuidOrder("uid")} FOR UPDATE",
                Ids("demandIds", demandIds), ct)));

        // 不依赖 NetDemand JOIN，完整读取本单坏引用，禁止把损坏关系当作独立采购。
        var ownLinkSql = $"""
            SELECT {Projection<DemandExecutionLink>()} FROM public.demand_execution_link
            WHERE rtrim(target_type,' ') COLLATE public.erp_dictionary_ci='PO'
                AND (target_head_id=@uid OR target_line_id IN (SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@uid))
            ORDER BY {UuidOrder("uid")} FOR UPDATE;
            """;
        var ownLinks = (await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(ownLinkSql, new { uid }, ct))).ToList();
        if (ownLinks.Any(link => !demandIds.Contains(link.NetDemandId)))
            return (null, new ServiceErrorDto { Code = "CONCURRENCY_CONFLICT", Message = "采购引用已变化或物料来源缺失，请重新预览。", Field = "ExpectedVersion" });
        facts.Links = facts.Links.Concat(ownLinks).DistinctBy(link => link.Uid).ToList();
        facts.Head = await context.Session.Connection.QuerySingleOrDefaultAsync<PurchaseOrderHead>(Command(
            $"SELECT {Projection<PurchaseOrderHead>()} FROM public.purchase_order_head WHERE uid=@uid FOR UPDATE", new { uid }, ct));
        facts.Lines = (await context.Session.Connection.QueryAsync<PurchaseOrderLine>(Command(
            $"SELECT {Projection<PurchaseOrderLine>("l")} FROM public.purchase_order_line l WHERE l.purchase_order_id=@uid ORDER BY {UuidOrder("l.uid")} FOR UPDATE OF l", new { uid }, ct))).ToList();
        var finalLinks = (await context.Session.Connection.QueryAsync<DemandExecutionLink>(Command(ownLinkSql, new { uid }, ct))).ToList();
        if (!ownLinks.Select(link => (link.Uid, link.Version, link.NetDemandId, link.TargetLineId)).ToHashSet()
            .SetEquals(finalLinks.Select(link => (link.Uid, link.Version, link.NetDemandId, link.TargetLineId))))
            return (null, new ServiceErrorDto { Code = "CONCURRENCY_CONFLICT", Message = "锁定过程中采购引用集合发生变化，请重新预览。", Field = "ExpectedVersion" });
        if (!includeDownstream) return (facts, null);

        // 可空采购行侧在 LATERAL 内共享锁实际行，外层只锁非空头行；不对外连接或 EXISTS 结果直接加更新锁。
        facts.HasUnattributedInbound = await context.Session.Connection.ExecuteScalarAsync<bool>(Command("""
            SELECT EXISTS(SELECT 1 FROM public.inbound_document h
                JOIN public.inbound_document_line l ON l.inbound_document_id=h.uid
                LEFT JOIN LATERAL (SELECT p.uid FROM public.purchase_order_line p WHERE p.uid=l.source_line_id FOR SHARE) p ON TRUE
                WHERE h.is_deleted=FALSE AND l.is_deleted=FALSE
                    AND ((h.posting_status IN(1,8) AND h.document_type IN(11,12))
                        OR (h.document_type=10 AND p.uid IS NULL AND (h.posting_status IN(1,8) OR h.lifecycle_status=0)))
                FOR SHARE OF h,l);
            """, null, ct));
        var lineIds = facts.Lines.Select(line => line.Uid).ToArray();
        if (lineIds.Length == 0) return (facts, null);
        facts.Inbounds = (await context.Session.Connection.QueryAsync<InboundDocument>(Command($"""
            SELECT {Projection<InboundDocument>("h")} FROM public.inbound_document h
            WHERE EXISTS(SELECT 1 FROM public.inbound_document_line l
                WHERE l.inbound_document_id=h.uid AND l.source_line_id=ANY(@lineIds) FOR SHARE)
            ORDER BY {UuidOrder("h.uid")} FOR UPDATE OF h;
            """, Ids("lineIds", lineIds), ct))).ToList();
        var inboundIds = facts.Inbounds.Select(inbound => inbound.Uid).ToArray();
        if (inboundIds.Length > 0)
        {
            facts.InboundLines = (await context.Session.Connection.QueryAsync<InboundDocumentLine>(Command(
                $"SELECT {Projection<InboundDocumentLine>()} FROM public.inbound_document_line WHERE inbound_document_id=ANY(@inboundIds) ORDER BY {UuidOrder("uid")} FOR UPDATE",
                Ids("inboundIds", inboundIds), ct))).ToList();
            facts.Ledgers = (await context.Session.Connection.QueryAsync<InventoryLedgerEntry>(Command($"""
                SELECT {Projection<InventoryLedgerEntry>("e")} FROM public.inventory_ledger_entry e
                WHERE e.source_document_id=ANY(@inboundIds)
                    OR e.source_line_id IN (SELECT uid FROM public.inbound_document_line WHERE inbound_document_id=ANY(@inboundIds))
                    OR e.reversal_of_ledger_id IN (SELECT uid FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@inboundIds))
                    OR e.replacement_of_ledger_id IN (SELECT uid FROM public.inventory_ledger_entry WHERE source_document_id=ANY(@inboundIds))
                FOR SHARE OF e;
                """, Ids("inboundIds", inboundIds), ct))).ToList();
        }
        facts.Reports = (await context.Session.Connection.QueryAsync<SupplierProgressReport>(Command(
            $"SELECT {Projection<SupplierProgressReport>()} FROM public.supplier_progress_report WHERE purchase_order_line_id=ANY(@lineIds) AND is_deleted=FALSE ORDER BY {UuidOrder("purchase_order_line_id")},sequence ASC NULLS FIRST FOR SHARE",
            Ids("lineIds", lineIds), ct))).ToList();
        return (facts, null);
    }

    /// <summary>在原事务内应用已校验反向；失败返回原写入结果，保留逐步实体副作用，由父用例回滚数据库。</summary>
    public async Task<RepositoryWriteResult> ApplyAsync(ProcurementReversalFacts facts, ProcurementReversalOperation operation, IReadOnlyCollection<Guid> lineIds, string reason, CancellationToken ct)
    {
        var head = facts.Head!;
        var oldVersion = head.Version;
        context.AuditStamp.StampForUpdate(head);
        var cancel = operation == ProcurementReversalOperation.CancelConfirmation;
        var delete = operation == ProcurementReversalOperation.DeleteLines;
        if (await context.Session.Connection.ExecuteAsync(Command("""
            UPDATE public.purchase_order_head SET lifecycle_status=CASE WHEN @Void THEN 9 ELSE lifecycle_status END,
                confirmation_status=CASE WHEN @Cancel THEN 0 ELSE confirmation_status END,
                active_confirmation_id=CASE WHEN @Cancel THEN NULL ELSE active_confirmation_id END,
                confirmed_date=CASE WHEN @Cancel THEN NULL ELSE confirmed_date END,
                confirmed_by=CASE WHEN @Cancel THEN NULL ELSE confirmed_by END,
                updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
            WHERE uid=@Uid AND version=@Version AND lifecycle_status=0 AND is_deleted=FALSE;
            """, HeadParameters(head, cancel, operation == ProcurementReversalOperation.Void), ct)) != 1)
            return RepositoryWriteResult.Fail(head.Uid, head.Version, "CONCURRENCY_CONFLICT", "采购状态已变化，反向操作未执行。");
        head.Version++;
        if (cancel)
        {
            head.ConfirmationStatus = ConfirmationStatusEnum.待确认;
            head.ActiveConfirmationId = null; head.ConfirmedDate = null; head.ConfirmedBy = null;
        }
        else
        {
            foreach (var link in facts.Links.Where(x => x.TargetType == "PO" && x.TargetHeadId == head.Uid && lineIds.Contains(x.TargetLineId) && !x.IsDeleted && x.LinkStatus == DemandExecutionLinkStatusEnum.Active))
            {
                context.AuditStamp.StampForUpdate(link);
                if (await context.Session.Connection.ExecuteAsync(Command("""
                    UPDATE public.demand_execution_link SET link_status=8,released_qty=allocated_qty,cancelled_at=@UpdatedDate,
                        cancelled_by=@UpdatedBy,cancelled_reason=@reason,updated_date=@UpdatedDate,updated_by=@UpdatedBy,version=version+1
                    WHERE uid=@Uid AND version=@Version AND link_status=0 AND is_deleted=FALSE;
                    """, ReleaseParameters(link, reason), ct)) != 1)
                    return RepositoryWriteResult.Fail(link.Uid, link.Version, "CONCURRENCY_CONFLICT", "采购贡献已变化，解除引用未执行。");
                link.Version++; link.LinkStatus = DemandExecutionLinkStatusEnum.Cancelled; link.ReleasedQty = link.AllocatedQty;
                link.CancelledAt = link.UpdatedDate; link.CancelledBy = link.UpdatedBy; link.CancelledReason = reason;
            }
            if (delete)
            {
                foreach (var line in facts.Lines.Where(x => lineIds.Contains(x.Uid)))
                {
                    context.AuditStamp.StampForUpdate(line);
                    if (await context.Session.Connection.ExecuteAsync(Command("""
                        UPDATE public.purchase_order_line SET is_deleted=TRUE,version=version+1,updated_date=@UpdatedDate,updated_by=@UpdatedBy
                        WHERE uid=@Uid AND purchase_order_id=@PurchaseOrderId AND version=@Version AND is_deleted=FALSE;
                        """, Parameters(line, "Uid", "PurchaseOrderId", "Version", "UpdatedDate", "UpdatedBy"), ct)) != 1) return RepositoryWriteResult.Fail(line.Uid, line.Version, "CONCURRENCY_CONFLICT", "采购行已变化，删除未执行。");
                    line.IsDeleted = true; line.Version++;
                }
                head.ExpectedDate = facts.Lines.Where(x => !x.IsDeleted).Min(x => x.PlannedDeliveryDate);
                await context.Session.Connection.ExecuteAsync(Command(
                    "UPDATE public.purchase_order_head SET expected_date=@ExpectedDate WHERE uid=@Uid;", Parameters(head, "Uid", "ExpectedDate"), ct));
            }
            else head.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
        }
        return RepositoryWriteResult.Ok(head.Uid, oldVersion, head.Version);
    }

    /// <summary>按当前 PG 持久化映射显式投影原实体字段，保留 Quantity 等 CLR 别名；不读取 NotMapped 属性。</summary>
    private static string Projection<T>(string alias = "") where T : BaseEntity =>
        string.Join(", ", PostgreSqlEntityMetadata.GetMetadata(typeof(T)).Columns.Select(column =>
            $"{(alias.Length == 0 ? "" : alias + ".")}{PostgreSqlNames.Quote(column.ColumnName)} AS {PostgreSqlNames.Quote(column.PropertyName)}"));

    /// <summary>显式绑定 UUID 数组，保留原输入集合，不展开或去除空身份。</summary>
    private static PostgreSqlParameters Ids(string name, IEnumerable<Guid> ids)
    {
        var parameters = new PostgreSqlParameters();
        parameters.AddIds(name, ids);
        return parameters;
    }

    /// <summary>只绑定当前写入使用的属性；日期按已有无时区墙上时间绑定，不改变实体。</summary>
    private static PostgreSqlParameters Parameters(BaseEntity entity, params string[] properties)
    {
        var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
        var parameters = new PostgreSqlParameters();
        foreach (var property in properties)
        {
            var column = metadata.GetProperty(property);
            var value = column.Property.GetValue(entity);
            if ((Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType) == typeof(DateTime))
                parameters.AddTimestamp(property, (DateTime?)value);
            else parameters.Add(property, value);
        }
        return parameters;
    }

    /// <summary>绑定原头版本及审计与取消/作废布尔分支，不使用调用方期望版本覆盖实体版本。</summary>
    private static PostgreSqlParameters HeadParameters(PurchaseOrderHead head, bool cancel, bool isVoid)
    {
        var parameters = Parameters(head, "Uid", "Version", "UpdatedDate", "UpdatedBy");
        parameters.Add("Cancel", cancel);
        parameters.Add("Void", isVoid);
        return parameters;
    }

    /// <summary>绑定原贡献解除审计和原因，不新增已释放量参数或计算口径。</summary>
    private static PostgreSqlParameters ReleaseParameters(DemandExecutionLink link, string reason)
    {
        var parameters = Parameters(link, "Uid", "Version", "UpdatedDate", "UpdatedBy");
        parameters.Add("reason", reason);
        return parameters;
    }

    /// <summary>数据库结果及锁行顺序保留源 SqlGuid 字节序，来源发现的.NET排序另按原方法执行。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',',
        new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(index => $"get_byte(uuid_send({column}),{index})")) + "]";
}

