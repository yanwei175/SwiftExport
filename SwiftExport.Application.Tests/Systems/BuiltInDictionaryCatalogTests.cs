using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Systems.Dictionaries;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;

namespace SwiftExport.Application.Tests.Systems;

/// <summary>验证内置目录的有效读取、旧身份保留和候选冲突，不访问数据库。</summary>
[TestClass]
public sealed class BuiltInDictionaryCatalogTests
{
    /// <summary>空存储提供十二个类型与六个必需明细；条款建议、属性模板、产品系列、测试 EXW 及单位明细不会凭空出现。</summary>
    [TestMethod]
    public void EmptyStorage_ReturnsOnlyDeclaredDefinitions()
    {
        var types = BuiltInDictionaryCatalog.TypeDtos([]);
        var data = BuiltInDictionaryCatalog.DataDtos([]);
        Assert.AreEqual(12, types.Count);
        Assert.AreEqual(6, data.Count);
        Assert.IsTrue(types.All(item => item.IsBuiltIn && item.IsSystem && item.Version == 0));
        Assert.IsTrue(data.All(item => item.IsBuiltIn && item.IsSystem && item.IsEnabled && item.Version == 0));
        Assert.IsTrue(data.All(item => item.CreatedDate is null && item.CreatedBy is null));
        Assert.IsFalse(data.Any(item => item.DataNameEn == "EXW" || item.DictCode == DictCodes.Unit));
        Assert.AreEqual(18, types.Select(item => item.Uid).Concat(data.Select(item => item.Uid)).Distinct().Count());
        Assert.IsTrue(types.Any(item => item.DictCode == DictCodes.IncotermLocation));
        Assert.IsTrue(types.Any(item => item.DictCode == DictCodes.IncotermSortRemark));
        Assert.IsFalse(data.Any(item => item.DictCode == DictCodes.IncotermLocation || item.DictCode == DictCodes.IncotermSortRemark));
    }

    /// <summary>模板类型固定身份和中文名称可在空库读取，但目录不能生成任何具体模板明细。</summary>
    [TestMethod]
    public void ProductAttributeProfile_RegistersStableTypeWithoutSeedValues()
    {
        var type = BuiltInDictionaryCatalog.TypeDtos([]).Single(item => item.DictCode == DictCodes.ProductAttributeProfile);
        Assert.AreEqual(Guid.Parse("00757a40-f61b-4e87-8750-db5a1c41b6f2"), type.Uid);
        Assert.AreEqual("产品属性模板", type.DictName);
        Assert.IsTrue(type.IsBuiltIn && type.IsSystem);
        Assert.AreEqual(0, type.Version);
        Assert.IsFalse(BuiltInDictionaryCatalog.DataDtos([]).Any(item => item.DictCode == DictCodes.ProductAttributeProfile));
    }

    /// <summary>系列类型在空库返回固定身份和 Products 分组，具体系列必须由用户维护。</summary>
    [TestMethod]
    public void ProductSeries_RegistersStableTypeWithoutSeedValues()
    {
        var type = BuiltInDictionaryCatalog.TypeDtos([]).Single(item => item.DictCode == DictCodes.ProductSeries);
        Assert.AreEqual(Guid.Parse("70ce6276-847b-46ab-926d-c0abcf08fb66"), type.Uid);
        Assert.AreEqual("产品系列", type.DictName);
        Assert.AreEqual("Products", type.GroupCode);
        Assert.IsTrue(type.IsBuiltIn && type.IsSystem);
        Assert.AreEqual(0, type.Version);
        Assert.IsFalse(BuiltInDictionaryCatalog.DataDtos([]).Any(item => item.DictCode == DictCodes.ProductSeries));
    }

    /// <summary>包装类型及四个常用角色可在空存储读取，固定身份与稳定代码不会随重复读取变化。</summary>
    [TestMethod]
    public void ProductPackageType_RegistersStableTypeAndFourPackagingRoles()
    {
        var type = BuiltInDictionaryCatalog.TypeDtos([]).Single(item => item.DictCode == DictCodes.ProductPackageType);
        Assert.AreEqual(Guid.Parse("55a113d5-1eb8-4510-a21d-d5a0912734d1"), type.Uid);
        Assert.AreEqual("产品包装类型", type.DictName);
        Assert.AreEqual("Products", type.GroupCode);
        Assert.IsTrue(type.IsBuiltIn && type.IsSystem);
        Assert.AreEqual(0, type.Version);
        Assert.IsTrue(DictCodes.DictCodeList.Contains(DictCodes.ProductPackageType));
        var values = BuiltInDictionaryCatalog.DataDtos([]).Where(item => item.DictCode == DictCodes.ProductPackageType).ToArray();
        CollectionAssert.AreEquivalent(new[] { "InnerBox:内盒", "ShippingBox:单套运输盒", "Carton:出货外箱", "Pallet:托盘" },
            values.Select(item => item.DataNameEn + ":" + item.DataNameZh).ToArray());
        Assert.IsTrue(values.All(item => item.IsBuiltIn && item.IsSystem && item.IsEnabled && item.Version == 0));
        CollectionAssert.AreEqual(values.Select(item => item.Uid).ToArray(), BuiltInDictionaryCatalog.DataDtos([])
            .Where(item => item.DictCode == DictCodes.ProductPackageType).Select(item => item.Uid).ToArray());
    }

    /// <summary>已有数据库身份和显示覆盖优先，归并不能原地修改仓储实体。</summary>
    [TestMethod]
    public void ExistingDefinitions_KeepLegacyGuidVersionAndDisplay()
    {
        var definition = BuiltInDictionaryCatalog.Data[1];
        var existing = new SysDictData
        {
            Uid = Guid.NewGuid(), Version = 8, DictCode = definition.DictCode,
            DataNameEn = " Unlimited ", DataNameZh = "自由流量", IsEnabled = true, IsSystem = false, SortOrder = 25
        };
        var item = BuiltInDictionaryCatalog.DataDtos([existing]).Single(item => item.DictCode == definition.DictCode);
        Assert.AreEqual(existing.Uid, item.Uid);
        Assert.AreEqual(8, item.Version);
        Assert.AreEqual("自由流量", item.DataNameZh);
        Assert.IsTrue(item.IsSystem && item.IsBuiltIn);
        Assert.AreEqual(" Unlimited ", existing.DataNameEn);
        Assert.IsFalse(existing.IsSystem);
        var type = new SysDictType { Uid = Guid.NewGuid(), DictCode = DictCodes.Unit, DictName = "基础单位", Version = 4 };
        var effectiveType = BuiltInDictionaryCatalog.TypeDtos([type]).Single(item => item.DictCode == DictCodes.Unit);
        Assert.AreEqual(type.Uid, effectiveType.Uid);
        Assert.AreEqual("基础单位", effectiveType.DictName);
        Assert.AreEqual(4, effectiveType.Version);
    }

    /// <summary>异常旧记录回退默认显示，但保留身份和版本供后续显式恢复；读取不篡改标记。</summary>
    [TestMethod]
    [DataRow(true, true)]
    [DataRow(false, false)]
    public void DeletedOrDisabledBuiltIn_UsesDefaultWithoutChangingStorage(bool deleted, bool enabled)
    {
        var definition = BuiltInDictionaryCatalog.Data[1];
        var stored = new SysDictData
        {
            Uid = Guid.NewGuid(), Version = 6, DictCode = definition.DictCode, DataNameEn = definition.DataNameEn,
            DataNameZh = "异常显示", IsDeleted = deleted, IsEnabled = enabled
        };
        var effective = BuiltInDictionaryCatalog.DataDtos([stored]).Single(item => item.DictCode == definition.DictCode);
        Assert.AreEqual(stored.Uid, effective.Uid);
        Assert.AreEqual(6, effective.Version);
        Assert.AreEqual("不限流", effective.DataNameZh);
        Assert.IsTrue(effective.IsEnabled);
        Assert.IsFalse(effective.IsDeleted);
        Assert.AreEqual(deleted, stored.IsDeleted);
        Assert.AreEqual(enabled, stored.IsEnabled);
    }

    /// <summary>旧系统标记不授予内置身份，普通软删除记录仍不可见。</summary>
    [TestMethod]
    public void LegacySystemAndCustomItems_ArePreservedWithoutPromotion()
    {
        var items = BuiltInDictionaryCatalog.DataDtos([
            new() { DictCode = DictCodes.Incoterm, DataNameEn = "EXW", DataNameZh = "EXW", IsSystem = true, IsEnabled = true },
            new() { DictCode = DictCodes.Unit, DataNameEn = "Pcs", DataNameZh = "个", IsEnabled = true },
            new() { DictCode = DictCodes.Unit, DataNameEn = "Deleted", IsDeleted = true }
        ]);
        Assert.AreEqual(8, items.Count);
        Assert.IsFalse(items.Single(item => item.DataNameEn == "EXW").IsBuiltIn);
        Assert.IsTrue(items.Single(item => item.DataNameEn == "EXW").IsSystem);
        Assert.AreEqual("Pcs", items.Single(item => item.DictCode == DictCodes.Unit).DataNameEn);
    }

    /// <summary>重复活动候选与固定身份占用必须明确失败，不能任意挑选而改写业务引用。</summary>
    [TestMethod]
    public void AmbiguousOrOccupiedIdentity_IsRejected()
    {
        var definition = BuiltInDictionaryCatalog.Data[0];
        Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInDictionaryCatalog.MergeData([
            new() { DictCode = definition.DictCode, DataNameEn = definition.DataNameEn },
            new() { DictCode = definition.DictCode, DataNameEn = definition.DataNameEn }
        ]));
        Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInDictionaryCatalog.MergeData([
            new() { Uid = definition.Uid, DictCode = DictCodes.Unit, DataNameEn = "Pcs" }
        ]));
    }
}
