using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>在显式开启的随机 LocalDB 中验证活动仓库选择器的生产 SQL、显示回退、特殊搜索和稳定分页。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WarehousePickerSqlTests
{
    private WarehouseCandidateSqlFixture? fixture;
    private WarehouseCandidateSqlFixture Db => fixture!;

    /// <summary>未设置专用开关时不连接数据库，避免普通回归隐式创建实例。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_WAREHOUSE_PICKER_SQL_TESTS") != "1")
            Assert.Inconclusive("未启用仓库选择器隔离 SQL 测试。");
        fixture = new WarehouseCandidateSqlFixture();
        await fixture.InitializeAsync();
    }

    /// <summary>每个场景删除自己创建的随机临时库。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (fixture is not null) await fixture.DisposeAsync();
    }

    /// <summary>仅排除仓库自身停用或删除；供应商停用、删除或缺失时仍用当前资料或快照显示。</summary>
    [TestMethod]
    public async Task EligibilityAndSupplierFallbackUseWarehouseFacts()
    {
        var inactiveSupplier = Guid.NewGuid();
        var deletedSupplier = Guid.NewGuid();
        var missingSupplier = Guid.NewGuid();
        await Db.ExecuteAsync("""
            INSERT dbo.Supplier VALUES(@inactiveSupplier,N'S-INACTIVE',N'停用供应商',0,0),(@deletedSupplier,N'S-DELETED',N'删除供应商',1,1);
            INSERT dbo.WarehouseSite VALUES
              (NEWID(),1,N'WH-01',N'停用供应商仓',0,@inactiveSupplier,N'OLD-1',N'旧名称一',1,0),
              (NEWID(),1,N'WH-02',N'删除供应商仓',0,@deletedSupplier,N'S-SNAPSHOT',N'快照供应商',1,0),
              (NEWID(),1,N'WH-03',N'缺失供应商仓',0,@missingSupplier,N'S-MISSING',N'缺失快照',1,0),
              (NEWID(),1,N'WH-STOP',N'停用仓',0,@inactiveSupplier,NULL,NULL,0,0),
              (NEWID(),1,N'WH-DELETED',N'删除仓',0,@inactiveSupplier,NULL,NULL,1,1);
            """, new { inactiveSupplier, deletedSupplier, missingSupplier });

        var result = await Db.QueryInventoryAsync(service => service.GetWarehousePickerPagedAsync(new PagedRequestBase()));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(3, result.Data.TotalCount);
        Assert.AreEqual("S-INACTIVE", result.Data.Items[0].SupplierCode);
        Assert.AreEqual("S-SNAPSHOT", result.Data.Items[1].SupplierCode);
        Assert.AreEqual("S-MISSING", result.Data.Items[2].SupplierCode);
    }

    /// <summary>LIKE 特殊字符按字面值搜索，且相同代码按 Uid 提供可重复分页顺序。</summary>
    [TestMethod]
    public async Task SpecialKeywordAndStablePagingAreLiteralAndDeterministic()
    {
        var supplier = Guid.NewGuid();
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await Db.ExecuteAsync("""
            INSERT dbo.Supplier VALUES(@supplier,N'S-%_[~',N'特殊供应商',1,0);
            INSERT dbo.WarehouseSite VALUES
              (@second,1,N'WH-SAME',N'后项',0,@supplier,NULL,NULL,1,0),
              (@first,1,N'WH-SAME',N'前项',0,@supplier,NULL,NULL,1,0);
            """, new { supplier, first, second });

        var special = await Db.QueryInventoryAsync(service => service.GetWarehousePickerPagedAsync(new PagedRequestBase
        {
            Keyword = "%_[~", PageIndex = 1, PageSize = 20
        }));
        var pageOne = await Db.QueryInventoryAsync(service => service.GetWarehousePickerPagedAsync(new PagedRequestBase
        {
            Keyword = "WH-SAME", PageIndex = 1, PageSize = 1
        }));
        var pageTwo = await Db.QueryInventoryAsync(service => service.GetWarehousePickerPagedAsync(new PagedRequestBase
        {
            Keyword = "WH-SAME", PageIndex = 2, PageSize = 1
        }));

        Assert.AreEqual(2, special.Data.TotalCount);
        Assert.AreEqual(first, pageOne.Data.Items.Single().Uid);
        Assert.AreEqual(second, pageTwo.Data.Items.Single().Uid);
    }
}
