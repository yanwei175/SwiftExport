using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>在显式开启的随机 LocalDB 中验证仓库候选资格、权限、搜索和分页。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WarehouseCandidateSqlTests
{
    private WarehouseCandidateSqlFixture? fixture;
    private WarehouseCandidateSqlFixture Db => fixture!;

    /// <summary>未设置专用开关时不连接数据库，避免普通回归隐式创建实例。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_WAREHOUSE_CANDIDATE_SQL_TESTS") != "1")
            Assert.Inconclusive("未启用仓库候选隔离 SQL 测试。");
        fixture = new WarehouseCandidateSqlFixture();
        await fixture.InitializeAsync();
    }

    /// <summary>每个场景删除自己创建的随机临时库。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (fixture is not null) await fixture.DisposeAsync();
    }

    /// <summary>只返回资料完整的活动供应商虚拟仓；停用、软删除、错误类型、无归属和停用供应商均排除。</summary>
    [TestMethod]
    public async Task CandidateRequiresValidWarehouseAndSupplierEligibility()
    {
        var supplier = Guid.NewGuid();
        var stoppedSupplier = Guid.NewGuid();
        await Db.ExecuteAsync("""
            INSERT dbo.Supplier VALUES(@supplier,N'S-01',N'供应商一',1,0),(@stoppedSupplier,N'S-02',N'停用供应商',0,0);
            INSERT dbo.WarehouseSite VALUES
              (NEWID(),1,N'WH-01',N'合格仓',0,@supplier,NULL,NULL,1,0),(NEWID(),1,N'WH-STOP',N'停用仓',0,@supplier,NULL,NULL,0,0),
              (NEWID(),1,N'WH-INTERNAL',N'其他类型',1,@supplier,NULL,NULL,1,0),(NEWID(),1,N'WH-NO-SUP',N'无归属',0,NULL,NULL,NULL,1,0),
              (NEWID(),1,N'WH-SUP-STOP',N'供应商停用仓',0,@stoppedSupplier,NULL,NULL,1,0),(NEWID(),1,N'WH-DELETED',N'删除仓',0,@supplier,NULL,NULL,1,1),
              (NEWID(),1,NCHAR(9),N'空白编码',0,@supplier,NULL,NULL,1,0);
            """, new { supplier, stoppedSupplier });
        var result = await Db.QueryAsync(s => s.SearchAsync(new()));
        var row = result.Items.Single();
        Assert.AreEqual("WH-01", row.Code);
        Assert.AreEqual(supplier, row.SupplierId);
        Assert.AreEqual(WarehouseSiteTypeEnum.SupplierVirtual, row.SiteType);
        Assert.IsTrue(row.IsActive && row.SupplierIsActive);
    }

    /// <summary>搜索供应商字段，稳定分页保留总数；撤销真实角色后同一服务入口拒绝访问。</summary>
    [TestMethod]
    public async Task SearchPaginationAndPermissionUseDatabaseFacts()
    {
        var supplier = Guid.NewGuid();
        await Db.ExecuteAsync("""
            INSERT dbo.Supplier VALUES(@supplier,N'S-SEARCH',N'搜索供应商',1,0);
            INSERT dbo.WarehouseSite VALUES(NEWID(),1,N'WH-02',N'乙仓',0,@supplier,NULL,NULL,1,0),(NEWID(),1,N'WH-01',N'甲仓',0,@supplier,NULL,NULL,1,0);
            """, new { supplier });
        var result = await Db.QueryAsync(s => s.SearchAsync(new() { Keyword = "搜索供应商", PageIndex = 2, PageSize = 1 }));
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual("WH-02", result.Items.Single().Code);
        await Db.ExecuteAsync("UPDATE dbo.SysUser_Roles SET IsDeleted=1 WHERE UserId=@UserId;", new { Db.UserId });
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchAsync(new())));
    }

    /// <summary>只有新采购模块关系且没有历史细权限角色时，兼容投影仍允许采购仓库候选。</summary>
    [TestMethod]
    public async Task ProcurementModuleProjectionAllowsCandidateQuery()
    {
        await Db.ExecuteAsync("""
            UPDATE dbo.SysUser_Roles SET IsDeleted=1 WHERE UserId=@UserId;
            INSERT dbo.SysUser_ModulePermissions(Uid,UserId,PermissionKey)
            VALUES(NEWID(),@UserId,N'Module.Procurement.Access');
            """, new { Db.UserId });
        var result = await Db.QueryAsync(service => service.SearchAsync(new()));
        Assert.AreEqual(0, result.TotalCount);
    }
}
