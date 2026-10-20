using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Infrastructure.Repositories.MasterData;

namespace SwiftExport.Application.Tests.Database;

/// <summary>真实 Dapper 的无网络采购候选检查，复用冻结夹具而不改写其行为；行资格由独立实库测试验证。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>五入口保持字面关键词、分离计数和分页、长整型偏移及外层事务；仓储不提交或回滚。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public async Task ProcurementQuery_FivePagesKeepLiteralPatternAndTransaction(int kind)
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        var uid = Guid.NewGuid(); f.Connection.Rows.Rows.Add(uid);
        var repo = new PostgreSqlProcurementMasterDataQueryRepository(DictionaryContext(f));
        var request = new ProcurementProductSearchRequest { Keyword = "A%_[~", PageIndex = 3, PageSize = 7 };
        var ids = await ProcurementPage(repo, kind, request, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { uid }, ids);
        Assert.HasCount(2, f.Connection.Commands);
        StringAssert.StartsWith(f.Connection.Commands[0].Sql, "SELECT COUNT(*) ");
        StringAssert.Contains(f.Connection.Text, "LIMIT @Take OFFSET @Skip");
        Assert.AreEqual("%A~%~_[~~%", f.Connection.Values["Keyword"]);
        Assert.AreEqual(14L, f.Connection.Values["Skip"]); Assert.AreEqual(7, f.Connection.Values["Take"]);
        Assert.AreEqual(Guid.Empty, f.Connection.Values["EmptyUid"]);
        Assert.IsTrue(f.Connection.Commands.All(command => ReferenceEquals(command.Transaction, f.Session.Transaction)));
        Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks); Assert.AreEqual(0, f.Connection.TransactionDisposals);
        Assert.IsFalse(f.Connection.Text.Contains("dbo.", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Text, "COLLATE public.erp_dictionary_ci");
    }

    /// <summary>五入口的无事务及预取消均零命令失败，不能伪装成空成功结果或创建隐式事务。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)]
    public async Task ProcurementQuery_RejectsMissingTransactionAndPreCancellation(int kind)
    {
        using var f = new Fixture();
        var repo = new PostgreSqlProcurementMasterDataQueryRepository(DictionaryContext(f));
        var original = f.Session.Transaction; f.Session.Transaction = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ProcurementPage(repo, kind, new(), CancellationToken.None));
        f.Session.Transaction = original;
        await Assert.ThrowsAsync<OperationCanceledException>(() => ProcurementPage(repo, kind, new(), new CancellationToken(true)));
        Assert.HasCount(0, f.Connection.Commands);
    }

    /// <summary>新增资格和历史范围保持差别；Unicode 空白集合精确传参，历史查询不追加启用或非空校验。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(3)] [DataRow(4)]
    public async Task ProcurementQuery_PartyEligibilityAndHistoryStayDistinct(int kind)
    {
        using var f = new Fixture();
        await ProcurementPage(new(DictionaryContext(f)), kind, new(), CancellationToken.None);
        var sql = f.Connection.Text;
        StringAssert.Contains(sql, "COALESCE(m.is_deleted,FALSE)=FALSE AND m.uid<>@EmptyUid");
        Assert.AreEqual(kind < 2, sql.Contains("m.is_active=TRUE", StringComparison.Ordinal));
        Assert.AreEqual(kind < 2, sql.Contains("btrim(", StringComparison.Ordinal));
        if (kind == 0) Assert.IsFalse(sql.Contains("btrim(m.customer_name", StringComparison.Ordinal));
        if (kind == 1) StringAssert.Contains(sql, "btrim(m.supplier_name,@Whitespace)");
        if (kind >= 3) StringAssert.Contains(sql, "m.is_active AS \"IsActive\"");
        if (kind < 2) Assert.AreEqual("\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000", f.Connection.Values["Whitespace"]);
    }

    /// <summary>实际供应商分支只补充关系且不筛其启用状态；无供应商不引用默认型号，客户关系按启用和 UUID 优先。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ProcurementQuery_ProductRootAndRelationDifferences(bool withSupplier)
    {
        using var f = new Fixture();
        var customer = Guid.NewGuid(); var supplier = Guid.NewGuid();
        await new PostgreSqlProcurementMasterDataQueryRepository(DictionaryContext(f)).SearchProductsAsync(
            new() { CustomerId = customer, SupplierId = withSupplier ? supplier : null }, CancellationToken.None);
        var sql = f.Connection.Text;
        StringAssert.Contains(sql, "FROM public.product p");
        StringAssert.Contains(sql, "relation.is_active=TRUE");
        StringAssert.Contains(sql, "ORDER BY relation.uid LIMIT 1");
        // 两个关系表的软删除列均为 NOT NULL；保持与部分索引一致，防止逐产品查询退化成重复全表扫描。
        foreach (var command in f.Connection.Commands)
        {
            StringAssert.Contains(command.Sql, "relation.is_deleted=FALSE");
            Assert.IsFalse(command.Sql.Contains("COALESCE(relation.is_deleted", StringComparison.Ordinal));
        }
        StringAssert.Contains(sql, "p.supply_type COLLATE public.erp_dictionary_ci = @PurchaseSupply");
        Assert.AreEqual("采购", f.Connection.Values["PurchaseSupply"]);
        Assert.AreEqual(customer, f.Connection.Values["CustomerId"]);
        Assert.IsFalse(sql.Contains("default_supplier", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("material_type=", StringComparison.Ordinal));
        if (withSupplier)
        {
            StringAssert.Contains(sql, "FROM public.supplier_product relation");
            var supplierSection = sql[(sql.IndexOf("FROM public.supplier_product", StringComparison.Ordinal))..];
            Assert.IsFalse(supplierSection.Contains("relation.is_active", StringComparison.Ordinal));
            Assert.AreEqual(supplier, f.Connection.Values["SupplierId"]);
        }
        else
        {
            Assert.IsFalse(sql.Contains("public.supplier_product", StringComparison.Ordinal));
            StringAssert.Contains(sql, "CAST('' AS text) AS \"SupplierSku\"");
            StringAssert.Contains(sql, "CAST(NULL AS numeric(18,6)) AS \"UnitPrice\"");
        }
    }

    /// <summary>真实映射保留全部显示字段、原单位及精确 nullable 价格；分页总数和元信息不会丢失。</summary>
    [TestMethod]
    public async Task ProcurementQuery_ProductProjectionMapsAllFieldsAndNullablePrices()
    {
        using var f = new Fixture();
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        string[] names = ["Sku", "MainImage", "CustomerSku", "LinkSku", "SupplierSku", "DescriptionZh", "DescriptionEn", "FinishZh", "FeaturesZh", "MaterialType", "SupplyType", "Unit"];
        foreach (var name in names) f.Connection.Rows.Columns.Add(name, typeof(string));
        f.Connection.Rows.Columns.Add("UnitPrice", typeof(decimal)); f.Connection.Rows.Columns.Add("TaxPrice", typeof(decimal));
        var uid = Guid.NewGuid();
        f.Connection.Rows.Rows.Add(new object[] { uid }.Concat(names.Cast<object>()).Concat(new object[] { 1.234567m, DBNull.Value }).ToArray());
        var page = await new PostgreSqlProcurementMasterDataQueryRepository(DictionaryContext(f)).SearchProductsAsync(
            new() { SupplierId = Guid.NewGuid(), PageIndex = 2, PageSize = 9 }, CancellationToken.None);
        var row = page.Items.Single(); Assert.AreEqual(uid, row.Uid);
        foreach (var name in names) Assert.AreEqual(name, typeof(ProcurementProductCandidateDto).GetProperty(name)!.GetValue(row));
        Assert.AreEqual(1.234567m, row.UnitPrice); Assert.IsNull(row.TaxPrice);
        Assert.AreEqual(1, page.TotalCount); Assert.AreEqual(2, page.PageIndex); Assert.AreEqual(9, page.PageSize);
    }

    /// <summary>四个双方投影通过实际 Dapper 恢复编码、名称及历史停用标志，避免只验证 SQL 别名。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(3)] [DataRow(4)]
    public async Task ProcurementQuery_PartyProjectionMapsDisplayAndHistoryStatus(int kind)
    {
        using var f = new Fixture();
        var customer = kind is 0 or 3;
        f.Connection.Rows.Columns.Add("Uid", typeof(Guid));
        f.Connection.Rows.Columns.Add(customer ? "CustomerCode" : "SupplierCode", typeof(string));
        f.Connection.Rows.Columns.Add(customer ? "CustomerName" : "SupplierName", typeof(string));
        f.Connection.Rows.Columns.Add("IsActive", typeof(bool));
        var uid = Guid.NewGuid(); f.Connection.Rows.Rows.Add(uid, "编码", "名称", false);
        var repo = new PostgreSqlProcurementMasterDataQueryRepository(DictionaryContext(f));
        object row = kind switch
        {
            0 => (await repo.SearchCustomersAsync(new(), default)).Items.Single(),
            1 => (await repo.SearchSuppliersAsync(new(), default)).Items.Single(),
            3 => (await repo.SearchCustomerHistoryFiltersAsync(new(), default)).Items.Single(),
            4 => (await repo.SearchSupplierHistoryFiltersAsync(new(), default)).Items.Single(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.AreEqual(uid, row.GetType().GetProperty("Uid")!.GetValue(row));
        Assert.AreEqual("编码", row.GetType().GetProperty(customer ? "CustomerCode" : "SupplierCode")!.GetValue(row));
        Assert.AreEqual("名称", row.GetType().GetProperty(customer ? "CustomerName" : "SupplierName")!.GetValue(row));
        if (kind >= 3) Assert.AreEqual(false, row.GetType().GetProperty("IsActive")!.GetValue(row));
    }

    /// <summary>空关键词不生成通配符；仓储保留服务传入的文本，不再次 Trim，使用 bigint 偏移避免整数乘法溢出。</summary>
    [TestMethod]
    [DataRow(null)] [DataRow("")] [DataRow(" A ")]
    public async Task ProcurementQuery_EmptyKeywordAndUnmodifiedRequest(string? keyword)
    {
        using var f = new Fixture();
        var request = new PagedRequestBase { Keyword = keyword, PageIndex = int.MaxValue, PageSize = 100 };
        await new PostgreSqlProcurementMasterDataQueryRepository(DictionaryContext(f)).SearchCustomersAsync(request, CancellationToken.None);
        Assert.AreEqual(string.IsNullOrEmpty(keyword) ? DBNull.Value : "% A %", f.Connection.Values["Keyword"]);
        Assert.AreEqual(214748364600L, f.Connection.Values["Skip"]); Assert.AreEqual(keyword, request.Keyword);
    }

    /// <summary>在统一断言中选择五个真实接口入口，返回各自 DTO 身份而不绕过 Dapper 查询。</summary>
    private static async Task<Guid[]> ProcurementPage(PostgreSqlProcurementMasterDataQueryRepository repo, int kind,
        ProcurementProductSearchRequest request, CancellationToken token) => kind switch
    {
        0 => (await repo.SearchCustomersAsync(request, token)).Items.Select(row => row.Uid).ToArray(),
        1 => (await repo.SearchSuppliersAsync(request, token)).Items.Select(row => row.Uid).ToArray(),
        2 => (await repo.SearchProductsAsync(request, token)).Items.Select(row => row.Uid).ToArray(),
        3 => (await repo.SearchCustomerHistoryFiltersAsync(request, token)).Items.Select(row => row.Uid).ToArray(),
        4 => (await repo.SearchSupplierHistoryFiltersAsync(request, token)).Items.Select(row => row.Uid).ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
