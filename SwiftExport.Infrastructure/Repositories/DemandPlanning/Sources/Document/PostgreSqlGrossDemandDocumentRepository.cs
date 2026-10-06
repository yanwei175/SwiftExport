using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Document;

/// <summary>保留来源九方法的写列、版本与消息；事务前来源/身份租约保护空范围，不在仓储内建立业务事务。</summary>
public sealed partial class PostgreSqlGrossDemandDocumentRepository(RepositoryBaseContext context)
    : IGrossDemandDocumentRepository, ISourceDemandTransactionPreparation,
      SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document.IMaterialDemandTransactionPreparation
{
    private static readonly EntityMetadata Map = PostgreSqlEntityMetadata.GetMetadata(typeof(GrossDemand));
    private static readonly string Table = PostgreSqlNames.Quote(Map.SchemaName) + "." + PostgreSqlNames.Quote(Map.TableName);
    private static readonly string Projection = string.Join(',', Map.Columns.Select(c => "d." + PostgreSqlNames.Quote(c.ColumnName) + " AS " + PostgreSqlNames.Quote(c.PropertyName)));
    private static readonly string[] SourceFields = ["SourceNo", "SourceLineNumber", "SourceVersion", "DemandBasisFingerprint", "CustomerId", "ProductId", "IsNonStandard", "SkuSnapshot", "LinkSkuSnapshot", "CustomerSkuSnapshot", "DescriptionSnapshot", "ColorSnapshot", "SpecificationSnapshot", "ImagePathSnapshot", "UnitSnapshot", "DemandQty", "RequiredDate", "Remark"];
    private static readonly string[] ManualFields = ["ProductId", "IsNonStandard", "SkuSnapshot", "LinkSkuSnapshot", "CustomerSkuSnapshot", "DescriptionSnapshot", "ColorSnapshot", "SpecificationSnapshot", "ImagePathSnapshot", "DemandBasisFingerprint", "DemandQty", "RequiredDate", "Remark"];

    /// <summary>事务外发现全部来源身份，先S后U并在获取后复核；改绑/新增集合只重新准备，不重复业务写入。</summary>
    public async Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(IReadOnlyCollection<Guid> sourceUids,
        IReadOnlyCollection<Guid> grossDemandIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceUids); ArgumentNullException.ThrowIfNull(grossDemandIds);
        if (context.Session.Transaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("来源准备必须先于父事务及首次快照。");
        // 复制准备资源，不改变业务枚举或返回排序；多轮发现共享同一个预算。
        var declaredSources = sourceUids.Distinct().ToArray(); var declaredGross = grossDemandIds.Distinct().ToArray();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                budget.Token.ThrowIfCancellationRequested();
                var before = await Bindings(declaredGross, false, budget.Token);
                var sources = declaredSources.Concat(before.Select(x => x.SourceUid)).Distinct().ToArray();
                var members = await Bindings(sources, true, budget.Token);
                var uids = declaredGross.Concat(members.Select(x => x.Uid)).Distinct().ToArray();
                var lease = await PostgreSqlSourceDemandLock.AcquireBeforeTransactionAsync(context.Session, sources, uids,
                    context.LoggerFactory.CreateLogger("PostgreSqlGrossDemandDocumentRepository"), budget.Token);
                if (lease is null) return null;
                try
                {
                    var after = await Bindings(declaredGross, false, budget.Token);
                    var currentMembers = await Bindings(sources, true, budget.Token);
                    var held = (PostgreSqlSourceDemandLock.Lease)lease;
                    if (SameBindings(before, after) && currentMembers.All(x => held.Covers("Gross", x.Uid)))
                        return lease;
                }
                catch { await lease.DisposeAsync(); throw; }
                // 禁止持U后追加S；全释放后再发现，尚未建立业务事务或执行任何业务委托。
                await lease.DisposeAsync();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && budget.IsCancellationRequested)
        { return null; }
    }

    /// <summary>锁未删身份，不按生命周期过滤；缺失UID仍要求事务前U保护。</summary>
    public Task<GrossDemand?> GetByIdForUpdateAsync(Guid uid) => InScope([], [uid], () =>
        context.Session.Connection.QuerySingleOrDefaultAsync<GrossDemand>(Command(
            $"SELECT {Projection} FROM {Table} d WHERE d.{C("Uid")}=@Uid AND d.{C("IsDeleted")}=FALSE FOR UPDATE;", Id(uid))));

    /// <summary>完整来源谓词与活动/未删条件不标准化入参；按源NULL方向与SqlGuid顺序返回。</summary>
    public Task<IReadOnlyList<GrossDemand>> GetActiveBySourceForUpdateAsync(string sourceType, Guid sourceUid) => InScope([sourceUid], [], async () =>
    {
        var p = Source(sourceType, sourceUid);
        var rows = await context.Session.Connection.QueryAsync<GrossDemand>(Command($"""
            SELECT {Projection} FROM {Table} d
            WHERE {SourceWhere} AND d.{C("LifecycleStatus")}=0 AND d.{C("IsDeleted")}=FALSE
            ORDER BY d.{C("SourceLineNumber")} ASC NULLS FIRST,{UuidOrder("d." + C("Uid"))} ASC NULLS FIRST FOR UPDATE;
            """, p));
        return (IReadOnlyList<GrossDemand>)rows.ToList();
    });

    /// <summary>按原枚举逐行写入和打戳，保留给定UID/正Version；无父事务时各行原样独立提交，不扩大批次原子性。</summary>
    public async Task<IReadOnlyList<RepositoryWriteResult>> AddSourceDemandsAsync(IEnumerable<GrossDemand> demands)
    {
        var results = new List<RepositoryWriteResult>();
        foreach (var demand in demands)
        {
            context.AuditStamp.StampForInsert(demand);
            if (demand.Uid == Guid.Empty) demand.Uid = GuidSequentialGenerator.NewGuid();
            if (demand.Version <= 0) demand.Version = 1;
            var rows = await InScope([demand.SourceUid], [demand.Uid], () => context.Session.Connection.ExecuteAsync(Command(
                $"INSERT INTO {Table} ({string.Join(',', Map.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)))}) VALUES ({string.Join(',', Map.Columns.Select(c => c.PropertyName == "IsDeleted" ? "FALSE" : "@" + c.ParameterName))});",
                PostgreSqlParameters.Entity(demand, Map))), allowNewIdentity: true);
            results.Add(rows == 1 ? RepositoryWriteResult.Ok(demand.Uid, null, demand.Version, "新增成功。")
                : RepositoryWriteResult.Fail(demand.Uid, null, "INSERT_FAILED", "新增毛需求失败。"));
        }
        return results;
    }

    /// <summary>更新原来源事实列，不改变来源身份/生命周期，不预推进输入实体Version。</summary>
    public Task<RepositoryWriteResult> UpdateSourceDemandAsync(GrossDemand demand, int expectedVersion) =>
        VersionWrite(demand, expectedVersion, SourceFields, "", "来源需求已更新。", "来源需求状态或版本已变化。");

    /// <summary>手工更新不改UnitSnapshot或客户/来源身份，MD/活动/未删/版本谓词和原消息保留。</summary>
    public Task<RepositoryWriteResult> UpdateManualAsync(GrossDemand demand, int expectedVersion) =>
        VersionWrite(demand, expectedVersion, ManualFields, $" AND {Dictionary(C("SourceType"))}='MD'", "手工毛需求更新成功。", "手工毛需求状态或版本已变化，请刷新后重试。");

    /// <summary>仅指定版本活动手工需求关闭，生命周期和更新审计同一次墙上时间，不改实体状态。</summary>
    public Task<RepositoryWriteResult> CloseManualAsync(Guid uid, int expectedVersion, string reason) => InScope([], [uid], async () =>
    {
        var p = Id(uid); p.Add("ExpectedVersion", expectedVersion); Stamp(p, reason); p.Add("Lifecycle", (int)GrossDemandLifecycleStatusEnum.Closed);
        var version = await context.Session.Connection.QuerySingleOrDefaultAsync<int?>(Command($"""
            UPDATE {Table} SET {LifecycleSet},{C("Version")}={C("Version")}+1
            WHERE {C("Uid")}=@Uid AND {C("Version")}=@ExpectedVersion AND {Dictionary(C("SourceType"))}='MD'
              AND {C("LifecycleStatus")}=0 AND {C("IsDeleted")}=FALSE RETURNING {C("Version")};
            """, p));
        return version.HasValue ? RepositoryWriteResult.Ok(uid, expectedVersion, version, "手工毛需求已关闭。")
            : RepositoryWriteResult.Fail(uid, expectedVersion, "CONCURRENCY_CONFLICT", "手工毛需求状态或版本已变化，请刷新后重试。");
    });

    /// <summary>上层已证明无物料历史后物理删除；本方法仅Distinct身份与活动未删条件，不增加版本/历史检查。</summary>
    public Task<int> HardDeleteForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds)
    {
        var ids = grossDemandIds.Distinct().ToArray(); if (ids.Length == 0) return Task.FromResult(0);
        return InScope([], ids, () => context.Session.Connection.ExecuteAsync(Command(
            $"DELETE FROM {Table} WHERE {C("Uid")}=ANY(@Uids) AND {C("LifecycleStatus")}=0 AND {C("IsDeleted")}=FALSE;", PostgreSqlParameters.Ids(ids))));
    }

    /// <summary>作废上层判定安全回退的活动未删集合，数量/Version/审计保持源规则，不新增历史过滤。</summary>
    public Task<int> VoidRolledBackForSourceCancellationAsync(IEnumerable<Guid> grossDemandIds, string reason)
    {
        var ids = grossDemandIds.Distinct().ToArray(); if (ids.Length == 0) return Task.FromResult(0);
        return InScope([], ids, () => { var p = PostgreSqlParameters.Ids(ids); Stamp(p, reason); p.Add("Lifecycle", (int)GrossDemandLifecycleStatusEnum.Voided);
            return context.Session.Connection.ExecuteAsync(Command($"UPDATE {Table} SET {LifecycleSet},{C("Version")}={C("Version")}+1 WHERE {C("Uid")}=ANY(@Uids) AND {C("LifecycleStatus")}=0 AND {C("IsDeleted")}=FALSE;", p)); });
    }

    /// <summary>按完整来源活动集合作废，不新增来源行提交范围/版本/历史检查。</summary>
    public Task<int> VoidPendingBySourceAsync(string sourceType, Guid sourceUid, string reason) => InScope([sourceUid], [], () =>
    { var p = Source(sourceType, sourceUid); Stamp(p, reason); p.Add("Lifecycle", (int)GrossDemandLifecycleStatusEnum.Voided);
        return context.Session.Connection.ExecuteAsync(Command($"UPDATE {Table} d SET {LifecycleSet},{C("Version")}=d.{C("Version")}+1 WHERE {SourceWhere} AND d.{C("LifecycleStatus")}=0 AND d.{C("IsDeleted")}=FALSE;", p)); });

    /// <summary>共同版本写入只修改原字段清单和更新审计；输出新版本，不回填输入对象。</summary>
    private Task<RepositoryWriteResult> VersionWrite(GrossDemand demand, int expectedVersion, string[] fields,
        string additionalWhere, string success, string conflict) => InScope([], [demand.Uid], async () =>
    {
        var p = PostgreSqlParameters.Entity(demand, Map); p.Add("ExpectedVersion", expectedVersion);
        p.AddTimestamp("Now", DateTime.Now); p.Add("UserName", context.CurrentUser.UserName);
        var version = await context.Session.Connection.QuerySingleOrDefaultAsync<int?>(Command($"""
            UPDATE {Table} SET {string.Join(',', fields.Select(f => C(f) + "=@" + f))},
                {C("UpdatedDate")}=@Now,{C("UpdatedBy")}=@UserName,{C("Version")}={C("Version")}+1
            WHERE {C("Uid")}=@Uid AND {C("Version")}=@ExpectedVersion AND {C("LifecycleStatus")}=0
              AND {C("IsDeleted")}=FALSE{additionalWhere} RETURNING {C("Version")};
            """, p));
        return version.HasValue ? RepositoryWriteResult.Ok(demand.Uid, expectedVersion, version, success)
            : RepositoryWriteResult.Fail(demand.Uid, expectedVersion, "CONCURRENCY_CONFLICT", conflict);
    });

    /// <summary>父事务必须已有覆盖，绝不事务内迟补S；无父事务时仅借用一次方法租约，不创建或改变业务事务。</summary>
    private async Task<T> InScope<T>(Guid[] sources, Guid[] uids, Func<Task<T>> action, bool allowNewIdentity = false)
    {
        IAsyncDisposable? owned = null;
        if (context.Session.Transaction is null && context.Session.SourceDemandLease is null)
            owned = await AcquireBeforeTransactionAsync(sources, uids) ?? throw new TimeoutException("来源资源准备等待超时。");
        await using var release = owned;
        var lease = context.Session.SourceDemandLease;
        if (lease is null || !lease.IsCurrent || sources.Any(uid => !lease.Covers("Source", uid)))
            throw new InvalidOperationException("来源业务事务缺少首次快照前的完整来源准备。");
        var missing = uids.Distinct().Where(uid => !lease.Covers("Gross", uid)).ToArray();
        if (missing.Length > 0)
        {
            if (context.Session.Transaction is null)
                throw new InvalidOperationException("无父事务时租约必须预先覆盖全部毛需求身份。");
            // 新建身份可能在业务委托内生成；已有S覆盖后才借事务级U，不追加任何S。
            var bindings = allowNewIdentity ? [] : await Bindings(missing, false, default);
            if (!allowNewIdentity && (bindings.Count != missing.Length || bindings.Any(x => !lease.Covers("Source", x.SourceUid))))
                throw new InvalidOperationException("毛需求身份未在首次快照前准备，不能事务内追加未知来源。");
            foreach (var key in missing.Select(uid => PostgreSqlSourceDemandLock.Key("Gross", uid)).Distinct().Order())
                await context.Session.Connection.ExecuteAsync(Command("SELECT pg_advisory_xact_lock(@Key);", new { Key = key }));
        }
        return await action();
    }

    /// <summary>来源身份发现含软删和全部生命周期，避免把不存在的活动事实误当无身份；空输入不发SQL。</summary>
    private async Task<IReadOnlyList<Binding>> Bindings(Guid[] ids, bool bySource, CancellationToken token)
    {
        if (ids.Length == 0) return [];
        var rows = await context.Session.Connection.QueryAsync<Binding>(new CommandDefinition(
            $"SELECT {C("Uid")} AS \"Uid\",{C("SourceUid")} AS \"SourceUid\" FROM {Table} WHERE {C(bySource ? "SourceUid" : "Uid")}=ANY(@Uids);",
            PostgreSqlParameters.Ids(ids), context.Session.Transaction, cancellationToken: token));
        return rows.ToList();
    }
    /// <summary>缺失与改绑均是资源重新发现信号，比较UID映射不引入SourceType规范化。</summary>
    private static bool SameBindings(IReadOnlyList<Binding> before, IReadOnlyList<Binding> after) =>
        before.Count == after.Count && before.All(x => after.Any(y => x.Uid == y.Uid && x.SourceUid == y.SourceUid));
    /// <summary>仅资源发现使用的私有投影，不是新领域表或API模型。</summary>
    private sealed class Binding { public Guid Uid { get; set; } public Guid SourceUid { get; set; } }
    /// <summary>按当前实体映射引用固定列名，禁止外部标识符。</summary>
    private static string C(string property) => PostgreSqlNames.Quote(Map.Columns.Single(c => c.PropertyName == property).ColumnName);
    /// <summary>沿当前字典和源尾ASCII规则；B10/B19广域等值及来源唯一索引仍需主控完成。</summary>
    private static string Dictionary(string column) => $"rtrim({column},' ') COLLATE public.erp_dictionary_ci";
    private static string SourceWhere => $"{Dictionary("d." + C("SourceType"))}=rtrim(@SourceType,' ') COLLATE public.erp_dictionary_ci AND d.{C("SourceUid")}=@SourceUid";
    private static string LifecycleSet => $"{C("LifecycleStatus")}=@Lifecycle,{C("LifecycleChangedAt")}=@Now,{C("LifecycleChangedBy")}=@UserName,{C("LifecycleChangedReason")}=@Reason,{C("UpdatedDate")}=@Now,{C("UpdatedBy")}=@UserName";
    /// <summary>只借当前连接与事务；九方法原无token，取消能力由事务前准备透传。</summary>
    private CommandDefinition Command(string sql, object parameters) => new(sql, parameters, context.Session.Transaction);
    /// <summary>单身份参数沿既有类型映射。</summary>
    private static PostgreSqlParameters Id(Guid uid) { var p = new PostgreSqlParameters(); p.Add("Uid", uid); return p; }
    /// <summary>可空SourceType显式text绑定，NULL仍不匹配，不自行Trim/Upper输入。</summary>
    private static SourceParameters Source(string type, Guid uid) => new(type, uid);
    /// <summary>生命周期审计保持原因原文和墙上时间。</summary>
    private void Stamp(PostgreSqlParameters p, string reason) { p.AddTimestamp("Now", DateTime.Now); p.Add("UserName", context.CurrentUser.UserName); p.Add("Reason", reason); }
    /// <summary>来源集合更新同样使用原原因、用户与一次墙上时间，组合参数不改变公共绑定。</summary>
    private void Stamp(SourceParameters p, string reason) { p.AddTimestamp("Now", DateTime.Now); p.Add("UserName", context.CurrentUser.UserName); p.Add("Reason", reason); }
    /// <summary>来源字典类型可空，当前命令拥有独立参数实例。</summary>
    private sealed class SourceParameters(string type, Guid uid) : SqlMapper.IDynamicParameters
    {
        private readonly PostgreSqlParameters values = new();
        /// <summary>新增本命令的审计或生命周期标量。</summary>
        public void Add(string name, object value) => values.Add(name, value);
        /// <summary>审计时间保持无时区墙上值。</summary>
        public void AddTimestamp(string name, DateTime value) => values.AddTimestamp(name, value);
        /// <summary>现有标量克隆后显式补齐可空来源文本和UUID。</summary>
        public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
        { values.AddParameters(command, identity); command.Parameters.Add(new NpgsqlParameter("SourceType", NpgsqlDbType.Text) { Value = (object?)type ?? DBNull.Value }); command.Parameters.Add(new NpgsqlParameter("SourceUid", NpgsqlDbType.Uuid) { Value = uid }); }
    }
    /// <summary>SQL Server uniqueidentifier显示顺序，独立于准备锁的long排序。</summary>
    private static string UuidOrder(string column) => "ARRAY[" + string.Join(',', new[] { 10, 11, 12, 13, 14, 15, 8, 9, 7, 6, 5, 4, 3, 2, 1, 0 }.Select(x => $"get_byte(uuid_send({column}),{x})")) + "]";
}
