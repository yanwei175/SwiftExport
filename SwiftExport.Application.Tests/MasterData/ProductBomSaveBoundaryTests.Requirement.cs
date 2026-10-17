using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.StaticEnums;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>验证无需 BOM、需要但待建立、已有绑定三态及其允许的资料保存边界。</summary>
public sealed partial class ProductBomSaveBoundaryTests
{
    /// <summary>普通物料保存允许三种供货成品首次自定义编号，新增、单项及批量保存共用绑定保护。</summary>
    [TestMethod]
    [DataRow("生产", "add", false)] [DataRow("采购", "add", false)] [DataRow("外协", "add", false)]
    [DataRow("生产", "update", false)] [DataRow("采购", "update", false)] [DataRow("外协", "update", false)]
    [DataRow("生产", "batch", false)] [DataRow("采购", "batch", false)] [DataRow("外协", "batch", false)]
    [DataRow("生产", "add", true)] [DataRow("采购", "add", true)] [DataRow("外协", "add", true)]
    [DataRow("生产", "update", true)] [DataRow("采购", "update", true)] [DataRow("外协", "update", true)]
    [DataRow("生产", "batch", true)] [DataRow("采购", "batch", true)] [DataRow("外协", "batch", true)]
    public async Task CustomNumber_FirstBindingCanUseUserNumber(string supply, string entry, bool shared)
    {
        const string number = "自定义装配-2026 A";
        var fixture = new Fixture();
        var target = fixture.Product("TARGET");
        target.MaterialType = MaterialTypesEnum.成品;
        target.BomVersion = 7;
        if (shared)
        {
            var source = fixture.Product("SOURCE", number, 3);
            fixture.Line(source, fixture.Product("LEAF"));
        }
        var dto = Dto(target);
        dto.Sku = entry == "add" ? "NEW" : target.Sku;
        dto.SupplyType = supply;
        dto.BomNo = "  " + number + "  ";
        var beforeBom = fixture.BomState();
        Guid savedId;
        if (entry == "batch")
        {
            var result = await fixture.Service.BatchSaveAsync(new()
            { UpdateItems = [new() { Uid = target.Uid, Version = target.Version, Data = dto }] });
            Assert.IsTrue(result.Success, result.Message);
            savedId = target.Uid;
        }
        else
        {
            var result = entry == "add"
                ? await fixture.Entry.AddAsync(new() { Data = dto })
                : await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
            Assert.IsTrue(result.Success, result.Message);
            savedId = result.Data.Uid!.Value;
        }
        Assert.AreEqual(number, fixture.Products[savedId].BomNo);
        Assert.AreEqual(7, fixture.Products[savedId].BomVersion);
        Assert.AreEqual(beforeBom, fixture.BomState(), "产品首次填写编号不得复制或改写组成。");
        fixture.AssertTransaction(1, 0);
        fixture.AssertNoBomBypass();
    }

    /// <summary>自定义输入不是已有绑定的改号通道，普通单项和批量修改均受保护，空组成也不得换号。</summary>
    [TestMethod]
    [DataRow("update", false)] [DataRow("update", true)] [DataRow("batch", false)] [DataRow("batch", true)]
    public async Task CustomNumber_ExistingBindingCannotChangeNumber(string entry, bool empty)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("BOUND", "ORIGINAL", 7);
        if (!empty) fixture.Line(parent, fixture.Product("LEAF"));
        var dto = Dto(parent);
        dto.BomNo = "另一个自定义编号";
        var before = fixture.State();
        var success = entry == "batch"
            ? (await fixture.Service.BatchSaveAsync(new() { UpdateItems = [new() { Uid = parent.Uid, Version = parent.Version, Data = dto }] })).Success
            : (await fixture.Entry.UpdateAsync(new() { Uid = parent.Uid, Version = parent.Version, Data = dto })).Success;
        fixture.AssertBlocked(success, before);
    }

    /// <summary>各供货方式成品可通过新增、单项更新及批量更新标记需要 BOM，尚无编号也可保存。</summary>
    [TestMethod]
    [DataRow("生产", "add")] [DataRow("采购", "add")] [DataRow("外协", "add")]
    [DataRow("生产", "update")] [DataRow("采购", "update")] [DataRow("外协", "update")]
    [DataRow("生产", "batch")] [DataRow("采购", "batch")] [DataRow("外协", "batch")]
    public async Task Requirement_PendingCanBeSaved(string supply, string entry)
    {
        var fixture = new Fixture();
        var target = fixture.Product("TARGET");
        var dto = Dto(target);
        dto.Sku = entry == "add" ? "NEW" : target.Sku;
        dto.MaterialType = MaterialTypesEnum.成品;
        dto.SupplyType = supply;
        dto.BomVersion = 7;
        dto.BomNo = null!;
        Guid savedId;
        if (entry == "batch")
        {
            var result = await fixture.Service.BatchSaveAsync(new()
            { UpdateItems = [new() { Uid = target.Uid, Version = target.Version, Data = dto }] });
            Assert.IsTrue(result.Success, result.Message);
            savedId = target.Uid;
        }
        else
        {
            var result = entry == "add"
                ? await fixture.Entry.AddAsync(new() { Data = dto })
                : await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto });
            Assert.IsTrue(result.Success, result.Message);
            savedId = result.Data.Uid!.Value;
        }
        Assert.AreEqual(7, fixture.Products[savedId].BomVersion);
        Assert.IsTrue(string.IsNullOrWhiteSpace(fixture.Products[savedId].BomNo));
        Assert.HasCount(0, fixture.Boms);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>非成品即使不填编号，也不能声明需要 BOM。</summary>
    [TestMethod]
    [DataRow("零件")] [DataRow("虚拟件")] [DataRow("客供件")] [DataRow("包材")]
    public async Task Requirement_NonFinishedPendingIsRejected(string material)
    {
        foreach (var entry in new[] { "add", "update", "batch" })
        {
            var fixture = new Fixture();
            var target = fixture.Product("TARGET");
            var dto = Dto(target);
            dto.Sku = entry == "add" ? "NEW" : target.Sku;
            dto.MaterialType = material;
            dto.BomVersion = 7;
            dto.BomNo = null!;
            var before = fixture.State();
            bool success;
            if (entry == "batch") success = (await fixture.Service.BatchSaveAsync(new()
            { UpdateItems = [new() { Uid = target.Uid, Version = target.Version, Data = dto }] })).Success;
            else success = entry == "add"
                ? (await fixture.Entry.AddAsync(new() { Data = dto })).Success
                : (await fixture.Entry.UpdateAsync(new() { Uid = target.Uid, Version = target.Version, Data = dto })).Success;
            fixture.AssertBlocked(success, before);
        }
    }

    /// <summary>待建立产品可保留任意正标记或取消为零，取消后可改为其他物料类型。</summary>
    [TestMethod]
    [DataRow(7, "成品")] [DataRow(0, "成品")] [DataRow(0, "零件")]
    public async Task Requirement_PendingCanRemainOrBeCancelled(int requestedMarker, string material)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("PENDING");
        parent.MaterialType = MaterialTypesEnum.成品;
        parent.BomVersion = 7;
        var dto = Dto(parent);
        dto.BomVersion = requestedMarker;
        dto.MaterialType = material;
        dto.Note = "仅调整需要 BOM 意图";
        var result = await fixture.Entry.UpdateAsync(new() { Uid = parent.Uid, Version = parent.Version, Data = dto });
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(requestedMarker, fixture.Products[parent.Uid].BomVersion);
        Assert.IsTrue(string.IsNullOrWhiteSpace(fixture.Products[parent.Uid].BomNo));
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>已有绑定不能取消需要、清编号或换组；尚无组成也必须保留绑定身份。</summary>
    [TestMethod]
    [DataRow("cancel", false)] [DataRow("clear", false)] [DataRow("move", false)]
    [DataRow("cancel", true)] [DataRow("clear", true)] [DataRow("move", true)]
    public async Task Requirement_ExistingBindingCannotBeRemoved(string change, bool empty)
    {
        var fixture = new Fixture();
        var parent = fixture.Product("BOUND", "SHARED", 7);
        if (!empty) fixture.Line(parent, fixture.Product("LEAF"));
        var dto = Dto(parent);
        if (change == "cancel") { dto.BomVersion = 0; dto.BomNo = null!; }
        if (change == "clear") dto.BomNo = null!;
        if (change == "move") dto.BomNo = "OTHER";
        var before = fixture.State();
        var result = await fixture.Entry.UpdateAsync(new() { Uid = parent.Uid, Version = parent.Version, Data = dto });
        fixture.AssertBlocked(result.Success, before);
    }

    /// <summary>详情显式解释可选意图；旧客户端省略保持、勾选保留原正数，未绑定取消可以归零。</summary>
    [TestMethod]
    [DataRow(null, 0, false, 0)] [DataRow(null, 7, false, 7)] [DataRow(null, 7, true, 7)]
    [DataRow(true, 0, false, 1)] [DataRow(true, 7, false, 7)] [DataRow(true, 7, true, 7)]
    [DataRow(false, 0, false, 0)] [DataRow(false, 7, false, 0)]
    public async Task Requirement_DetailIntentPreservesOrChangesMarker(bool? requiresBom, int originalMarker, bool bound, int expectedMarker)
    {
        var fixture = DetailFixture(out var product, out _, out _);
        product.MaterialType = MaterialTypesEnum.成品;
        product.BomVersion = originalMarker;
        product.BomNo = bound ? "DETAIL-BOM" : null;
        var request = DetailRequest(product, null);
        request.Data.RequiresBom = requiresBom;
        var result = await fixture.Service.SaveDetailAsync(request);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(expectedMarker, fixture.Products[product.Uid].BomVersion);
        Assert.AreEqual(product.BomNo, fixture.Products[product.Uid].BomNo);
        Assert.AreEqual(expectedMarker, result.Data.Detail.BomVersion);
        fixture.AssertTransaction(1, 0);
    }

    /// <summary>详情不能取消已有编号，即使组成为空；四类非成品也不能通过详情勾选需要 BOM。</summary>
    [TestMethod]
    [DataRow("成品", false, true)] [DataRow("成品", false, false)]
    [DataRow("零件", true, false)] [DataRow("虚拟件", true, false)]
    [DataRow("客供件", true, false)] [DataRow("包材", true, false)]
    public async Task Requirement_DetailRejectsBoundCancellationOrNonFinished(string material, bool requiresBom, bool hasLines)
    {
        var fixture = DetailFixture(out var product, out _, out _);
        product.MaterialType = material;
        if (!requiresBom)
        {
            product.BomVersion = 7;
            product.BomNo = "DETAIL-BOM";
            if (hasLines) fixture.Line(product, fixture.Product("LEAF"));
        }
        var request = DetailRequest(product, null);
        request.Data.RequiresBom = requiresBom;
        var before = fixture.State();
        var result = await fixture.Service.SaveDetailAsync(request);
        fixture.AssertBlocked(result.Success, before);
    }
}
