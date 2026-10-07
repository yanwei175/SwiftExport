using System.Data;
using System.Data.SqlTypes;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实分组聚合/详情查询场景只编写编译，由主控在固定故障库门禁下执行；随机夹具单事务回滚。</summary>
[DoNotParallelize]
public sealed partial class PostgreSqlGrossDemandWorkspaceDetailMigrationTests
{
    /// <summary>同SourceUid跨类型/编号独立MAX，混合状态仅按入参过滤，软删除不计数；版本更新不替代MAX。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandWorkspaceDetailIntegration")]
    [DataRow("all")][DataRow("null")][DataRow("active")][DataRow("history")][DataRow("unknown")]
    public Task RealPg_GroupsKeepIndependentMaxAndLifecycleFilter(string mode) => Rollback(async f =>
    {
        var source = Guid.NewGuid(); var rows = new[]
        {
            Row(source, "a", "B", 0), Row(source, "B", "a", 8), Row(source, "A", "0", 9), Row(source, "Z", "Z", 0)
        };
        rows[0].Version = 99; rows[0].CreatedDate = new(2026, 10, 4); rows[1].Version = 1; rows[1].CreatedDate = new(2026, 9, 1);
        rows[3].IsDeleted = true;
        foreach (var row in rows) await f.Insert(row);
        var statuses = mode switch
        {
            "null" => null!, "active" => new List<GrossDemandLifecycleStatusEnum> { (GrossDemandLifecycleStatusEnum)0, (GrossDemandLifecycleStatusEnum)0 },
            "history" => [(GrossDemandLifecycleStatusEnum)8, (GrossDemandLifecycleStatusEnum)9], "unknown" => [(GrossDemandLifecycleStatusEnum)777],
            _ => new List<GrossDemandLifecycleStatusEnum>()
        };
        var groups = await f.Repository.SearchSourceGroupsAsync(new() { LifecycleStatuses = statuses });
        var group = groups.SingleOrDefault(row => row.SourceUid == source);
        if (mode == "unknown") { Assert.IsNull(group); return; }
        Assert.IsNotNull(group);
        Assert.AreEqual(mode is "active" ? 1 : mode is "history" ? 2 : 3, group.DemandCount);
        // 冻结字典比较下B>a，与Ordinal和“取最新一行”不同；两个MAX来自不同原始行。
        Assert.AreEqual(mode == "active" ? "a" : "B", group.SourceType);
        Assert.AreEqual(mode == "history" ? "a" : "B", group.SourceNo);
        Assert.AreSame(f.Transaction, f.Session.Transaction); Assert.AreEqual(1, await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: f.Transaction));
    });

    /// <summary>仅软删集合不出现，缺客户/产品及关闭/作废仍参与，不增加最新来源或当前资格。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandWorkspaceDetailIntegration")]
    public Task RealPg_GroupsDoNotAddEligibilityAndIgnoreDeletedOnly() => Rollback(async f =>
    {
        var hidden = Guid.NewGuid(); var visible = Guid.NewGuid();
        var deleted = Row(hidden, "MD", UniqueNo(), 0); deleted.IsDeleted = true; await f.Insert(deleted);
        var closed = Row(visible, "CR", UniqueNo(), 8); var voided = Row(visible, "SO", closed.SourceNo, 9);
        await f.Insert(closed); await f.Insert(voided);
        var groups = await f.Repository.SearchSourceGroupsAsync(new());
        Assert.IsFalse(groups.Any(row => row.SourceUid == hidden));
        var actual = groups.Single(row => row.SourceUid == visible); Assert.AreEqual(2, actual.DemandCount); Assert.AreEqual("SO", actual.SourceType);
    });

    /// <summary>相同MAX类型/编号的尾键保留源SqlGuid而非PG UUID序，预期来自SqlGuid比较；不使用Ordinal替代聚合。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandWorkspaceDetailIntegration")]
    public Task RealPg_GroupsUseSourceUuidTieOrder() => Rollback(async f =>
    {
        var seed = Guid.NewGuid().ToString("N");
        var first = Guid.ParseExact("a0000000" + seed.Substring(8, 22) + "00", "N");
        var second = Guid.ParseExact("00000000" + seed.Substring(8, 22) + "01", "N");
        var no = UniqueNo(); await f.Insert(Row(first, "MD", no, 0)); await f.Insert(Row(second, "MD", no, 0));
        var groups = (await f.Repository.SearchSourceGroupsAsync(new())).Where(row => row.SourceUid == first || row.SourceUid == second).ToArray();
        var expected = new[] { first, second }.OrderBy(value => new SqlGuid(value)).ToArray();
        Assert.HasCount(2, groups); CollectionAssert.AreEqual(expected, groups.Select(row => row.SourceUid).ToArray());
        Assert.IsFalse(expected.SequenceEqual(new[] { first, second }.OrderBy(value => value)), "夹具必须区分源与PG字节顺序");
    });

    /// <summary>详情投影所有原字段，包括历史生命周期、NULL客户/产品、版本、单位原文与审计墙上时间。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandWorkspaceDetailIntegration")]
    [DataRow(0)][DataRow(8)][DataRow(9)]
    public Task RealPg_DetailPreservesSnapshotsStatusAndWallTime(int state) => Rollback(async f =>
    {
        var row = Row(Guid.NewGuid(), "SO", " RAW ", state); row.Version = 7; row.SourceVersion = 5; row.SourceLineNumber = 4;
        row.LinkSkuSnapshot = " LINK "; row.CustomerSkuSnapshot = "CS"; row.DescriptionSnapshot = "说明"; row.ColorSnapshot = "颜色";
        row.SpecificationSnapshot = "规格"; row.ImagePathSnapshot = "图片"; row.UnitSnapshot = " PCS "; row.DemandQty = 12.3456m;
        row.RequiredDate = new(2026, 9, 3, 10, 11, 12); row.Remark = "备注"; row.LifecycleChangedAt = new(2026, 9, 4, 13, 14, 15);
        row.LifecycleChangedBy = "状态人"; row.LifecycleChangedReason = "原因"; row.CreatedDate = new(2026, 9, 1, 8, 9, 10);
        row.CreatedBy = "建立人"; row.UpdatedDate = new(2026, 9, 2, 11, 12, 13); row.UpdatedBy = "修改人";
        await f.Insert(row); var actual = await f.Repository.GetWorkspaceDetailAsync(row.Uid);
        Assert.IsNotNull(actual); Assert.AreEqual(row.Uid, actual.Uid); Assert.AreEqual(7, actual.Version);
        Assert.AreEqual(row.LifecycleStatus, actual.LifecycleStatus); Assert.AreEqual(row.SourceUid, actual.SourceUid); Assert.AreEqual(row.SourceLineId, actual.SourceLineId);
        Assert.AreEqual("SO", actual.SourceType); Assert.AreEqual(" RAW ", actual.SourceNo); Assert.AreEqual(4, actual.SourceLineNumber); Assert.AreEqual(5, actual.SourceVersion);
        Assert.IsNull(actual.CustomerId); Assert.IsNull(actual.ProductId); Assert.IsTrue(actual.IsNonStandard); Assert.AreEqual(row.SkuSnapshot, actual.Sku);
        Assert.AreEqual(" LINK ", actual.LinkSku); Assert.AreEqual("CS", actual.CustomerSku); Assert.AreEqual("说明", actual.Description); Assert.AreEqual("颜色", actual.Color);
        Assert.AreEqual("规格", actual.Specification); Assert.AreEqual("图片", actual.ImagePath); Assert.AreEqual(" PCS ", actual.Unit); Assert.AreEqual(12.3456m, actual.DemandQty);
        Assert.IsTrue(row.RequiredDate == actual.RequiredDate); Assert.AreEqual("备注", actual.Remark);
        Assert.IsTrue(row.LifecycleChangedAt == actual.LifecycleChangedAt); Assert.AreEqual("状态人", actual.LifecycleChangedBy); Assert.AreEqual("原因", actual.LifecycleChangedReason);
        Assert.IsTrue(row.CreatedDate == actual.CreatedDate); Assert.AreEqual("建立人", actual.CreatedBy); Assert.IsTrue(row.UpdatedDate == actual.UpdatedDate); Assert.AreEqual("修改人", actual.UpdatedBy);
        Assert.AreEqual(DateTimeKind.Unspecified, actual.CreatedDate!.Value.Kind);
        // 调用者同事务写入立即可见，重复读取并非仓储缓存。
        await f.Session.Connection.ExecuteAsync("UPDATE public.gross_demand SET remark='第二次' WHERE uid=@Uid", new { row.Uid }, f.Transaction);
        Assert.AreEqual("第二次", (await f.Repository.GetWorkspaceDetailAsync(row.Uid))!.Remark); Assert.AreSame(f.Transaction, f.Session.Transaction);
    });

    /// <summary>合法NULL快照和四种单位边界原样返回，不打戳补空审计值，不借指纹推断资格。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandWorkspaceDetailIntegration")]
    [DataRow(null)][DataRow("")][DataRow(" PCS ")][DataRow("\t\u3000")]
    public Task RealPg_DetailKeepsNullableFieldsAndRawUnit(string? unit) => Rollback(async f =>
    {
        var row = Row(Guid.NewGuid(), "MD", UniqueNo(), 0); row.UnitSnapshot = unit;
        await f.Insert(row); var actual = await f.Repository.GetWorkspaceDetailAsync(row.Uid);
        Assert.IsNotNull(actual); Assert.AreEqual(unit, actual.Unit); Assert.IsNull(actual.SourceVersion); Assert.IsNull(actual.SourceLineNumber);
        Assert.IsNull(actual.CustomerId); Assert.IsNull(actual.ProductId); Assert.IsNull(actual.LinkSku); Assert.IsNull(actual.CustomerSku);
        Assert.IsNull(actual.Description); Assert.IsNull(actual.Color); Assert.IsNull(actual.Specification); Assert.IsNull(actual.ImagePath);
        Assert.IsNull(actual.RequiredDate); Assert.IsNull(actual.Remark); Assert.IsNull(actual.LifecycleChangedAt); Assert.IsNull(actual.LifecycleChangedBy);
        Assert.IsNull(actual.LifecycleChangedReason); Assert.IsNull(actual.CreatedDate); Assert.IsNull(actual.CreatedBy); Assert.IsNull(actual.UpdatedDate); Assert.IsNull(actual.UpdatedBy);
    });

    /// <summary>软删详情与不存在/空Uid返回NULL，软删不通过通用Add重新置活。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandWorkspaceDetailIntegration")]
    public Task RealPg_DeletedAndMissingDetailReturnNull() => Rollback(async f =>
    {
        var row = Row(Guid.NewGuid(), "MD", UniqueNo(), 0); row.IsDeleted = true; await f.Insert(row);
        Assert.IsNull(await f.Repository.GetWorkspaceDetailAsync(row.Uid)); Assert.IsNull(await f.Repository.GetWorkspaceDetailAsync(Guid.NewGuid()));
        Assert.IsNull(await f.Repository.GetWorkspaceDetailAsync(Guid.Empty)); Assert.AreSame(f.Transaction, f.Session.Transaction);
    });

    /// <summary>可空事务及两个入口预取消真实执行后连接仍可查询，源NULL请求优先级不变。</summary>
    [TestMethod, TestCategory("PostgreSqlGrossDemandWorkspaceDetailIntegration")]
    public async Task RealPg_OptionalTransactionAndCancellation()
    {
        using var session = Session(); var repo = new PostgreSqlGrossDemandQueryRepository(Context(session));
        Assert.IsNull(session.Transaction); Assert.IsNull(await repo.GetWorkspaceDetailAsync(Guid.NewGuid()));
        await repo.SearchSourceGroupsAsync(new() { LifecycleStatuses = [(GrossDemandLifecycleStatusEnum)777] });
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.GetWorkspaceDetailAsync(Guid.NewGuid(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repo.SearchSourceGroupsAsync(new(), token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SearchSourceGroupsAsync(null!, token));
        Assert.IsNull(session.Transaction); Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
    }

    /// <summary>只允许现有固定故障库，未配置则未运行，不提供默认连接。</summary>
    private static DbSession Session() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>只复用会话和审计依赖，不修改公共DI。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new WorkspaceUser(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }

    /// <summary>所有随机实体仅在一条ReadCommitted事务内写读，finally回滚，不初始化/清理或提交。</summary>
    private static async Task Rollback(Func<WorkspaceFixture, Task> action)
    {
        using var session = Session(); using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        try { await action(new(session, transaction)); Assert.AreSame(transaction, session.Transaction); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>保留SourceUid跨行关联，每行SourceLineId独立以满足当前来源唯一约束；合法非标来源不依赖产品。</summary>
    private static GrossDemand Row(Guid source, string type, string no, int state) => new()
    {
        Uid = Guid.NewGuid(), SourceUid = source, SourceLineId = Guid.NewGuid(), SourceType = type, SourceNo = no,
        LifecycleStatus = (GrossDemandLifecycleStatusEnum)state, IsNonStandard = true, SkuSnapshot = "SKU", DemandQty = 1m
    };

    /// <summary>测试数据独占唯一编号。</summary>
    private static string UniqueNo() => "PGGW" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    /// <summary>直接按既有元数据INSERT保存所有指定值；不打戳补NULL，不经Add改写身份或软删除。</summary>
    private sealed class WorkspaceFixture(DbSession session, IDbTransaction transaction)
    {
        public DbSession Session => session;
        public IDbTransaction Transaction => transaction;
        public PostgreSqlGrossDemandQueryRepository Repository { get; } = new(Context(session));
        /// <summary>仅插入本例合法实体，不绕CHECK、唯一约束或触发器。</summary>
        public async Task Insert(GrossDemand row)
        {
            var metadata = EntityMetadataCache.GetMetadata(typeof(GrossDemand));
            var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
            var values = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
            Assert.AreEqual(1, await session.Connection.ExecuteAsync(new CommandDefinition(
                $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
                PostgreSqlParameters.Entity(row, metadata), transaction)));
        }
    }
}
