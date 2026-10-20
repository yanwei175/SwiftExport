using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Execution;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SwiftExport.Application.Tests.Database;

/// <summary>只验证输入、SQL翻译和参数边界；无连接会话不能冒充真实原生租约。</summary>
[TestClass]
public sealed class PostgreSqlNetDemandExecutionLinkMigrationTests
{
    /// <summary>三个空查询与空插入免SQL；Item过滤Empty，空输入不新增取消检查。</summary>
    [TestMethod]
    public async Task EmptyInputs_DoNotUseConnectionOrObserveCancellation()
    {
        var (repo, stamp) = Repository();
        var canceled = new CancellationToken(true);
        Assert.AreEqual(0, (await repo.GetActiveByNetDemandIdsForUpdateAsync([])).Count);
        Assert.AreEqual(0, (await repo.GetRollbackBlockersAsync([])).Count);
        Assert.AreEqual(0, (await repo.GetByCommandItemIdsForUpdateAsync([Guid.Empty, Guid.Empty], canceled)).Count);
        Assert.AreEqual(0, await repo.InsertPurchaseLinksAsync([], canceled));
        Assert.AreEqual(0, stamp.Calls);
    }

    /// <summary>插入null明确拒绝，其余查询保持原LINQ空参异常而不准备锁。</summary>
    [TestMethod]
    public async Task NullInputs_KeepOriginalArgumentBoundary()
    {
        var (repo, stamp) = Repository();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => repo.InsertPurchaseLinksAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => repo.GetActiveByNetDemandIdsForUpdateAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => repo.GetRollbackBlockersAsync(null!));
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => repo.GetByCommandItemIdsForUpdateAsync(null!));
        Assert.AreEqual(0, stamp.Calls);
    }

    /// <summary>整批先校验UID，再校验类型/贡献；首行合法也不能提前打戳。</summary>
    [TestMethod]
    [DataRow("null")]
    [DataRow("uid")]
    [DataRow("type")]
    [DataRow("type-space")]
    [DataRow("allocate")]
    [DataRow("release-negative")]
    [DataRow("release-over")]
    [DataRow("deleted")]
    public async Task InvalidBatch_IsRejectedBeforeAnyStamp(string kind)
    {
        var (repo, stamp) = Repository();
        var good = Link(); var bad = Link();
        switch (kind)
        {
            case "null": bad = null!; break;
            case "uid": bad.Uid = Guid.Empty; break;
            case "type": bad.TargetType = "po"; break;
            case "type-space": bad.TargetType = "PO "; break;
            case "allocate": bad.AllocatedQty = 0; break;
            case "release-negative": bad.ReleasedQty = -1; break;
            case "release-over": bad.ReleasedQty = 11; break;
            case "deleted": bad.IsDeleted = true; break;
        }
        var error = await Assert.ThrowsExactlyAsync<ArgumentException>(() => repo.InsertPurchaseLinksAsync([good, bad]));
        Assert.AreEqual("links", error.ParamName);
        StringAssert.StartsWith(error.Message, kind is "null" or "uid"
            ? "采购 Link 必须由 Application 预生成 Uid。" : "采购 Link 的目标类型、贡献数量或删除状态无效。");
        Assert.AreEqual(0, stamp.Calls);
        Assert.AreEqual(0, good.Version);
    }

    /// <summary>合法整批先全部打戳和版本归一，再拒绝缺失父准备；不新增空Item或状态预校验。</summary>
    [TestMethod]
    public async Task MissingParentPreparation_IsAfterOriginalStampPhase()
    {
        var (repo, stamp) = Repository();
        var first = Link(); var second = Link();
        first.CommandItemId = Guid.Empty; first.LinkStatus = (DemandExecutionLinkStatusEnum)77;
        first.Version = -1; second.Version = 12;
        var original = first.Uid;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.InsertPurchaseLinksAsync([first, second]));
        Assert.AreEqual(2, stamp.Calls);
        Assert.AreEqual(original, first.Uid);
        Assert.AreEqual(1, first.Version); Assert.AreEqual(12, second.Version);
        Assert.IsFalse(first.IsDeleted); Assert.AreEqual(Guid.Empty, first.CommandItemId);
        Assert.AreEqual((DemandExecutionLinkStatusEnum)77, first.LinkStatus);
    }

    /// <summary>原每行token边界保留：首行取消不打戳，打戳后取消阻止下一行。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_StopsAtOriginalPerRowBoundary(bool between)
    {
        using var source = new CancellationTokenSource();
        var (repo, stamp) = Repository();
        if (between) stamp.AfterInsert = source.Cancel; else source.Cancel();
        var first = Link(); var second = Link();
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => repo.InsertPurchaseLinksAsync([first, second], source.Token));
        Assert.AreEqual(source.Token, error.CancellationToken);
        Assert.AreEqual(between ? 1 : 0, stamp.Calls);
        Assert.AreEqual(0, second.Version);
    }

    /// <summary>三种非空读缺少父租约均在技术SQL之前拒绝；不伪造native持锁。</summary>
    [TestMethod]
    public async Task NonemptyReads_RejectMissingParentPreparationWithoutConnection()
    {
        var (repo, _) = Repository(); var id = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.GetActiveByNetDemandIdsForUpdateAsync([id]));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.GetRollbackBlockersAsync([id]));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repo.GetByCommandItemIdsForUpdateAsync([id]));
    }

    /// <summary>SQL合约验证数量投影、无状态裁剪、历史查重和无Net存在性过滤，不能代表实库通过。</summary>
    [TestMethod]
    public void QuerySql_PreservesOriginalReadPredicatesAndProjection()
    {
        var active = Sql("ActiveSql"); var blockers = Sql("BlockersSql"); var items = Sql("ItemsSql");
        StringAssert.Contains(active, "\"allocated_qty\"-l.\"released_qty\" AS \"AllocatedQty\"");
        StringAssert.Contains(active, "COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(active, "\"created_date\" NULLS FIRST");
        StringAssert.Contains(active,"link.link_status=0");
        StringAssert.Contains(active,"AS \"ReviewReason\"");
        StringAssert.Contains(active,"LEFT JOIN public.purchase_order_head");
        var whereBlockers = blockers.Split("WHERE")[1].Split("ORDER BY")[0];
        Assert.IsFalse(whereBlockers.Contains("target_type", StringComparison.Ordinal));
        StringAssert.Contains(whereBlockers,"l.link_status=0");
        StringAssert.Contains(blockers, "concat('净需求已关联 '");
        var whereItems = items.Split("WHERE")[1].Split("ORDER BY")[0];
        Assert.IsFalse(whereItems.Contains("link_status", StringComparison.Ordinal));
        Assert.IsFalse(whereItems.Contains("allocated_qty", StringComparison.Ordinal));
        Assert.IsFalse(whereItems.Contains("target_type", StringComparison.Ordinal));
        foreach (var sql in new[] { active, blockers, items })
        { StringAssert.Contains(sql, "FOR UPDATE OF l"); }
        Assert.IsFalse(items.Contains("JOIN",StringComparison.Ordinal));
        Assert.IsFalse(blockers.Contains("JOIN",StringComparison.Ordinal));
        var metadata = EntityMetadataCache.GetMetadata(typeof(DemandExecutionLink));
        foreach (var column in metadata.Columns)
        { StringAssert.Contains(items, "AS " + PostgreSqlNames.Quote(column.PropertyName)); StringAssert.Contains((string)typeof(PostgreSqlNetDemandExecutionLinkRepository).GetField("ExecutionLinkInsertSql", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!, "@" + column.ParameterName); }
    }

    /// <summary>未打开的真实Npgsql命令验证全列、可空text/日期及枚举参数，不执行SQL。</summary>
    [TestMethod]
    public void InsertParameters_AreCompleteAndExplicitForNullableValues()
    {
        var type = typeof(PostgreSqlNetDemandExecutionLinkRepository).GetNestedType("LinkParameters", BindingFlags.NonPublic)!;
        var link = Link(); link.CreatedDate = DateTime.SpecifyKind(new DateTime(2026, 10, 5), DateTimeKind.Utc);
        link.PlannedDeliveryDate = DateTime.SpecifyKind(new DateTime(2026, 10, 6), DateTimeKind.Utc);
        var parameters = (SqlMapper.IDynamicParameters)Activator.CreateInstance(type, link)!;
        using var command = new NpgsqlCommand(); parameters.AddParameters(command, null!);
        var metadata = EntityMetadataCache.GetMetadata(typeof(DemandExecutionLink));
        Assert.AreEqual(metadata.Columns.Count, command.Parameters.Count);
        foreach (var column in metadata.Columns)
        {
            var parameter = command.Parameters[column.ParameterName];
            if (column.Property.PropertyType == typeof(string)) Assert.AreEqual(NpgsqlDbType.Text, parameter.NpgsqlDbType);
            if (Nullable.GetUnderlyingType(column.Property.PropertyType) == typeof(DateTime))
                Assert.AreEqual(NpgsqlDbType.Timestamp, parameter.NpgsqlDbType);
        }
        Assert.AreEqual(DBNull.Value, command.Parameters["CancelledReason"].Value);
        Assert.AreEqual(link.Uid, command.Parameters["Uid"].Value);
        Assert.AreEqual(DateTimeKind.Unspecified, ((DateTime)command.Parameters["PlannedDeliveryDate"].Value!).Kind);
    }

    /// <summary>反射只读取私有SQL文本，不篡改租约、会话或锁覆盖。</summary>
    private static string Sql(string method) => (string)typeof(PostgreSqlNetDemandExecutionLinkRepository)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
    /// <summary>无连接会话只允许免SQL和父准备拒绝分支；若提前用连接，测试立即失败。</summary>
    private static (PostgreSqlNetDemandExecutionLinkRepository, Stamp) Repository()
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        session.Transaction = new Parent();
        var stamp = new Stamp();
        return (new(new RepositoryBaseContext(session, NullLoggerFactory.Instance, stamp, new User())), stamp);
    }
    /// <summary>构造原C#校验合法的独立对象，不当作实库逻辑引用夹具。</summary>
    private static DemandExecutionLink Link() => new()
    { Uid = Guid.NewGuid(), Version = 0, NetDemandId = Guid.NewGuid(), CommandItemId = Guid.NewGuid(), TargetType = "PO", AllocatedQty = 10 };
    /// <summary>离线观察打戳次数与逐行取消阶段，不模拟数据库持锁。</summary>
    private sealed class Stamp : IAuditStampService
    {
        internal int Calls; internal Action? AfterInsert;
        /// <summary>原样观察对象并执行测试取消屏障。</summary>
        public void StampForInsert(BaseEntity entity) { Calls++; entity.CreatedBy = "NL离线"; AfterInsert?.Invoke(); }
        /// <summary>本批不允许更新打戳。</summary>
        public void StampForUpdate(BaseEntity entity) => throw new NotSupportedException();
    }
    /// <summary>离线当前用户，无权限或认证模拟。</summary>
    private sealed class User : ICurrentUser
    {
        /// <summary>离线可辨识用户名称。</summary>
        public string UserName => "NL离线";
        /// <summary>不借用真实账号。</summary>
        public Guid? UserId => null;
        /// <summary>离线不声称已认证。</summary>
        public bool IsAuthenticated => false;
    }
    /// <summary>仅表明存在父事务；任何提交、回滚或连接访问均禁止。</summary>
    private sealed class Parent : IDbTransaction
    {
        /// <summary>任何连接访问代表违反免SQL边界。</summary>
        public IDbConnection Connection => throw new NotSupportedException();
        /// <summary>仅表明已有RC父事务。</summary>
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        /// <summary>离线不得提交。</summary>
        public void Commit() => throw new NotSupportedException();
        /// <summary>离线不得回滚。</summary>
        public void Rollback() => throw new NotSupportedException();
        /// <summary>无真实资源需要释放。</summary>
        public void Dispose() { }
    }
}
