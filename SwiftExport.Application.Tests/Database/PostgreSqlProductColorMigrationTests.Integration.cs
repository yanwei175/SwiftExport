using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Colors;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>颜色实库回归：随机数据全程回滚；竞争测试只读取并锁既有行，不提交或修改旧数据。</summary>
[TestClass]
public sealed class PostgreSqlProductColorMigrationIntegrationTests
{
    /// <summary>仅有中性产品引用也必须返回颜色；软删除该引用后解除阻断，所有随机写入回滚。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task NeutralReference_BlocksColorUntilSoftDeleted() => InRollback(async (context, repository) =>
    {
        var color = new ProductColor { ColorCodeUnique = Prefix(), ColorCode = Prefix() };
        var master = new ProductMaster { MasterSku = Prefix() };
        Assert.IsTrue((await repository.AddAsync(color)).Success);
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductMaster>(context).AddAsync(master)).Success);
        var neutral = new ProductNeutral { ProductMasterId = master.Uid, ColorId = color.Uid, ProductCode = Prefix() };
        var neutrals = new PostgreSqlDapperRepository<ProductNeutral>(context);
        Assert.IsTrue((await neutrals.AddAsync(neutral)).Success);
        CollectionAssert.AreEqual(new[] { color.Uid }, (await repository.GetReferencedColorIdsAsync([color.Uid])).ToArray());
        Assert.IsTrue((await neutrals.DeleteAsync(neutral.Uid, neutral.Version)).Success);
        Assert.HasCount(0, await repository.GetReferencedColorIdsAsync([color.Uid]));
    });

    /// <summary>五方法保持软删、原码、窄投影和产品引用去重。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task ColorMethods_PreserveOriginalBoundaries() => InRollback(async (context, repository) =>
    {
        var prefix = Prefix();
        var color = new ProductColor { ColorCodeUnique = prefix + "Ab ", ColorCode = prefix + "Alias", ColorNameEn = null, ColorNameZh = null };
        var deleted = new ProductColor { ColorCodeUnique = prefix + "D", ColorCode = prefix + "D" };
        Assert.IsTrue((await repository.AddAsync(color)).Success);
        Assert.IsTrue((await repository.AddAsync(deleted)).Success);
        Assert.IsTrue((await repository.DeleteAsync(deleted.Uid, deleted.Version)).Success);
        Assert.IsTrue(await repository.ExistByColorCode(prefix + "ab"));
        Assert.IsTrue(await repository.ExistByColorCode(deleted.ColorCodeUnique));
        Assert.IsFalse(await repository.ExistByColorCode(" " + prefix + "Ab"));
        Assert.IsFalse(await repository.ExistByColorCode(null!));
        CollectionAssert.AreEquivalent(new[] { color.ColorCodeUnique, deleted.ColorCodeUnique },
            (await repository.GetExistingColorCodesAsync([prefix + "AB", prefix + "AB", deleted.ColorCodeUnique])).ToArray());
        var selected = await repository.GetByColorCodesAsync([" " + prefix + "ab ", prefix + "AB", " ", deleted.ColorCodeUnique], true);
        Assert.AreEqual(color.Uid, selected.Single().Uid);
        Assert.IsNull(selected.Single().ColorNameZh);
        Assert.IsNull(selected.Single().CreatedBy, "窄投影不能被扩成完整审计投影。");
        var productRepository = new PostgreSqlDapperRepository<Product>(context);
        foreach (var active in new[] { true, false })
            Assert.IsTrue((await productRepository.AddAsync(new Product { Sku = Prefix(), ColorId = color.Uid, SupplyType = "Purchase", IsActive = active })).Success);
        var deletedProduct = new Product { Sku = Prefix(), ColorId = deleted.Uid, SupplyType = "Purchase" };
        Assert.IsTrue((await productRepository.AddAsync(deletedProduct)).Success);
        Assert.IsTrue((await productRepository.DeleteAsync(deletedProduct.Uid, deletedProduct.Version)).Success);
        CollectionAssert.AreEqual(new[] { color.Uid },
            (await repository.GetReferencedColorIdsAsync([color.Uid, color.Uid, deleted.Uid, Guid.Empty])).ToArray());
        var page = await repository.GetProductColorPagedAsync(new ProductColorSearchRequest { Keyword = prefix });
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(color.Uid, page.Items.Single().Uid);
        Assert.IsNotNull(page.Items.Single().CreatedBy);
    });

    /// <summary>关键词、业务色码批量筛选保留；请求默认二十条，无效页大小回退五十，排序及可空名称正确。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task ColorPaging_PreservesBatchKeywordAndStableOrder() => InRollback(async (_, repository) =>
    {
        var prefix = Prefix();
        var first = new ProductColor { ColorCodeUnique = prefix + "A", ColorCode = prefix + "Alias", ColorNameEn = prefix + "~%_[\\ X1Z" };
        var second = new ProductColor { ColorCodeUnique = prefix + "B", ColorCode = prefix + "Other", ColorNameZh = null };
        Assert.IsTrue((await repository.AddAsync(first)).Success);
        Assert.IsTrue((await repository.AddAsync(second)).Success);
        var page = await repository.GetProductColorPagedAsync(new ProductColorSearchRequest
        { Keyword = prefix + "~%_[\\ X?Z", BatchKeywords = [" " + first.ColorCode.ToLowerInvariant() + " ", first.ColorCode, " "] });
        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(20, page.PageSize);
        Assert.AreEqual(first.Uid, page.Items.Single().Uid);
        Assert.IsNull(page.Items.Single().ColorNameZh);
        var fallback = await repository.GetProductColorPagedAsync(new ProductColorSearchRequest { Keyword = prefix, PageSize = 0 });
        Assert.AreEqual(50, fallback.PageSize);
        var one = await repository.GetProductColorPagedAsync(new ProductColorSearchRequest { Keyword = prefix, PageSize = 1 });
        var two = await repository.GetProductColorPagedAsync(new ProductColorSearchRequest { Keyword = prefix, PageSize = 1, PageIndex = 2 });
        Assert.AreEqual(2, one.TotalCount);
        Assert.AreEqual(first.Uid, one.Items.Single().Uid);
        Assert.AreEqual(second.Uid, two.Items.Single().Uid);
    });

    /// <summary>继承新增、更新及软删保留乐观版本和可空字段，旧版本拒绝写入。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    public Task ColorCrud_PreservesVersionAndNulls() => InRollback(async (_, repository) =>
    {
        var color = new ProductColor { ColorCodeUnique = Prefix(), ColorCode = Prefix(), ColorNameZh = "名称" };
        Assert.AreEqual(1, (await repository.AddAsync(color)).NewVersion);
        color.ColorNameZh = null;
        Assert.AreEqual(2, (await repository.UpdateAsync(color)).NewVersion);
        var saved = await repository.GetByIdAsync(color.Uid);
        Assert.AreEqual(2, saved!.Version);
        Assert.IsNull(saved.ColorNameZh);
        color.Version = 1;
        Assert.IsFalse((await repository.UpdateAsync(color)).Success);
        Assert.IsFalse((await repository.DeleteAsync(color.Uid, 1)).Success);
        Assert.IsTrue((await repository.DeleteAsync(color.Uid, 2)).Success);
        Assert.IsNull(await repository.GetByIdAsync(color.Uid));
        Assert.IsTrue(await repository.ExistByColorCode(color.ColorCodeUnique));
    });

    /// <summary>只锁既有可见颜色行：普通读不阻止另一连接取行锁；显式锁读由服务器证明等待，回滚后完成。不证明随机未提交行 UPDATE。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlClassificationIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ColorReads_ObserveRealLockWaitWithoutChangingExistingRows(bool locking)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var owner = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var waiter = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        var target = await observer.Connection.QueryFirstOrDefaultAsync<ProductColor>(
            "SELECT uid AS \"Uid\",color_code_unique AS \"ColorCodeUnique\" FROM public.product_color WHERE is_deleted=FALSE ORDER BY uid LIMIT 1");
        if (target is null) Assert.Inconclusive("专用库没有已提交颜色行；禁止提交夹具，锁等待未验证。");
        using var ownerTransaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var waiterTransaction = waiter.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        owner.Transaction = ownerTransaction;
        waiter.Transaction = waiterTransaction;
        Task<IReadOnlyList<ProductColor>>? pending = null;
        try
        {
            var ownerRepository = new PostgreSqlProductColorRepository(Context(owner));
            Assert.IsTrue((await ownerRepository.GetByColorCodesAsync([target!.ColorCodeUnique], locking)).Any(x => x.Uid == target.Uid));
            var ownerPid = await owner.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: ownerTransaction);
            var waiterPid = await waiter.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: waiterTransaction);
            pending = new PostgreSqlProductColorRepository(Context(waiter)).GetByColorCodesAsync([target.ColorCodeUnique], true);
            if (locking)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT @OwnerPid=ANY(pg_blocking_pids(@WaiterPid)) AND EXISTS (SELECT 1 FROM pg_locks WHERE pid=@WaiterPid AND NOT granted)",
                    new { OwnerPid = ownerPid, WaiterPid = waiterPid }, cancellationToken: deadline.Token)))
                {
                    Assert.IsFalse(pending.IsCompleted, "尚未观测到锁等待，竞争命令已提前结束。");
                    await Task.Delay(50, deadline.Token);
                }
                Assert.IsFalse(pending.IsCompleted);
                ownerTransaction.Rollback();
                owner.Transaction = null;
            }
            Assert.IsTrue((await pending.WaitAsync(TimeSpan.FromSeconds(15))).Any(x => x.Uid == target.Uid));
        }
        finally
        {
            // 先释放持锁方，再等待接收方结束，避免测试断言失败遗留等待中的驱动操作。
            if (owner.Transaction is not null) { ownerTransaction.Rollback(); owner.Transaction = null; }
            try { if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(35)); }
            finally { waiterTransaction.Rollback(); waiter.Transaction = null; }
        }
    }

    /// <summary>仅专用库随机颜色及产品操作，最终回滚当前事务，不提交或删除旧记录。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlProductColorRepository, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { var context = Context(session); await scenario(context, new PostgreSqlProductColorRepository(context)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>共享当前会话与审计打戳，不引入连接或事务拥有者。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    {
        var user = new TestUser();
        return new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }

    /// <summary>随机前缀隔离本次夹具与已有业务编码。</summary>
    private static string Prefix() => "PGC" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>只提供测试审计名称，不模拟已有业务用户。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_color__";
        public bool IsAuthenticated => false;
    }
}
