using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>使用真实 SQL Server 验证候选资格、字典唯一性、授权、分页与历史筛选；显式开关之外不连接数据库。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProcurementMasterDataSqlTests
{
    private ProcurementMasterDataSqlFixture? fixture;
    private ProcurementMasterDataSqlFixture Db => fixture!;

    /// <summary>只有显式授权的专用开关开启才建立随机临时库。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_MASTERDATA_SQL_TESTS") != "1")
            Assert.Inconclusive("未启用主数据候选 LocalDB 测试；未验证 SQL。");
        fixture = new ProcurementMasterDataSqlFixture();
        await fixture.InitializeAsync();
    }

    /// <summary>每个场景结束后删除本场景临时库，即使断言失败也执行清理。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (fixture is not null) await fixture.DisposeAsync();
    }

    /// <summary>新增与历史筛选分别处理停用；软删除和空 Uid 不作为可选身份，历史不受名称完整性限制。</summary>
    [TestMethod]
    public async Task PartiesSeparateNewEligibilityFromHistoricalFiltering()
    {
        await Db.ExecuteAsync("""
            INSERT dbo.Customer VALUES(NEWID(),N'C-active',N'活动客户',1,0),(NEWID(),N'C-stopped',N'停用客户',0,0),
              (NEWID(),N'C-deleted',N'删除客户',1,1),(NEWID(),NCHAR(9)+NCHAR(12288),N'空白编码',1,0),
              ('00000000-0000-0000-0000-000000000000',N'C-empty',N'空身份',1,0);
            INSERT dbo.Supplier VALUES(NEWID(),N'S-active',N'活动供应商',1,0),(NEWID(),N'S-stopped',N'停用供应商',0,0),
              (NEWID(),N'S-deleted',N'删除供应商',1,1),(NEWID(),N'S-blank',NCHAR(9)+NCHAR(12288),1,0);
            """);
        var customers = await Db.QueryAsync(s => s.SearchCustomersAsync(new()));
        var suppliers = await Db.QueryAsync(s => s.SearchSuppliersAsync(new()));
        Assert.AreEqual("C-active", customers.Items.Single().CustomerCode);
        Assert.AreEqual("S-active", suppliers.Items.Single().SupplierCode);
        var historicalCustomers = await Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new()));
        var historicalSuppliers = await Db.QueryAsync(s => s.SearchSupplierHistoryFiltersAsync(new()));
        Assert.AreEqual(3, historicalCustomers.TotalCount);
        Assert.AreEqual(3, historicalSuppliers.TotalCount);
        Assert.IsFalse(historicalCustomers.Items.Single(x => x.CustomerCode == "C-stopped").IsActive);
        Assert.IsFalse(historicalSuppliers.Items.Single(x => x.SupplierCode == "S-stopped").IsActive);
        Assert.AreEqual(suppliers.Items[0].Uid, historicalSuppliers.Items.Single(x => x.SupplierCode == "S-active").Uid);
    }

    /// <summary>标准采购三类物料可选；停用、删除、虚拟、非标、生产、空编码和无单位产品均排除。</summary>
    [TestMethod]
    public async Task ProductsRequireStandardPurchaseIdentityAndCanonicalUnit()
    {
        var good = await Db.AddProductAsync("P-good");
        await Db.AddProductAsync("P-part", material: "零件");
        await Db.AddProductAsync("P-customer", material: "客供件");
        await Db.AddProductAsync("P-stopped", active: false);
        await Db.AddProductAsync("P-deleted", deleted: true);
        await Db.AddProductAsync("P-virtual", material: "虚拟件");
        await Db.AddProductAsync("非标件001");
        await Db.AddProductAsync("P-produced", supply: "生产");
        await Db.AddProductAsync("P-outsourced", supply: "外协");
        await Db.AddProductAsync("P-no-unit", unit: null);
        await Db.AddProductAsync("\t\u3000");
        var result = await Db.QueryAsync(s => s.SearchProductsAsync(new()));
        Assert.AreEqual(3, result.TotalCount);
        Assert.IsTrue(result.Items.Any(x => x.Uid == good));
        Assert.IsTrue(result.Items.All(x => x.Unit == "EA"));
        Assert.IsTrue(result.Items.All(x => x.DescriptionZh == "产品描述"));
    }

    /// <summary>无效、停用或重复的单位不得随意选择；删除字典项不参与唯一性计数。</summary>
    [TestMethod]
    public async Task UnitLookupRejectsDuplicatesDisabledMissingAndWhitespace()
    {
        await Db.ExecuteAsync("""
            INSERT dbo.SysDictData VALUES
              (NEWID(),N'Unit',N'KG',1,0),(NEWID(),N'Unit',N' kg ',1,0),
              (NEWID(),N'Unit',N'BOX',0,0),(NEWID(),N'Unit',N'EA',1,1),
              (NEWID(),N'Unit',NCHAR(9),1,0);
            """);
        await Db.AddProductAsync("P-ok");
        await Db.AddProductAsync("P-duplicate", unit: "KG");
        await Db.AddProductAsync("P-disabled", unit: "BOX");
        await Db.AddProductAsync("P-unknown", unit: "UNKNOWN");
        await Db.AddProductAsync("P-whitespace", unit: "\t");
        var result = await Db.QueryAsync(s => s.SearchProductsAsync(new()));
        Assert.AreEqual("P-ok", result.Items.Single().Sku);
        Assert.AreEqual("EA", result.Items[0].Unit);
    }

    /// <summary>创建和读取角色不可互相越权，Admin 可访问两种查询；失败后可再次使用服务。</summary>
    [TestMethod]
    public async Task CreationAndHistoryUseDifferentRealRoles()
    {
        await Db.ExecuteAsync("UPDATE dbo.SysRoles SET RoleName=N'Procurement.Order.Read';");
        await Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new()));
        await Db.QueryAsync(s => s.SearchSupplierHistoryFiltersAsync(new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchCustomersAsync(new())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchSuppliersAsync(new())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchProductsAsync(new())));
        await Db.ExecuteAsync("UPDATE dbo.SysRoles SET RoleName=N'Procurement.Order.Create';");
        await Db.QueryAsync(s => s.SearchCustomersAsync(new()));
        await Db.QueryAsync(s => s.SearchSuppliersAsync(new()));
        await Db.QueryAsync(s => s.SearchProductsAsync(new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new())));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchSupplierHistoryFiltersAsync(new())));
        await Db.ExecuteAsync("UPDATE dbo.SysRoles SET RoleName=N'Admin';");
        await Db.QueryAsync(s => s.SearchProductsAsync(new()));
        await Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new()));
    }

    /// <summary>停用账号、删除账号/关联/角色均失去查询权限；恢复资料后下一请求重新授权。</summary>
    [TestMethod]
    public async Task RevokedUserRoleAndMembershipCannotRead()
    {
        foreach (var mutation in new[]
        {
            "UPDATE dbo.SysUser SET IsActive=0;",
            "UPDATE dbo.SysUser SET IsDeleted=1;",
            "UPDATE dbo.SysUser_Roles SET IsDeleted=1;",
            "UPDATE dbo.SysRoles SET IsDeleted=1;",
            "UPDATE dbo.SysRoles SET RoleName=N'Unrelated';"
        })
        {
            await Db.ExecuteAsync(mutation);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchCustomersAsync(new())));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new())));
            await Db.ExecuteAsync("""
                UPDATE dbo.SysUser SET IsActive=1,IsDeleted=0;
                UPDATE dbo.SysUser_Roles SET IsDeleted=0;
                UPDATE dbo.SysRoles SET IsDeleted=0,RoleName=N'Admin';
                """);
        }
        await Db.QueryAsync(s => s.SearchCustomersAsync(new()));
    }

    /// <summary>特殊字符按字面匹配，重复编码用 Uid 稳定排序；无结果和超末页均返回正确计数。</summary>
    [TestMethod]
    public async Task SearchIsLiteralAndPaginationStable()
    {
        await Db.ExecuteAsync("""
            INSERT dbo.Customer VALUES(NEWID(),N'C-%_[~',N'特殊字符',1,0),
              (NEWID(),N'C-other',N'普通客户',1,0),(NEWID(),N'C-other',N'同码客户',1,0);
            INSERT dbo.Supplier VALUES(NEWID(),N'S-%_[~',N'特殊字符',1,0),(NEWID(),N'S-other',N'普通供应商',1,0);
            """);
        await Db.AddProductAsync("P-%_[~");
        await Db.AddProductAsync("P-other");
        foreach (var keyword in new[] { "%", "_", "[", "~" })
        {
            Assert.AreEqual(1, (await Db.QueryAsync(s => s.SearchCustomersAsync(new() { Keyword = keyword }))).TotalCount);
            Assert.AreEqual(1, (await Db.QueryAsync(s => s.SearchSuppliersAsync(new() { Keyword = keyword }))).TotalCount);
            Assert.AreEqual(1, (await Db.QueryAsync(s => s.SearchProductsAsync(new() { Keyword = keyword }))).TotalCount);
            Assert.AreEqual(1, (await Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new() { Keyword = keyword }))).TotalCount);
            Assert.AreEqual(1, (await Db.QueryAsync(s => s.SearchSupplierHistoryFiltersAsync(new() { Keyword = keyword }))).TotalCount);
        }
        var first = await Db.QueryAsync(s => s.SearchCustomersAsync(new() { Keyword = " C-other ", PageSize = 1 }));
        var second = await Db.QueryAsync(s => s.SearchCustomersAsync(new() { Keyword = "C-other", PageSize = 1, PageIndex = 2 }));
        var repeated = await Db.QueryAsync(s => s.SearchCustomersAsync(new() { Keyword = "C-other", PageSize = 1 }));
        Assert.AreEqual(2, first.TotalCount);
        Assert.AreEqual(2, second.TotalCount);
        Assert.AreNotEqual(first.Items.Single().Uid, second.Items.Single().Uid);
        Assert.AreEqual(first.Items.Single().Uid, repeated.Items.Single().Uid);
        var afterLast = await Db.QueryAsync(s => s.SearchCustomersAsync(new() { PageIndex = 4, PageSize = 1 }));
        Assert.AreEqual(3, afterLast.TotalCount);
        Assert.AreEqual(0, afterLast.Items.Count);
        var empty = await Db.QueryAsync(s => s.SearchProductsAsync(new() { Keyword = "' OR 1=1--" }));
        Assert.AreEqual(0, empty.TotalCount);
        Assert.AreEqual(0, empty.Items.Count);
    }

    /// <summary>选择后停用立即影响下一次候选查询，但保留历史筛选；候选本身没有锁定未来资格。</summary>
    [TestMethod]
    public async Task EligibilityIsRefreshedWithoutRemovingHistoricalIdentity()
    {
        await Db.ExecuteAsync("INSERT dbo.Customer VALUES(NEWID(),N'C-change',N'状态变更客户',1,0);");
        var before = await Db.QueryAsync(s => s.SearchCustomersAsync(new()));
        await Db.ExecuteAsync("UPDATE dbo.Customer SET IsActive=0;");
        Assert.AreEqual(0, (await Db.QueryAsync(s => s.SearchCustomersAsync(new()))).TotalCount);
        var history = await Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new()));
        Assert.AreEqual(before.Items.Single().Uid, history.Items.Single().Uid);
        Assert.IsFalse(history.Items.Single().IsActive);
    }
}
