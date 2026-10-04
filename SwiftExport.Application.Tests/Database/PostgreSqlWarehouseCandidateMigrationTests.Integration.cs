using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在保留的专用库中验证候选资格与文本语义；仅新增随机测试行，所有插入随外层事务回滚。</summary>
[TestClass]
[TestCategory("PostgreSqlMasterDataIntegration")]
public sealed class PostgreSqlWarehouseCandidateMigrationIntegrationTests
{
    /// <summary>真实供应商与仓库双方有效才入选；快照不能补造缺失关系或覆盖当前供应商资料。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task WarehouseCandidate_RequiresCurrentQualifiedSupplierAndVirtualWarehouse()
    {
        using var fixture = new Fixture();
        var baseline = await fixture.SearchAsync(null);
        var expected = new List<Guid>();
        expected.Add((await fixture.AddAsync()).Uid);
        await fixture.AddAsync(warehouse => warehouse.IsActive = false);
        await fixture.AddAsync(warehouse => warehouse.IsDeleted = true);
        await fixture.AddAsync(supplierChange: supplier => supplier.IsActive = false);
        await fixture.AddAsync(supplierChange: supplier => supplier.IsDeleted = true);
        await fixture.AddAsync(warehouse => warehouse.SiteType = (WarehouseSiteTypeEnum)1);
        await fixture.AddAsync(warehouse => warehouse.SupplierId = null);
        await fixture.AddAsync(warehouse => warehouse.SupplierId = Guid.NewGuid());
        var result = await fixture.SearchAsync(fixture.Prefix.ToLowerInvariant());
        Assert.AreEqual(1, result.TotalCount);
        CollectionAssert.AreEqual(expected, result.Items.Select(item => item.Uid).ToList());
        var row = result.Items.Single();
        Assert.AreEqual(WarehouseSiteTypeEnum.SupplierVirtual, row.SiteType);
        Assert.IsTrue(row.IsActive);
        Assert.IsTrue(row.SupplierIsActive);
        Assert.AreEqual("当前供应商" + fixture.Prefix + "1", row.SupplierName);
        Assert.AreNotEqual("历史名称", row.SupplierName);
        var unfiltered = await fixture.SearchAsync(null);
        Assert.AreEqual(baseline.TotalCount + 1, unfiltered.TotalCount, "空关键词仍只增加一个合法候选，且可空参数可正常绑定。");
        Assert.IsTrue(unfiltered.Items.Count <= 100);
        await fixture.AssertInvisibleAsync();
        await fixture.RollbackAndAssertAbsentAsync();
    }

    /// <summary>完整 Unicode 空白在四个身份字段中均不合格；零宽空格等非空白字符保留资格。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task WarehouseCandidate_UsesExactUnicodeWhitespaceForEveryIdentityField()
    {
        using var fixture = new Fixture();
        var whitespace = new string(Enumerable.Range(0, char.MaxValue + 1).Select(value => (char)value).Where(char.IsWhiteSpace).ToArray());
        await fixture.AddAsync(warehouse => warehouse.Code = whitespace);
        await fixture.AddAsync(warehouse => warehouse.Name = whitespace);
        await fixture.AddAsync(supplierChange: supplier => supplier.SupplierCode = whitespace);
        await fixture.AddAsync(supplierChange: supplier => supplier.SupplierName = whitespace);
        var expected = new List<Guid>
        {
            (await fixture.AddAsync(warehouse => warehouse.Code = "\u200B")).Uid,
            (await fixture.AddAsync(warehouse => warehouse.Name = "\u200B")).Uid,
            (await fixture.AddAsync(supplierChange: supplier => supplier.SupplierCode = "\u200B")).Uid,
            (await fixture.AddAsync(supplierChange: supplier => supplier.SupplierName = "\u200B")).Uid
        };
        var result = await fixture.SearchAsync(fixture.Prefix);
        Assert.AreEqual(expected.Count, result.TotalCount);
        CollectionAssert.AreEquivalent(expected, result.Items.Select(item => item.Uid).ToList());
        await fixture.AssertInvisibleAsync();
        await fixture.RollbackAndAssertAbsentAsync();
    }

    /// <summary>搜索四个当前字段时保持大小写语义、字面通配及稳定分页，空格和问号不被额外规范化。</summary>
    [TestMethod]
    [Timeout(60000)]
    public async Task WarehouseCandidate_LiteralSearchAndPagingPreserveOriginalSemantics()
    {
        using var fixture = new Fixture();
        var special = fixture.Prefix + " ~%_[\\? ";
        var expected = new List<Guid>
        {
            (await fixture.AddAsync(warehouse => warehouse.Code = special + "A")).Uid,
            (await fixture.AddAsync(warehouse => warehouse.Name = special + "B")).Uid,
            (await fixture.AddAsync(supplierChange: supplier => supplier.SupplierCode = special + "C")).Uid,
            (await fixture.AddAsync(supplierChange: supplier => supplier.SupplierName = special + "D")).Uid
        };
        await fixture.AddAsync(warehouse => warehouse.Name = fixture.Prefix + " anything ");
        var all = await fixture.SearchAsync(special.ToLowerInvariant());
        Assert.AreEqual(4, all.TotalCount);
        CollectionAssert.AreEquivalent(expected, all.Items.Select(item => item.Uid).ToList());
        var first = await fixture.SearchAsync(special, 1, 2);
        var second = await fixture.SearchAsync(special, 2, 2);
        Assert.AreEqual(4, first.TotalCount);
        Assert.AreEqual(4, second.TotalCount);
        CollectionAssert.AreEqual(all.Items.Select(item => item.Uid).ToList(),
            first.Items.Concat(second.Items).Select(item => item.Uid).ToList());
        Assert.HasCount(0, (await fixture.SearchAsync(special, 3, 2)).Items);
        Assert.AreEqual(0, (await fixture.SearchAsync(special + " ")).TotalCount, "仓储不移除关键词尾部字面空格。");
        await fixture.AssertInvisibleAsync();
        await fixture.RollbackAndAssertAbsentAsync();
    }

    /// <summary>管理每例随机实体及共享事务；不提交、删除、建表或改变比较约束。</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly DbSession session;
        private readonly DbSession observer;
        private readonly IDbTransaction transaction;
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(40));
        private readonly PostgreSqlDapperRepository<Supplier> suppliers;
        private readonly PostgreSqlDapperRepository<WarehouseSite> warehouses;
        private readonly PostgreSqlWarehouseCandidateRepository candidates;
        private readonly List<Guid> supplierIds = [];
        private readonly List<Guid> warehouseIds = [];
        private bool rolledBack;
        public string Prefix { get; } = "W" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

        /// <summary>只使用主控校验后的保留专用库，两个连接用于事务内读取与外部不可见核验。</summary>
        public Fixture()
        {
            var connection = PostgreSqlIntegrationEnvironment.GetFailureConnectionString();
            session = new DbSession(new PostgreSqlConnectionFactory(connection));
            observer = new DbSession(new PostgreSqlConnectionFactory(connection));
            transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable);
            session.Transaction = transaction;
            var user = new TestUser();
            var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            suppliers = new PostgreSqlDapperRepository<Supplier>(context);
            warehouses = new PostgreSqlDapperRepository<WarehouseSite>(context);
            candidates = new PostgreSqlWarehouseCandidateRepository(context);
        }

        /// <summary>每仓创建独立供应商以遵守活动虚拟仓关系唯一性，变体只改变本例尚未插入的实体。</summary>
        public async Task<WarehouseSite> AddAsync(Action<WarehouseSite>? warehouseChange = null, Action<Supplier>? supplierChange = null)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var suffix = (supplierIds.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var supplier = new Supplier
            {
                SupplierCode = Prefix + "S" + suffix, SupplierName = "当前供应商" + Prefix + suffix,
                DocRelateNo = Prefix + suffix, IsActive = true
            };
            supplierChange?.Invoke(supplier);
            var supplierDeleted = supplier.IsDeleted;
            Assert.IsTrue((await suppliers.AddAsync(supplier)).Success);
            supplierIds.Add(supplier.Uid);
            if (supplierDeleted)
                Assert.AreEqual(1, await session.Connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE public.supplier SET is_deleted=TRUE WHERE uid=@Uid", new { supplier.Uid }, transaction,
                    cancellationToken: timeout.Token)));
            var warehouse = new WarehouseSite
            {
                Code = Prefix + "W" + suffix, Name = "仓库" + Prefix + suffix, SiteType = WarehouseSiteTypeEnum.SupplierVirtual,
                SupplierId = supplier.Uid, SupplierCodeSnapshot = "历史编码", SupplierNameSnapshot = "历史名称", IsActive = true
            };
            warehouseChange?.Invoke(warehouse);
            var warehouseDeleted = warehouse.IsDeleted;
            Assert.IsTrue((await warehouses.AddAsync(warehouse)).Success);
            warehouseIds.Add(warehouse.Uid);
            // 通用新增会清除软删标记；仅将本事务刚创建的随机行设置为测试要求的历史状态。
            if (warehouseDeleted)
                Assert.AreEqual(1, await session.Connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE public.warehouse_site SET is_deleted=TRUE WHERE uid=@Uid", new { warehouse.Uid }, transaction,
                    cancellationToken: timeout.Token)));
            return warehouse;
        }

        /// <summary>直接验证仓储参数语义，页面请求上限沿用现有 Application 门禁。</summary>
        public Task<PagedResponseBase<SwiftExport.Contracts.DTOs.Inventory.WarehouseCandidateDto>> SearchAsync(string? keyword,
            int pageIndex = 1, int pageSize = 100) => candidates.SearchAsync(new PagedRequestBase
            { Keyword = keyword!, PageIndex = pageIndex, PageSize = pageSize }, timeout.Token);

        /// <summary>另一连接始终看不到本例尚未提交的供应商和仓库，回滚后也使用相同身份核验。</summary>
        public async Task AssertInvisibleAsync()
        {
            var parameters = PostgreSqlParameters.Ids(supplierIds.Concat(warehouseIds));
            Assert.AreEqual(0L, await observer.Connection.ExecuteScalarAsync<long>(new CommandDefinition("""
                SELECT (SELECT count(*) FROM public.supplier WHERE uid=ANY(@Uids))
                     + (SELECT count(*) FROM public.warehouse_site WHERE uid=ANY(@Uids));
                """, parameters, cancellationToken: timeout.Token)));
        }

        /// <summary>正常路径显式回滚后再次核对两个表没有本例残留；失败路径仍由 Dispose 兜底回滚。</summary>
        public async Task RollbackAndAssertAbsentAsync()
        {
            transaction.Rollback();
            rolledBack = true;
            session.Transaction = null;
            await AssertInvisibleAsync();
        }

        /// <summary>无论断言或查询是否失败都回滚本例事务并释放两个连接，不执行清理 SQL。</summary>
        public void Dispose()
        {
            try { if (!rolledBack) transaction.Rollback(); }
            finally
            {
                session.Transaction = null;
                transaction.Dispose();
                session.Dispose();
                observer.Dispose();
                timeout.Dispose();
            }
        }
    }

    /// <summary>提供随机审计身份，不创建真实账户。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "__pgtest_candidate";
        public bool IsAuthenticated => true;
    }
}
