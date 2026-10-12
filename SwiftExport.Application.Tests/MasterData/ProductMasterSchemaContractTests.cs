using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.DatabaseSchema;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证主产品实体命名和全新数据库约束保持一致。</summary>
[TestClass]
public sealed class ProductMasterSchemaContractTests
{
    /// <summary>父级默认装率在生成结构中允许为空，但实际包装装率继续必填且受正数约束保护。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldAllowUnsetParentPcsAndRequireActualPackagePcs()
    {
        var sql = new PostgreSqlSchemaGenerator().Generate(typeof(ProductMaster).Assembly);
        StringAssert.Contains(sql, "\"default_pcs_per_package\" integer NULL");
        StringAssert.Contains(sql, "CONSTRAINT \"ck_product_master_pcs\" CHECK (\"default_pcs_per_package\" > 0)");
        StringAssert.Contains(sql, "\"pcs_per_package\" integer NOT NULL");
        StringAssert.Contains(sql, "CONSTRAINT \"ck_product_package_pcs\" CHECK (\"pcs_per_package\" > 0)");
    }

    /// <summary>验证已锁定的四个逻辑关系字段命名。</summary>
    [TestMethod]
    public void Entities_ShouldExposeCanonicalRelationshipNames()
    {
        Assert.IsNotNull(typeof(ProductMaster).GetProperty(nameof(ProductMaster.MasterSku)));
        Assert.IsNotNull(typeof(ProductMaster).GetProperty(nameof(ProductMaster.DefaultSupplierId)));
        Assert.IsNotNull(typeof(ProductNeutral).GetProperty(nameof(ProductNeutral.ProductMasterId)));
        Assert.IsNotNull(typeof(ProductNeutral).GetProperty(nameof(ProductNeutral.ColorId)));
        Assert.IsNotNull(typeof(ProductNeutral).GetProperty(nameof(ProductNeutral.IsMainImage)));
        Assert.IsNull(typeof(ProductMaster).GetProperty("MasterProductCode"));
        Assert.IsNull(typeof(ProductNeutral).GetProperty("MasterProductUid"));
    }

    /// <summary>验证 PG 实体生成基线包含唯一、查询及数值完整性约束，不引入物理外键。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldCreateProductMasterAggregateConstraints()
    {
        var sql = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.PostgreSql.Schema.preview.sql"));
        StringAssert.Contains(sql, "CREATE TABLE \"public\".\"product_neutral\"");
        StringAssert.Contains(sql, "ux_product_master_master_sku");
        StringAssert.Contains(sql, "ux_product_neutral_product_code");
        StringAssert.Contains(sql, "ix_product_master_category");
        StringAssert.Contains(sql, "ix_product_master_default_supplier");
        StringAssert.Contains(sql, "ix_product_neutral_product_master");
        StringAssert.Contains(sql, "ix_product_neutral_color");
        StringAssert.Contains(sql, "ck_product_master_pcs");
        StringAssert.Contains(sql, "ck_product_neutral_exchange_rate");
        var start = sql.IndexOf("CREATE TABLE \"public\".\"product_master\"", StringComparison.Ordinal);
        var end = sql.IndexOf("CREATE TABLE \"public\".\"product_package\"", start, StringComparison.Ordinal);
        var productMasterAggregate = sql.Substring(start, end - start);
        Assert.IsFalse(productMasterAggregate.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase), "主产品关系按当前约定使用逻辑外键。");
    }

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}
