using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Repositories.Products.BomManagement;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实 Dapper 的管理查询与窄更新离线回归；记录 SQL、参数和结果，不访问实例。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>有效子件筛选在数据库分页前执行，count/list 与子件投影同口径；取消勾选不限制空组成或待建立项。</summary>
    [TestMethod]
    [DataRow(null, true)] [DataRow(true, true)] [DataRow(false, true)]
    [DataRow(null, false)] [DataRow(true, false)] [DataRow(false, false)]
    public async Task BomManagement_HasComponentsFiltersCountAndPage(bool? bound, bool hasComponents)
    {
        using var fixture = new Fixture();
        var count = new DataTable(); count.Columns.Add("Total", typeof(int)); count.Rows.Add(0);
        var rows = new DataTable(); rows.Columns.Add("Uid", typeof(Guid));
        fixture.Connection.ResultSets = [count, rows];
        var page = await new PostgreSqlBomManagementQueryRepository(DictionaryContext(fixture)).SearchProductsAsync(new()
        { IsBound = bound, HasComponents = hasComponents, Keyword = "custom%", PageIndex = 2, PageSize = 5 }, default);
        Assert.AreEqual(0, page.TotalCount); Assert.HasCount(0, page.Items);
        Assert.AreEqual(5, fixture.Connection.Values["Offset"]);
        Assert.AreEqual("%custom~%%", fixture.Connection.Values["Keyword"]);
        var sql = fixture.Connection.Text;
        const string projection = "CASE WHEN p.bom_version>0 AND p.bom_no IS NOT NULL AND btrim(p.bom_no,' ')<>'' THEN COALESCE(components.current_count,0) ELSE 0 END";
        var predicate = "(" + projection + ")>0";
        StringAssert.Contains(sql, projection + " AS \"ComponentCount\"");
        Assert.AreEqual(hasComponents ? 2 : 0, sql.Split(predicate, StringSplitOptions.None).Length - 1,
            "有效子件筛选必须同时出现在总数和分页中；false 不代表仅查空组成。");
        var whereParts = sql.Split("WHERE p.is_deleted=FALSE", StringSplitOptions.None);
        Assert.AreEqual(3, whereParts.Length);
        var countWhere = whereParts[1].Split(';')[0].Trim();
        var pageWhere = whereParts[2].Split("ORDER BY", StringSplitOptions.None)[0].Trim();
        Assert.AreEqual(countWhere, pageWhere, "总数和页数据必须使用完全相同的组合筛选。");
        StringAssert.Contains(sql, "count(*) FILTER(WHERE bom_version>0)::integer AS current_count");
        StringAssert.Contains(sql, "FROM public.bom WHERE is_deleted=FALSE GROUP BY");
        Assert.IsFalse(countWhere.Contains("stored_count", StringComparison.Ordinal), "不能用仅有历史或无效行的物理数量判定有效子件。");
        if (bound == false) StringAssert.Contains(countWhere, "NOT COALESCE((p.bom_version>0");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>固定需要 BOM 的成品，count/list 同范围且稳定排序；真实共享引用统计不随列表资格缩减。</summary>
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
        Assert.AreEqual(2, sql.Split("WHERE p.is_deleted=FALSE AND p.material_type='成品' AND p.bom_version>0", StringSplitOptions.None).Length - 1,
            "计数和分页必须固定筛选需要 BOM 的成品，不能只在页数据上过滤。");
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

    /// <summary>默认列表映射待建立和已有绑定状态，空编号保留未绑定事实，总数来自同一查询命令。</summary>
    [TestMethod]
    public async Task BomManagement_DefaultPagePreservesPendingAndBoundStates()
    {
        using var fixture = new Fixture();
        var count = new DataTable(); count.Columns.Add("Total", typeof(int)); count.Rows.Add(2);
        var rows = new DataTable();
        rows.Columns.Add("Uid", typeof(Guid)); rows.Columns.Add("Sku", typeof(string));
        rows.Columns.Add("BomVersion", typeof(int)); rows.Columns.Add("BomNo", typeof(string));
        rows.Columns.Add("IsBound", typeof(bool)); rows.Columns.Add("ComponentCount", typeof(int));
        rows.Columns.Add("SharedProductCount", typeof(int));
        rows.Rows.Add(Guid.NewGuid(), "PENDING", 7, DBNull.Value, false, 0, 0);
        rows.Rows.Add(Guid.NewGuid(), "BOUND", 3, "SHARED", true, 0, 2);
        fixture.Connection.ResultSets = [count, rows];
        var page = await new PostgreSqlBomManagementQueryRepository(DictionaryContext(fixture)).SearchProductsAsync(new(), default);
        Assert.AreEqual(2, page.TotalCount); Assert.HasCount(2, page.Items);
        var pending = page.Items.Single(item => item.Sku == "PENDING");
        Assert.AreEqual(7, pending.BomVersion); Assert.IsNull(pending.BomNo); Assert.IsFalse(pending.IsBound);
        Assert.AreEqual(0, pending.ComponentCount); Assert.AreEqual(0, pending.SharedProductCount);
        var bound = page.Items.Single(item => item.Sku == "BOUND");
        Assert.IsTrue(bound.IsBound); Assert.AreEqual(0, bound.ComponentCount); Assert.AreEqual(2, bound.SharedProductCount);
        var where = fixture.Connection.Text.Split("WHERE p.is_deleted=FALSE", StringSplitOptions.None)[1];
        StringAssert.StartsWith(where, " AND p.material_type='成品' AND p.bom_version>0");
        Assert.IsFalse(where.StartsWith(" AND p.material_type='成品' AND p.bom_version>0 AND", StringComparison.Ordinal),
            "默认范围不能追加必须有编号或组成的条件，待建立必须保留。");
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
        var product = new Product { Sku = "UNCHANGED", ProductMasterId = Guid.NewGuid(), BomNo = "NEW", BomVersion = 7, Version = 7, Note = "保留", USDPrice = 12m };
        var repository = new PostgreSqlProductBomBindingRepository(DictionaryContext(fixture));
        var result = await repository.BindAsync(product, 7, default);
        Assert.IsTrue(result.Success); Assert.AreEqual(8, result.NewVersion); Assert.AreEqual(7, result.OldVersion);
        var sql = fixture.Connection.Text;
        StringAssert.Contains(sql, "bom_no=@BomNo,version=version+1");
        Assert.IsFalse(sql.Contains("SET bom_version", StringComparison.Ordinal) || sql.Contains(",bom_version=", StringComparison.Ordinal),
            "建立编号保留既有正标记，不应把全部正数改为一。");
        StringAssert.Contains(sql, "AND material_type='成品' AND bom_version>0");
        StringAssert.Contains(sql, "WHERE uid=@Uid AND version=@Version AND is_deleted=FALSE");
        Assert.IsFalse(sql.Contains("product_master_id", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(new[] { "Uid", "Version", "BomNo", "UpdatedBy", "UpdatedDate" }, fixture.Connection.Values.Keys.ToArray());
        Assert.AreEqual("保留", product.Note); Assert.AreEqual(12m, product.USDPrice);
        Assert.AreEqual(7, product.BomVersion);
        fixture.Connection.Rows.Rows.Clear();
        Assert.IsFalse((await repository.BindAsync(product, 7, default)).Success);
        fixture.Session.Transaction!.Dispose(); fixture.Session.Transaction = null;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => repository.BindAsync(product, 7, default));
    }
}
