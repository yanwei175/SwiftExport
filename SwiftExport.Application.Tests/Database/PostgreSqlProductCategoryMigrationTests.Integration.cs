using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Categories;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>分类真实 SQL 验收只使用保留专用库；随机数据回滚，现存数据只参与只读行锁竞争。</summary>
[TestClass]
public sealed class PostgreSqlProductCategoryMigrationIntegrationTests
{
    /// <summary>子树包含自身，软删节点断开后代；根不存在或已删时不返回有效后代。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task Tree_IncludesRootAndStopsAtDeletedParent() => InRollback(async context =>
    {
        var root = Category(Prefix());
        var child = Category(Prefix(), root.Uid);
        var deleted = Category(Prefix(), root.Uid, deleted: true);
        var disconnected = Category(Prefix(), deleted.Uid);
        foreach (var entity in new[] { root, child, deleted, disconnected }) await Insert(context, entity);
        var repository = new PostgreSqlProductCategoryRepository(context);
        CollectionAssert.AreEquivalent(new[] { root.Uid, child.Uid }, (await repository.GetSubById(root.Uid)).Select(row => row.Uid).ToArray());
        Assert.HasCount(0, await repository.GetSubById(deleted.Uid));
        Assert.HasCount(0, await repository.GetSubById(Guid.NewGuid()));
        Assert.AreEqual(disconnected.Uid, (await repository.GetSubById(disconnected.Uid)).Single().Uid);
    });

    /// <summary>真实有限链确认根零计数：一百层允许，一百零一层拒绝返回部分数据。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    [DataRow(100)]
    [DataRow(101)]
    public Task Tree_EnforcesSqlServerDefaultDepthBoundary(int depth) => InRollback(async context =>
    {
        var root = Category(Prefix());
        await Insert(context, root);
        var parent = root.Uid;
        for (var i = 1; i <= depth; i++)
        {
            var child = Category(Prefix(), parent);
            await Insert(context, child);
            parent = child.Uid;
        }
        var repository = new PostgreSqlProductCategoryRepository(context);
        if (depth == 100) Assert.HasCount(101, await repository.GetSubById(root.Uid));
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetSubById(root.Uid));
    });

    /// <summary>循环坏数据无需无限递归或静默去重，仍固定失败且没有产生数据库事务错误。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public Task Tree_CycleFailsWithoutAbortingCallerTransaction(bool selfCycle) => InRollback(async context =>
    {
        var first = Category(Prefix());
        var second = Category(Prefix(), first.Uid);
        first.ParentId = selfCycle ? first.Uid : second.Uid;
        await Insert(context, first);
        await Insert(context, second);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new PostgreSqlProductCategoryRepository(context).GetSubById(first.Uid));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: context.Session.Transaction));
    });

    /// <summary>分页保持二十默认且无上限、窄投影空值、ICU匹配及 Build 字面与通配行为。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task Page_MapsNullsAndPreservesPatternsAndPaging() => InRollback(async context =>
    {
        var prefix = Prefix();
        var first = Category(prefix + "~%_[\\ X1Z");
        first.CategoryCode = prefix + "A";
        var second = Category(prefix + "Other");
        second.SortNo = 1;
        second.FakeName = prefix + "Alias";
        var deleted = Category(prefix + "Deleted", deleted: true);
        foreach (var entity in new[] { first, second, deleted }) await Insert(context, entity);
        var repository = new PostgreSqlProductCategoryRepository(context);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix.ToLowerInvariant(), PageIndex = 0, PageSize = 0 });
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(20, page.PageSize);
        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(first.Uid, page.Items[0].Uid);
        Assert.IsNull(page.Items[0].FakeName);
        Assert.IsNull(page.Items[0].ParentId);
        var literal = await repository.GetPagedAsync(new() { Keyword = prefix + "~%_[\\ X?Z", PageSize = 999 });
        Assert.AreEqual(999, literal.PageSize);
        Assert.AreEqual(first.Uid, literal.Items.Single().Uid);
        Assert.AreEqual(second.Uid, (await repository.GetPagedAsync(new() { Keyword = prefix + "Alias" })).Items.Single().Uid);
        var last = await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 2, PageSize = 1 });
        Assert.AreEqual(2, last.TotalCount);
        Assert.AreEqual(second.Uid, last.Items.Single().Uid);
        var empty = await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 3, PageSize = 1 });
        Assert.AreEqual(2, empty.TotalCount);
        Assert.HasCount(0, empty.Items);
    });

    /// <summary>版本冲突及软删沿用通用 CRUD；引用只统计未删主型号，多个主型号映射为同一分类身份。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task CrudAndReferences_KeepVersionAndMasterOnlyDeduplication() => InRollback(async context =>
    {
        var repository = new PostgreSqlProductCategoryRepository(context);
        var category = Category(Prefix());
        Assert.IsTrue((await repository.AddAsync(category)).Success);
        category.CategoryName = Prefix();
        Assert.IsTrue((await repository.UpdateAsync(category)).Success);
        Assert.AreEqual(2, (await repository.GetByIdAsync(category.Uid))!.Version);
        category.Version = 1;
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(category)).ErrorCode);
        var other = Category(Prefix());
        await Insert(context, other);
        foreach (var entity in new[] { Master(category.Uid), Master(category.Uid), Master(other.Uid, deleted: true) })
            await Insert(context, entity);
        CollectionAssert.AreEqual(new[] { category.Uid },
            (await repository.GetReferencedCategoryIdsAsync([Guid.Empty, category.Uid, category.Uid, other.Uid])).ToArray());
        Assert.IsTrue((await repository.GetAllForUpdateAsync()).Any(row => row.Uid == category.Uid && row.Version == 2));
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.DeleteAsync(category.Uid, 1)).ErrorCode);
        Assert.IsTrue((await repository.DeleteAsync(category.Uid, 2)).Success);
        Assert.IsNull(await repository.GetByIdAsync(category.Uid));
    });

    /// <summary>普通分类树与分页读取不阻塞其他连接的行锁；只读选出的现存叶子分类不进行任何数据修改。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task OrdinaryReads_DoNotBlockExistingCategoryRowLock() => Compete("category", takeLock: false);

    /// <summary>维护锁确实阻塞另一连接同一现存行，观测实际锁等待后依次回滚；主型号也只取锁不修改。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    [DataRow("category")]
    [DataRow("master")]
    public Task MaintenanceLocks_BlockExistingRowUntilRollback(string domain) => Compete(domain, takeLock: true);

    /// <summary>使用现存只读行避免未提交夹具不可见；没有可用行明确未运行，不创建提交型夹具。</summary>
    private static async Task Compete(string domain, bool takeLock)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var first = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var second = new DbSession(new PostgreSqlConnectionFactory(connection));
        var master = domain == "master";
        var selected = await first.Connection.QueryFirstOrDefaultAsync<LockTarget>(master ? """
            SELECT uid AS Uid,category_id AS CategoryId FROM public.product_master
            WHERE COALESCE(is_deleted,FALSE)=FALSE AND category_id<>@Empty ORDER BY uid LIMIT 1
            """ : """
            SELECT c.uid AS Uid FROM public.product_category c WHERE COALESCE(c.is_deleted,FALSE)=FALSE
            AND NOT EXISTS (SELECT 1 FROM public.product_category child WHERE child.parent_id=c.uid AND COALESCE(child.is_deleted,FALSE)=FALSE)
            ORDER BY c.uid LIMIT 1
            """, new { Empty = Guid.Empty });
        if (selected is null) Assert.Inconclusive("保留专用库没有可供只读锁竞争的有效记录；未创建提交夹具。");
        using var transactionA = first.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var transactionB = second.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        first.Transaction = transactionA;
        second.Transaction = transactionB;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<LockedSnapshot>? pending = null;
        var rolledBackA = false;
        try
        {
            var repository = new PostgreSqlProductCategoryRepository(Context(first));
            var table = master ? "product_master" : "product_category";
            var before = await first.Connection.QuerySingleAsync<LockedSnapshot>(
                $"SELECT uid AS Uid,to_jsonb(t)::text AS Content FROM public.{table} t WHERE uid=@Uid", new { selected!.Uid }, transactionA);
            if (takeLock)
            {
                if (master) Assert.IsTrue((await repository.GetReferencedCategoryIdsAsync([selected.CategoryId])).Contains(selected.CategoryId));
                else Assert.IsTrue((await repository.GetAllForUpdateAsync()).Any(row => row.Uid == selected.Uid));
            }
            else
            {
                Assert.AreEqual(selected.Uid, (await repository.GetSubById(selected.Uid)).Single().Uid);
                await repository.GetPagedAsync(new ProductCategorySearchRequest());
            }
            var pidA = await first.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionA);
            var pidB = await second.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionB);
            pending = second.Connection.QuerySingleAsync<LockedSnapshot>(new CommandDefinition(
                $"SELECT uid AS Uid,to_jsonb(t)::text AS Content FROM public.{table} t WHERE uid=@Uid FOR UPDATE",
                new { selected.Uid }, transactionB, commandTimeout: 15, cancellationToken: deadline.Token));
            if (takeLock)
            {
                await ObserveWait(first, pidA, pidB, pending, deadline.Token);
                Assert.IsFalse(pending.IsCompleted);
            }
            else
            {
                var acquired = await pending.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.AreEqual(before.Content, acquired.Content);
            }
            transactionA.Rollback();
            rolledBackA = true;
            first.Transaction = null;
            var after = await pending;
            Assert.AreEqual(selected.Uid, after.Uid);
            Assert.AreEqual(before.Content, after.Content, "只读锁竞争不得改变现存行内容。");
        }
        finally
        {
            try { if (!rolledBackA) transactionA.Rollback(); }
            finally
            {
                first.Transaction = null;
                deadline.Cancel();
                try { if (pending is not null) await pending; }
                catch (Exception) { /* 主路径保留失败，收尾任务后才能安全释放 B 的事务。 */ }
                try { transactionB.Rollback(); }
                finally { second.Transaction = null; }
            }
        }
    }

    /// <summary>实际锁等待及阻塞进程共同确认竞争，轮询间隔不作为等待成功证据。</summary>
    private static async Task ObserveWait(DbSession session, int pidA, int pidB, Task pending, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await session.Connection.ExecuteAsync(new CommandDefinition("SELECT pg_stat_clear_snapshot()", transaction: session.Transaction, cancellationToken: token));
            if (await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity a WHERE a.pid=@PidB AND a.state='active'
                    AND a.wait_event_type='Lock' AND @PidA=ANY(pg_blocking_pids(a.pid))
                    AND EXISTS (SELECT 1 FROM pg_locks l WHERE l.pid=a.pid AND NOT l.granted))
                """, new { PidA = pidA, PidB = pidB }, session.Transaction, cancellationToken: token))) return;
            Assert.IsFalse(pending.IsCompleted, "锁读取尚未形成可观察等待，竞争命令已提前结束。");
            await Task.Delay(50, token);
        }
        Assert.Fail("没有观测到预期只读行锁等待。");
    }

    /// <summary>单事务随机数据场景只回滚；不提交、不清理旧数据、不部署数据库结构。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await scenario(Context(session)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>沿用共享元数据及时间参数绑定，保存随机分类或主型号的软删夹具。</summary>
    private static async Task Insert(RepositoryBaseContext context, BaseEntity entity)
    {
        context.AuditStamp.StampForInsert(entity);
        var metadata = PostgreSqlEntityMetadata.GetMetadata(entity.GetType());
        var columns = string.Join(",", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var values = string.Join(",", metadata.Columns.Select(column => "@" + column.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(
            $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
            PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction));
    }

    /// <summary>随机分类保留可空字段，用于树和窄投影回归。</summary>
    private static ProductCategory Category(string name, Guid? parent = null, bool deleted = false) => new()
    { Uid = Guid.NewGuid(), CategoryName = name, ParentId = parent, IsDeleted = deleted, Version = 1 };

    /// <summary>仅新增随机主型号验证分类引用，不扩展到产品或其他关联域。</summary>
    private static ProductMaster Master(Guid categoryId, bool deleted = false) => new()
    { Uid = Guid.NewGuid(), MasterSku = Prefix(), CategoryId = categoryId, IsDeleted = deleted, Version = 1 };

    /// <summary>复用会话及标准审计上下文，不开启业务事务或新连接。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    {
        var user = new TestUser();
        return new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }

    /// <summary>随机短前缀使分页及文本匹配仅选中本场景的数据。</summary>
    private static string Prefix() => "PGC" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>只读竞争目标包含主型号的分类身份。</summary>
    private sealed class LockTarget
    {
        public Guid Uid { get; set; }
        public Guid CategoryId { get; set; }
    }

    /// <summary>整个现存行只读文本快照用于确认取锁前后数据不变。</summary>
    private sealed class LockedSnapshot
    {
        public Guid Uid { get; set; }
        public string Content { get; set; } = "";
    }

    /// <summary>提供随机测试记录的审计名，不代表真实登录权限。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_category__";
        public bool IsAuthenticated => false;
    }
}
