using Dapper;
using SwiftExport.Core.Interfaces.DemandPlanning;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Document;

/// <summary>以事务前Gross租约保护净需求全部历史和空范围，保留原七方法的过滤、写列及逐行副作用。</summary>
public sealed class PostgreSqlMaterialDemandDocumentRepository(RepositoryBaseContext context)
    : IMaterialDemandDocumentRepository, IMaterialDemandTransactionPreparation
{
    private static readonly EntityMetadata Map = PostgreSqlEntityMetadata.GetMetadata(typeof(NetDemand));
    private static readonly EntityMetadata GrossMap = PostgreSqlEntityMetadata.GetMetadata(typeof(GrossDemand));
    private static readonly string Table = TableName(Map);
    private static readonly string GrossTable = TableName(GrossMap);
    private static readonly string Projection = string.Join(',', Map.Columns.Select(c => "nd." + PostgreSqlNames.Quote(c.ColumnName) + " AS " + PostgreSqlNames.Quote(c.PropertyName)));

    /// <summary>复用同会话的共同准备能力；只发现并保护资源，不拥有业务事务或重放业务。</summary>
    public Task<IAsyncDisposable?> AcquireMaterialBeforeTransactionAsync(IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossDemandIds, IReadOnlyCollection<Guid> netDemandIds,
        IReadOnlyCollection<Guid> commandItemIds, CancellationToken cancellationToken = default)
        => new PostgreSqlGrossDemandDocumentRepository(context).AcquireMaterialBeforeTransactionAsync(
            sourceUids, grossDemandIds, netDemandIds, commandItemIds, cancellationToken);

    /// <summary>锁定活动未删净需求，完整返回快照，按修订、行号和SqlGuid稳定排序。</summary>
    public Task<IReadOnlyList<NetDemand>> GetActiveByGrossDemandForUpdateAsync(Guid grossDemandId)
        => InGrossScope([grossDemandId], async () =>
        {
            var rows = await context.Session.Connection.QueryAsync<NetDemand>(Command($"SELECT {Projection} FROM {Table} nd WHERE nd.{C("GrossDemandId")}=@GrossDemandId AND nd.{C("LifecycleStatus")}=0 AND nd.{C("IsDeleted")}=FALSE ORDER BY nd.{C("DecompositionRevision")},nd.{C("LineNumber")},{UuidOrder("nd." + C("Uid"))} FOR UPDATE;", Gross(grossDemandId)));
            return (IReadOnlyList<NetDemand>)[.. rows];
        });

    /// <summary>锁定全部现有历史后取MAX，再执行原C#整数加一；方法局部租约不保证跨方法MAX到新增的原子性。</summary>
    public Task<int> GetNextDecompositionRevisionForUpdateAsync(Guid grossDemandId)
        => InGrossScope([grossDemandId], async () =>
        {
            var parameters = Gross(grossDemandId);
            await context.Session.Connection.QueryAsync<Guid>(Command($"SELECT {C("Uid")} FROM {Table} WHERE {C("GrossDemandId")}=@GrossDemandId FOR UPDATE;", parameters));
            var latest = await context.Session.Connection.ExecuteScalarAsync<int>(Command($"SELECT COALESCE(MAX({C("DecompositionRevision")}),0) FROM {Table} WHERE {C("GrossDemandId")}=@GrossDemandId;", parameters));
            return unchecked(latest + 1);
        });

    /// <summary>锁定匹配Gross及全部净需求历史，再评估活动与历史完整性；缺失Gross不补造，无历史为安全。</summary>
    public Task<IReadOnlyList<MaterialDemandSourceExitAssessment>> AssessSourceExitForUpdateAsync(IEnumerable<Guid> grossDemandIds)
    {
        var ids = grossDemandIds.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0) return Task.FromResult<IReadOnlyList<MaterialDemandSourceExitAssessment>>([]);
        return InGrossScope(ids, async () =>
        {
            var parameters = PostgreSqlParameters.Ids(ids);
            var existing = (await context.Session.Connection.QueryAsync<Guid>(Command($"SELECT {G("Uid")} FROM {GrossTable} WHERE {G("Uid")}=ANY(@Uids) FOR UPDATE;", parameters))).ToArray();
            if (existing.Length > 0)
                await context.Session.Connection.QueryAsync<Guid>(Command($"SELECT {C("Uid")} FROM {Table} WHERE {C("GrossDemandId")}=ANY(@Uids) FOR UPDATE;", PostgreSqlParameters.Ids(existing)));
            // 聚合与LEFT可空侧不能直接FOR UPDATE；CASE保持源NULL判断，不引入安全默认值。
            var sql = $"""
                SELECT gd.{G("Uid")} AS "GrossDemandId",COUNT(nd.{C("Uid")})::integer AS "HistoryCount",
                  SUM(CASE WHEN nd.{C("Uid")} IS NOT NULL AND nd.{C("IsDeleted")}=FALSE AND nd.{C("LifecycleStatus")}=0 THEN 1 ELSE 0 END)>0 AS "HasActiveRevision",
                  SUM(CASE WHEN nd.{C("Uid")} IS NOT NULL AND (nd.{C("IsDeleted")}=TRUE OR nd.{C("LifecycleStatus")} NOT IN (8,9) OR nd.{C("DecompositionRevision")}<1) THEN 1 ELSE 0 END)=0 AS "IsHistoryComplete"
                FROM {GrossTable} gd LEFT JOIN {Table} nd ON nd.{C("GrossDemandId")}=gd.{G("Uid")}
                WHERE gd.{G("Uid")}=ANY(@Uids) GROUP BY gd.{G("Uid")} ORDER BY {UuidOrder("gd." + G("Uid"))};
                """;
            return (IReadOnlyList<MaterialDemandSourceExitAssessment>)[.. await context.Session.Connection.QueryAsync<MaterialDemandSourceExitAssessment>(Command(sql, parameters))];
        });
    }

    /// <summary>逐行打戳、补身份版本并插入完整快照；数据库删除标志固定false，不改变输入或新增批次事务。</summary>
    public async Task<IReadOnlyList<RepositoryWriteResult>> AddDecompositionAsync(IEnumerable<NetDemand> demands)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var demand in demands)
        {
            context.AuditStamp.StampForInsert(demand);
            if (demand.Uid == Guid.Empty) demand.Uid = GuidSequentialGenerator.NewGuid();
            if (demand.Version <= 0) demand.Version = 1;
            var rows = await InGrossScope([demand.GrossDemandId], () => context.Session.Connection.ExecuteAsync(Command(
                $"INSERT INTO {Table} ({string.Join(',', Map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)))}) VALUES ({string.Join(',', Map.Columns.Select(c => c.PropertyName == "IsDeleted" ? "FALSE" : "@" + c.ParameterName))});", PostgreSqlParameters.Entity(demand, Map))));
            results.Add(rows == 1 ? RepositoryWriteResult.Ok(demand.Uid, null, demand.Version, "新增成功。")
                : RepositoryWriteResult.Fail(demand.Uid, null, "INSERT_FAILED", "新增净需求失败。"));
        }
        return results;
    }

    /// <summary>只修改活动未删且版本匹配的推荐供应商及更新审计；NULL清空原样传递，零受影响仍返回零。</summary>
    public Task<int> UpdateSuggestedPurchaseSupplierAsync(Guid netDemandId, int expectedVersion, Guid? suggestedPurchaseSupplierId)
        => InNetScope(netDemandId, false, 0, locator =>
        {
            var parameters = new PostgreSqlParameters(); parameters.Add("NetDemandId", netDemandId);
            parameters.Add("ExpectedVersion", expectedVersion); parameters.AddTimestamp("Now", DateTime.Now);
            parameters.Add("UserName", context.CurrentUser.UserName);
            return context.Session.Connection.ExecuteAsync(Command($"UPDATE {Table} SET {C("SuggestedPurchaseSupplierId")}=@SuggestedPurchaseSupplierId,{C("UpdatedDate") }=@Now,{C("UpdatedBy")}=@UserName,{C("Version")}={C("Version")}+1 WHERE {C("Uid")}=@NetDemandId AND {C("Version")}=@ExpectedVersion AND {C("LifecycleStatus")}=0 AND {C("IsDeleted")}=FALSE;", new SupplierParameters(parameters, suggestedPurchaseSupplierId)));
        });

    /// <summary>回退指定Gross活动未删修订，原原因不修剪，统一记录一次时间、审计和版本增量。</summary>
    public Task<int> RollbackRevisionAsync(Guid grossDemandId, int revision, string reason)
        => InGrossScope([grossDemandId], () =>
        {
            var parameters = Gross(grossDemandId); parameters.Add("Revision", revision); parameters.Add("Reason", reason);
            parameters.AddTimestamp("Now", DateTime.Now); parameters.Add("UserName", context.CurrentUser.UserName);
            return context.Session.Connection.ExecuteAsync(Command($"UPDATE {Table} SET {C("LifecycleStatus")}=8,{C("RolledBackAt")}=@Now,{C("RolledBackBy")}=@UserName,{C("RolledBackReason")}=@Reason,{C("UpdatedDate")}=@Now,{C("UpdatedBy")}=@UserName,{C("Version")}={C("Version")}+1 WHERE {C("GrossDemandId")}=@GrossDemandId AND {C("DecompositionRevision")}=@Revision AND {C("LifecycleStatus")}=0 AND {C("IsDeleted")}=FALSE;", parameters));
        });

    /// <summary>依次定位活动Net、锁定有效Gross、锁定同Gross和Root活动家族；PG定位遵循父隔离级别，不提供NOLOCK脏读。</summary>
    public Task<IReadOnlyList<NetDemand>> GetActiveFamilyForUpdateAsync(Guid netDemandId)
        => InNetScope<IReadOnlyList<NetDemand>>(netDemandId, true, [], async locator =>
        {
            var parameters = Gross(locator.GrossDemandId); parameters.Add("RootNetDemandId", locator.RootNetDemandId);
            var gross = await context.Session.Connection.QuerySingleOrDefaultAsync<Guid?>(Command($"SELECT {G("Uid")} FROM {GrossTable} WHERE {G("Uid")}=@GrossDemandId AND {G("LifecycleStatus")}=0 AND {G("IsDeleted")}=FALSE FOR UPDATE;", parameters));
            if (!gross.HasValue) return [];
            return [.. await context.Session.Connection.QueryAsync<NetDemand>(Command($"SELECT {Projection} FROM {Table} nd WHERE nd.{C("GrossDemandId")}=@GrossDemandId AND nd.{C("RootNetDemandId")}=@RootNetDemandId AND nd.{C("LifecycleStatus")}=0 AND nd.{C("IsDeleted")}=FALSE ORDER BY nd.{C("LineNumber")},{UuidOrder("nd." + C("Uid"))} FOR UPDATE;", parameters))];
        });

    /// <summary>仅事务外且没有租约时取得方法局部资源；父事务和现有租约只能消费，绝不晚补或释放借用租约。</summary>
    private async Task<T> InGrossScope<T>(Guid[] grossIds, Func<Task<T>> action)
    {
        IAsyncDisposable? owned = null;
        try
        {
            if (context.Session.Transaction is null && context.Session.SourceDemandLease is null)
                owned = await AcquireMaterialBeforeTransactionAsync([], grossIds, [], []) ?? throw new TimeoutException("净需求资源准备超时。");
            foreach (var id in grossIds) RequireGross(id);
            return await action();
        }
        finally { if (owned is not null) await owned.DisposeAsync(); }
    }

    /// <summary>保留未知Net的原定位缺失结果；真实定位之后才消费其Gross覆盖，发现改绑不追加资源。</summary>
    private async Task<T> InNetScope<T>(Guid uid, bool activeOnly, T missing, Func<Locator, Task<T>> action)
    {
        IAsyncDisposable? owned = null;
        try
        {
            if (context.Session.Transaction is null && context.Session.SourceDemandLease is null)
                owned = await AcquireMaterialBeforeTransactionAsync([], [], [uid], []) ?? throw new TimeoutException("净需求资源准备超时。");
            var parameters = new PostgreSqlParameters(); parameters.Add("NetDemandId", uid);
            var filter = activeOnly ? $" AND {C("LifecycleStatus")}=0 AND {C("IsDeleted")}=FALSE" : "";
            var locator = await context.Session.Connection.QuerySingleOrDefaultAsync<Locator>(Command($"SELECT {C("GrossDemandId")} AS \"GrossDemandId\",{C("RootNetDemandId")} AS \"RootNetDemandId\" FROM {Table} WHERE {C("Uid")}=@NetDemandId{filter};", parameters));
            if (locator is null) return missing;
            RequireGross(locator.GrossDemandId);
            return await action(locator);
        }
        finally { if (owned is not null) await owned.DisposeAsync(); }
    }

    /// <summary>只认可当前同物理连接的真实Gross租约，拒绝缺覆盖、旧租约和晚期准备。</summary>
    private void RequireGross(Guid uid)
    {
        var lease = context.Session.SourceDemandLease;
        if (lease is null || !lease.IsCurrent || !lease.Covers("Gross", uid))
            throw new InvalidOperationException("净需求操作必须消费事务前准备的当前Gross租约。");
    }
    /// <summary>共享父事务原样传递，不建立隐藏事务或连接。</summary>
    private CommandDefinition Command(string sql, object parameters) => new(sql, parameters, context.Session.Transaction);
    /// <summary>Gross身份标量绑定。</summary>
    private static PostgreSqlParameters Gross(Guid uid) { var p = new PostgreSqlParameters(); p.Add("GrossDemandId", uid); return p; }
    /// <summary>从固定元数据解析净需求列名。</summary>
    private static string C(string property) => PostgreSqlNames.Quote(Map.Columns.Single(c => c.PropertyName == property).ColumnName);
    /// <summary>从固定元数据解析来源列名。</summary>
    private static string G(string property) => PostgreSqlNames.Quote(GrossMap.Columns.Single(c => c.PropertyName == property).ColumnName);
    /// <summary>引用固定元数据表名。</summary>
    private static string TableName(EntityMetadata map) => PostgreSqlNames.Quote(map.SchemaName) + "." + PostgreSqlNames.Quote(map.TableName);
    /// <summary>按SQL Server SqlGuid字节优先级排序，不采用PG UUID或CLR Guid默认序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(x => $"get_byte(uuid_send({column}),{x})")) + "]";
    /// <summary>原定位投影，不将Root或目标身份提升为额外资源。</summary>
    private sealed class Locator { public Guid GrossDemandId { get; set; } public Guid RootNetDemandId { get; set; } }
    /// <summary>仅供应商NULL需要明确uuid类型，不改变共享参数包或原业务输入。</summary>
    private sealed class SupplierParameters(PostgreSqlParameters parameters, Guid? supplier) : SqlMapper.IDynamicParameters
    {
        /// <summary>复制公共参数并添加局部显式可空uuid，命令独占参数实例。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        {
            parameters.AddParameters(command, identity);
            command.Parameters.Add(new NpgsqlParameter("SuggestedPurchaseSupplierId", NpgsqlDbType.Uuid) { Value = supplier.HasValue ? supplier.Value : DBNull.Value });
        }
    }
}
