using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证主型号与中性产品分页仓储保持一致的多分类和未分类组合筛选约定。</summary>
[TestClass]
public sealed class ProductCategoryFilterRepositorySqlContractTests
{
    /// <summary>主型号请求必须公开未分类开关，且仓储覆盖分类、未分类及二者组合分支。</summary>
    [TestMethod]
    public void ProductMaster_ShouldSupportCombinedCategoryFilter()
    {
        Assert.IsNotNull(typeof(ProductMasterSearchRequest).GetProperty(nameof(ProductMasterSearchRequest.IncludeUncategorized)));
        AssertRepositoryFilter(
            "ProductMasterRepository.cs",
            "pm.CategoryId IN @CategoryIds",
            "pm.CategoryId=@EmptyGuid OR pm.CategoryId IS NULL",
            "pm.CategoryId IN @CategoryIds OR pm.CategoryId=@EmptyGuid OR pm.CategoryId IS NULL");
    }

    /// <summary>中性产品必须按所属主型号分类应用相同组合语义。</summary>
    [TestMethod]
    public void ProductNeutral_ShouldUseOwningMasterCategoryFilter()
    {
        AssertRepositoryFilter(
            "ProductNeutralManagerRepository.cs",
            "pm.CategoryId IN @CategoryIds",
            "pm.CategoryId=@EmptyGuid OR pm.CategoryId IS NULL",
            "pm.CategoryId IN @CategoryIds OR pm.CategoryId=@EmptyGuid OR pm.CategoryId IS NULL");
    }

    /// <summary>产品分页和 Lookup 必须经 ProductMasterId 读取及筛选分类，不得回读废弃列。</summary>
    [TestMethod]
    public void ProductQueries_ShouldUseOwningMasterCategory()
    {
        var source = ReadRepository("Products/Catalog", "ProductRepository.cs");
        StringAssert.Contains(source, "p.{nameof(Product.ProductMasterId)} = pm.{nameof(ProductMaster.Uid)}");
        StringAssert.Contains(source, "pm.{nameof(ProductMaster.CategoryId)} = @CategoryId");
        StringAssert.Contains(source, "pm.{nameof(ProductMaster.CategoryId)} IN @CategoryIds");
        StringAssert.Contains(source, "N'未分类'");
        StringAssert.Contains(source, "N'分类已失效'");
        Assert.IsFalse(source.Contains("p.{nameof(Product.CategoryId)}", StringComparison.Ordinal));
    }

    /// <summary>客户和供应商产品的分类展示必须通过所属主型号联表得到。</summary>
    [TestMethod]
    public void PartyProductQueries_ShouldUseOwningMasterCategory()
    {
        foreach (var source in new[]
                 {
                     ReadRepository("Customers/CustomerProducts", "CustomerProductRepository.cs"),
                     ReadRepository("Suppliers/SupplierProducts", "SupplierProductRepository.cs")
                 })
        {
            StringAssert.Contains(source, "ProductMaster");
            StringAssert.Contains(source, "pm.CategoryId");
            StringAssert.Contains(source, "N'未分类'");
            StringAssert.Contains(source, "N'分类已失效'");
        }
    }

    /// <summary>删除分类前的引用检查只允许查询主型号表。</summary>
    [TestMethod]
    public void CategoryReferenceCheck_ShouldOnlyUseProductMaster()
    {
        var source = ReadRepository("Products/Categories", "ProductCategoryRepository.cs");
        StringAssert.Contains(source, "ProductMasterTable");
        Assert.IsFalse(source.Contains("GetTableName<Product>()", StringComparison.Ordinal));
    }

    /// <summary>产品分类仅归属主型号，已删除的产品冗余分类字段不得恢复。</summary>
    [TestMethod]
    public void ProductCategoryId_ShouldBeRemovedAndOwnedByProductMaster()
    {
        Assert.IsNull(typeof(Product).GetProperty("CategoryId"));
        Assert.IsNotNull(typeof(ProductMaster).GetProperty(nameof(ProductMaster.CategoryId)));
    }

    /// <summary>检查仓储先清理空标识和重复值，再生成三个参数化分类条件。</summary>
    private static void AssertRepositoryFilter(string fileName, params string[] expectedFragments)
    {
        var resource = fileName == "ProductMasterRepository.cs" ? "Products/ProductMasters" : "Products/ProductNeutrals";
        var source = ReadRepository(resource, fileName);

        StringAssert.Contains(source, "Where(id => id != Guid.Empty).Distinct().ToArray()");
        foreach (var fragment in expectedFragments) StringAssert.Contains(source, fragment);
        Assert.IsFalse(source.Contains("CategoryId IN (", StringComparison.OrdinalIgnoreCase), "分类值不得拼接进 SQL。");
    }

    /// <summary>读取指定业务目录下的仓储源码，用于约束分类联表边界。</summary>
    private static string ReadRepository(string area, string fileName) => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "SwiftExport.Infrastructure",
        "Repositories",
        area.Replace('/', Path.DirectorySeparatorChar),
        fileName));

    /// <summary>从测试运行目录向上定位仓库根目录。</summary>
    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}
