using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>专用故障库中性产品管理实库回归；随机夹具全程回滚，只读既有行的竞争另行验证。</summary>
[TestClass]
public sealed class PostgreSqlProductNeutralManagerMigrationIntegrationTests
{
    /// <summary>跨主型号筛选保留有效父型号、实时左联接名称、颜色与分类组合及编码占用独立范围。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductNeutralManagerIntegration")]
    public Task NeutralManagerPaging_PreservesParentFiltersAndNameFallbacks() => InRollback(async (context, repository, prefix) =>
    {
        var masters = new PostgreSqlDapperRepository<ProductMaster>(context);
        var colors = new PostgreSqlDapperRepository<ProductColor>(context);
        var categories = new PostgreSqlDapperRepository<ProductCategory>(context);
        var category = new ProductCategory { CategoryCode = prefix, CategoryName = "分类" };
        Assert.IsTrue((await categories.AddAsync(category)).Success);
        var categorized = new ProductMaster { MasterSku = prefix + "M1", CategoryId = category.Uid, DefaultDescriptionZh = prefix + "~%_[\\ X1Z" };
        var uncategorized = new ProductMaster { MasterSku = prefix + "M2", CategoryId = Guid.Empty, DefaultDescriptionEn = prefix + "English" };
        var deletedMaster = new ProductMaster { MasterSku = prefix + "M3" };
        foreach (var master in new[] { categorized, uncategorized, deletedMaster })
            Assert.IsTrue((await masters.AddAsync(master)).Success);
        Assert.IsTrue((await masters.DeleteAsync(deletedMaster.Uid, deletedMaster.Version)).Success);
        var variants = new[]
        {
            new ProductColor { ColorCodeUnique = prefix + "C1", ColorCode = prefix + "Alias1", ColorNameZh = "中文", ColorNameEn = "English" },
            new ProductColor { ColorCodeUnique = prefix + "C2", ColorCode = prefix + "Alias2", ColorNameZh = "", ColorNameEn = "English" },
            new ProductColor { ColorCodeUnique = prefix + "C3", ColorCode = prefix + "Alias3", ColorNameZh = "   ", ColorNameEn = null },
            new ProductColor { ColorCodeUnique = prefix + "C4", ColorCode = prefix + "Alias4", ColorNameZh = null, ColorNameEn = "" },
            new ProductColor { ColorCodeUnique = prefix + "C5", ColorCode = prefix + "Alias5", ColorNameZh = "\u200B", ColorNameEn = "English" }
        };
        var expectedNames = new[] { "中文", "English", variants[2].ColorCode, "", "\u200B" };
        var items = new List<ProductNeutral>();
        for (var i = 0; i < variants.Length; i++)
        {
            Assert.IsTrue((await colors.AddAsync(variants[i])).Success);
            var item = new ProductNeutral { ProductCode = prefix + i, ProductMasterId = i == 0 ? categorized.Uid : uncategorized.Uid, ColorId = variants[i].Uid, IsMainImage = i == 0 };
            Assert.IsTrue((await repository.AddAsync(item)).Success);
            items.Add(item);
        }
        var deleted = new ProductNeutral { ProductCode = prefix + "Deleted", ProductMasterId = categorized.Uid, ColorId = variants[1].Uid };
        var hiddenParent = new ProductNeutral { ProductCode = prefix + "Hidden", ProductMasterId = deletedMaster.Uid, ColorId = variants[0].Uid };
        var missingParent = new ProductNeutral { ProductCode = prefix + "Missing", ProductMasterId = Guid.NewGuid(), ColorId = variants[0].Uid };
        foreach (var item in new[] { deleted, hiddenParent, missingParent })
            Assert.IsTrue((await repository.AddAsync(item)).Success);
        Assert.IsTrue((await repository.DeleteAsync(deleted.Uid, deleted.Version)).Success);
        var page = await repository.GetPagedAsync(new() { Keyword = prefix, PageSize = 5001 });
        Assert.AreEqual(5, page.TotalCount);
        Assert.AreEqual(5001, page.PageSize);
        CollectionAssert.AreEqual(items.Select(x => x.Uid).ToArray(), page.Items.Select(x => x.Uid).ToArray());
        CollectionAssert.AreEqual(expectedNames, page.Items.Select(x => x.ColorName).ToArray());
        Assert.AreEqual("分类", page.Items[0].CategoryName);
        Assert.AreEqual(categorized.MasterSku, page.Items[0].MasterSku);
        Assert.AreEqual(categorized.DefaultDescriptionZh, page.Items[0].ProductMasterDescriptionZh);
        Assert.AreEqual(categorized.Uid, page.Items[0].Data.ProductMasterId);
        Assert.IsNull(page.Items[0].Data.DefaultSalesPriceUsd);
        Assert.IsNotNull(page.Items[0].CreatedBy);
        var onlyCategory = await repository.GetPagedAsync(new() { Keyword = prefix, CategoryIds = [category.Uid, category.Uid, Guid.Empty] });
        Assert.AreEqual(items[0].Uid, onlyCategory.Items.Single().Uid);
        Assert.AreEqual(4, (await repository.GetPagedAsync(new() { Keyword = prefix, IncludeUncategorized = true })).TotalCount);
        var combined = await repository.GetPagedAsync(new()
        { Keyword = prefix, IncludeUncategorized = true, CategoryIds = [category.Uid], ColorIds = [variants[0].Uid, variants[1].Uid, Guid.Empty, variants[1].Uid] });
        Assert.AreEqual(2, combined.TotalCount);
        var literal = await repository.GetPagedAsync(new() { Keyword = prefix + "~%_[\\ X?Z" });
        Assert.AreEqual(items[0].Uid, literal.Items.Single().Uid);
        Assert.AreEqual(4, (await repository.GetPagedAsync(new() { Keyword = prefix + "english" })).TotalCount);
        var secondPage = await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 2, PageSize = 1 });
        Assert.AreEqual(5, secondPage.TotalCount);
        Assert.AreEqual(items[1].Uid, secondPage.Items.Single().Uid);
        Assert.AreEqual(50, (await repository.GetPagedAsync(new() { Keyword = prefix, PageIndex = 0, PageSize = 0 })).PageSize);
        var occupied = await repository.GetByProductCodesAsync([" " + hiddenParent.ProductCode.ToLowerInvariant() + " ", hiddenParent.ProductCode, missingParent.ProductCode, deleted.ProductCode], true);
        CollectionAssert.AreEquivalent(new[] { hiddenParent.Uid, missingParent.Uid }, occupied.Select(x => x.Uid).ToArray());
        // 分类和颜色失效只影响名称，未删除中性产品仍能显示和参与对应身份筛选。
        Assert.IsTrue((await categories.DeleteAsync(category.Uid, category.Version)).Success);
        Assert.IsTrue((await colors.DeleteAsync(variants[0].Uid, variants[0].Version)).Success);
        var noNames = await repository.GetPagedAsync(new() { Keyword = prefix, CategoryIds = [category.Uid], ColorIds = [variants[0].Uid] });
        Assert.AreEqual(items[0].Uid, noNames.Items.Single().Uid);
        Assert.IsNull(noNames.Items.Single().CategoryName);
        Assert.IsNull(noNames.Items.Single().ColorName);
    });

    /// <summary>继承 CRUD 保留版本拒绝、主图及价格 NULL，编码查询忽略大小写与尾部 ASCII 空格但不移除前导空格。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductNeutralManagerIntegration")]
    public Task NeutralManagerCrud_PreservesVersionAndOriginalCode() => InRollback(async (context, repository, prefix) =>
    {
        var master = new ProductMaster { MasterSku = prefix + "M" };
        var color = new ProductColor { ColorCodeUnique = prefix + "C", ColorCode = prefix + "C" };
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductMaster>(context).AddAsync(master)).Success);
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductColor>(context).AddAsync(color)).Success);
        var item = new ProductNeutral { ProductCode = prefix + "Ab ", ProductMasterId = master.Uid, ColorId = color.Uid, IsMainImage = true, DefaultSalesPriceUsd = 12.5m };
        Assert.AreEqual(1, (await repository.AddAsync(item)).NewVersion);
        var owners = await repository.GetByProductCodesAsync([prefix + "ab", " " + prefix + "AB ", " "], true);
        Assert.AreEqual(item.Uid, owners.Single().Uid);
        Assert.AreEqual(item.ProductCode, owners.Single().ProductCode);
        Assert.IsTrue(owners.Single().IsMainImage);
        // 输入集合会 Trim，因此前导空格边界用真实存储值验证。
        var otherColor = new ProductColor { ColorCodeUnique = prefix + "C2", ColorCode = prefix + "C2" };
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductColor>(context).AddAsync(otherColor)).Success);
        var leading = new ProductNeutral { ProductCode = " " + prefix + "Leading", ProductMasterId = master.Uid, ColorId = otherColor.Uid };
        Assert.IsTrue((await repository.AddAsync(leading)).Success);
        Assert.HasCount(0, await repository.GetByProductCodesAsync([leading.ProductCode]));
        item.DefaultSalesPriceUsd = null;
        item.ImagePath = null;
        Assert.AreEqual(2, (await repository.UpdateAsync(item)).NewVersion);
        var saved = await repository.GetByIdAsync(item.Uid);
        Assert.IsNull(saved!.DefaultSalesPriceUsd);
        Assert.IsNull(saved.ImagePath);
        Assert.IsTrue(saved.IsMainImage);
        item.Version = 1;
        Assert.IsFalse((await repository.UpdateAsync(item)).Success);
        Assert.IsFalse((await repository.DeleteAsync(item.Uid, 1)).Success);
        Assert.IsTrue((await repository.DeleteAsync(item.Uid, 2)).Success);
        Assert.IsNull(await repository.GetByIdAsync(item.Uid));
        Assert.HasCount(0, await repository.GetByProductCodesAsync([item.ProductCode], true));
    });

    /// <summary>对专用库既有已提交行证明普通读不阻塞行锁、显式读产生服务器锁等待；没有既有行时明确标记未验证。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductNeutralManagerIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NeutralManagerReads_ObserveExistingRowLockWait(bool locking)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var owner = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var waiter = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        var target = await observer.Connection.QueryFirstOrDefaultAsync<ProductNeutral>(
            "SELECT uid AS \"Uid\", product_code AS \"ProductCode\" FROM public.product_neutral WHERE is_deleted=FALSE AND product_code IS NOT NULL AND product_code<>'' ORDER BY uid LIMIT 1");
        if (target is null || string.IsNullOrWhiteSpace(target.ProductCode) || target.ProductCode != target.ProductCode.Trim())
            Assert.Inconclusive("没有可通过规范化编码读取的既有中性产品；禁止提交随机夹具，双连接竞争未验证。");
        using var ownerTransaction = owner.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var waiterTransaction = waiter.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        owner.Transaction = ownerTransaction;
        waiter.Transaction = waiterTransaction;
        Task<IReadOnlyList<ProductNeutral>>? pending = null;
        try
        {
            Assert.IsTrue((await new PostgreSqlProductNeutralManagerRepository(Context(owner)).GetByProductCodesAsync([target!.ProductCode], locking)).Any(x => x.Uid == target.Uid));
            var ownerPid = await owner.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: ownerTransaction);
            var waiterPid = await waiter.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: waiterTransaction);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            pending = new PostgreSqlProductNeutralManagerRepository(Context(waiter)).GetByProductCodesAsync([target.ProductCode], true, deadline.Token);
            if (locking)
            {
                while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT @OwnerPid=ANY(pg_blocking_pids(@WaiterPid)) AND EXISTS (SELECT 1 FROM pg_locks WHERE pid=@WaiterPid AND NOT granted)",
                    new { OwnerPid = ownerPid, WaiterPid = waiterPid }, cancellationToken: deadline.Token)))
                {
                    Assert.IsFalse(pending.IsCompleted, "尚未观测到锁等待，竞争命令提前结束。");
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
            if (owner.Transaction is not null) { ownerTransaction.Rollback(); owner.Transaction = null; }
            try { if (pending is not null) { try { await pending.WaitAsync(TimeSpan.FromSeconds(35)); } catch (OperationCanceledException) { } } }
            finally { waiterTransaction.Rollback(); waiter.Transaction = null; }
        }
    }

    /// <summary>固定专用库，随机新增数据只能在当前事务修改；回滚后独立连接确认四种夹具均未留存。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlProductNeutralManagerRepository, string, Task> scenario)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        var prefix = "PGNM" + Guid.NewGuid().ToString("N")[..12];
        using var session = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try { var context = Context(session); await scenario(context, new PostgreSqlProductNeutralManagerRepository(context), prefix); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        var remaining = await observer.Connection.ExecuteScalarAsync<int>("""
            SELECT (SELECT COUNT(*) FROM public.product_neutral WHERE starts_with(ltrim(product_code, ' '), @Prefix))
                 + (SELECT COUNT(*) FROM public.product_master WHERE starts_with(master_sku, @Prefix))
                 + (SELECT COUNT(*) FROM public.product_color WHERE starts_with(color_code_unique, @Prefix))
                 + (SELECT COUNT(*) FROM public.product_category WHERE starts_with(category_code, @Prefix))
            """, new { Prefix = prefix });
        Assert.AreEqual(0, remaining, "随机夹具必须全部回滚，不得提交或清理旧数据。");
    }

    /// <summary>会话及审计身份共享，不接管仓储事务。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    {
        var user = new TestUser();
        return new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
    }

    /// <summary>仅提供随机夹具的审计名称。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_neutral_manager__";
        public bool IsAuthenticated => false;
    }
}
