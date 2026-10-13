using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.Attributes;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证共享标签的祖先继承、来源去重与最终图选择保护，不依赖数据库。</summary>
[TestClass]
public sealed class ProductAttributeApplicabilityTests
{
    /// <summary>共享组在不同分支分别适用，子分类合并多层来源且相同组只出现一次。</summary>
    [TestMethod]
    public void ApplicableGroups_InheritAncestorsAndDeduplicateSharedGroups()
    {
        var root = Guid.NewGuid(); var first = Guid.NewGuid(); var leaf = Guid.NewGuid(); var second = Guid.NewGuid();
        var installation = Guid.NewGuid(); var size = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [root] = null, [first] = root, [leaf] = first, [second] = root };
        ProductCategoryAttributeGroupDto[] relations = [Relation(root, installation), Relation(first, size), Relation(leaf, installation), Relation(second, size)];
        CollectionAssert.AreEqual(new[] { leaf, first, root }, ProductAttributeApplicability.GetAncestorIds(leaf, parents).ToArray());
        Assert.IsTrue(ProductAttributeApplicability.GetApplicableGroupIds(leaf, parents, relations).SetEquals([installation, size]));
        Assert.IsTrue(ProductAttributeApplicability.GetApplicableGroupIds(second, parents, relations).SetEquals([installation, size]));
        Assert.IsTrue(ProductAttributeApplicability.GetApplicableGroupIds(root, parents, relations).SetEquals([installation]));
    }

    /// <summary>删除直接来源后祖先仍提供同一组时，原选择保持适用。</summary>
    [TestMethod]
    public void RemainingAncestorRelation_PreservesSelection()
    {
        var root = Guid.NewGuid(); var leaf = Guid.NewGuid(); var group = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [root] = null, [leaf] = root };
        var reference = Selection(Guid.NewGuid(), leaf, group);
        Assert.AreEqual(0, ProductAttributeApplicability.CountUnavailableMasters(parents, [Relation(root, group)], [reference]));
        Assert.AreEqual(1, ProductAttributeApplicability.CountUnavailableMasters(parents, [], [reference]));
    }

    /// <summary>多个失效值只统计一次主型号，不把产品数量误报成选项数量。</summary>
    [TestMethod]
    public void UnavailableSelections_CountDistinctMasters()
    {
        var category = Guid.NewGuid(); var master = Guid.NewGuid(); var group = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [category] = null };
        ProductMasterAttributeSelectionDto[] selections = [Selection(master, category, group), Selection(master, category, group), Selection(Guid.NewGuid(), category, group)];
        Assert.AreEqual(2, ProductAttributeApplicability.CountUnavailableMasters(parents, [], selections));
    }

    /// <summary>分类移动依据最终父关系判断适用性，不能沿用原祖先关系。</summary>
    [TestMethod]
    public void ReparentedCategory_UsesFinalTree()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var leaf = Guid.NewGuid(); var group = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [first] = null, [second] = null, [leaf] = first };
        var selected = Selection(Guid.NewGuid(), leaf, group);
        Assert.AreEqual(0, ProductAttributeApplicability.CountUnavailableMasters(parents, [Relation(first, group)], [selected]));
        parents[leaf] = second;
        Assert.AreEqual(1, ProductAttributeApplicability.CountUnavailableMasters(parents, [Relation(first, group)], [selected]));
        Assert.AreEqual(0, ProductAttributeApplicability.CountUnavailableMasters(parents, [Relation(first, group), Relation(second, group)], [selected]));
    }

    /// <summary>缺失节点、循环和空分类不能返回部分继承结果；保护已有选择时计为不适用。</summary>
    [TestMethod]
    public void InvalidHierarchy_FailsResolutionAndProtectsSelections()
    {
        var category = Guid.NewGuid(); var missing = Guid.NewGuid(); var group = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [category] = missing };
        Assert.ThrowsExactly<InvalidOperationException>(() => ProductAttributeApplicability.GetAncestorIds(category, parents));
        Assert.AreEqual(1, ProductAttributeApplicability.CountUnavailableMasters(parents, [Relation(category, group)], [Selection(Guid.NewGuid(), category, group)]));
        parents[missing] = category;
        Assert.ThrowsExactly<InvalidOperationException>(() => ProductAttributeApplicability.GetApplicableGroupIds(category, parents, [Relation(category, group)]));
        Assert.ThrowsExactly<InvalidOperationException>(() => ProductAttributeApplicability.GetAncestorIds(Guid.Empty, parents));
    }

    /// <summary>正常空定义成功解析为空集合，空父身份沿用既有分类根节点表达。</summary>
    [TestMethod]
    public void EmptyDefinitionsAndEmptyParent_AreValid()
    {
        var category = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [category] = Guid.Empty };
        Assert.AreEqual(0, ProductAttributeApplicability.GetApplicableGroupIds(category, parents, []).Count);
        Assert.AreEqual(0, ProductAttributeApplicability.CountUnavailableMasters(parents, [], []));
    }

    /// <summary>构造直接关联快照，身份不参与继承去重。</summary>
    private static ProductCategoryAttributeGroupDto Relation(Guid categoryId, Guid groupId) => new()
    { Uid = Guid.NewGuid(), CategoryId = categoryId, AttributeGroupId = groupId };

    /// <summary>构造具有主型号真实分类的选择引用。</summary>
    private static ProductMasterAttributeSelectionDto Selection(Guid masterId, Guid categoryId, Guid groupId) => new()
    { ProductMasterId = masterId, CategoryId = categoryId, AttributeGroupId = groupId, OptionId = Guid.NewGuid() };
}
