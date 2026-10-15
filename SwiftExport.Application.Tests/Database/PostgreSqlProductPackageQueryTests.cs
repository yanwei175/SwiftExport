using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Infrastructure.Repositories.ForExcel;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Shipping;

namespace SwiftExport.Application.Tests.Database;

/// <summary>执行三个包装查询的参数与空值映射契约；离线夹具不执行 SQL，类型筛选实证另由 PG 矩阵负责。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>Excel 采购候选接纳包材，同时保留客户关系、采购供货方式和启用条件，沿用外层事务。</summary>
    [TestMethod]
    public async Task Packaging_ExcelPurchaseCandidateIncludesMaterialTypeWithoutChangingEligibility()
    {
        using var fixture = new Fixture();
        await new PostgreSqlForExcelPurchaseOrderRepository(DictionaryContext(fixture))
            .MatchProductsAsync(Guid.NewGuid(), ["PACK"], default);
        Assert.AreEqual(MaterialTypesEnum.包材, fixture.Connection.Values["PackagingMaterialType"]);
        StringAssert.Contains(fixture.Connection.Text, "IN ('成品','零件','客供件',@PackagingMaterialType)");
        StringAssert.Contains(fixture.Connection.Text, "cp.is_active=TRUE AND p.is_active=TRUE");
        StringAssert.Contains(fixture.Connection.Text, "rtrim(p.supply_type,' ') COLLATE public.erp_dictionary_ci='采购'");
        StringAssert.Contains(fixture.Connection.Text, "cp.customer_id=@CustomerId AND cp.is_deleted=FALSE");
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }

    /// <summary>分页和详情只关联当前物料的有效 Carton，不采用装率排序、默认项或空值补量。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Packaging_CatalogUsesOnlyCartonAndKeepsNullableMeasures(bool hasCarton)
    {
        using var fixture = new Fixture();
        var id = Guid.NewGuid();
        var rows = fixture.Connection.Rows;
        rows.Columns.Add("Sku", typeof(string));
        rows.Columns.Add("Uid", typeof(Guid));
        foreach (var name in new[] { "PcsOfCarton", "NwOfCarton", "GwOfCarton", "CbmOfCarton" })
            rows.Columns.Add(name, typeof(decimal));
        object Value(decimal value) => hasCarton ? value : DBNull.Value;
        rows.Rows.Add("PACKED", id, Value(12m), Value(20m), Value(22m), Value(0.4m));
        var repository = new PostgreSqlProductRepository(DictionaryContext(fixture));
        var page = (await repository.GetPagedAsync(new())).Items.Single();
        var detail = await repository.GetDetailAsync(id);
        Assert.IsNotNull(detail);
        Assert.AreEqual(hasCarton ? 12m : (decimal?)null, page.PcsOfCarton);
        Assert.AreEqual(page.PcsOfCarton, detail.PcsOfCarton);
        Assert.AreEqual(hasCarton ? 20m : (decimal?)null, detail.NwOfCarton);
        Assert.AreEqual(hasCarton ? 22m : (decimal?)null, detail.GwOfCarton);
        Assert.AreEqual(hasCarton ? 0.4m : (decimal?)null, detail.CbmOfCarton);
        Assert.AreEqual(detail.NwOfCarton, page.NwOfCarton);
        Assert.AreEqual(detail.GwOfCarton, page.GwOfCarton);
        Assert.AreEqual(detail.CbmOfCarton, page.CbmOfCarton);
        foreach (var command in fixture.Connection.Commands.Where(command => command.Sql.Contains("public.product_package", StringComparison.Ordinal)))
        {
            Assert.AreEqual(ProductPackageTypeCodes.Carton, command.Values["CartonPackageType"]);
            StringAssert.Contains(command.Sql, "pkg.owner_product_id=p.uid");
            StringAssert.Contains(command.Sql, "pkg.package_type=@CartonPackageType AND pkg.is_deleted=FALSE");
            Assert.IsFalse(command.Sql.Contains("is_default", StringComparison.Ordinal));
            Assert.IsFalse(command.Sql.Contains("pcs_per_package DESC", StringComparison.Ordinal));
            Assert.IsFalse(command.Sql.Contains("COALESCE(pkg.", StringComparison.Ordinal));
            Assert.AreSame(fixture.Session.Transaction, command.Transaction);
        }
    }

    /// <summary>箱单来源绑定同一 Carton 参数，保持非标排除、来源匹配和空包装数据，不默认零或一。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Packaging_SourceKeepsCartonNonStandardAndNullBoundaries(bool hasCarton)
    {
        using var fixture = new Fixture();
        var id = Guid.NewGuid();
        fixture.Connection.Rows.Columns.Add("GrossDemandId", typeof(Guid));
        foreach (var name in new[] { "PcsPerPackage", "NwPerPackage", "GwPerPackage", "CbmPerPackage" })
            fixture.Connection.Rows.Columns.Add(name, typeof(decimal));
        object Value(decimal value) => hasCarton ? value : DBNull.Value;
        fixture.Connection.Rows.Rows.Add(id, Value(12m), Value(20m), Value(22m), Value(0.4m));
        var repository = new PostgreSqlPackingListRepository(DictionaryContext(fixture));
        var result = (await repository.GetSourceEnrichmentsAsync([id])).Single();
        Assert.AreEqual(hasCarton ? 12m : (decimal?)null, result.PcsPerPackage);
        Assert.AreEqual(hasCarton ? 20m : (decimal?)null, result.NwPerPackage);
        Assert.AreEqual(hasCarton ? 22m : (decimal?)null, result.GwPerPackage);
        Assert.AreEqual(hasCarton ? 0.4m : (decimal?)null, result.CbmPerPackage);
        var sql = fixture.Connection.Text;
        Assert.AreEqual(ProductPackageTypeCodes.Carton, fixture.Connection.Values["CartonPackageType"]);
        StringAssert.Contains(sql, "g.is_non_standard=FALSE AND pp.owner_product_id=g.product_id");
        StringAssert.Contains(sql, "pp.is_deleted=FALSE AND pp.package_type=@CartonPackageType");
        StringAssert.Contains(sql, "sol.product_id=g.product_id AND sol.is_non_standard=g.is_non_standard");
        StringAssert.Contains(sql, "ash.customer_id=g.customer_id AND ash.is_deleted=FALSE");
        StringAssert.Contains(sql, "asl.header_id=ash.uid AND asl.is_deleted=FALSE");
        StringAssert.Contains(sql, "WHERE g.uid=ANY(@Ids) AND g.is_deleted=FALSE");
        Assert.IsFalse(sql.Contains("is_default", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("COALESCE(pkg.", StringComparison.Ordinal));
        Assert.AreSame(fixture.Session.Transaction, fixture.Connection.LastTransaction);
    }
}
