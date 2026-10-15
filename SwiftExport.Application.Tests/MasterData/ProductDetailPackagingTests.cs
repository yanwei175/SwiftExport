using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>包装详情的真实 Application 用例离线测试；事务替身验证原子性，不替代 PG 锁与唯一索引。</summary>
public sealed partial class ProductBomSaveBoundaryTests
{
    /// <summary>后序标签写入失败同时恢复已写入的包装、物料资料和旧标签，防止三个区块出现部分提交。</summary>
    [TestMethod]
    public async Task DetailPackaging_TagFailureRollsBackAllThreeBlocks()
    {
        var fixture = DetailFixture(out var owner, out var first, out var second);
        fixture.Selection(owner.Uid, first);
        var package = Package(fixture, owner);
        var before = fixture.State();
        fixture.FailSelectionWrite = true;
        var request = DetailRequest(owner, [second]); request.Data.Note = "三个区块均应回滚";
        request.PackagingChanges = new() { UpdateItems = [new() { Uid = package.Uid, Version = package.Version,
            Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 99 } }] };
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsFalse(result.Success); Assert.AreEqual(before, fixture.State());
        Assert.AreEqual(package.PcsPerPackage, fixture.Packages[package.Uid].PcsPerPackage);
        CollectionAssert.AreEquivalent(new[] { first }, fixture.Selections.Values.Where(row => !row.IsDeleted).Select(row => row.OptionId).ToArray());
        fixture.AssertTransaction(0, 1);
    }

    /// <summary>null 与空变更都保持已有包装，旧客户端仅保存资料不会丢失关系或行版本。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task DetailPackaging_OmittedOrEmptyChangesKeepRelations(bool empty)
    {
        var fixture = new Fixture(); var owner = fixture.Product("OWNER"); var package = Package(fixture, owner);
        var request = DetailRequest(owner, null); request.Data.Note = "只改资料";
        request.PackagingChanges = empty ? new() : null;
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(3, fixture.Packages[package.Uid].Version);
        Assert.AreEqual(package.Uid, result.Data.Detail.Packages.Single().Uid);
        Assert.AreEqual("只改资料", fixture.Products[owner.Uid].Note);
    }

    /// <summary>新增多层包装、更新/删除和类型互换均以同一父事务提交，内置 Carton 无数据库行仍有效。</summary>
    [TestMethod]
    public async Task DetailPackaging_MultiLevelChangesAndTypeSwapPreserveIdentity()
    {
        var fixture = new Fixture(); var owner = fixture.Product("OWNER"); var material = fixture.Product("PACK"); material.MaterialType = MaterialTypesEnum.包材;
        var carton = Package(fixture, owner); var inner = Package(fixture, owner, ProductPackageTypeCodes.InnerBox);
        var request = DetailRequest(owner, null); request.PackagingChanges = new()
        {
            CreateItems = [new() { Data = new() { PackageType = ProductPackageTypeCodes.ShippingBox, PackagingMaterialId = material.Uid, PcsPerPackage = 1, Cbm = 0.123456m } }],
            UpdateItems = [new() { Uid = carton.Uid, Version = 3, Data = new() { PackageType = ProductPackageTypeCodes.InnerBox, PcsPerPackage = 2 } },
                new() { Uid = inner.Uid, Version = 3, Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 24, Nw_KG = 10, Gw_KG = 11 } }]
        };
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message); Assert.HasCount(3, result.Data.Detail.Packages);
        Assert.AreEqual(4, fixture.Packages[carton.Uid].Version); Assert.AreEqual(ProductPackageTypeCodes.InnerBox, fixture.Packages[carton.Uid].PackageType);
        Assert.AreEqual(ProductPackageTypeCodes.Carton, fixture.Packages[inner.Uid].PackageType);
        Assert.AreEqual(24, fixture.Packages[inner.Uid].PcsPerPackage);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>未知代码、重复类型、非正装率、负数、净毛重倒置与无效包材均拒绝，所有草稿事实不变。</summary>
    [TestMethod]
    [DataRow("type")] [DataRow("duplicate")] [DataRow("pcs")] [DataRow("negative")]
    [DataRow("weights")] [DataRow("precision")] [DataRow("missing-material")]
    [DataRow("inactive-material")] [DataRow("wrong-material-type")]
    public async Task DetailPackaging_InvalidFinalValuesRollBack(string invalid)
    {
        var fixture = new Fixture(); var owner = fixture.Product("OWNER"); Package(fixture, owner);
        var material = fixture.Product("PACK"); material.MaterialType = MaterialTypesEnum.包材;
        var data = new ProductPackageWriteDto { PackageType = ProductPackageTypeCodes.InnerBox, PcsPerPackage = 2 };
        switch (invalid)
        {
            case "type": data.PackageType = "出货外箱"; break;
            case "duplicate": data.PackageType = ProductPackageTypeCodes.Carton; break;
            case "pcs": data.PcsPerPackage = 0; break;
            case "negative": data.Length_CM = -1; break;
            case "weights": data.Nw_KG = 10; data.Gw_KG = 9; break;
            case "precision": data.Cbm = 0.0000001m; break;
            case "missing-material": data.PackagingMaterialId = Guid.NewGuid(); break;
            case "inactive-material": material.IsActive = false; data.PackagingMaterialId = material.Uid; break;
            case "wrong-material-type": material.MaterialType = MaterialTypesEnum.零件; data.PackagingMaterialId = material.Uid; break;
        }
        var before = fixture.State(); var request = DetailRequest(owner, null); request.Data.Note = "不应提交";
        request.PackagingChanges = new() { CreateItems = [new() { Data = data }] };
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>跨 Owner 行以及旧行版本、旧父版本不能写入，验证发生在任何包装变更前。</summary>
    [TestMethod]
    [DataRow("owner")] [DataRow("row-version")] [DataRow("parent-version")]
    public async Task DetailPackaging_OwnerAndVersionBoundariesAreAuthoritative(string invalid)
    {
        var fixture = new Fixture(); var owner = fixture.Product("OWNER"); var other = fixture.Product("OTHER");
        var package = Package(fixture, invalid == "owner" ? other : owner);
        var request = DetailRequest(owner, null); if (invalid == "parent-version") request.ExpectedVersion--;
        request.PackagingChanges = new() { UpdateItems = [new() { Uid = package.Uid, Version = invalid == "row-version" ? 2 : 3, Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 12 } }] };
        var before = fixture.State(); var result = await fixture.Service.SaveDetailAsync(request);
        fixture.AssertBlocked(result.Success, before);
        Assert.AreEqual(invalid == "owner" ? ServiceErrorCodes.Validation : ServiceErrorCodes.VersionConflict, result.Error.Code);
    }

    /// <summary>包装已写入而后序物料写入失败时，资料、标签和包装全部回滚，不能留下半套关系。</summary>
    [TestMethod]
    public async Task DetailPackaging_LaterProductFailureRollsBackEarlierPackagingWrite()
    {
        var fixture = new Fixture(); var owner = fixture.Product("OWNER"); Package(fixture, owner);
        var before = fixture.State(); fixture.FailWriteNumber = 1;
        var request = DetailRequest(owner, null); request.Data.Note = "不应提交";
        request.PackagingChanges = new() { CreateItems = [new() { Data = new() { PackageType = ProductPackageTypeCodes.InnerBox, PcsPerPackage = 3 } }] };
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsFalse(result.Success); Assert.AreEqual(before, fixture.State()); fixture.AssertTransaction(0, 1);
    }

    /// <summary>失效引用未改选时可以保留，删除包装后 Owner 和包材均能解除引用保护，不引入级联删除。</summary>
    [TestMethod]
    [DataRow("delete")] [DataRow("batch-delete")] [DataRow("soft-delete")]
    public async Task DetailPackaging_OwnerMustRemovePackagesBeforeDeletion(string entry)
    {
        var fixture = new Fixture(); var owner = fixture.Product("OWNER"); var package = Package(fixture, owner);
        var before = fixture.State(); bool success;
        if (entry == "delete") success = (await fixture.Entry.DeleteAsync(new() { Uid = owner.Uid, Version = owner.Version })).Success;
        else if (entry == "batch-delete") success = (await fixture.Service.BatchSaveAsync(new ProductBatchSaveRequest { DeleteItems = [new() { Uid = owner.Uid, Version = owner.Version }] })).Success;
        else { var dto = Dto(owner); dto.IsDeleted = true; success = (await fixture.Entry.UpdateAsync(new() { Uid = owner.Uid, Version = owner.Version, Data = dto })).Success; }
        fixture.AssertBlocked(success, before);
        // 使用新的独立夹具验证包装移除后的删除闭环，避免将前一失败事务计数误当下一用例结果。
        var fresh = new Fixture(); var freshOwner = fresh.Product("CLEAN"); var relation = Package(fresh, freshOwner);
        var removal = DetailRequest(freshOwner, null);
        removal.PackagingChanges = new() { DeleteItems = [new() { Uid = relation.Uid, Version = relation.Version }] };
        var cleared = await fresh.Service.SaveDetailAsync(removal); Assert.IsTrue(cleared.Success, cleared.Message);
        var removed = await fresh.Entry.DeleteAsync(new() { Uid = freshOwner.Uid, Version = fresh.Products[freshOwner.Uid].Version });
        Assert.IsTrue(removed.Success, removed.Message);
    }

    /// <summary>断链包材只在显式改选时重验，修改包装备注不会偷偷清除既有引用。</summary>
    [TestMethod]
    public async Task DetailPackaging_UnchangedBrokenReferenceCanBeRepairedExplicitly()
    {
        var fixture = new Fixture(); var owner = fixture.Product("OWNER"); var package = Package(fixture, owner);
        package.PackagingMaterialId = Guid.NewGuid();
        var data = package.Adapt<ProductPackageWriteDto>(); data.Note = "保留断链待修复";
        var request = DetailRequest(owner, null); request.PackagingChanges = new() { UpdateItems = [new() { Uid = package.Uid, Version = package.Version, Data = data }] };
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(package.PackagingMaterialId, fixture.Packages[package.Uid].PackagingMaterialId);
    }

    /// <summary>批量新建未选颜色的物料保存真正的空关联，随后详情可以配置包装，不能卡在空 Guid。</summary>
    [TestMethod]
    public async Task DetailPackaging_NewMaterialWithoutColorCanOpenAndSave()
    {
        var fixture = new Fixture();
        var added = await fixture.Entry.AddAsync(new() { Data = new() { Sku = "NEW-OWNER", MaterialType = MaterialTypesEnum.成品, SupplyType = SupplyTypeEnums.采购 } });
        Assert.IsTrue(added.Success, added.Message);
        var owner = fixture.Products[added.Data.Uid!.Value]; Assert.IsNull(owner.ColorId);
        var request = DetailRequest(owner, null); request.PackagingChanges = new() { CreateItems = [new() { Data = new() { PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 12 } }] };
        var saved = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(saved.Success, saved.Message); Assert.HasCount(1, saved.Data.Detail.Packages);
    }

    /// <summary>建立独立包装事实，行身份和版本由夹具明确设置，不经过待测写入。</summary>
    private static ProductPackage Package(Fixture fixture, Product owner, string type = ProductPackageTypeCodes.Carton)
    {
        var package = new ProductPackage { OwnerProductId = owner.Uid, PackageType = type, Version = 3, PcsPerPackage = 12 };
        fixture.Packages.Add(package.Uid, package); return package;
    }
}
