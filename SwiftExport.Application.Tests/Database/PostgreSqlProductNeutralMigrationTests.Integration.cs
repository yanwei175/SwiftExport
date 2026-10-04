using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.ProductNeutrals;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>专用库中性产品测试：随机夹具全回滚；双连接竞争只读锁既有行，不提交或修改旧数据。</summary>
[TestClass]
public sealed class PostgreSqlProductNeutralMigrationIntegrationTests
{
    /// <summary>颜色中文空串或空格回退英文，英文空串保留，软删颜色显示空；有效明细按编码排序且可空价格保留。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductNeutralIntegration")]
    public Task Neutral_DetailsPreserveColorFallbackAndNullableValues() => InRollback(async context =>
    {
        var repository = new PostgreSqlProductNeutralRepository(context);
        var master = await AddMaster(context);
        var colors = new[] {
            new ProductColor { ColorCodeUnique = Prefix(), ColorCode = "代码1", ColorNameZh = "中文", ColorNameEn = "English" },
            new ProductColor { ColorCodeUnique = Prefix(), ColorCode = "代码2", ColorNameZh = "   ", ColorNameEn = "English" },
            new ProductColor { ColorCodeUnique = Prefix(), ColorCode = "代码3", ColorNameZh = "", ColorNameEn = "" },
            new ProductColor { ColorCodeUnique = Prefix(), ColorCode = "代码4", ColorNameZh = null, ColorNameEn = null },
            new ProductColor { ColorCodeUnique = Prefix(), ColorCode = "代码5", ColorNameZh = "已删" },
            new ProductColor { ColorCodeUnique = Prefix(), ColorCode = "代码6", ColorNameZh = "\u200B", ColorNameEn = "不能回退" } };
        var colorRepository = new PostgreSqlDapperRepository<ProductColor>(context);
        var prefix = Prefix();
        var rows = new List<ProductNeutral>();
        for (var i = 0; i < colors.Length; i++)
        {
            Assert.IsTrue((await colorRepository.AddAsync(colors[i])).Success);
            var neutral = NewNeutral(master.Uid, colors[i].Uid, prefix + i);
            neutral.IsMainImage = i == 0;
            Assert.IsTrue((await repository.AddAsync(neutral)).Success);
            rows.Add(neutral);
        }
        Assert.IsTrue((await colorRepository.DeleteAsync(colors[4].Uid, colors[4].Version)).Success);
        var details = await repository.GetByProductMasterIdAsync(master.Uid, true);
        CollectionAssert.AreEqual(rows.Select(x => x.Uid).ToArray(), details.Select(x => x.Uid).ToArray());
        CollectionAssert.AreEqual(new string?[] { "中文", "English", "", "代码4", null, "\u200B" }, details.Select(x => x.ColorName).ToArray());
        Assert.IsTrue(details[0].Data.IsMainImage);
        Assert.IsTrue(details.All(x => x.Data.DefaultSalesPriceUsd is null && x.Data.ImagePath is null));
        Assert.AreEqual(rows[0].Uid, (await repository.GetByProductCodesAsync([" " + rows[0].ProductCode.ToLowerInvariant() + " ", rows[0].ProductCode], true)).Single().Uid);
        Assert.HasCount(0, await repository.GetByProductCodesAsync([]));
        Assert.HasCount(0, await repository.GetByProductMasterIdAsync(Guid.NewGuid()));
    });

    /// <summary>级联只修改指定父型号的有效行，递增版本及审计；已有软删与其他父型号不变，重复零行仍成功。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductNeutralIntegration")]
    public Task Neutral_CascadePreservesVersionsAuditAndZeroRowSuccess() => InRollback(async context =>
    {
        var repository = new PostgreSqlProductNeutralRepository(context);
        var master = await AddMaster(context);
        var otherMaster = await AddMaster(context);
        var firstColor = await AddColor(context);
        var secondColor = await AddColor(context);
        var live = NewNeutral(master.Uid, firstColor.Uid, Prefix());
        var deleted = NewNeutral(master.Uid, secondColor.Uid, Prefix());
        var other = NewNeutral(otherMaster.Uid, firstColor.Uid, Prefix());
        foreach (var row in new[] { live, deleted, other }) Assert.IsTrue((await repository.AddAsync(row)).Success);
        Assert.IsTrue((await repository.DeleteAsync(deleted.Uid, 1)).Success);
        var deletedBefore = await context.Session.Connection.ExecuteScalarAsync<string>(
            "SELECT to_jsonb(pn)::text FROM public.product_neutral pn WHERE uid=@Uid", new { deleted.Uid }, context.Session.Transaction);
        Assert.IsTrue((await repository.SoftDeleteByProductMasterIdAsync(master.Uid)).Success);
        var facts = (await context.Session.Connection.QueryAsync<ProductNeutral>(
            "SELECT uid AS \"Uid\",version AS \"Version\",is_deleted AS \"IsDeleted\",updated_by AS \"UpdatedBy\",updated_date AS \"UpdatedDate\" FROM public.product_neutral WHERE product_master_id=@Master",
            new { Master = master.Uid }, context.Session.Transaction)).ToList();
        Assert.AreEqual(2, facts.Single(x => x.Uid == live.Uid).Version);
        Assert.AreEqual(1, facts.Single(x => x.Uid == deleted.Uid).Version, "通用软删沿用源行为不递增版本，级联不得再次修改已删行。");
        Assert.AreEqual(deletedBefore, await context.Session.Connection.ExecuteScalarAsync<string>(
            "SELECT to_jsonb(pn)::text FROM public.product_neutral pn WHERE uid=@Uid", new { deleted.Uid }, context.Session.Transaction));
        Assert.AreEqual("__pgtest_neutral__", facts.Single(x => x.Uid == live.Uid).UpdatedBy);
        Assert.IsNotNull(facts.Single(x => x.Uid == live.Uid).UpdatedDate);
        Assert.IsTrue(facts.All(x => x.IsDeleted));
        Assert.AreEqual(1, (await repository.GetByIdAsync(other.Uid))!.Version);
        Assert.HasCount(0, await repository.GetByProductMasterIdAsync(master.Uid));
        Assert.HasCount(0, await repository.GetByProductCodesAsync([live.ProductCode, deleted.ProductCode]));
        Assert.IsTrue((await repository.SoftDeleteByProductMasterIdAsync(master.Uid)).Success);
        Assert.IsTrue((await repository.SoftDeleteByProductMasterIdAsync(Guid.NewGuid())).Success);
        Assert.AreEqual(2, await context.Session.Connection.ExecuteScalarAsync<int>(
            "SELECT version FROM public.product_neutral WHERE uid=@Uid", new { live.Uid }, context.Session.Transaction));
    });

    /// <summary>普通更新保留并发版本，可空字段清除；取消不影响事务内已存在行。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductNeutralIntegration")]
    public Task Neutral_CrudAndCancellationPreserveTransaction() => InRollback(async context =>
    {
        var repository = new PostgreSqlProductNeutralRepository(context);
        var master = await AddMaster(context);
        var color = await AddColor(context);
        var row = NewNeutral(master.Uid, color.Uid, Prefix()); row.DefaultSalesPriceUsd = 3m;
        Assert.AreEqual(1, (await repository.AddAsync(row)).NewVersion);
        row.DefaultSalesPriceUsd = null; row.IsMainImage = true;
        Assert.AreEqual(2, (await repository.UpdateAsync(row)).NewVersion);
        Assert.IsNull((await repository.GetByIdAsync(row.Uid))!.DefaultSalesPriceUsd);
        row.Version = 1;
        Assert.IsFalse((await repository.UpdateAsync(row)).Success);
        var canceled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByProductMasterIdAsync(master.Uid, cancellationToken: canceled));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.GetByProductCodesAsync([row.ProductCode], cancellationToken: canceled));
        await Assert.ThrowsAsync<OperationCanceledException>(() => repository.SoftDeleteByProductMasterIdAsync(master.Uid, canceled));
        Assert.AreEqual(2, (await repository.GetByIdAsync(row.Uid))!.Version);
        Assert.IsFalse((await repository.DeleteAsync(row.Uid, 1)).Success);
        Assert.IsTrue((await repository.DeleteAsync(row.Uid, 2)).Success);
    });

    /// <summary>既有行只读竞争：普通明细不阻止行锁，显式锁仅 pn 产生真实等待，持锁方回滚后释放。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlProductNeutralIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Neutral_DetailLockObservesRealWaitingWithoutOldDataWrites(bool locking)
    {
        var connectionString = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
        using var owner = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var waiter = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        using var observer = new DbSession(new PostgreSqlConnectionFactory(connectionString));
        var target = await observer.Connection.QueryFirstOrDefaultAsync<ProductNeutral>(
            "SELECT uid AS \"Uid\",product_master_id AS \"ProductMasterId\" FROM public.product_neutral WHERE is_deleted=FALSE ORDER BY uid LIMIT 1");
        if (target is null) Assert.Inconclusive("专用库没有已提交有效中性行；不提交随机夹具，竞争未验证。");
        using var ownerTransaction = owner.Connection.BeginTransaction();
        using var waiterTransaction = waiter.Connection.BeginTransaction();
        owner.Transaction = ownerTransaction; waiter.Transaction = waiterTransaction;
        Task<IReadOnlyList<SwiftExport.Contracts.DTOs.Products.ProductMasters.ProductNeutralDto>>? pending = null;
        try
        {
            Assert.IsTrue((await new PostgreSqlProductNeutralRepository(Context(owner)).GetByProductMasterIdAsync(target!.ProductMasterId, locking)).Any(x => x.Uid == target.Uid));
            var holding = await owner.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: ownerTransaction);
            var waiting = await waiter.Connection.ExecuteScalarAsync<int>("SELECT pg_backend_pid()", transaction: waiterTransaction);
            pending = new PostgreSqlProductNeutralRepository(Context(waiter)).GetByProductMasterIdAsync(target.ProductMasterId, true);
            if (locking)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!await observer.Connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "SELECT @Holding=ANY(pg_blocking_pids(@Waiting)) AND EXISTS(SELECT 1 FROM pg_locks WHERE pid=@Waiting AND NOT granted)",
                    new { Holding = holding, Waiting = waiting }, cancellationToken: deadline.Token))) deadline.Token.ThrowIfCancellationRequested();
                Assert.IsFalse(pending.IsCompleted);
                ownerTransaction.Rollback(); owner.Transaction = null;
            }
            Assert.IsTrue((await pending.WaitAsync(TimeSpan.FromSeconds(15))).Any(x => x.Uid == target.Uid));
        }
        finally
        {
            if (owner.Transaction is not null) { ownerTransaction.Rollback(); owner.Transaction = null; }
            try { if (pending is not null) await pending.WaitAsync(TimeSpan.FromSeconds(35)); }
            finally { waiterTransaction.Rollback(); waiter.Transaction = null; }
        }
    }

    /// <summary>所有随机夹具在已批准专用库的当前事务中创建并回滚，不提交或清理旧行。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> action)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted); session.Transaction = transaction;
        try { await action(Context(session)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>共享当前会话及审计服务。</summary>
    private static RepositoryBaseContext Context(DbSession session)
    { var user = new TestUser(); return new(session, NullLoggerFactory.Instance, new AuditStampService(user), user); }

    /// <summary>仅创建带合法必填装率的随机父型号；空分类及供应商身份不伪装成业务维护调用。</summary>
    private static async Task<ProductMaster> AddMaster(RepositoryBaseContext context)
    {
        var master = new ProductMaster { MasterSku = Prefix(), DefaultPcsPerPackage = 1 };
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductMaster>(context).AddAsync(master)).Success); return master;
    }

    /// <summary>创建供当前随机明细引用的颜色。</summary>
    private static async Task<ProductColor> AddColor(RepositoryBaseContext context)
    {
        var color = new ProductColor { ColorCodeUnique = Prefix(), ColorCode = Prefix() };
        Assert.IsTrue((await new PostgreSqlDapperRepository<ProductColor>(context).AddAsync(color)).Success); return color;
    }

    /// <summary>构造当前父型号及颜色下的临时可空明细。</summary>
    private static ProductNeutral NewNeutral(Guid master, Guid color, string code) => new()
    { ProductMasterId = master, ColorId = color, ProductCode = code };

    /// <summary>生成短随机编码防止影响既有记录。</summary>
    private static string Prefix() => "PGN" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>为临时记录提供审计名称。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_neutral__";
        public bool IsAuthenticated => false;
    }
}
