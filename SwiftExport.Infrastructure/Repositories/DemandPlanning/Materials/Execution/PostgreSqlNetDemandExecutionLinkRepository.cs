using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;
using System.Data;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Execution;

/// <summary>在同一PG会话读取和插入净需求执行关系；消费事务前命令项/来源准备，不接管父事务。</summary>
public sealed class PostgreSqlNetDemandExecutionLinkRepository(RepositoryBaseContext context)
    : INetDemandExecutionLinkRepository, IMaterialDemandTransactionPreparation
{
    /// <summary>本仓储固定 SQL，保留原字段顺序、参数及写入条件。</summary>
    private const string ExecutionLinkInsertSql = """
        INSERT INTO "public"."demand_execution_link" ("uid",
        "version",
        "is_deleted",
        "created_date",
        "created_by",
        "updated_date",
        "updated_by",
        "net_demand_id",
        "execution_request_id",
        "command_item_id",
        "target_type",
        "target_head_id",
        "target_no",
        "target_line_id",
        "link_status",
        "allocated_qty",
        "released_qty",
        "demand_required_date",
        "planned_delivery_date",
        "cancelled_at",
        "cancelled_by",
        "cancelled_reason",
        "reversed_at",
        "reversed_by",
        "reversed_reason",
        "remark") VALUES (@Uid,
        @Version,
        @IsDeleted,
        @CreatedDate,
        @CreatedBy,
        @UpdatedDate,
        @UpdatedBy,
        @NetDemandId,
        @ExecutionRequestId,
        @CommandItemId,
        @TargetType,
        @TargetHeadId,
        @TargetNo,
        @TargetLineId,
        @LinkStatus,
        @AllocatedQty,
        @ReleasedQty,
        @DemandRequiredDate,
        @PlannedDeliveryDate,
        @CancelledAt,
        @CancelledBy,
        @CancelledReason,
        @ReversedAt,
        @ReversedBy,
        @ReversedReason,
        @Remark);
    """;

    private static readonly EntityMetadata Map = EntityMetadataCache.GetMetadata(typeof(DemandExecutionLink));
    private const string Projection = """
        l."uid" AS "Uid",
        l."version" AS "Version",
        l."is_deleted" AS "IsDeleted",
        l."created_date" AS "CreatedDate",
        l."created_by" AS "CreatedBy",
        l."updated_date" AS "UpdatedDate",
        l."updated_by" AS "UpdatedBy",
        l."net_demand_id" AS "NetDemandId",
        l."execution_request_id" AS "ExecutionRequestId",
        l."command_item_id" AS "CommandItemId",
        l."target_type" AS "TargetType",
        l."target_head_id" AS "TargetHeadId",
        l."target_no" AS "TargetNo",
        l."target_line_id" AS "TargetLineId",
        l."link_status" AS "LinkStatus",
        l."allocated_qty" AS "AllocatedQty",
        l."released_qty" AS "ReleasedQty",
        l."demand_required_date" AS "DemandRequiredDate",
        l."planned_delivery_date" AS "PlannedDeliveryDate",
        l."cancelled_at" AS "CancelledAt",
        l."cancelled_by" AS "CancelledBy",
        l."cancelled_reason" AS "CancelledReason",
        l."reversed_at" AS "ReversedAt",
        l."reversed_by" AS "ReversedBy",
        l."reversed_reason" AS "ReversedReason",
        l."remark" AS "Remark"
    """;

    /// <summary>复用同context的四集合发现与整组租约，不复制锁框架、资格判断或创建业务事务。</summary>
    public Task<IAsyncDisposable?> AcquireMaterialBeforeTransactionAsync(IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossDemandIds, IReadOnlyCollection<Guid> netDemandIds,
        IReadOnlyCollection<Guid> commandItemIds, CancellationToken cancellationToken = default) =>
        new PostgreSqlGrossDemandDocumentRepository(context).AcquireMaterialBeforeTransactionAsync(
            sourceUids, grossDemandIds, netDemandIds, commandItemIds, cancellationToken);

    /// <summary>按净需求锁未删采购净贡献，不按关系生命周期裁剪；空输入免SQL。</summary>
    public async Task<IReadOnlyList<PurchaseDemandCommitmentDto>> GetActiveByNetDemandIdsForUpdateAsync(IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await InScope(ids, [], async () =>
        {
            var rows = await context.Session.Connection.QueryAsync<PurchaseDemandCommitmentDto>(
                Command(ActiveSql(), new QueryParameters(ids, purchase: true)));
            return rows.ToList();
        }, default);
    }

    /// <summary>读取所有类型的未释放阻断事实与原中文提示，不新增状态或目标过滤。</summary>
    public async Task<IReadOnlyList<MaterialDemandRollbackBlockerDto>> GetRollbackBlockersAsync(IEnumerable<Guid> netDemandIds)
    {
        var ids = netDemandIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await InScope(ids, [], async () =>
        {
            var rows = await context.Session.Connection.QueryAsync<MaterialDemandRollbackBlockerDto>(
                Command(BlockersSql(), PostgreSqlParameters.Ids(ids)));
            return rows.ToList();
        }, default);
    }

    /// <summary>按全局命令项读取完整历史关系，已取消仍查重；原Empty过滤和空输入取消边界保持。</summary>
    public async Task<IReadOnlyList<DemandExecutionLink>> GetByCommandItemIdsForUpdateAsync(
        IEnumerable<Guid> commandItemIds, CancellationToken cancellationToken = default)
    {
        var ids = commandItemIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await InScope([], ids, async () =>
        {
            var rows = await context.Session.Connection.QueryAsync<DemandExecutionLink>(
                Command(ItemsSql(), PostgreSqlParameters.Ids(ids), cancellationToken));
            return rows.ToList();
        }, cancellationToken);
    }

    /// <summary>保留原全批校验/逐行打戳后Dapper逐条插入；无父事务仍可能部分成功，UID由Application提供。</summary>
    public async Task<int> InsertPurchaseLinksAsync(IEnumerable<DemandExecutionLink> links, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(links);
        var materialized = links.ToArray();
        if (materialized.Length == 0) return 0;
        if (materialized.Any(x => x is null || x.Uid == Guid.Empty))
            throw new ArgumentException("采购 Link 必须由 Application 预生成 Uid。", nameof(links));
        if (materialized.Any(x => x.TargetType != "PO" || x.AllocatedQty <= 0m
            || x.ReleasedQty < 0m || x.ReleasedQty > x.AllocatedQty || x.IsDeleted))
            throw new ArgumentException("采购 Link 的目标类型、贡献数量或删除状态无效。", nameof(links));
        foreach (var link in materialized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.AuditStamp.StampForInsert(link);
            link.IsDeleted = false;
            if (link.Version <= 0) link.Version = 1;
        }
        return await InScope(materialized.Select(x => x.NetDemandId).Distinct().ToArray(),
            materialized.Select(x => x.CommandItemId).Distinct().ToArray(),
            () => context.Session.Connection.ExecuteAsync(Command(ExecutionLinkInsertSql,
                materialized.Select(x => new LinkParameters(x)).ToArray(), cancellationToken)), cancellationToken);
    }

    /// <summary>无父时方法局部准备；有父只消费真实current覆盖，不能在业务快照之后补Item/S/U。</summary>
    private async Task<T> InScope<T>(Guid[] nets, Guid[] items, Func<Task<T>> action, CancellationToken token)
    {
        IAsyncDisposable? owned = null;
        if (context.Session.Transaction is null && context.Session.SourceDemandLease is null)
            owned = await AcquireMaterialBeforeTransactionAsync([], [], nets, items, token)
                ?? throw new TimeoutException("物料事务前资源准备等待超时。");
        await using var release = owned;
        var lease = context.Session.SourceDemandLease;
        if (lease is null || !lease.IsCurrent || items.Any(x => !lease.Covers("CommandItem", x)))
            throw new InvalidOperationException("执行关联缺少首次快照前的完整命令项与来源准备。");

        // 技术定位含全部历史；缺Net不造Gross或提前业务NotFound，真实写入约束仍为最终边界。
        var parameters = new PostgreSqlParameters();
        parameters.AddIds("Nets", nets); parameters.AddIds("Items", items);
        var gross = await context.Session.Connection.QueryAsync<Guid>(Command($"""
            SELECT n."gross_demand_id" FROM "public"."net_demand" AS n
            WHERE n."uid"=ANY(@Nets)
               OR EXISTS(SELECT 1 FROM "public"."demand_execution_link" AS l
                   WHERE l."net_demand_id"=n."uid"
                     AND l."command_item_id"=ANY(@Items));
            """, parameters, token));
        if (!lease.IsCurrent || gross.Any(x => !lease.Covers("Gross", x)))
            throw new InvalidOperationException("执行关联的成品需求身份未在首次快照前完整准备。");
        return await action();
    }

    /// <summary>净贡献由SQL计算，日期NULL先与源SqlGuid尾键保持原序，只锁实际匹配关系行。</summary>
    private static string ActiveSql() => $"""
        SELECT l."uid" AS "Uid",l."net_demand_id" AS "NetDemandId",
               l."allocated_qty"-l."released_qty" AS "AllocatedQty"
        FROM "public"."demand_execution_link" AS l
        WHERE l."net_demand_id"=ANY(@Uids)
          AND {Dictionary("l." + "\"target_type\"")}={Dictionary("@PurchaseTargetType")}
          AND l."allocated_qty">l."released_qty" AND l."is_deleted"=FALSE
        ORDER BY l."created_date" NULLS FIRST,{UuidOrder("l." + "\"uid\"")}
        FOR UPDATE OF l;
        """;

    /// <summary>保留SQL CONCAT对NULL的空串语义与原数据库字典/身份排序。</summary>
    private static string BlockersSql() => $"""
        SELECT l."net_demand_id" AS "NetDemandId",l."target_type" AS "TargetType",
               l."target_no" AS "TargetNo",l."target_line_id" AS "TargetLineId",
               l."link_status" AS "LinkStatus",
               concat('净需求已关联 ',l."target_type",' ',l."target_no",'，请先取消或完成反向处理。') AS "Message"
        FROM "public"."demand_execution_link" AS l
        WHERE l."net_demand_id"=ANY(@Uids) AND l."allocated_qty">l."released_qty"
          AND l."is_deleted"=FALSE
        ORDER BY {Dictionary("l." + "\"target_type\"")} NULLS FIRST,
                 {Dictionary("l." + "\"target_no\"")} NULLS FIRST,{UuidOrder("l." + "\"target_line_id\"")}
        FOR UPDATE OF l;
        """;

    /// <summary>全列投影按原两个SqlGuid键排序，不筛取消/反向或贡献数量。</summary>
    private static string ItemsSql() => $"""
        SELECT {Projection} FROM "public"."demand_execution_link" AS l
        WHERE l."command_item_id"=ANY(@Uids) AND l."is_deleted"=FALSE
        ORDER BY {UuidOrder("l." + "\"command_item_id\"")},{UuidOrder("l." + "\"uid\"")}
        FOR UPDATE OF l;
        """;
    /// <summary>借用原父事务与token，不自行提交或选择隔离。</summary>
    private CommandDefinition Command(string sql, object parameters, CancellationToken token = default) =>
        new(sql, parameters, context.Session.Transaction, cancellationToken: token);
    /// <summary>只去掉ASCII尾空格，不改写存储文本或C#精确PO校验。</summary>
    private static string Dictionary(string value) => $"rtrim({value},' ') COLLATE public.erp_dictionary_ci";
    /// <summary>SQL Server uniqueidentifier按末字节组优先比较，与PG uuid默认序分别处理。</summary>
    private static string UuidOrder(string value) => "ARRAY[" + string.Join(",", new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(x => $"get_byte(uuid_send({value}),{x})")) + "]";

    /// <summary>查询参数复用UUID数组，PO明确text，避免驱动猜测可空文本。</summary>
    private sealed class QueryParameters(Guid[] ids, bool purchase) : SqlMapper.IDynamicParameters
    {
        /// <summary>为本命令绑定独立数组与采购类型参数。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            PostgreSqlParameters.Ids(ids).AddParameters(command, identity);
            if (purchase) command.Parameters.Add(new NpgsqlParameter("PurchaseTargetType", NpgsqlDbType.Text) { Value = "PO" });
        }
    }

    /// <summary>复用实体时间/枚举/数值绑定，仅为所有可空文本明确text类型，不修改公共参数框架。</summary>
    private sealed class LinkParameters(DemandExecutionLink link) : SqlMapper.IDynamicParameters
    {
        /// <summary>每条Dapper插入获得独立参数实例；原nullable文本/日期及输入UID保持。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            PostgreSqlParameters.Entity(link, Map).AddParameters(command, identity);
            foreach (var column in Map.Columns.Where(x => x.Property.PropertyType == typeof(string)))
                ((NpgsqlParameter)command.Parameters[column.ParameterName]!).NpgsqlDbType = NpgsqlDbType.Text;
        }
    }
}

