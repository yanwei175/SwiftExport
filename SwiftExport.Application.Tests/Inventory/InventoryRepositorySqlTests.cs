using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>通过真实 InventoryRepository 和隔离 PostgreSQL 验证库存查询及 LIKE 字面量转义。</summary>
[TestClass]
[DoNotParallelize]
public sealed class InventoryRepositorySqlTests
{
    /// <summary>关联本例完整事实导出。</summary>
    public TestContext TestContext { get; set; } = default!;
    private InventoryRepositorySqlFixture? fixture;
    private InventoryRepositorySqlFixture Db => fixture!;

    /// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
    [TestInitialize]
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_LEGACY_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_PG_LEGACY_TESTS=1 后运行库存查询隔离 SQL 测试。");
        fixture = new InventoryRepositorySqlFixture();
        await fixture.InitializeAsync(TestContext);
    }

    /// <summary>获批固定PG测试库中串行执行；每例完整导出后仅重置该库public结构。</summary>
    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (fixture is not null) await fixture.DisposeAsync();
    }

    /// <summary>余额 count/page SQL 支持空关键词、普通文本和所有 LIKE 特殊字符的字面量查询。</summary>
    [TestMethod]
    public async Task SearchBalances_ShouldExecuteCountAndPageWithLiteralKeywords()
    {
        var page = await Db.QueryAsync(repository => repository.SearchBalancesAsync(new InventoryBalanceSearchRequest
        {
            Keyword = " ", PageIndex = 2, PageSize = 2
        }));
        Assert.AreEqual(5, page.TotalCount);
        Assert.AreEqual(2, page.Items.Count);

        foreach (var keyword in new[] { "普通", "%", "_", "[", "\\" })
        {
            var result = await Db.QueryAsync(repository => repository.SearchBalancesAsync(new InventoryBalanceSearchRequest
            {
                Keyword = keyword, PageIndex = 1, PageSize = 10
            }));
            Assert.AreEqual(1, result.TotalCount, $"余额关键词 {keyword} 应按字面量匹配。 ");
            Assert.AreEqual(1, result.Items.Count);
        }

        var detail = await Db.QueryAsync(repository => repository.GetBalanceDtoAsync(Db.BalanceIds[0]));
        Assert.IsNotNull(detail);
        Assert.AreEqual("SUP-QUERY", detail.SupplierCode);
        Assert.AreEqual("库存查询供应商", detail.SupplierName);
        Assert.AreEqual("images/product-1.png", detail.ImagePath);

        var deletedProduct = await Db.QueryAsync(repository => repository.GetBalanceDtoAsync(Db.BalanceIds[1]));
        var missingProduct = await Db.QueryAsync(repository => repository.GetBalanceDtoAsync(Db.BalanceIds[2]));
        Assert.IsNotNull(deletedProduct);
        Assert.IsNotNull(missingProduct);
        Assert.IsNull(deletedProduct.ImagePath, "已删除产品不得提供当前主图，但余额必须保留。");
        Assert.IsNull(missingProduct.ImagePath, "缺失产品不得导致余额被内连接过滤。");

        var allRows = await Db.QueryAsync(repository => repository.SearchBalancesAsync(new InventoryBalanceSearchRequest
        {
            PageIndex = 1, PageSize = 10
        }));
        Assert.AreEqual(5, allRows.TotalCount);
        Assert.AreEqual(5, allRows.Items.Count);
        Assert.AreEqual("images/product-1.png", allRows.Items.Single(item => item.Uid == Db.BalanceIds[0]).ImagePath);
        Assert.IsNull(allRows.Items.Single(item => item.Uid == Db.BalanceIds[1]).ImagePath);
        Assert.IsNull(allRows.Items.Single(item => item.Uid == Db.BalanceIds[2]).ImagePath);
    }

    /// <summary>流水 count/page SQL 对关键词和来源单号都按字面量处理 LIKE 特殊字符。</summary>
    [TestMethod]
    public async Task SearchLedger_ShouldExecuteCountAndPageWithLiteralKeywordsAndDocumentNumbers()
    {
        var page = await Db.QueryAsync(repository => repository.SearchLedgerAsync(new InventoryLedgerSearchRequest
        {
            Keyword = null!, SourceDocumentNo = null!, PageIndex = 2, PageSize = 2
        }));
        Assert.AreEqual(5, page.TotalCount);
        Assert.AreEqual(2, page.Items.Count);

        foreach (var keyword in new[] { "普通", "%", "_", "[", "\\" })
        {
            var keywordResult = await Db.QueryAsync(repository => repository.SearchLedgerAsync(new InventoryLedgerSearchRequest
            {
                Keyword = keyword, SourceDocumentNo = null!, PageIndex = 1, PageSize = 10
            }));
            Assert.AreEqual(1, keywordResult.TotalCount, $"流水关键词 {keyword} 应按字面量匹配。 ");

            var documentResult = await Db.QueryAsync(repository => repository.SearchLedgerAsync(new InventoryLedgerSearchRequest
            {
                Keyword = null!, SourceDocumentNo = keyword, PageIndex = 1, PageSize = 10
            }));
            Assert.AreEqual(1, documentResult.TotalCount, $"来源单号 {keyword} 应按字面量匹配。 ");
            Assert.AreEqual(1, documentResult.Items.Count);
        }
    }
}
