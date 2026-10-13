using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Products.ProductMasters;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>主型号迁移只在保留专用库验收；随机数据全部回滚，现存行只参与只读锁竞争。</summary>
[TestClass]
public sealed class PostgreSqlProductMasterMigrationIntegrationTests
{
    /// <summary>稳定主图选择及中性编码 EXISTS 不重复分页，名称保留软删和停用边界，组合分类筛选正确。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductMasterIntegration")]
    public Task Page_KeepsStableImageCategoryBranchesAndNullableProjection() => InRollback(async context =>
    {
        var prefix = Prefix();
        var category = new ProductCategory { CategoryName = prefix + "Category" };
        var deletedCategory = new ProductCategory { CategoryName = prefix + "Deleted", IsDeleted = true };
        var supplier = new Supplier { SupplierCode = prefix, SupplierName = prefix + "Supplier", DocRelateNo = prefix, IsActive = false };
        var deletedSupplier = new Supplier { SupplierCode = prefix + "Deleted", SupplierName = prefix + "DeletedSupplier", DocRelateNo = prefix, IsDeleted = true };
        foreach (var entity in new BaseEntity[] { category, deletedCategory, supplier, deletedSupplier }) await Insert(context, entity);
        var first = Master(prefix + "A", category.Uid);
        first.DefaultSupplierId = supplier.Uid;
        var second = Master(prefix + "B");
        var invalid = Master(prefix + "C", deletedCategory.Uid);
        invalid.DefaultSupplierId = deletedSupplier.Uid;
        var deleted = Master(prefix + "D", deleted: true);
        foreach (var master in new[] { first, second, invalid, deleted }) await Insert(context, master);
        var later = Neutral(first.Uid, prefix + "Z", main: true, image: "later");
        var earliest = Neutral(first.Uid, prefix + "A", main: true, image: "chosen");
        earliest.DefaultSalesPriceUsd = 12m;
        earliest.DefaultPurchasePriceCny = 25m;
        var hidden = Neutral(first.Uid, prefix + "0", main: true, image: "deleted", deleted: true);
        var keyword = prefix + "~%_[\\ X1Z";
        foreach (var neutral in new[] { later, earliest, hidden, Neutral(first.Uid, keyword), Neutral(first.Uid, keyword + "2"), Neutral(second.Uid, keyword, deleted: true) })
            await Insert(context, neutral);
        var repository = new PostgreSqlProductMasterRepository(context);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix.ToLowerInvariant(), PageIndex = 0, PageSize = 0 });
        Assert.AreEqual(3, page.TotalCount);
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(50, page.PageSize);
        Assert.AreEqual(first.Uid, page.Items[0].Uid);
        Assert.AreEqual("chosen", page.Items[0].MainImagePath);
        Assert.AreEqual(12m, page.Items[0].DefaultSalesPriceUsd);
        Assert.AreEqual(25m, page.Items[0].DefaultPurchasePriceCny);
        Assert.AreEqual(category.CategoryName, page.Items[0].CategoryName);
        Assert.AreEqual(supplier.SupplierName, page.Items[0].DefaultSupplierName);
        Assert.IsNull(page.Items[0].Data.DefaultDescriptionEn);
        Assert.IsNull(page.Items[1].MainImagePath);
        Assert.IsNull(page.Items[1].DefaultSupplierName);
        Assert.AreEqual("未分类", page.Items[1].CategoryName);
        Assert.AreEqual("分类已失效", page.Items[2].CategoryName);
        Assert.IsNull(page.Items[2].DefaultSupplierName);
        var exists = await repository.GetPagedAsync(new() { Keyword = prefix + "~%_[\\ X?Z", PageSize = 999 });
        Assert.AreEqual(1, exists.TotalCount);
        Assert.AreEqual(999, exists.PageSize);
        Assert.AreEqual(first.Uid, exists.Items.Single().Uid);
        var onlyCategory = await repository.GetPagedAsync(new() { Keyword = prefix, CategoryIds = [Guid.Empty, category.Uid, category.Uid] });
        Assert.AreEqual(first.Uid, onlyCategory.Items.Single().Uid);
        Assert.AreEqual(second.Uid, (await repository.GetPagedAsync(new() { Keyword = prefix, IncludeUncategorized = true })).Items.Single().Uid);
        var combined = await repository.GetPagedAsync(new() { Keyword = prefix, IncludeUncategorized = true, CategoryIds = [category.Uid] });
        CollectionAssert.AreEquivalent(new[] { first.Uid, second.Uid }, combined.Items.Select(row => row.Uid).ToArray());
        Assert.AreEqual(second.Uid, (await repository.GetPagedAsync(new()
            { Keyword = prefix, IncludeUncategorized = true, CategoryIds = [category.Uid], ExcludeCategoryId = category.Uid })).Items.Single().Uid);
        Assert.AreEqual(second.Uid, (await repository.GetPagedAsync(new() { Keyword = prefix, PageSize = 1, PageIndex = 2 })).Items.Single().Uid);
        var empty = await repository.GetPagedAsync(new() { Keyword = prefix, PageSize = 1, PageIndex = 4 });
        Assert.AreEqual(3, empty.TotalCount);
        Assert.HasCount(0, empty.Items);
    });

    /// <summary>详情仅锁主型号而容许可空联表；输入 Trim、尾空格及大小写比较不修剪存储前导空格，不含软删行。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductMasterIntegration")]
    public Task DetailAndCodes_PreserveNullJoinsAndNormalization() => InRollback(async context =>
    {
        var prefix = Prefix();
        var first = Master(prefix + "Ab ");
        first.CategoryId = Guid.NewGuid();
        first.DefaultSupplierId = Guid.NewGuid();
        var leading = Master(" " + prefix + "Lead");
        var deleted = Master(prefix + "Deleted", deleted: true);
        foreach (var master in new[] { first, leading, deleted }) await Insert(context, master);
        var repository = new PostgreSqlProductMasterRepository(context);
        var detail = await repository.GetDetailHeaderAsync(first.Uid, true);
        Assert.AreEqual("分类已失效", detail!.CategoryName);
        Assert.IsNull(detail.DefaultSupplierName);
        Assert.IsNull(detail.Master.DefaultFeaturesEn);
        Assert.IsNull(await repository.GetDetailHeaderAsync(deleted.Uid));
        Assert.IsNull(await repository.GetDetailHeaderAsync(Guid.NewGuid()));
        var owners = await repository.GetByMasterSkusAsync([" " + prefix.ToLowerInvariant() + "ab ", prefix + "AB", " ", deleted.MasterSku], true);
        Assert.AreEqual(first.Uid, owners.Single().Uid);
        Assert.AreEqual(first.MasterSku, owners.Single().MasterSku);
        Assert.HasCount(0, await repository.GetByMasterSkusAsync([leading.MasterSku]));
        Assert.HasCount(0, await repository.GetByMasterSkusAsync([] , true));
    });

    /// <summary>归类只改分类审计与版本；相同分类、旧版本、软删或不存在均冲突，继承 CRUD 保持乐观版本。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductMasterIntegration")]
    public Task CrudAndAssignCategory_PreserveActualChangeAndVersion() => InRollback(async context =>
    {
        var repository = new PostgreSqlProductMasterRepository(context);
        var master = Master(Prefix());
        master.ProductSeries = "保留系列";
        Assert.IsTrue((await repository.AddAsync(master)).Success);
        master.DefaultDescriptionZh = "修改描述";
        Assert.IsTrue((await repository.UpdateAsync(master)).Success);
        Assert.AreEqual(2, (await repository.GetByIdAsync(master.Uid))!.Version);
        var category = new ProductCategory { CategoryName = Prefix() };
        await Insert(context, category);
        Assert.IsTrue((await repository.AssignCategoryAsync(master.Uid, 2, category.Uid)).Success);
        var saved = await repository.GetByIdAsync(master.Uid);
        Assert.AreEqual(3, saved!.Version);
        Assert.AreEqual(category.Uid, saved.CategoryId);
        Assert.AreEqual(master.MasterSku, saved.MasterSku);
        Assert.AreEqual("保留系列", saved.ProductSeries);
        Assert.AreEqual("修改描述", saved.DefaultDescriptionZh);
        Assert.AreEqual("__pgtest_master__", saved.UpdatedBy);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.AssignCategoryAsync(master.Uid, 3, category.Uid)).ErrorCode);
        Assert.AreEqual(3, (await repository.GetByIdAsync(master.Uid))!.Version);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.AssignCategoryAsync(master.Uid, 2, Guid.Empty)).ErrorCode);
        // 仓储保留原本允许空 Guid 的条件；Application 的目标分类校验不在本测试中改变。
        Assert.IsTrue((await repository.AssignCategoryAsync(master.Uid, 3, Guid.Empty)).Success);
        Assert.AreEqual(4, (await repository.GetByIdAsync(master.Uid))!.Version);
        master.Version = 1;
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.UpdateAsync(master)).ErrorCode);
        Assert.IsTrue((await repository.DeleteAsync(master.Uid, 4)).Success);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.AssignCategoryAsync(master.Uid, 4, category.Uid)).ErrorCode);
        Assert.AreEqual("CONCURRENCY_CONFLICT", (await repository.AssignCategoryAsync(Guid.NewGuid(), 1, category.Uid)).ErrorCode);
    });

    /// <summary>四直接方法预取消不破坏外层事务；无事务维护锁必须在访问数据库前失败。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductMasterIntegration")]
    public Task CancellationAndLockPrerequisites_LeaveCallerTransactionUsable() => InRollback(async context =>
    {
        var repository = new PostgreSqlProductMasterRepository(context);
        var token = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetPagedAsync(new(), token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetDetailHeaderAsync(Guid.NewGuid(), false, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByMasterSkusAsync([Prefix()], false, token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.AssignCategoryAsync(Guid.NewGuid(), 1, Guid.NewGuid(), token));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteScalarAsync<int>("SELECT 1", transaction: context.Session.Transaction));
        var transaction = context.Session.Transaction;
        context.Session.Transaction = null;
        try
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetDetailHeaderAsync(Guid.NewGuid(), true));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.GetByMasterSkusAsync([Prefix()], true));
            Assert.HasCount(0, await repository.GetByMasterSkusAsync([], true));
        }
        finally { context.Session.Transaction = transaction; }
    });

    /// <summary>普通详情分页不阻塞同行锁；详情或编码维护锁实际阻塞 B，回滚释放后双方均只读且现存行不变。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductMasterIntegration")]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task ReadLocks_OnlyProtectExistingMasterWhenRequested(int mode)
    {
        var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var first = new DbSession(new PostgreSqlConnectionFactory(connection));
        using var second = new DbSession(new PostgreSqlConnectionFactory(connection));
        var selected = await first.Connection.QueryFirstOrDefaultAsync<ProductMaster>(
            "SELECT uid AS Uid,master_sku AS MasterSku FROM public.product_master WHERE is_deleted=FALSE AND master_sku=btrim(master_sku,' ') ORDER BY uid LIMIT 1");
        if (selected is null) Assert.Inconclusive("专用库没有可供只读锁竞争的有效主型号；不新增提交型夹具。");
        var target = selected!;
        using var transactionA = first.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var transactionB = second.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        first.Transaction = transactionA;
        second.Transaction = transactionB;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<string>? pending = null;
        var rolledBackA = false;
        try
        {
            var before = await first.Connection.ExecuteScalarAsync<string>(
                "SELECT to_jsonb(pm)::text FROM public.product_master pm WHERE uid=@Uid", new { target.Uid }, transactionA);
            var repository = new PostgreSqlProductMasterRepository(Context(first));
            if (mode == 2) Assert.IsTrue((await repository.GetByMasterSkusAsync([target.MasterSku], true)).Any(row => row.Uid == target.Uid));
            else Assert.AreEqual(target.Uid, (await repository.GetDetailHeaderAsync(target.Uid, mode == 1))!.Uid);
            if (mode == 0) await repository.GetPagedAsync(new());
            var pidA = await first.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionA);
            var pidB = await second.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: transactionB);
            pending = second.Connection.QuerySingleAsync<string>(new CommandDefinition(
                "SELECT to_jsonb(pm)::text FROM public.product_master pm WHERE uid=@Uid FOR UPDATE OF pm",
                new { target.Uid }, transactionB, commandTimeout: 15, cancellationToken: deadline.Token));
            if (mode == 0) Assert.AreEqual(before, await pending.WaitAsync(TimeSpan.FromSeconds(3)));
            else
            {
                await ObserveWait(first, pidA, pidB, pending, deadline.Token);
                Assert.IsFalse(pending.IsCompleted);
            }
            transactionA.Rollback();
            rolledBackA = true;
            first.Transaction = null;
            Assert.AreEqual(before, await pending, "只读锁竞争不应修改现存主型号。");
        }
        finally
        {
            try { if (!rolledBackA) transactionA.Rollback(); }
            finally
            {
                first.Transaction = null;
                deadline.Cancel();
                try { if (pending is not null) await pending; }
                catch (Exception) { /* 主路径保留失败，收尾在途命令后再释放事务。 */ }
                try { transactionB.Rollback(); }
                finally { second.Transaction = null; }
            }
        }
    }

    /// <summary>刷新统计快照并同时核实 B 的事务锁等待及 A 阻塞身份，不以延迟作为锁成立证据。</summary>
    private static async Task ObserveWait(DbSession session, int pidA, int pidB, Task pending, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await session.Connection.ExecuteAsync(new CommandDefinition("SELECT pg_stat_clear_snapshot()", transaction: session.Transaction, cancellationToken: token));
            if (await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity a WHERE a.pid=@PidB AND a.state='active'
                    AND a.wait_event_type='Lock' AND @PidA=ANY(pg_blocking_pids(a.pid))
                    AND EXISTS (SELECT 1 FROM pg_locks l WHERE l.pid=a.pid AND NOT l.granted AND l.locktype='transactionid'))
                """, new { PidA = pidA, PidB = pidB }, session.Transaction, cancellationToken: token))) return;
            Assert.IsFalse(pending.IsCompleted, "尚未观测到主型号锁等待，竞争读已提前结束。");
            await Task.Delay(50, token);
        }
        Assert.Fail("没有观测到预期主型号事务锁等待。");
    }

    /// <summary>随机夹具场景统一回滚，不提交、删除旧行或部署结构。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { await scenario(Context(session)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>按真实实体元数据绑定完整随机记录及审计时间，不通过数据库默认值隐藏空字段。</summary>
    private static async Task Insert(RepositoryBaseContext context, BaseEntity entity)
    {
        context.AuditStamp.StampForInsert(entity);
        var metadata = EntityMetadataCache.GetMetadata(entity.GetType());
        var columns = string.Join(",", metadata.Columns.Select(c => PostgreSqlNames.Quote(c.ColumnName)));
        var values = string.Join(",", metadata.Columns.Select(c => "@" + c.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(
            $"INSERT INTO public.{PostgreSqlNames.Quote(metadata.TableName)} ({columns}) VALUES ({values})",
            PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction));
    }

    /// <summary>随机主型号使用有效默认装率及逻辑关联身份，无需修改已有主数据。</summary>
    private static ProductMaster Master(string code, Guid category = default, bool deleted = false) => new()
    { Uid = Guid.NewGuid(), MasterSku = code, CategoryId = category, IsDeleted = deleted, Version = 1 };

    /// <summary>每条随机中性记录使用不同颜色身份，避免原主型号颜色唯一索引干扰主图排序测试。</summary>
    private static ProductNeutral Neutral(Guid masterId, string code, bool main = false, string? image = null, bool deleted = false) => new()
    { Uid = Guid.NewGuid(), ProductMasterId = masterId, ColorId = Guid.NewGuid(), ProductCode = code, IsMainImage = main, ImagePath = image, IsDeleted = deleted, Version = 1 };

    /// <summary>共享当前会话及标准审计上下文，不自行开启业务事务。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    {
        var user = new TestUser();
        return new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }

    /// <summary>短随机前缀隔离本场景编码与分页匹配。</summary>
    private static string Prefix() => "PGM" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>仅提供随机夹具审计名称，不模拟授权。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_master__";
        public bool IsAuthenticated => false;
    }
}
