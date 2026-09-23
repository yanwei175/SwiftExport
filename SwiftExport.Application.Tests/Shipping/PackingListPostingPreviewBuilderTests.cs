using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Core.Entities.Shipping;
using static SwiftExport.Application.Services.Shipping.PackingListPostingPreviewBuilder;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证箱单只读预览的来源交付、库存方案及指纹；不替代数据库查询和并发验证。</summary>
[TestClass]
public sealed class PackingListPostingPreviewBuilderTests
{
    /// <summary>无库存来源不要求需求和采购，同时仍参与客户和交付数量检查。</summary>
    [TestMethod]
    public void DeliveryOnlyNeedsNoStock()
    {
        var f = new Fixture();
        var source = f.Source with { DeliveryOnly = true, RootId = null, PurchaseLineIds = [] };
        var result = f.Build(source);
        Assert.AreEqual(0, result.Blockers.Count);
        Assert.AreEqual("Delivery", result.Actions.Single().Kind);
        Assert.IsNotNull(result.Token);
        Assert.AreEqual("CUSTOMER_MISMATCH", f.Build(source with { CustomerId = Guid.NewGuid() }).Blockers.Single().Code);
        Assert.AreEqual("DELIVERY_EXCEEDED", f.Build(source with { RemainingQty = 1 }).Blockers.Single().Code);
    }

    /// <summary>既有库存足够时不要求关联采购仍可新增收货。</summary>
    [TestMethod]
    public void ExistingStockAvoidsAutomaticReceipt()
    {
        var f = new Fixture();
        var result = f.Build(f.Source, [f.Stock]);
        Assert.AreEqual(0, result.Blockers.Count);
        Assert.AreEqual("Shipment", result.Actions.Single().Kind);
        Assert.AreEqual(10m, result.Actions.Single().Qty);
    }

    /// <summary>仅缺口新增收货并从同仓发货，执行数量守恒。</summary>
    [TestMethod]
    public void OnlyShortfallIsReceived()
    {
        var f = new Fixture();
        var result = f.Build(f.Source, [f.Stock with { Qty = 6 }], [f.Receipt]);
        Assert.AreEqual(0, result.Blockers.Count);
        Assert.AreEqual(4m, result.Actions.Where(x => x.Kind == "Receipt").Sum(x => x.Qty));
        Assert.AreEqual(10m, result.Actions.Where(x => x.Kind == "Shipment").Sum(x => x.Qty));
        Assert.AreEqual(f.Receipt.Id, result.Actions.Single(x => x.Kind == "Receipt").PurchaseLineId);
    }

    /// <summary>客供件库存无需自身采购关系，成品补入库不会代替耗用库存。</summary>
    [TestMethod]
    public void ComponentsNeedStockButNoPurchaseLink()
    {
        var f = new Fixture();
        var component = new Component(Guid.NewGuid(), Guid.NewGuid(), "PCS", 20, 1);
        var source = f.Source with { Components = [component] };
        var stock = f.Stock with { Id = Guid.NewGuid(), ProductId = component.ProductId, Qty = 2 };
        var result = f.Build(source, [stock], [f.Receipt]);
        Assert.AreEqual(0, result.Blockers.Count);
        Assert.AreEqual(2m, result.Actions.Single(x => x.Kind == "Consumption").Qty);
        var blocked = f.Build(source, [], [f.Receipt]);
        Assert.AreEqual("COMPONENT_SHORTFALL", blocked.Blockers.Single().Code);
        Assert.AreEqual(0, blocked.Actions.Count);
        Assert.IsNull(blocked.Token);
    }

    /// <summary>版本和业务日期改变时令牌失效，同样事实重复计算不变化。</summary>
    [TestMethod]
    public void TokenIncludesSourceVersionAndBusinessDate()
    {
        var f = new Fixture();
        var first = f.Build(f.Source, [f.Stock]);
        Assert.AreEqual(first.Token, f.Build(f.Source, [f.Stock]).Token);
        Assert.AreNotEqual(first.Token, f.Build(f.Source with { Version = 2 }, [f.Stock]).Token);
        var secondDate = new PackingListPostingPreviewBuilder().Build(f.Head, [f.Line], [f.Source], [f.Stock], [], new DateTime(2026, 9, 18));
        Assert.AreNotEqual(first.Token, secondDate.Token);
    }

    /// <summary>物理拆箱保持来源总耗用只算一次。</summary>
    [TestMethod]
    public void SplitLinesPreserveTotalConsumption()
    {
        var f = new Fixture();
        var component = new Component(Guid.NewGuid(), Guid.NewGuid(), "PCS", 10, 1);
        f.Line.Qty = 7;
        var tail = new PackingListLine { Uid = Guid.NewGuid(), PackingListHeadId = f.Head.Uid, LineNumber = 2,
            GrossDemandId = f.Source.Id, ProductId = f.Source.ProductId, UnitSnapshot = "PCS", Qty = 3 };
        var result = new PackingListPostingPreviewBuilder().Build(f.Head, [tail, f.Line],
            [f.Source with { Components = [component] }],
            [f.Stock, f.Stock with { Id = Guid.NewGuid(), ProductId = component.ProductId, Qty = 1 }], [], new DateTime(2026, 9, 17));
        Assert.AreEqual(0, result.Blockers.Count);
        Assert.AreEqual(1m, result.Actions.Where(x => x.Kind == "Consumption").Sum(x => x.Qty));
        Assert.AreEqual(0.3m, result.Actions.Single(x => x.Kind == "Consumption" && x.PackingListLineId == tail.Uid).Qty);
    }

    /// <summary>拼箱只改变包装事实和预览令牌，不改变收货、发货及客供件动作。</summary>
    [TestMethod]
    public void PackingGroupChangesTokenNotInventoryActions()
    {
        var f = new Fixture();
        var component = new Component(Guid.NewGuid(), Guid.NewGuid(), "PCS", 10, 1);
        var source = f.Source with { Components = [component] };
        var stock = f.Stock with { ProductId = component.ProductId, Qty = 1 };
        var original = f.Build(source, [stock], [f.Receipt]);
        f.Line.PackingGroupId = Guid.NewGuid();
        f.Line.PcsPerPackage = f.Line.Qty;
        var mixed = f.Build(source, [stock], [f.Receipt]);
        Assert.AreEqual(0, mixed.Blockers.Count);
        CollectionAssert.AreEqual(original.Actions.ToArray(), mixed.Actions.ToArray());
        Assert.AreNotEqual(original.Token, mixed.Token);
    }

    /// <summary>最小有效规范化事实，避免测试依赖外部主数据。</summary>
    private sealed class Fixture
    {
        public PackingListHead Head { get; } = new() { Uid = Guid.NewGuid(), CustomerId = Guid.NewGuid(), PackingListNo = "PL-TEST" };
        public Source Source { get; }
        public PackingListLine Line { get; }
        public Stock Stock { get; }
        public Receipt Receipt { get; }

        public Fixture()
        {
            var product = Guid.NewGuid();
            var purchase = Guid.NewGuid();
            var warehouse = Guid.NewGuid();
            Source = new(Guid.NewGuid(), Head.CustomerId, product, "PCS", true, false, 100, Guid.NewGuid(), 100, 1, [purchase], [warehouse], []);
            Line = new() { Uid = Guid.NewGuid(), PackingListHeadId = Head.Uid, LineNumber = 1, GrossDemandId = Source.Id,
                ProductId = product, UnitSnapshot = "PCS", Qty = 10 };
            Stock = new(Guid.NewGuid(), warehouse, "W1", product, "PCS", 10, 1);
            Receipt = new(purchase, warehouse, "W1", product, "PCS", 10, "PO1", 1, 1);
        }

        public Preview Build(Source source, IReadOnlyList<Stock>? stocks = null, IReadOnlyList<Receipt>? receipts = null) =>
            new PackingListPostingPreviewBuilder().Build(Head, [Line], [source], stocks ?? [], receipts ?? [], new DateTime(2026, 9, 17));
    }
}
