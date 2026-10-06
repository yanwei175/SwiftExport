using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主控补验物料工作台无外层事务、真正等待取消及连接复用；不插入或重置数据，局部表锁最终回滚。</summary>
[TestClass]
[DoNotParallelize]
[TestCategory("PostgreSqlIntegration")]
public sealed class PostgreSqlMaterialDemandWorkspaceProtocolIntegrationTests
{
    /// <summary>空事务实际执行两方法，空页及空来源保留分页契约且不产生会话临时资源。</summary>
    [TestMethod]
    public async Task WithoutOuterTransaction_ExecutesBothReadsAndKeepsEmptyPageContract()
    {
        using var session = Open(); var repository = Repository(session);
        var page = await repository.SearchManagerAsync(new() { NetDemandIds = [Guid.NewGuid()], PageIndex = 99, PageSize = 20 });
        Assert.AreEqual(0, page.TotalCount); Assert.HasCount(0, page.Items); Assert.AreEqual(99, page.PageIndex); Assert.AreEqual(20, page.PageSize);
        var source = await repository.SearchManagerAsync(new() { SourceUid = Guid.NewGuid(), PageIndex = 99, PageSize = 200 });
        Assert.AreEqual(0, source.TotalCount); Assert.HasCount(0, source.Items); Assert.AreEqual(1, source.PageIndex); Assert.AreEqual(1, source.PageSize);
        Assert.HasCount(0, await repository.SearchMaterialSourceGroupsAsync(new() { SourceTypes = ["__EMPTY_" + Guid.NewGuid().ToString("N")] }));
        await Reusable(session);
    }

    /// <summary>两种公开读取的预取消仍保留原取消异常，无外层事务及临时对象遗留，随后连接可查询。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Precancellation_ReleasesConnectionForNextRead(bool groups)
    {
        using var session = Open(); var repository = Repository(session); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        if (groups) await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SearchMaterialSourceGroupsAsync(new(), cancellation.Token));
        else await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SearchManagerAsync(new() { NetDemandIds = [Guid.NewGuid()] }, cancellation.Token));
        await Reusable(session);
    }

    /// <summary>独立连接局部锁让真实SELECT等待后取消，事务为空、锁归零且连接随后可用；不做DDL或数据写入。</summary>
    [TestMethod]
    public async Task CancellationDuringServerWait_ReleasesReadAndKeepsSessionUsable()
    {
        using var reader = Open(); using var holder = Open(); using var observer = Open();
        using var transaction = holder.Connection.BeginTransaction(IsolationLevel.ReadCommitted); holder.Transaction = transaction;
        using var cancellation = new CancellationTokenSource();
        await holder.Connection.ExecuteAsync("LOCK TABLE public.net_demand IN ACCESS EXCLUSIVE MODE", transaction: transaction);
        var pending = Repository(reader).SearchManagerAsync(new() { NetDemandIds = [Guid.NewGuid()] }, cancellation.Token);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pid = ((NpgsqlConnection)reader.Connection).ProcessID;
            while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT cardinality(pg_blocking_pids(@Pid))>0", new { Pid = pid }, cancellationToken: deadline.Token)))
                await Task.Delay(25, deadline.Token);
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            cancellation.Cancel();
            try { await pending; } catch (OperationCanceledException) { /* 已断言取消，先收完驱动任务再释放测试持锁方。 */ }
            transaction.Rollback(); holder.Transaction = null;
        }
        await Reusable(reader);
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype IN ('advisory','relation','tuple')", new { Pid = ((NpgsqlConnection)reader.Connection).ProcessID }));
        Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_locks WHERE pid=@Pid AND locktype IN ('advisory','relation','tuple')", new { Pid = ((NpgsqlConnection)holder.Connection).ProcessID }));
    }

    /// <summary>复用已有严格故障库门禁，连接内容不输出；本测试仅无事务读取及短暂局部表锁。</summary>
    private static DbSession Open() => new(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));

    /// <summary>借用同一会话和既有审计上下文，不自建仓储事务或共享生产依赖。</summary>
    private static PostgreSqlMaterialDemandQueryRepository Repository(DbSession session)
    {
        var identity = new Identity();
        return new(new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(identity), identity));
    }

    /// <summary>通过后续真实查询确认连接可继续使用、逻辑事务为空及会话无临时表。</summary>
    private static async Task Reusable(DbSession session)
    {
        Assert.IsNull(session.Transaction); Assert.AreEqual(1, await session.Connection.ExecuteScalarAsync<int>("SELECT 1"));
        Assert.AreEqual(0L, await session.Connection.ExecuteScalarAsync<long>("SELECT count(*) FROM pg_class WHERE relnamespace=pg_my_temp_schema() AND relkind IN ('r','p')"));
    }

    /// <summary>只读夹具身份，不使用真实用户或记录连接秘密。</summary>
    private sealed class Identity : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "物料工作台读取协议";
        public bool IsAuthenticated => false;
    }
}
