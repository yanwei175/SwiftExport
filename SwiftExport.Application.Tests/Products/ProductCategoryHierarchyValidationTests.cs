using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products;

namespace SwiftExport.Application.Tests.Products;

[TestClass]
public sealed class ProductCategoryHierarchyValidationTests
{
    [TestMethod]
    public void ValidHierarchy_Passes()
    {
        var root = Node("根分类", "ROOT");
        var child = Node("子分类", "CHILD", root.Uid);

        var error = ProductCategoryService.ValidateFinalGraph(Nodes(root, child));

        Assert.IsNull(error);
    }

    [TestMethod]
    public void DuplicateCategoryCode_IsRejectedIgnoringCase()
    {
        var first = Node("分类一", "ABC");
        var second = Node("分类二", "abc");

        var error = ProductCategoryService.ValidateFinalGraph(Nodes(first, second));

        StringAssert.Contains(error, "分类编码不能重复");
    }

    [TestMethod]
    public void MissingParent_IsRejected()
    {
        var child = Node("孤立分类", "ORPHAN", Guid.NewGuid());

        var error = ProductCategoryService.ValidateFinalGraph(Nodes(child));

        StringAssert.Contains(error, "上级分类不存在");
    }

    [TestMethod]
    public void IndirectCycle_IsRejected()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new ProductCategoryService.CategoryNode(firstId, secondId, "分类一", "ONE");
        var second = new ProductCategoryService.CategoryNode(secondId, firstId, "分类二", "TWO");

        var error = ProductCategoryService.ValidateFinalGraph(Nodes(first, second));

        StringAssert.Contains(error, "循环引用");
    }

    private static ProductCategoryService.CategoryNode Node(
        string name,
        string code,
        Guid? parentId = null) => new(Guid.NewGuid(), parentId, name, code);

    private static IReadOnlyDictionary<Guid, ProductCategoryService.CategoryNode> Nodes(
        params ProductCategoryService.CategoryNode[] nodes) => nodes.ToDictionary(node => node.Uid);
}
