using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WarehouseCandidateSqlTests
{
    /// <summary>关联本例完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    private WarehouseCandidateSqlFixture? fixture;
    private WarehouseCandidateSqlFixture Db => fixture!;

    /// <summary>未设置专用开关时不连接数据库，避免普通回归隐式创建实例。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS") != "1")
            Assert.Inconclusive("未启用仓库候选隔离 SQL 测试。");
        fixture = new WarehouseCandidateSqlFixture();
        await fixture.InitializeAsync(TestContext);
    }

    /// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
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
        var blankCodeSupplier = Guid.NewGuid();
        // 空白编码场景使用独立供应商，避免先触发同供应商同类型活动仓唯一约束。
        await Db.ExecuteAsync("""
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,is_active,is_deleted,doc_relate_no) VALUES (@supplier,'S-01','供应商一',TRUE,FALSE,'TEST'),(@stoppedSupplier,'S-02','停用供应商',FALSE,FALSE,'TEST'),(@blankCodeSupplier,'S-03','空白仓编码供应商',TRUE,FALSE,'TEST');
            INSERT INTO public.warehouse_site(uid,version,code,name,site_type,supplier_id,supplier_code_snapshot,supplier_name_snapshot,is_active,is_deleted) VALUES (gen_random_uuid(),1,'WH-01','合格仓',0,@supplier,NULL,NULL,TRUE,FALSE),(gen_random_uuid(),1,'WH-STOP','停用仓',0,@supplier,NULL,NULL,FALSE,FALSE),
              (gen_random_uuid(),1,'WH-INTERNAL','其他类型',1,@supplier,NULL,NULL,TRUE,FALSE),(gen_random_uuid(),1,'WH-NO-SUP','无归属',0,NULL,NULL,NULL,TRUE,FALSE),
              (gen_random_uuid(),1,'WH-SUP-STOP','供应商停用仓',0,@stoppedSupplier,NULL,NULL,TRUE,FALSE),(gen_random_uuid(),1,'WH-DELETED','删除仓',0,@supplier,NULL,NULL,TRUE,TRUE),
              (gen_random_uuid(),1,chr(9),'空白编码',0,@blankCodeSupplier,NULL,NULL,TRUE,FALSE);
            """, new { supplier, stoppedSupplier, blankCodeSupplier });
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
        var otherSupplier = Guid.NewGuid();
        // 两个供应商保留相同可搜索名称，每个供应商只建一个活动虚拟仓。
        await Db.ExecuteAsync("""
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,is_active,is_deleted,doc_relate_no) VALUES (@supplier,'S-SEARCH-1','搜索供应商',TRUE,FALSE,'TEST'),(@otherSupplier,'S-SEARCH-2','搜索供应商',TRUE,FALSE,'TEST');
            INSERT INTO public.warehouse_site(uid,version,code,name,site_type,supplier_id,supplier_code_snapshot,supplier_name_snapshot,is_active,is_deleted) VALUES (gen_random_uuid(),1,'WH-02','乙仓',0,@supplier,NULL,NULL,TRUE,FALSE),(gen_random_uuid(),1,'WH-01','甲仓',0,@otherSupplier,NULL,NULL,TRUE,FALSE);
            """, new { supplier, otherSupplier });
        var result = await Db.QueryAsync(s => s.SearchAsync(new() { Keyword = "搜索供应商", PageIndex = 2, PageSize = 1 }));
        Assert.AreEqual(2, result.TotalCount);
        Assert.AreEqual("WH-02", result.Items.Single().Code);
        await Db.ExecuteAsync("UPDATE public.sys_user_role SET is_deleted=TRUE WHERE user_id=@UserId;", new { Db.UserId });
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => Db.QueryAsync(s => s.SearchAsync(new())));
    }

    /// <summary>撤销角色再重新授予具有采购创建权限的角色后，仓库候选恢复可读。</summary>
    [TestMethod]
    public async Task ExplicitProcurementRoleAllowsCandidateQuery()
    {
        await Db.ExecuteAsync("""
            UPDATE public.sys_user_role SET is_deleted=TRUE WHERE user_id=@UserId;
            INSERT INTO public.sys_user_role(uid,user_id,role_id,is_deleted)
            VALUES(gen_random_uuid(),@UserId,@RoleId,FALSE);
            """, new { Db.UserId, Db.RoleId });
        var result = await Db.QueryAsync(service => service.SearchAsync(new()));
        Assert.AreEqual(0, result.TotalCount);
    }
}
