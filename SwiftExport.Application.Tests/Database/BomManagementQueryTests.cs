using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products.BomManagement;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实 Dapper 的管理查询与窄更新离线回归；记录 SQL、参数和结果，不访问实例。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>筛选使用参数化条件、固定成品和稳定排序；计数独立聚合物理行，不随共享父放大。</summary>
    [TestMethod]
    [DataRow(null)] [DataRow(true)] [DataRow(false)]
    public async Task BomManagement_PageMapsAggregatedCountsAndFilters(bool? bound)
    {
        using var fixture = new Fixture();
        var count = new DataTable(); count.Columns.Add("Total", typeof(int)); count.Rows.Add(37);
        var rows = new DataTable();
        rows.Columns.Add("Uid", typeof(Guid)); rows.Columns.Add("Sku", typeof(string));
        rows.Columns.Add("ComponentCount", typeof(int)); rows.Columns.Add("SharedProductCount", typeof(int));
        rows.Columns.Add("IsBound", typeof(bool)); rows.Rows.Add(Guid.NewGuid(), "SKU", 2, 3, true);
        fixture.Connection.ResultSets = [count, rows];
        var category = Guid.NewGuid(); var color = Guid.NewGuid();
        var page = await new PostgreSqlBomManagementQueryRepository(DictionaryContext(fixture)).SearchProductsAsync(new()
        { PageIndex = 3, PageSize = 10, IsBound = bound, Keyword = "a%_", CategoryIds = [category], ColorIds = [color] }, default);
        Assert.AreEqual(37, page.TotalCount); Assert.AreEqual(3, page.PageIndex); Assert.AreEqual(10, page.PageSize);
        Assert.AreEqual(2, page.Items.Single().ComponentCount); Assert.AreEqual(3, page.Items.Single().SharedProductCount);
        Assert.AreEqual(20, fixture.Connection.Values["Offset"]); Assert.AreEqual(10, fixture.Connection.Values["PageSize"]);
        Assert.AreEqual("%a~%~_%", fixture.Connection.Values["Keyword"]);
        CollectionAssert.AreEqual(new[] { category }, (Guid[])fixture.Connection.Values["CategoryIds"]!);
        CollectionAssert.AreEqual(new[] { color }, (Guid[])fixture.Connection.Values["ColorIds"]!);
        var sql = fixture.Connection.Text;
        StringAssert.Contains(sql, "p.material_type='成品'"); StringAssert.Contains(sql, "p.is_deleted=FALSE");
        Assert.AreEqual(2, sql.Split("WHERE p.is_deleted=FALSE AND p.material_type='成品'", StringSplitOptions.None).Length - 1,
            "计数和分页必须使用同一成品筛选，不能只在页数据上过滤。");
        Assert.IsFalse(sql.Contains("p.is_active", StringComparison.Ordinal), "父件资格不增加启用状态过滤。");
        StringAssert.Contains(sql, "ORDER BY p.sku COLLATE public.erp_dictionary_ci,p.uid OFFSET @Offset LIMIT @PageSize");
        StringAssert.Contains(sql, "LEFT JOIN parents ON"); StringAssert.Contains(sql, "LEFT JOIN components ON");
        StringAssert.Contains(sql, "count(*) FILTER(WHERE bom_version>0)");
        var parentCte = sql[..sql.IndexOf(", components", StringComparison.Ordinal)];
        Assert.IsFalse(parentCte.Contains("material_type", StringComparison.Ordinal), "共享人数须包含所有物料类型。");
        Assert.IsFalse(sql.Contains("p.supply_type=", StringComparison.Ordinal), "成品查询不能按供货类型排除。");
        if (bound == false) StringAssert.Contains(sql, "NOT COALESCE((p.bom_version>0");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>编号预览分别映射有效组成和既有存储事实，不能把异常或孤立行当作可绑定结构。</summary>
    [TestMethod]
    public async Task BomManagement_NumberInfoPreservesStorageFacts()
    {
        using var fixture = new Fixture();
        var table = fixture.Connection.Rows;
        table.Columns.Add("BomNo", typeof(string)); table.Columns.Add("BoundProductCount", typeof(int));
        table.Columns.Add("ComponentCount", typeof(int)); table.Columns.Add("StoredComponentCount", typeof(int));
        table.Columns.Add("HasInvalidComponents", typeof(bool)); table.Rows.Add("SHARED", 3, 2, 4, true);
        var info = await new PostgreSqlBomManagementQueryRepository(DictionaryContext(fixture)).GetNumberInfoAsync(" SHARED ", default);
        Assert.AreEqual(3, info.BoundProductCount); Assert.AreEqual(2, info.ComponentCount);
        Assert.AreEqual(4, info.StoredComponentCount); Assert.IsTrue(info.HasInvalidComponents);
        Assert.AreEqual("SHARED", fixture.Connection.Values["BomNo"]);
        StringAssert.Contains(fixture.Connection.Text, "CASE WHEN parents.parent_count>0 THEN");
        StringAssert.Contains(fixture.Connection.Text, "bool_or(bom_version<=0)");
    }

    /// <summary>窄更新的实际 SQL 仅写绑定、版本与审计，主型号及其他资料没有参数和 SET 项。</summary>
    [TestMethod]
    public async Task BomManagement_BindingWritesOnlyBomAndSystemFields()
    {
        using var fixture = new Fixture();
        fixture.Connection.Rows.Columns.Add("version", typeof(int)); fixture.Connection.Rows.Rows.Add(8);
        var product = new Product { Sku = "UNCHANGED", ProductMasterId = Guid.NewGuid(), BomNo = "NEW", Version = 7, Note = "保留", USDPrice = 12m };
        var repository = new PostgreSqlProductBomBindingRepository(DictionaryContext(fixture));
        var result = await repository.BindAsync(product, 7, default);
        Assert.IsTrue(result.Success); Assert.AreEqual(8, result.NewVersion); Assert.AreEqual(7, result.OldVersion);
        var sql = fixture.Connection.Text;
        StringAssert.Contains(sql, "bom_no=@BomNo,bom_version=1,version=version+1");
        StringAssert.Contains(sql, "WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE");
        Assert.IsFalse(sql.Contains("product_master_id", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(new[] { "Uid", "Version", "BomNo", "UpdatedBy", "UpdatedDate" }, fixture.Connection.Values.Keys.ToArray());
        Assert.AreEqual("保留", product.Note); Assert.AreEqual(12m, product.USDPrice);
        fixture.Connection.Rows.Rows.Clear();
        Assert.IsFalse((await repository.BindAsync(product, 7, default)).Success);
        fixture.Session.Transaction!.Dispose(); fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.BindAsync(product, 7, default));
    }
}
