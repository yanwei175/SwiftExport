using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Models.Shipping;

namespace SwiftExport.Application.Tests.Shipping;

[TestClass]
public sealed class PackingListPreviewBuilderTests
{
    private readonly PackingListPreviewBuilder _builder = new();

    [TestMethod]
    public void Build_UsesAllocationInboundInsteadOfGlobalBalance_ForAutoInbound()
    {
        var snapshot = Snapshot(planned: 10, packing: 6, shipped: 2, inbound: 3, balance: 99);

        var result = _builder.Build(snapshot);

        Assert.AreEqual(5m, result.Lines[0].AutoInboundQty);
        Assert.IsFalse(result.HasBlockers);
    }

    [TestMethod]
    public void Build_BlocksDifferentFinalCustomer()
    {
        var snapshot = Snapshot();
        snapshot.Lines[0].CustomerId = Guid.NewGuid();

        var result = _builder.Build(snapshot);

        Assert.IsTrue(result.Blockers.Any(x => x.Code == "CUSTOMER_MISMATCH"));
    }

    [TestMethod]
    public void Build_BlocksOverShipmentAndNormalNegativeStock()
    {
        var snapshot = Snapshot(planned: 5, packing: 6, shipped: 0, inbound: 6, balance: 0);

        var result = _builder.Build(snapshot);

        Assert.IsTrue(result.Blockers.Any(x => x.Code == "SHIP_QUANTITY_EXCEEDED"));
        Assert.IsTrue(result.Blockers.Any(x => x.Code == "INSUFFICIENT_STOCK"));
    }

    [TestMethod]
    public void Build_AggregatesSameWarehouseProductBeforeCheckingStock()
    {
        var snapshot = Snapshot(planned: 10, packing: 4, shipped: 0, inbound: 4, balance: 5);
        var second = Snapshot(planned: 10, packing: 4, shipped: 0, inbound: 4, balance: 5).Lines[0];
        second.CustomerId = snapshot.Document.CustomerId;
        second.Line.Uid = Guid.NewGuid();
        second.Line.NetDemandAllocationId = Guid.NewGuid();
        second.Line.ProductId = snapshot.Lines[0].Line.ProductId;
        second.Line.WarehouseSiteId = snapshot.Lines[0].Line.WarehouseSiteId;
        snapshot.Lines = [snapshot.Lines[0], second];

        var result = _builder.Build(snapshot);

        Assert.IsTrue(result.Blockers.Any(x => x.Code == "INSUFFICIENT_STOCK"));
        Assert.AreEqual(-3m, result.InventoryEffects.Single().BalanceAfter);
    }

    [TestMethod]
    public void Build_PartialCustomerSuppliedConsumption_RoundsAwayFromZero()
    {
        var snapshot = Snapshot(planned: 3, packing: 1, shipped: 0, inbound: 1, balance: 1);
        snapshot.Lines[0].CustomerSuppliedItems =
        [
            new PackingListCustomerSuppliedSnapshot
            {
                AllocationId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Sku = "CS-1", PlannedQty = 1,
                ParentPlannedQty = 3, HistoricalConsumptionQty = 0, BalanceQty = 1
            }
        ];

        var result = _builder.Build(snapshot);

        Assert.AreEqual(0.3333m, result.Lines[0].CustomerSuppliedConsumptionQty);
    }

    [TestMethod]
    public void Build_LastShipmentAbsorbsCustomerSuppliedTailDifference()
    {
        var snapshot = Snapshot(planned: 3, packing: 1, shipped: 2, inbound: 3, balance: 1);
        snapshot.Lines[0].CustomerSuppliedItems =
        [
            new PackingListCustomerSuppliedSnapshot
            {
                AllocationId = Guid.NewGuid(), ProductId = Guid.NewGuid(), Sku = "CS-1", PlannedQty = 1,
                ParentPlannedQty = 3, HistoricalConsumptionQty = 0.6667m, BalanceQty = 1
            }
        ];

        var result = _builder.Build(snapshot);

        Assert.AreEqual(0.3333m, result.Lines[0].CustomerSuppliedConsumptionQty);
    }

    [TestMethod]
    public void Build_ProducesStrongWarningsAndStableToken()
    {
        var snapshot = Snapshot(planned: 10, packing: 5, shipped: 0, inbound: 5, balance: 5);
        snapshot.Document.ShipmentDate = new DateTime(2026, 7, 1);
        snapshot.Lines[0].Line.SnapshotRequiredDate = new DateTime(2026, 7, 10);
        snapshot.Lines[0].ReadyQty = 2;
        var second = Snapshot().Lines[0];
        second.Line.Uid = Guid.NewGuid(); second.Line.NetDemandAllocationId = Guid.NewGuid(); second.Line.SnapshotRequiredDate = new DateTime(2026, 7, 20);
        snapshot.Lines = [snapshot.Lines[0], second];

        var first = _builder.Build(snapshot);
        var secondResult = _builder.Build(snapshot);

        CollectionAssert.IsSubsetOf(new[] { PackingListPreviewBuilder.EarlyShipment, PackingListPreviewBuilder.MixedRequiredDates, PackingListPreviewBuilder.ReadyQtyShortfall }, first.Warnings.Select(x => x.Code).ToArray());
        Assert.AreEqual(first.PreviewToken, secondResult.PreviewToken);
    }

    [TestMethod]
    public void Build_TokenChangesWhenReadyFactChanges()
    {
        var snapshot = Snapshot();
        var first = _builder.Build(snapshot);

        snapshot.Lines[0].ReadyQty--;
        var changed = _builder.Build(snapshot);

        Assert.AreNotEqual(first.PreviewToken, changed.PreviewToken);
    }

    private static PackingListPlanningSnapshot Snapshot(decimal planned = 10, decimal packing = 4, decimal shipped = 0, decimal inbound = 4, decimal balance = 4)
    {
        var customerId = Guid.NewGuid();
        return new PackingListPlanningSnapshot
        {
            Document = new PackingListDocument { Uid = Guid.NewGuid(), Version = 1, CustomerId = customerId, ShipmentDate = new DateTime(2026, 7, 17) },
            Lines =
            [
                new PackingListPlanningLineSnapshot
                {
                    CustomerId = customerId, IsSourceActive = true, IsProductActive = true, IsWarehouseActive = true,
                    DemandVersion = 1, SourceVersion = 1, HasOrderPoolOwnership = true,
                    PlannedQty = planned, ShippedNetQty = shipped, SourceInboundNetQty = inbound, ReadyQty = planned,
                    BalanceQty = balance, BalanceVersion = 1,
                    Line = new PackingListContributionLine
                    {
                        Uid = Guid.NewGuid(), Version = 1, NetDemandAllocationId = Guid.NewGuid(), ProductId = Guid.NewGuid(), WarehouseSiteId = Guid.NewGuid(),
                        SourceDemandVersionSnapshot = 1, SourceVersionSnapshot = 1,
                        SkuSnapshot = "SKU-1", Quantity = packing
                    }
                }
            ]
        };
    }
}
