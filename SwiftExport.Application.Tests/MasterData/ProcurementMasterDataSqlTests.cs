using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>使用真实 PostgreSQL 验证候选资格、字典唯一性、授权、分页与历史筛选；显式开关之外不连接数据库。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProcurementMasterDataSqlTests
{
    /// <summary>关联本例完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    private ProcurementMasterDataSqlFixture? fixture;
    private ProcurementMasterDataSqlFixture Db => fixture!;

    /// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS") != "1")
            Assert.Inconclusive("未启用主数据候选 PG 测试；未验证 SQL。");
        fixture = new ProcurementMasterDataSqlFixture();
        await fixture.InitializeAsync(TestContext);
    }

    /// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
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
            INSERT INTO public.customer(uid,customer_code,customer_name,is_active,is_deleted,doc_relate_no,deposit_rate,lead_time_days) VALUES (gen_random_uuid(),'C-active','活动客户',TRUE,FALSE,'TEST',0,0),(gen_random_uuid(),'C-stopped','停用客户',FALSE,FALSE,'TEST',0,0),
              (gen_random_uuid(),'C-deleted','删除客户',TRUE,TRUE,'TEST',0,0),(gen_random_uuid(),chr(9)||chr(12288),'空白编码',TRUE,FALSE,'TEST',0,0),
              ('00000000-0000-0000-0000-000000000000','C-empty','空身份',TRUE,FALSE,'TEST',0,0);
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,is_active,is_deleted,doc_relate_no) VALUES (gen_random_uuid(),'S-active','活动供应商',TRUE,FALSE,'TEST'),(gen_random_uuid(),'S-stopped','停用供应商',FALSE,FALSE,'TEST'),
              (gen_random_uuid(),'S-deleted','删除供应商',TRUE,TRUE,'TEST'),(gen_random_uuid(),'S-blank',chr(9)||chr(12288),TRUE,FALSE,'TEST');
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

    /// <summary>采购供货的有效产品均可选；物料类型、编码前缀和单位不在候选 SQL 中承担最终资格校验。</summary>
    [TestMethod]
    public async Task ProductsOnlyRequireActivePurchaseIdentity()
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
        Assert.AreEqual(6, result.TotalCount);
        Assert.IsTrue(result.Items.Any(x => x.Uid == good));
        Assert.IsTrue(result.Items.Any(x => x.Sku == "P-virtual"));
        Assert.IsTrue(result.Items.Any(x => x.Sku == "非标件001"));
        Assert.AreEqual(string.Empty, result.Items.Single(x => x.Sku == "P-no-unit").Unit);
        Assert.AreEqual(" ea ", result.Items.Single(x => x.Uid == good).Unit);
        Assert.IsFalse(result.Items.Any(x => x.Sku is "P-produced" or "P-outsourced" or "P-stopped" or "P-deleted"));
        Assert.IsTrue(result.Items.All(x => x.DescriptionZh == "产品描述"));
    }

    /// <summary>产品候选按当前客户和实际供应商读取有效关系，主体产品不存在关系时仍可选择。</summary>
    [TestMethod]
    public async Task ProductSearchReturnsContextualCustomerAndSupplierCodes()
    {
        var customerId = Guid.NewGuid(); var supplierId = Guid.NewGuid();
        var related = await Db.AddProductAsync("P-related"); var unrelated = await Db.AddProductAsync("P-unrelated");
        await Db.ExecuteAsync("""
            INSERT INTO public.customer_product(uid,customer_id,sku_id,customer_sku,is_active,is_deleted,customer_code) VALUES (gen_random_uuid(),@customerId,@related,'CUST-001',TRUE,FALSE,'TEST');
            INSERT INTO public.supplier_product(uid,supplier_id,sku_id,supplier_sku,unit_price,tax_price,is_deleted,supplier_code,product_sku) VALUES (gen_random_uuid(),@supplierId,@related,'SUP-001',12.5,14.1,FALSE,'TEST','TEST');
            """, new { customerId, supplierId, related });

        var result = await Db.QueryAsync(service => service.SearchProductsAsync(new()
        { CustomerId = customerId, SupplierId = supplierId }));

        var item = result.Items.Single(x => x.Uid == related);
        Assert.AreEqual("CUST-001", item.CustomerSku);
        Assert.AreEqual("SUP-001", item.SupplierSku);
        Assert.AreEqual("/images/main.png", item.MainImage);
        Assert.AreEqual(string.Empty, result.Items.Single(x => x.Uid == unrelated).CustomerSku);
        Assert.AreEqual(12.5m, item.UnitPrice);
        var withoutSupplier = await Db.QueryAsync(service => service.SearchProductsAsync(new() { CustomerId = customerId }));
        Assert.IsNull(withoutSupplier.Items.Single(x => x.Uid == related).UnitPrice);
        // 原SQL Server与当前PG均在未指定供应商时返回空型号，不凭任意供货关系推断默认供应商。
        Assert.AreEqual(string.Empty, withoutSupplier.Items.Single(x => x.Uid == related).SupplierSku);
    }

    /// <summary>单位字典缺失、停用或重复不影响候选；产品当前单位值原样返回供用户识别。</summary>
    [TestMethod]
    public async Task UnitDictionaryDoesNotFilterProductCandidates()
    {
        await Db.ExecuteAsync("""
            INSERT INTO public.sys_dict_data(uid,dict_code,data_name_en,is_enabled,is_deleted,is_system) VALUES (gen_random_uuid(),'Unit','KG',TRUE,FALSE,FALSE),(gen_random_uuid(),'Unit',' kg ',TRUE,FALSE,FALSE),
              (gen_random_uuid(),'Unit','BOX',FALSE,FALSE,FALSE),(gen_random_uuid(),'Unit','EA',TRUE,TRUE,FALSE),
              (gen_random_uuid(),'Unit',chr(9),TRUE,FALSE,FALSE);
            """);
        await Db.AddProductAsync("P-ok");
        await Db.AddProductAsync("P-duplicate", unit: "KG");
        await Db.AddProductAsync("P-disabled", unit: "BOX");
        await Db.AddProductAsync("P-unknown", unit: "UNKNOWN");
        await Db.AddProductAsync("P-whitespace", unit: "\t");
        var result = await Db.QueryAsync(s => s.SearchProductsAsync(new()));
        Assert.AreEqual(5, result.TotalCount);
        Assert.AreEqual("KG", result.Items.Single(x => x.Sku == "P-duplicate").Unit);
        Assert.AreEqual("BOX", result.Items.Single(x => x.Sku == "P-disabled").Unit);
        Assert.AreEqual("UNKNOWN", result.Items.Single(x => x.Sku == "P-unknown").Unit);
        Assert.AreEqual("\t", result.Items.Single(x => x.Sku == "P-whitespace").Unit);
    }

    /// <summary>五个只读候选路由仅依赖当前登录身份；固定基线的用户、角色及成员表保持空。</summary>
    [TestMethod]
    public async Task AllQueriesWorkWithoutDatabaseRoleTables()
    {
        await Db.QueryAsync(s => s.SearchCustomersAsync(new()));
        await Db.QueryAsync(s => s.SearchSuppliersAsync(new()));
        await Db.QueryAsync(s => s.SearchProductsAsync(new()));
        await Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new()));
        await Db.QueryAsync(s => s.SearchSupplierHistoryFiltersAsync(new()));
    }

    /// <summary>用不同合法编码验证字面匹配和稳定分页；无结果和超末页仍返回正确计数。</summary>
    [TestMethod]
    public async Task SearchIsLiteralAndPaginationStable()
    {
        // 正式结构禁止未删除客户同码；保留共同搜索前缀，以合法身份验证两页及重复查询。
        await Db.ExecuteAsync("""
            INSERT INTO public.customer(uid,customer_code,customer_name,is_active,is_deleted,doc_relate_no,deposit_rate,lead_time_days) VALUES (gen_random_uuid(),'C-%_[~','特殊字符',TRUE,FALSE,'TEST',0,0),
              (gen_random_uuid(),'C-other-A','普通客户',TRUE,FALSE,'TEST',0,0),(gen_random_uuid(),'C-other-B','另一客户',TRUE,FALSE,'TEST',0,0);
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,is_active,is_deleted,doc_relate_no) VALUES (gen_random_uuid(),'S-%_[~','特殊字符',TRUE,FALSE,'TEST'),(gen_random_uuid(),'S-other','普通供应商',TRUE,FALSE,'TEST');
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
        Assert.AreEqual("C-other-A", first.Items.Single().CustomerCode);
        Assert.AreEqual("C-other-B", second.Items.Single().CustomerCode);
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
        await Db.ExecuteAsync("INSERT INTO public.customer(uid,customer_code,customer_name,is_active,is_deleted,doc_relate_no,deposit_rate,lead_time_days) VALUES (gen_random_uuid(),'C-change','状态变更客户',TRUE,FALSE,'TEST',0,0);");
        var before = await Db.QueryAsync(s => s.SearchCustomersAsync(new()));
        await Db.ExecuteAsync("UPDATE public.customer SET is_active=FALSE;");
        Assert.AreEqual(0, (await Db.QueryAsync(s => s.SearchCustomersAsync(new()))).TotalCount);
        var history = await Db.QueryAsync(s => s.SearchCustomerHistoryFiltersAsync(new()));
        Assert.AreEqual(before.Items.Single().Uid, history.Items.Single().Uid);
        Assert.IsFalse(history.Items.Single().IsActive);
    }
}
