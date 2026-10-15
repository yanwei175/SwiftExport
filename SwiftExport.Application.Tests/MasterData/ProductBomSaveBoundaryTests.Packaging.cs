using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>在既有物料保存内核上验证包装引用保护与旧批量调用的自重兼容，不连接数据库。</summary>
public sealed partial class ProductBomSaveBoundaryTests
{
    /// <summary>单项、批量、DTO 软删与详情都不能删除或改型已被包装规格引用的包材。</summary>
    [TestMethod]
    [DataRow("delete")]
    [DataRow("batch-delete")]
    [DataRow("update")]
    [DataRow("soft-delete")]
    [DataRow("detail")]
    public async Task PackagingReference_AllWriteEntriesRejectInvalidation(string entry)
    {
        var fixture = new Fixture();
        var target = fixture.Product("PACK"); target.MaterialType = MaterialTypesEnum.包材;
        fixture.ReferencedPackagingMaterialIds.Add(target.Uid);
        var before = fixture.State();
        var dto = Dto(target);
        dto.MaterialType = MaterialTypesEnum.零件;
        if (entry == "soft-delete") { dto.MaterialType = MaterialTypesEnum.包材; dto.IsDeleted = true; }
        bool success;
        if (entry == "delete") success = (await fixture.Entry.DeleteAsync(new() { Uid = target.Uid, Version = target.Version })).Success;
        else if (entry == "batch-delete") success = (await fixture.Service.BatchSaveAsync(new() { DeleteItems = [new() { Uid = target.Uid, Version = target.Version }] })).Success;
        else if (entry == "detail")
        {
            var data = target.Adapt<ProductDetailWriteDto>(); data.MaterialType = MaterialTypesEnum.零件;
            success = (await fixture.Service.SaveDetailAsync(new SaveProductDetailRequest { ProductId = target.Uid, ExpectedVersion = target.Version, Data = data })).Success;
        }
        else success = (await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto })).Success;
        fixture.AssertBlocked(success, before);
    }

    /// <summary>已有包材引用不妨碍正常资料更新；旧客户端省略自重保留原值，显式提交可修改或清空。</summary>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task PackagingWeight_ExplicitUpdateDistinguishesOmissionAndClearing(bool update, bool clear)
    {
        var fixture = new Fixture();
        var target = fixture.Product("PACK"); target.MaterialType = MaterialTypesEnum.包材; target.UnitWeight = 7m;
        fixture.ReferencedPackagingMaterialIds.Add(target.Uid);
        var dto = Dto(target); dto.UnitWeight = clear ? null : 0.25m; dto.UpdateUnitWeight = update;
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(update ? dto.UnitWeight : 7m, fixture.Products[target.Uid].UnitWeight);
        fixture.AssertTransaction(1, 0);
    }
}
