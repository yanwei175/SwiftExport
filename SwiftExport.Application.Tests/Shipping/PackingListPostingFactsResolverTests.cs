using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Shipping;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证领域原始事实到预览模型的准入，禁止丢失异常或恢复旧采购耗用限制。</summary>
[TestClass]
public sealed class PackingListPostingFactsResolverTests
{
    /// <summary>根需求、Link 和现有库存足够，不需要该采购仍可新增收货。</summary>
    [TestMethod]
    public void ExistingStockDoesNotNeedReceivablePurchase()
    {
        var f = new Fixture();
        var result = f.Resolve();
        Assert.AreEqual(0, result.Preview.Blockers.Count);
        Assert.AreEqual("Shipment", result.Preview.Actions.Single().Kind);
        Assert.AreEqual(1, result.PurchaseLineIds.Count);
    }

    /// <summary>当前来源内容变化后不能继续沿用旧拆解。</summary>
    [TestMethod]
    public void RejectsStaleAndAmbiguousRoots()
    {
        var f = new Fixture();
        f.Facts.Sources[0].DemandBasisFingerprint = "changed";
        Assert.AreEqual("DEMAND_STALE", f.Resolve().Preview.Blockers.Single().Code);
        f.Facts.Sources[0].DemandBasisFingerprint = "basis";
        f.Facts.Demands = [f.Root, new NetDemand { GrossDemandId = f.Source.Uid, ProductId = f.Product.Uid }];
        Assert.AreEqual("DEMAND_INVALID", f.Resolve().Preview.Blockers.Single().Code);
    }

    /// <summary>关联断裂必须阻断，不能凭库存掩盖普通来源的执行关系异常。</summary>
    [TestMethod]
    public void RejectsMismatchedPurchaseTarget()
    {
        var f = new Fixture();
        f.Facts.Links[0].TargetHeadId = Guid.NewGuid();
        Assert.AreEqual("EXECUTION_LINK_INVALID", f.Resolve().Preview.Blockers.Single().Code);
    }

    /// <summary>即使已有库存，未确认或非活动采购Link也不能伪装为正式执行依据。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public void DraftOrClosedPurchaseCannotSupplyFormalExecution(bool closed)
    {
        var f=new Fixture();
        if(closed)f.Facts.PurchaseHeads[0].LifecycleStatus=PurchaseOrderLifecycleStatusEnum.Closed;
        else f.Facts.PurchaseHeads[0].ConfirmationStatus=ConfirmationStatusEnum.待确认;
        Assert.AreEqual("EXECUTION_LINK_INVALID",f.Resolve().Preview.Blockers.Single().Code);
    }

    /// <summary>客供件使用需求数量快照，允许没有自己的 Link。</summary>
    [TestMethod]
    public void ComponentWithoutLinkCanConsumeStock()
    {
        var f = new Fixture();
        var component = new Product { Sku = "B", Unit = "PCS", MaterialType = MaterialTypesEnum.客供件 };
        f.Facts.Products = [f.Product, component];
        f.Facts.Demands = [f.Root, new NetDemand { GrossDemandId = f.Source.Uid, ParentNetDemandId = f.Root.Uid,
            RootNetDemandId = f.Root.Uid, DeliveryTargetNetDemandId = f.Root.Uid,
            DeliveryMode = DemandDeliveryModeEnum.ParentExecutionSupplier, MaterialTypeSnapshot = MaterialTypesEnum.客供件,
            ProductId = component.Uid, GrossQty = 20, DecompositionRevision = f.Root.DecompositionRevision }];
        f.Facts.Balances = [f.Facts.Balances[0], new InventoryBalance { ProductId = component.Uid,
            WarehouseSiteId = f.Warehouse.Uid, UnitSnapshot = "PCS", OnHandQty = 2 }];
        var result = f.Resolve();
        Assert.AreEqual(0, result.Preview.Blockers.Count);
        Assert.AreEqual(2m, result.Preview.Actions.Single(x => x.Kind == "Consumption").Qty);
    }

    /// <summary>售后无根需求及采购仍能交付，历史入库异常不影响无库存动作。</summary>
    [TestMethod]
    public void AfterSalesSkipsInventoryDependencies()
    {
        var f = new Fixture();
        f.Source.SourceType = DocumentTypeStaticsEnum.ComplaintReplenishment;
        f.Facts.Demands = [];
        f.Facts.Links = [];
        f.Facts.PurchaseLines = [];
        f.Facts.PurchaseHeads = [];
        f.Facts.Balances = [];
        f.Facts.Warehouses = [];
        var result = f.Resolve(true);
        Assert.AreEqual(0, result.Preview.Blockers.Count);
        Assert.AreEqual("Delivery", result.Preview.Actions.Single().Kind);
        Assert.AreEqual(0, result.Stocks.Count);
    }

    /// <summary>真实净收货余量决定自动入库容量，供应商虚拟仓必须唯一。</summary>
    [TestMethod]
    public void ReceiptCapacityAndWarehouseAreValidated()
    {
        var f = new Fixture();
        f.Facts.Balances = [];
        var receipt = new InboundPurchasePostingSourceDto { SourceLineId = f.Facts.PurchaseLines[0].Uid,
            PurchaseOrderId = f.Facts.PurchaseHeads[0].Uid, ProductId = f.Product.Uid, Unit = "PCS", CurrentUnit = "PCS",
            IsReceivable = true, OriginalQty = 20, PostedNetQty = 10 };
        var resolver = new PackingListPostingFactsResolver();
        var result = resolver.Resolve(f.Facts, [receipt], DateTime.Today, false);
        Assert.AreEqual(0, result.Preview.Blockers.Count);
        Assert.AreEqual(10m, result.Preview.Actions.Single(x => x.Kind == "Receipt").Qty);
        receipt.PostedNetQty = 15;
        Assert.AreEqual("SUPPLY_SHORTFALL", resolver.Resolve(f.Facts, [receipt], DateTime.Today, false).Preview.Blockers.Single().Code);
        receipt.PostedNetQty = 10;
        f.Facts.Warehouses = [f.Warehouse, new WarehouseSite { IsActive = true, SiteType = WarehouseSiteTypeEnum.SupplierVirtual,
            SupplierId = f.Warehouse.SupplierId, Code = "W2" }];
        Assert.AreEqual("SUPPLY_SHORTFALL", resolver.Resolve(f.Facts, [receipt], DateTime.Today, false).Preview.Blockers.Single().Code);
    }

    /// <summary>领域实体最小有效组合，只用于纯解析测试，不创建数据库。</summary>
    private sealed class Fixture
    {
        public PackingListPostingFacts Facts { get; } = new() { CustomerExists = true };
        public Product Product { get; } = new() { Sku = "A", Unit = "PCS" };
        public GrossDemand Source { get; }
        public NetDemand Root { get; }
        public WarehouseSite Warehouse { get; }
        public Fixture()
        {
            var customer = Guid.NewGuid();
            var supplier = Guid.NewGuid();
            Facts.Head = new PackingListHead { CustomerId = customer };
            Source = new GrossDemand { CustomerId = customer, ProductId = Product.Uid, UnitSnapshot = "PCS", DemandQty = 100,
                DemandBasisFingerprint = "basis" };
            Root = new NetDemand { GrossDemandId = Source.Uid, ProductId = Product.Uid, GrossQty = 100,
                SourceDemandBasisFingerprintSnapshot = "basis", DecompositionRevision = 1 };
            Root.RootNetDemandId = Root.Uid;
            Warehouse = new WarehouseSite { Code = "W1", SupplierId = supplier, IsActive = true, SiteType = WarehouseSiteTypeEnum.SupplierVirtual };
            var po = new PurchaseOrderHead { SupplierId = supplier,ConfirmationStatus=ConfirmationStatusEnum.已确认 };
            var line = new PurchaseOrderLine { PurchaseOrderId = po.Uid, ProductId = Product.Uid, Quantity = 100 };
            Facts.Sources = [Source]; Facts.Demands = [Root]; Facts.Products = [Product]; Facts.Warehouses = [Warehouse];
            Facts.PurchaseHeads = [po]; Facts.PurchaseLines = [line];
            Facts.Links = [new DemandExecutionLink { NetDemandId = Root.Uid, TargetType = "PO", TargetHeadId = po.Uid,
                TargetLineId = line.Uid, AllocatedQty = 100 }];
            Facts.Lines = [new PackingListLine { PackingListHeadId = Facts.Head.Uid, LineNumber = 1, GrossDemandId = Source.Uid,
                ProductId = Product.Uid, UnitSnapshot = "PCS", Qty = 10 }];
            Facts.Balances = [new InventoryBalance { WarehouseSiteId = Warehouse.Uid, ProductId = Product.Uid, UnitSnapshot = "PCS", OnHandQty = 10 }];
        }
        public PackingListPostingFactsResolver.Resolved Resolve(bool unproven = false) =>
            new PackingListPostingFactsResolver().Resolve(Facts, [], DateTime.Today, unproven);
    }
}
