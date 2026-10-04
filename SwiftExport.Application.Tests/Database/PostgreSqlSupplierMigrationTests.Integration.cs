using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Suppliers.Management;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>供应商实库回归；仅专用库事务内新增随机记录并回滚，不证明编码 CI 唯一索引或并发保护已完成。</summary>
[TestClass]
public sealed class PostgreSqlSupplierMigrationIntegrationTests
{
    /// <summary>六个专用入口保留停用及软删差异，映射完整实体的可空字段及原 DTO 投影。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task SupplierMethods_PreserveDeletedInactiveAndNullable() => InRollback(async (context, repository) =>
    {
        var prefix = Prefix();
        var active = NewSupplier(prefix + "A");
        var inactive = NewSupplier(prefix + "I", active: false);
        var deleted = NewSupplier(prefix + "D");
        foreach (var supplier in new[] { active, inactive, deleted }) Assert.IsTrue((await repository.AddAsync(supplier)).Success);
        Assert.IsTrue((await repository.DeleteAsync(deleted.Uid, deleted.Version)).Success);
        Assert.IsTrue(await repository.ExistBySupplierCode(deleted.SupplierCode.ToLowerInvariant()));
        Assert.IsFalse(await repository.ExistBySupplierCode(prefix + "missing"));
        Assert.IsFalse(await repository.ExistBySupplierCode(null!));
        Assert.IsNull(await repository.GetByCodeAsync(deleted.SupplierCode));
        Assert.IsNull(await repository.GetByCodeAsync(" "));
        var detail = await repository.GetByCodeAsync(" " + inactive.SupplierCode.ToLowerInvariant() + " ");
        Assert.AreEqual(inactive.Uid, detail!.Uid);
        Assert.IsFalse(detail.IsActive);
        Assert.IsNull(detail.TaxRate);
        Assert.IsNull(detail.ContactEmail);
        Assert.IsNull(detail.DefaultLeadTime);
        var input = new[] { " " + active.SupplierCode + " ", active.SupplierCode.ToLowerInvariant(), inactive.SupplierCode, deleted.SupplierCode, " " };
        CollectionAssert.AreEquivalent(new[] { active.Uid, inactive.Uid },
            (await repository.GetBySupplierCodeListAsync(input)).Select(x => x.Uid).ToArray());
        Assert.HasCount(0, await repository.GetBySupplierCodeListAsync(null!));
        CollectionAssert.AreEquivalent(new[] { active.SupplierCode, deleted.SupplierCode },
            (await repository.GetExistingSupplierCodesAsync([active.SupplierCode.ToLowerInvariant(), deleted.SupplierCode])).ToArray());
        CollectionAssert.AreEquivalent(new[] { deleted.SupplierCode },
            (await repository.GetSupplierCodesUsedByOtherSuppliersAsync([(active.Uid, active.SupplierCode), (Guid.NewGuid(), deleted.SupplierCode)])).ToArray());
        var page = await repository.GetPagedAsync(new SupplierSearchRequest { Keyword = prefix, PageIndex = 0, PageSize = 999 });
        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(1, page.PageIndex);
        Assert.AreEqual(200, page.PageSize);
        CollectionAssert.AreEquivalent(new[] { active.Uid, inactive.Uid }, page.Items.Select(x => x.Uid).ToArray());
        Assert.IsTrue(page.Items.All(x => x.ZheKou is null && x.TaxRate is null && x.DefaultLeadTime is null));
        Assert.IsNotNull(context.Session.Transaction);
    });

    /// <summary>比较忽略大小写和尾部空格，去重返回原码；测试数据含软删重复，不验证当前唯一索引的 CI 能力。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task SupplierCodes_CompareAndDeduplicateWithoutChangingStoredText() => InRollback(async (context, repository) =>
    {
        var prefix = Prefix();
        var original = NewSupplier(prefix + "Ab ");
        var duplicate = NewSupplier(prefix + "Deleted");
        Assert.IsTrue((await repository.AddAsync(original)).Success);
        Assert.IsTrue((await repository.AddAsync(duplicate)).Success);
        Assert.IsTrue((await repository.DeleteAsync(duplicate.Uid, duplicate.Version)).Success);
        // 先软删再构造重复文本，避免夹具依赖当前活动唯一索引是否已升级 CI。
        duplicate.SupplierCode = prefix.ToLowerInvariant() + "aB";
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(
            "UPDATE public.supplier SET supplier_code=@Code WHERE uid=@Uid AND is_deleted=TRUE",
            new { Code = duplicate.SupplierCode, duplicate.Uid }, context.Session.Transaction));
        Assert.IsTrue(await repository.ExistBySupplierCode(prefix + "AB"));
        Assert.IsFalse(await repository.ExistBySupplierCode(" " + prefix + "AB"));
        Assert.AreEqual(original.SupplierCode, (await repository.GetByCodeAsync(prefix + "ab"))!.SupplierCode);
        Assert.HasCount(2, await repository.GetExistingSupplierCodesAsync([prefix + "AB"]));
        var used = await repository.GetSupplierCodesUsedByOtherSuppliersAsync([(Guid.NewGuid(), prefix + "AB")]);
        Assert.HasCount(1, used);
        Assert.IsTrue(new[] { original.SupplierCode, duplicate.SupplierCode }.Contains(used.Single()));
        Assert.AreEqual(original.Uid, (await repository.GetBySupplierCodeListAsync([prefix + "AB", " " + prefix + "ab " ])).Single().Uid);
        Assert.IsNotNull(context.Session.Transaction);
    });

    /// <summary>通配规则保留问号和空格转换、其他特殊字符按字面匹配，分页计数与成员一致。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task SupplierPage_PreservesWildcardAndStablePaging() => InRollback(async (_, repository) =>
    {
        var prefix = Prefix();
        var literal = NewSupplier(prefix + "A", name: prefix + "~%_[\\* X1Z");
        var other = NewSupplier(prefix + "B", name: prefix + "other");
        Assert.IsTrue((await repository.AddAsync(literal)).Success);
        Assert.IsTrue((await repository.AddAsync(other)).Success);
        var wildcard = await repository.GetPagedAsync(new SupplierSearchRequest { Keyword = prefix + "~%_[\\* X?Z" });
        Assert.AreEqual(1, wildcard.TotalCount);
        Assert.AreEqual(literal.Uid, wildcard.Items.Single().Uid);
        var first = await repository.GetPagedAsync(new SupplierSearchRequest { Keyword = prefix, PageSize = 1 });
        var second = await repository.GetPagedAsync(new SupplierSearchRequest { Keyword = prefix, PageSize = 1, PageIndex = 2 });
        Assert.AreEqual(2, first.TotalCount);
        Assert.AreEqual(literal.Uid, first.Items.Single().Uid);
        Assert.AreEqual(other.Uid, second.Items.Single().Uid);
    });

    /// <summary>继承 CRUD 保持乐观版本及软删，普通更新请求的启停值不得覆盖数据库状态。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPartyIntegration")]
    public Task SupplierCrud_UpdatesVersionWithoutChangingLifecycle() => InRollback(async (_, repository) =>
    {
        var supplier = NewSupplier(Prefix(), active: false);
        var added = await repository.AddAsync(supplier);
        Assert.IsTrue(added.Success);
        Assert.AreEqual(1, added.NewVersion);
        supplier.IsActive = true;
        supplier.SupplierName = "普通资料更新";
        supplier.ZheKou = 0.85m;
        var updated = await repository.UpdateAsync(supplier);
        Assert.IsTrue(updated.Success);
        Assert.AreEqual(2, updated.NewVersion);
        var persisted = await repository.GetByIdAsync(supplier.Uid, true);
        Assert.IsFalse(persisted!.IsActive);
        Assert.AreEqual(2, persisted.Version);
        Assert.AreEqual(0.85m, persisted.ZheKou);
        supplier.Version = 1;
        Assert.IsFalse((await repository.UpdateAsync(supplier)).Success);
        Assert.IsFalse((await repository.DeleteAsync(supplier.Uid, 1)).Success);
        Assert.IsTrue((await repository.DeleteAsync(supplier.Uid, 2)).Success);
        Assert.IsNull(await repository.GetByIdAsync(supplier.Uid));
        Assert.IsTrue(await repository.ExistBySupplierCode(supplier.SupplierCode));
    });

    /// <summary>测试只用显式专用连接，finally 回滚本次事务，不提交、清库或修改已有供应商。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, PostgreSqlSupplierRepository, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new TestUser();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        try { await scenario(context, new PostgreSqlSupplierRepository(context)); }
        finally { try { transaction.Rollback(); } finally { session.Transaction = null; } }
    }

    /// <summary>生成仅供当前事务使用的完整必填记录，其他字段保留空值。</summary>
    private static Supplier NewSupplier(string code, bool active = true, string? name = null) => new()
    { SupplierCode = code, SupplierName = name ?? code, DocRelateNo = Prefix(), IsActive = active,
        Currency = null, TaxRate = null, ZheKou = null, ContactEmail = null, DefaultLeadTime = null };

    /// <summary>短随机前缀隔离当前测试的数据与分页查询。</summary>
    private static string Prefix() => "PGS" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>仅为事务内临时数据提供审计名称。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_supplier__";
        public bool IsAuthenticated => false;
    }
}
