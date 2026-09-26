using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证预览到内部端口的确定性接线，避免缺失资源或丢失同产品不同依据行。</summary>
[TestClass]
public sealed class PackingListAutomaticCommandBuilderTests
{
    /// <summary>售后只交付不准备库存资源。</summary>
    [TestMethod]
    public void DeliveryOnlySkipsInventory()
    {
        var preview = new PackingListPostingPreviewBuilder.Preview("token", [], []);
        var result = new PackingListAutomaticCommandBuilder().Build(Guid.NewGuid(), Guid.NewGuid(), "PL", DateTime.Today, preview, [], []);
        Assert.IsNull(result.Resources);
        Assert.IsNull(result.Inbound);
        Assert.IsNull(result.Outbound);
    }

    /// <summary>同产品不同箱单行完整保留，命令、单据、行身份在重试中稳定。</summary>
    [TestMethod]
    public void StableCommandsPreservePhysicalLinesAndTypes()
    {
        var command = Guid.NewGuid();
        var head = Guid.NewGuid();
        var product = Guid.NewGuid();
        var warehouse = Guid.NewGuid();
        var purchase = Guid.NewGuid();
        var action = new PackingListPostingPreviewBuilder.Action("Shipment", Guid.NewGuid(), Guid.NewGuid(), product,
            "PCS", warehouse, null, Guid.NewGuid(), null, 10, 0, 2, 1);
        var preview = new PackingListPostingPreviewBuilder.Preview("token",
            [action, action with { PackingListLineId = Guid.NewGuid() }, action with { Kind = "Consumption" },
                action with { Kind = "Receipt", PurchaseLineId = purchase }], []);
        AutomaticStockKey[] keys = [new(warehouse, product)];
        var builder = new PackingListAutomaticCommandBuilder();
        var first = builder.Build(command, head, "PL", DateTime.Today, preview, keys, [purchase]);
        var second = builder.Build(command, head, "PL", DateTime.Today, preview with { Actions = preview.Actions.Reverse().ToArray() }, keys, [purchase]);
        Assert.AreEqual(2, first.Outbound!.Groups.Count);
        Assert.AreEqual(2, first.Outbound.Groups.Single(x => x.OutboundType == OutboundDocumentTypeEnum.FinalCustomerShipment).Lines.Count);
        Assert.AreEqual(4, first.LineActions.Count);
        CollectionAssert.AreEquivalent(first.LineActions.Keys.ToArray(), second.LineActions.Keys.ToArray());
        foreach (var pair in first.LineActions) Assert.AreEqual(pair.Value, second.LineActions[pair.Key]);
        Assert.AreNotEqual(first.Inbound!.CommandId, first.Outbound.CommandId);
        Assert.AreEqual(4, first.Resources!.CommandItemIds.Count);
    }

    /// <summary>缺少候选全集中的库存键时不能临时补锁后继续。</summary>
    [TestMethod]
    public void RejectsUndeclaredStock()
    {
        var action = new PackingListPostingPreviewBuilder.Action("Shipment", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "PCS", Guid.NewGuid(), null, null, null, 1, 0, 1, 1);
        Assert.ThrowsExactly<ArgumentException>(() => new PackingListAutomaticCommandBuilder().Build(Guid.NewGuid(), Guid.NewGuid(),
            "PL", DateTime.Today, new("token", [action], []), [], []));
    }
}
