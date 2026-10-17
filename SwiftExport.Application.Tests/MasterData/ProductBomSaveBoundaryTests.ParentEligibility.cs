using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>通过产品保存真实入口验证父件资格、空组成及编号标记的一致性，不连接数据库。</summary>
public sealed partial class ProductBomSaveBoundaryTests
{
    /// <summary>三种供货成品都能新增或修改为合法父件，既有共享组成和新空编号均支持。</summary>
    [TestMethod]
    [DataRow("生产", "add", false)] [DataRow("采购", "add", false)] [DataRow("外协", "add", false)]
    [DataRow("生产", "update", false)] [DataRow("采购", "update", false)] [DataRow("外协", "update", false)]
    [DataRow("生产", "add", true)] [DataRow("采购", "add", true)] [DataRow("外协", "add", true)]
    [DataRow("生产", "update", true)] [DataRow("采购", "update", true)] [DataRow("外协", "update", true)]
    public async Task ParentEligibility_FinishedProductCanBind(string supply, string entry, bool empty)
    {
        var fixture = new Fixture();
        var target = fixture.Product("TARGET");
        var source = fixture.Product("SOURCE", "SHARED");
        if (!empty) fixture.Line(source, fixture.Product("LEAF"));
        var dto = Dto(target);
        dto.Sku = entry == "add" ? "NEW" : target.Sku;
        dto.MaterialType = MaterialTypesEnum.成品;
        dto.SupplyType = supply;
        dto.BomNo = empty ? "NEW-EMPTY" : "SHARED";
        dto.BomVersion = 7;
        var beforeBom = fixture.BomState();
        var result = entry == "add"
            ? await fixture.Entry.AddAsync(new() { Data = dto })
            : await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(7, fixture.Products[result.Data.Uid!.Value].BomVersion);
        Assert.AreEqual(beforeBom, fixture.BomState());
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>四类非成品不能通过普通产品新增或修改建立 BOM，包括还没有组成的新编号。</summary>
    [TestMethod]
    [DataRow("零件", "add", false)] [DataRow("虚拟件", "add", false)] [DataRow("客供件", "add", false)] [DataRow("包材", "add", false)]
    [DataRow("零件", "update", false)] [DataRow("虚拟件", "update", false)] [DataRow("客供件", "update", false)] [DataRow("包材", "update", false)]
    [DataRow("零件", "add", true)] [DataRow("虚拟件", "add", true)] [DataRow("客供件", "add", true)] [DataRow("包材", "add", true)]
    [DataRow("零件", "update", true)] [DataRow("虚拟件", "update", true)] [DataRow("客供件", "update", true)] [DataRow("包材", "update", true)]
    public async Task ParentEligibility_NonFinishedBindingIsRejected(string material, string entry, bool empty)
    {
        var fixture = new Fixture();
        var target = fixture.Product("TARGET");
        var source = fixture.Product("SOURCE", "SHARED");
        if (!empty) fixture.Line(source, fixture.Product("LEAF"));
        var dto = Dto(target);
        dto.Sku = entry == "add" ? "NEW" : target.Sku;
        dto.MaterialType = material;
        dto.BomNo = empty ? "NEW-EMPTY" : "SHARED";
        dto.BomVersion = 1;
        var before = fixture.State();
        var result = entry == "add"
            ? await fixture.Entry.AddAsync(new() { Data = dto })
            : await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>已绑定成品不能改为非成品，即使当前只建立了父件、尚无任何组成。</summary>
    [TestMethod]
    [DataRow("零件", false)] [DataRow("虚拟件", false)] [DataRow("客供件", false)] [DataRow("包材", false)]
    [DataRow("零件", true)] [DataRow("虚拟件", true)] [DataRow("客供件", true)] [DataRow("包材", true)]
    public async Task ParentEligibility_BoundRoleChangeIsRejected(string material, bool empty)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("PARENT", "SHARED");
        if (!empty) fixture.Line(parent, fixture.Product("LEAF"));
        var dto = Dto(parent);
        dto.MaterialType = material;
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = parent.Uid, Version = parent.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>物料详情复用同一资格边界，保留只读 BOM 字段时也不能把已绑定成品改为非成品。</summary>
    [TestMethod]
    [DataRow("零件")] [DataRow("虚拟件")] [DataRow("客供件")] [DataRow("包材")]
    public async Task ParentEligibility_DetailRoleChangeIsRejected(string material)
    {
        var fixture = DetailFixture(out var parent, out _, out _);
        parent.MaterialType = MaterialTypesEnum.成品;
        parent.BomNo = "DETAIL-BOM";
        parent.BomVersion = 3;
        var request = DetailRequest(parent, null);
        request.Data.MaterialType = material;
        var before = fixture.State();
        var result = await fixture.Service.SaveDetailAsync(request);
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>普通产品绑定同样检查共享组的全部实际父件，不能借一个合法来源父掩盖历史非法引用者。</summary>
    [TestMethod]
    [DataRow("零件")] [DataRow("虚拟件")] [DataRow("客供件")] [DataRow("包材")]
    public async Task ParentEligibility_OrdinaryBindingRejectsIllegalSharedParent(string material)
    {
        var fixture = new Fixture();
        var target = fixture.Product("TARGET");
        var source = fixture.Product("SOURCE", "SHARED");
        var other = fixture.Product("OTHER", "SHARED");
        other.MaterialType = material;
        fixture.Line(source, fixture.Product("LEAF"));
        var dto = Dto(target);
        dto.MaterialType = MaterialTypesEnum.成品;
        dto.BomNo = "SHARED";
        dto.BomVersion = 1;
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>把类型、编号和标记同时清改也不能绕过既有成品绑定保护，空组成不授予自动解绑权限。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task ParentEligibility_RoleChangeCannotClearExistingBinding(bool empty)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("PARENT", "SHARED");
        if (!empty) fixture.Line(parent, fixture.Product("LEAF"));
        var dto = Dto(parent);
        dto.MaterialType = MaterialTypesEnum.零件;
        dto.BomNo = null!;
        dto.BomVersion = 0;
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = parent.Uid, Version = parent.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>编号与有效标记必须成对，不能用零或负标记配编号绕过资格，也不能只提交正标记。</summary>
    [TestMethod]
    [DataRow("成品", "NUMBER", 0)] [DataRow("零件", "NUMBER", 0)]
    [DataRow("成品", "NUMBER", -1)] [DataRow("零件", "NUMBER", -1)]
    [DataRow("成品", null, 1)] [DataRow("零件", null, 1)]
    [DataRow("成品", "  ", 1)] [DataRow("零件", "  ", 1)]
    [DataRow("成品", null, -1)] [DataRow("零件", null, -1)]
    public async Task ParentEligibility_InconsistentBindingIsRejected(string material, string? number, int marker)
    {
        foreach (var entry in new[] { "add", "update" })
        {
            var fixture = new Fixture();
            var target = fixture.Product("TARGET");
            var dto = Dto(target);
            dto.Sku = entry == "add" ? "NEW" : target.Sku;
            dto.MaterialType = material;
            dto.BomNo = number!;
            dto.BomVersion = marker;
            var before = fixture.State();
            var result = entry == "add"
                ? await fixture.Entry.AddAsync(new() { Data = dto })
                : await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
            fixture.AssertBlocked(result.Success, before);
        }
    }
}
