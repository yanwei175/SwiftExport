using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
[TestClass]
[DoNotParallelize]
public sealed class WarehousePickerSqlTests
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
            Assert.Inconclusive("未启用仓库选择器隔离 SQL 测试。");
        fixture = new WarehouseCandidateSqlFixture();
        await fixture.InitializeAsync(TestContext);
    }

    /// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
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
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,is_active,is_deleted,doc_relate_no) VALUES (@inactiveSupplier,'S-INACTIVE','停用供应商',FALSE,FALSE,'TEST'),(@deletedSupplier,'S-DELETED','删除供应商',TRUE,TRUE,'TEST');
            INSERT INTO public.warehouse_site(uid,version,code,name,site_type,supplier_id,supplier_code_snapshot,supplier_name_snapshot,is_active,is_deleted) VALUES (gen_random_uuid(),1,'WH-01','停用供应商仓',0,@inactiveSupplier,'OLD-1','旧名称一',TRUE,FALSE),
              (gen_random_uuid(),1,'WH-02','删除供应商仓',0,@deletedSupplier,'S-SNAPSHOT','快照供应商',TRUE,FALSE),
              (gen_random_uuid(),1,'WH-03','缺失供应商仓',0,@missingSupplier,'S-MISSING','缺失快照',TRUE,FALSE),
              (gen_random_uuid(),1,'WH-STOP','停用仓',0,@inactiveSupplier,NULL,NULL,FALSE,FALSE),
              (gen_random_uuid(),1,'WH-DELETED','删除仓',0,@inactiveSupplier,NULL,NULL,TRUE,TRUE);
            """, new { inactiveSupplier, deletedSupplier, missingSupplier });

        var result = await Db.QueryInventoryAsync(service => service.GetWarehousePickerPagedAsync(new PagedRequestBase()));

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(3, result.Data.TotalCount);
        Assert.AreEqual("S-INACTIVE", result.Data.Items[0].SupplierCode);
        Assert.AreEqual("S-SNAPSHOT", result.Data.Items[1].SupplierCode);
        Assert.AreEqual("S-MISSING", result.Data.Items[2].SupplierCode);
    }

    /// <summary>使用唯一仓库编码及独立供应商验证字面搜索和可重复分页，不构造正式约束禁止的同码活动仓。</summary>
    [TestMethod]
    public async Task SpecialKeywordAndStablePagingAreLiteralAndDeterministic()
    {
        var supplier = Guid.NewGuid();
        var otherSupplier = Guid.NewGuid();
        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        await Db.ExecuteAsync("""
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,is_active,is_deleted,doc_relate_no) VALUES (@supplier,'S-%_[~-1','特殊供应商',TRUE,FALSE,'TEST'),(@otherSupplier,'S-%_[~-2','特殊供应商',TRUE,FALSE,'TEST');
            INSERT INTO public.warehouse_site(uid,version,code,name,site_type,supplier_id,supplier_code_snapshot,supplier_name_snapshot,is_active,is_deleted) VALUES (@second,1,'WH-SAME-B','后项',0,@supplier,NULL,NULL,TRUE,FALSE),
              (@first,1,'WH-SAME-A','前项',0,@otherSupplier,NULL,NULL,TRUE,FALSE);
            """, new { supplier, otherSupplier, first, second });

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
        var repeated = await Db.QueryInventoryAsync(service => service.GetWarehousePickerPagedAsync(new PagedRequestBase
        {
            Keyword = "WH-SAME", PageIndex = 1, PageSize = 1
        }));
        Assert.AreEqual(first, repeated.Data.Items.Single().Uid);
    }
}
