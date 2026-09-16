using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证主产品实体命名和全新数据库约束保持一致。</summary>
[TestClass]
public sealed class ProductMasterSchemaContractTests
{
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

    /// <summary>验证实体生成基线包含唯一、查询及数值完整性约束。</summary>
    [TestMethod]
    public void GeneratedSchema_ShouldCreateProductMasterAggregateConstraints()
    {
        var sql = File.ReadAllText(Path.Combine(Root(), "SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql"));
        StringAssert.Contains(sql, "CREATE TABLE [dbo].[ProductNeutral]");
        StringAssert.Contains(sql, "UX_ProductMaster_MasterSku");
        StringAssert.Contains(sql, "UX_ProductNeutral_ProductCode");
        StringAssert.Contains(sql, "IX_ProductMaster_Category");
        StringAssert.Contains(sql, "IX_ProductMaster_DefaultSupplier");
        StringAssert.Contains(sql, "IX_ProductNeutral_ProductMaster");
        StringAssert.Contains(sql, "IX_ProductNeutral_Color");
        StringAssert.Contains(sql, "CK_ProductMaster_Pcs");
        StringAssert.Contains(sql, "CK_ProductNeutral_ExchangeRate");
        var start = sql.IndexOf("CREATE TABLE [dbo].[ProductMaster]", StringComparison.Ordinal);
        var end = sql.IndexOf("CREATE TABLE [dbo].[ProductPackage]", start, StringComparison.Ordinal);
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
