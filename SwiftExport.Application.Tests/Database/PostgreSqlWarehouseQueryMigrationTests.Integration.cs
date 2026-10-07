using System.Data;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Inventory.Warehouses;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>在明确授权的专用测试库验证仓库查询语义；只插入随机基础资料，并在同一事务内回滚。</summary>
[TestClass]
public sealed class PostgreSqlWarehouseQueryMigrationIntegrationTests
{
    /// <summary>身份和管理查询保留停用仓，所有入口排除软删仓，产品及仓库候选仅返回活动仓。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    public Task WarehouseStates_KeepInactiveOnlyForExistingReferences() => InRollback(async context =>
    {
        var prefix = Prefix();
        var supplier = await InsertSupplier(context, prefix + "S");
        var active = await InsertWarehouse(context, prefix + "A", supplier.Uid);
        var inactive = await InsertWarehouse(context, prefix + "I", supplier.Uid, active: false);
        var deleted = await InsertWarehouse(context, prefix + "D", supplier.Uid, active: false, deleted: true);
        var repository = new PostgreSqlWarehouseQueryRepository(context);
        var ids = await repository.GetByIdsAsync([active.Uid, inactive.Uid, deleted.Uid, Guid.NewGuid()]);
        CollectionAssert.AreEquivalent(new[] { active.Uid, inactive.Uid }, ids.Select(row => row.Uid).ToArray());
        Assert.IsFalse(ids.Single(row => row.Uid == inactive.Uid).IsActive);
        Assert.IsFalse((await repository.GetDtoAsync(inactive.Uid))!.IsActive);
        Assert.IsNull(await repository.GetDtoAsync(deleted.Uid));
        Assert.IsNull(await repository.GetDtoAsync(Guid.NewGuid()));
        var request = new PagedRequestBase { Keyword = prefix };
        var products = await repository.GetProductDefaultCandidatesAsync(request);
        var picker = await repository.GetPickerPagedAsync(request);
        var management = await repository.SearchAsync(new WarehouseSiteSearchRequest { Keyword = prefix });
        Assert.AreEqual(1, products.TotalCount);
        Assert.AreEqual(active.Uid, products.Items.Single().Uid);
        Assert.AreEqual(1, picker.TotalCount);
        Assert.AreEqual(active.Uid, picker.Items.Single().Uid);
        Assert.AreEqual(WarehouseSiteTypeEnum.SupplierVirtual, picker.Items.Single().SiteType);
        Assert.AreEqual(2, management.TotalCount);
        CollectionAssert.AreEqual(new[] { active.Uid, inactive.Uid }, management.Items.Select(row => row.Uid).ToArray());
    });

    /// <summary>供应商停用不自动过滤其仓库；软删供应商回退快照，选择器与管理关键词保留不同事实来源。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    public Task SupplierStateAndSnapshots_PreserveEachQueryBoundary() => InRollback(async context =>
    {
        var prefix = Prefix();
        var inactive = await InsertSupplier(context, prefix + "LIVE", active: false, name: "");
        var deleted = await InsertSupplier(context, prefix + "DELETED", deleted: true);
        var liveWarehouse = await InsertWarehouse(context, prefix + "W1", inactive.Uid, snapshotName: prefix + "OldName");
        var historical = await InsertWarehouse(context, prefix + "W2", deleted.Uid,
            snapshotCode: prefix + "OldCode", snapshotName: prefix + "HistoricalName");
        var noSupplier = await InsertWarehouse(context, prefix + "W3", null);
        var repository = new PostgreSqlWarehouseQueryRepository(context);
        var picker = await repository.GetPickerPagedAsync(new PagedRequestBase { Keyword = prefix });
        Assert.AreEqual(3, picker.TotalCount);
        var live = picker.Items.Single(row => row.Uid == liveWarehouse.Uid);
        Assert.AreEqual(inactive.SupplierCode, live.SupplierCode);
        Assert.AreEqual("", live.SupplierName, "实时空串不能被快照覆盖。");
        Assert.AreEqual(prefix + "HistoricalName", picker.Items.Single(row => row.Uid == historical.Uid).SupplierName);
        var empty = picker.Items.Single(row => row.Uid == noSupplier.Uid);
        Assert.IsNull(empty.SupplierId);
        Assert.AreEqual("", empty.SupplierCode);
        Assert.AreEqual("", empty.SupplierName);
        var detail = await repository.GetDtoAsync(noSupplier.Uid);
        Assert.IsNotNull(detail);
        Assert.IsNull(detail.SupplierCode);
        Assert.IsNull(detail.SupplierName);
        Assert.IsNotNull(detail.UpdatedDate);
        Assert.AreEqual(DateTimeKind.Unspecified, detail.UpdatedDate.Value.Kind);
        Assert.AreEqual(0, (await repository.SearchSupplierLookupAsync(new PagedRequestBase { Keyword = prefix })).TotalCount);
        Assert.AreEqual(1, (await repository.GetPickerPagedAsync(new PagedRequestBase { Keyword = prefix + "HistoricalName" })).TotalCount);
        Assert.AreEqual(0, (await repository.SearchAsync(new WarehouseSiteSearchRequest { Keyword = prefix + "HistoricalName" })).TotalCount);
        Assert.AreEqual(prefix + "OldCode", (await repository.GetDtoAsync(historical.Uid))!.SupplierCode);
    });

    /// <summary>可空身份、状态和关键词在真实驱动中可绑定；管理页活动优先、再按编码和身份稳定翻页。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    public Task NullableFiltersAndPaging_KeepCountsAndPageMembership() => InRollback(async context =>
    {
        var prefix = Prefix();
        var supplier1 = await InsertSupplier(context, prefix + "S1");
        var supplier2 = await InsertSupplier(context, prefix + "S2");
        var first = await InsertWarehouse(context, prefix + "A", supplier1.Uid);
        var second = await InsertWarehouse(context, prefix + "B", supplier2.Uid);
        var inactive = await InsertWarehouse(context, prefix + "0", supplier1.Uid, active: false);
        var repository = new PostgreSqlWarehouseQueryRepository(context);
        var expected = new[] { first.Uid, second.Uid, inactive.Uid };
        for (var index = 1; index <= expected.Length; index++)
        {
            var page = await repository.SearchAsync(new WarehouseSiteSearchRequest { Keyword = prefix, PageIndex = index, PageSize = 1 });
            Assert.AreEqual(3, page.TotalCount);
            Assert.AreEqual(expected[index - 1], page.Items.Single().Uid);
        }
        var emptyPage = await repository.SearchAsync(new WarehouseSiteSearchRequest { Keyword = prefix, PageIndex = 4, PageSize = 1 });
        Assert.AreEqual(3, emptyPage.TotalCount);
        Assert.HasCount(0, emptyPage.Items);
        var allStates = await repository.SearchAsync(new WarehouseSiteSearchRequest { SupplierId = supplier1.Uid });
        Assert.AreEqual(2, allStates.TotalCount);
        Assert.AreEqual(first.Uid, allStates.Items[0].Uid);
        Assert.AreEqual(inactive.Uid, (await repository.SearchAsync(new WarehouseSiteSearchRequest
            { SupplierId = supplier1.Uid, IsActive = false })).Items.Single().Uid);
        Assert.AreEqual(first.Uid, (await repository.SearchAsync(new WarehouseSiteSearchRequest
            { SupplierId = supplier1.Uid, IsActive = true, Keyword = "  " })).Items.Single().Uid);
        Assert.AreEqual(0, (await repository.SearchAsync(new WarehouseSiteSearchRequest { SupplierId = Guid.NewGuid() })).TotalCount);
        // 空关键词候选也必须成功执行，目标行在默认页之外时不假定其页内位置。
        await repository.GetProductDefaultCandidatesAsync(new PagedRequestBase());
        await repository.GetPickerPagedAsync(new PagedRequestBase());
        await repository.SearchSupplierLookupAsync(new PagedRequestBase());
    });

    /// <summary>普通文本按显式大小写不敏感排序规则匹配；原通配字符、空格、问号和反斜杠仍按字面搜索。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlMasterDataIntegration")]
    [DataRow("%")]
    [DataRow("_")]
    [DataRow("~")]
    [DataRow("[")]
    [DataRow("?")]
    [DataRow(" ")]
    [DataRow("\\")]
    public Task LiteralKeywords_DoNotExpandCandidates(string character) => InRollback(async context =>
    {
        var prefix = Prefix();
        var name = prefix + "Value" + character + "Ending";
        var targetSupplier = await InsertSupplier(context, prefix + "S1", name: name);
        var otherSupplier = await InsertSupplier(context, prefix + "S2", name: prefix + "ValueXEnding");
        var target = await InsertWarehouse(context, prefix + "W1", targetSupplier.Uid, name: name);
        await InsertWarehouse(context, prefix + "W2", otherSupplier.Uid, name: prefix + "ValueXEnding");
        var repository = new PostgreSqlWarehouseQueryRepository(context);
        var keyword = "  " + name.ToLowerInvariant() + "  ";
        var request = new PagedRequestBase { Keyword = keyword };
        var products = await repository.GetProductDefaultCandidatesAsync(request);
        var picker = await repository.GetPickerPagedAsync(request);
        var management = await repository.SearchAsync(new WarehouseSiteSearchRequest { Keyword = keyword });
        var suppliers = await repository.SearchSupplierLookupAsync(request);
        Assert.AreEqual(1, products.TotalCount);
        Assert.AreEqual(target.Uid, products.Items.Single().Uid);
        Assert.AreEqual(1, picker.TotalCount);
        Assert.AreEqual(target.Uid, picker.Items.Single().Uid);
        Assert.AreEqual(1, management.TotalCount);
        Assert.AreEqual(target.Uid, management.Items.Single().Uid);
        Assert.AreEqual(1, suppliers.TotalCount);
        Assert.AreEqual(targetSupplier.Uid, suppliers.Items.Single().Uid);
    });

    /// <summary>只使用主控明确校验的专用库；所有临时数据在 finally 回滚，不提交或删除已有数据。</summary>
    private static async Task InRollback(Func<RepositoryBaseContext, Task> scenario)
    {
        using var session = new DbSession(new PostgreSqlConnectionFactory(PostgreSqlIntegrationEnvironment.GetFailureConnectionString()));
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        var user = new TestUser();
        var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
        try { await scenario(context); }
        finally
        {
            try { transaction.Rollback(); }
            finally { session.Transaction = null; }
        }
    }

    /// <summary>创建不可与现存编码混淆的供应商；状态由场景显式指定，不调用维护服务。</summary>
    private static async Task<Supplier> InsertSupplier(RepositoryBaseContext context, string code,
        bool active = true, bool deleted = false, string? name = null)
    {
        var supplier = new Supplier { Uid = Guid.NewGuid(), SupplierCode = code, SupplierName = name ?? code,
            DocRelateNo = Prefix(), IsActive = active, IsDeleted = deleted };
        await Insert(context, supplier);
        return supplier;
    }

    /// <summary>创建场景专用仓库及可空供应商快照，不修改已存在的仓库资料。</summary>
    private static async Task<WarehouseSite> InsertWarehouse(RepositoryBaseContext context, string code, Guid? supplierId,
        bool active = true, bool deleted = false, string? name = null, string? snapshotCode = null, string? snapshotName = null)
    {
        var warehouse = new WarehouseSite { Uid = Guid.NewGuid(), Code = code, Name = name ?? code, SupplierId = supplierId,
            SiteType = WarehouseSiteTypeEnum.SupplierVirtual, IsActive = active, IsDeleted = deleted,
            SupplierCodeSnapshot = snapshotCode, SupplierNameSnapshot = snapshotName };
        await Insert(context, warehouse);
        return warehouse;
    }

    /// <summary>按实体元数据及时间类型只追加临时记录，保留预先生成的身份和软删除标志。</summary>
    private static async Task Insert<T>(RepositoryBaseContext context, T entity) where T : BaseEntity
    {
        context.AuditStamp.StampForInsert(entity);
        var metadata = EntityMetadataCache.GetMetadata(typeof(T));
        var table = $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}";
        var columns = string.Join(", ", metadata.Columns.Select(column => PostgreSqlNames.Quote(column.ColumnName)));
        var parameters = string.Join(", ", metadata.Columns.Select(column => "@" + column.ParameterName));
        Assert.AreEqual(1, await context.Session.Connection.ExecuteAsync(new CommandDefinition(
            $"INSERT INTO {table} ({columns}) VALUES ({parameters});", PostgreSqlParameters.Entity(entity, metadata), context.Session.Transaction)));
    }

    /// <summary>生成短随机前缀，适配供应商与仓库的既有长度及唯一约束。</summary>
    private static string Prefix() => "PGW" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>只提供临时记录的审计名称，不模拟真实用户或授权。</summary>
    private sealed class TestUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_warehouse_query__";
        public bool IsAuthenticated => false;
    }
}
