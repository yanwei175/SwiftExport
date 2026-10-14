using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证模板直接关联与统一对象身份的选择保护，不依赖分类层级或数据库。</summary>
[TestClass]
public sealed class ProductAttributeApplicabilityTests
{
    /// <summary>同一组可以关联不同模板；解除一个模板的关联不会保留该模板的适用性。</summary>
    [TestMethod]
    public void DirectProfileRelations_DoNotInheritAnotherProfile()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid(); var group = Guid.NewGuid();
        var selected = Selection(ProductAttributeOwnerType.ProductMaster, Guid.NewGuid(), first, group);
        Assert.AreEqual(0, ProductAttributeApplicability.CountUnavailableOwners([Relation(first, group), Relation(second, group)], [selected]));
        Assert.AreEqual(1, ProductAttributeApplicability.CountUnavailableOwners([Relation(second, group)], [selected]));
    }

    /// <summary>同对象多个失效值只统计一次，相同 UUID 的不同对象类型分别统计。</summary>
    [TestMethod]
    public void UnavailableSelections_CountDistinctTypedOwners()
    {
        var profile = Guid.NewGuid(); var owner = Guid.NewGuid(); var group = Guid.NewGuid();
        ProductAttributeSelectionDto[] selected = [
            Selection(ProductAttributeOwnerType.ProductMaster, owner, profile, group),
            Selection(ProductAttributeOwnerType.ProductMaster, owner, profile, group),
            Selection(ProductAttributeOwnerType.CustomerProduct, owner, profile, group)];
        Assert.AreEqual(2, ProductAttributeApplicability.CountUnavailableOwners([], selected));
        Assert.AreEqual(0, ProductAttributeApplicability.CountUnavailableOwners([Relation(profile, group)], selected));
    }

    /// <summary>缺失模板不能将已有选择当成合法空定义，正常空输入仍返回零。</summary>
    [TestMethod]
    public void MissingProfile_ProtectsExistingSelection()
    {
        var group = Guid.NewGuid();
        Assert.AreEqual(1, ProductAttributeApplicability.CountUnavailableOwners(
            [Relation(Guid.NewGuid(), group)], [Selection(ProductAttributeOwnerType.ProductMaster, Guid.NewGuid(), null, group)]));
        Assert.AreEqual(0, ProductAttributeApplicability.CountUnavailableOwners([], []));
    }

    /// <summary>构造一个模板直接关联，关联身份不替代模板和组的业务组合。</summary>
    private static ProductAttributeProfileGroupDto Relation(Guid profileId, Guid groupId) => new()
    { Uid = Guid.NewGuid(), ProductAttributeProfileId = profileId, AttributeGroupId = groupId };

    /// <summary>构造对象当前模板及选项所属组的引用快照。</summary>
    private static ProductAttributeSelectionDto Selection(ProductAttributeOwnerType kind, Guid ownerId, Guid? profileId, Guid groupId) => new()
    { OwnerType = kind, OwnerId = ownerId, ProductAttributeProfileId = profileId, AttributeGroupId = groupId, OptionId = Guid.NewGuid() };
}
