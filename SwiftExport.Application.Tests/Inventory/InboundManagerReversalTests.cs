using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>人工入库整单反向的数量、幂等与事实证明；不冒充真实数据库锁验证。</summary>
public sealed partial class InboundManagerServiceTests
{
    /// <summary>余额足够时扣减一次，不重新要求采购可收；重放不重复扣库存。</summary>
    [TestMethod]
    public async Task Reverse_DeductsOnceWithoutPurchaseEligibility()
    {
        var f = await Fixture.Reversible();
        f.Receivable = false;
        Assert.IsTrue((await f.Service.PreviewReverseAsync(f.Header!.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        var cmd = new ReverseInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "  录错数量  " };
        var result = await f.Service.ReverseAsync(f.Header.Uid, cmd);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("录错数量", result.Data.ReverseReason);
        Assert.AreEqual(90m, f.Balance);
        Assert.AreEqual(10m, f.SavedLines.Single().Quantity);
        Assert.AreEqual(InboundPostingStatusEnum.Reversed, f.Header.PostingStatus);
        Assert.IsTrue((await f.Service.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(1, f.ReverseCalls);
        Assert.IsFalse((await f.Service.ReverseAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 3, Reason = "重复" })).Success);
    }

    /// <summary>同产品多行按合计扣减，余额不足或原流水异常不能部分撤销。</summary>
    [TestMethod]
    public async Task Reverse_GroupsQuantityAndRejectsInvalidFacts()
    {
        var f = await Fixture.Reversible();
        var first = f.SavedLines[0];
        var second = new InboundDocumentLine { Uid = Guid.NewGuid(), InboundDocumentId = f.Header!.Uid,
            ProductId = first.ProductId, SourceLineId = Guid.NewGuid(), Quantity = 10, UnitSnapshot = "PCS" };
        f.SavedLines.Add(second); f.Facts.Add(f.Fact(second)); f.Balance = 15;
        var preview = await f.Service.PreviewReverseAsync(f.Header.Uid, new() { ExpectedVersion = 2 });
        Assert.IsFalse(preview.Data.CanReverse);
        Assert.AreEqual(20m, preview.Data.StockImpacts.Single().ReverseQuantity);
        f.Balance = 25;
        Assert.IsTrue((await f.Service.PreviewReverseAsync(f.Header.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        f.Facts[0].ReplacementOfLedgerId = Guid.NewGuid();
        Assert.IsFalse((await f.Service.PreviewReverseAsync(f.Header.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
    }

    /// <summary>预览后余额变化重新阻断；日志失败回滚原状态和余额。</summary>
    [TestMethod]
    public async Task Reverse_RechecksBalanceAndRollsBackLogFailure()
    {
        var f = await Fixture.Reversible();
        Assert.IsTrue((await f.Service.PreviewReverseAsync(f.Header!.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        f.Balance = 5;
        var cmd = new ReverseInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "错误" };
        Assert.IsFalse((await f.Service.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(0, f.ReverseCalls);
        f.Balance = 100; f.FailLog = true;
        Assert.IsFalse((await f.Service.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(100m, f.Balance);
        Assert.AreEqual(InboundPostingStatusEnum.Posted, f.Header.PostingStatus);
        Assert.AreEqual(2, f.Header.Version);
    }

    private sealed partial class Fixture
    {
        public decimal Balance = 100;
        public int ReverseCalls;
        public List<InventoryLedgerEntry> Facts = [];
        public static async Task<Fixture> Reversible()
        {
            var f = await Prepared();
            Assert.IsTrue((await f.Service.PostAsync(f.Header!.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
            f.Facts.AddRange(f.SavedLines.Select(f.Fact));
            return f;
        }
        public InventoryLedgerEntry Fact(InboundDocumentLine row) => new() { Uid = Guid.NewGuid(), CommandItemId = row.Uid,
            SourceDocumentId = Header!.Uid, SourceDocumentType = "InboundDocument", SourceLineId = row.Uid,
            WarehouseSiteId = WarehouseId, ProductId = row.ProductId, QuantityDelta = row.Quantity, UnitSnapshot = row.UnitSnapshot,
            TransactionType = InventoryTransactionTypeEnum.PurchaseReceiptInbound };
        private object ReverseRepository(string method) => method switch
        {
            "ReadReverseLedgersAsync" => Task.FromResult<IReadOnlyList<InventoryLedgerEntry>>(Facts),
            "ReadReverseBalanceAsync" => Task.FromResult<InventoryBalance?>(new() { OnHandQty = Balance, UnitSnapshot = "PCS" }),
            "ReverseWarehouseExistsAsync" => Task.FromResult(true),
            "MarkReversedAsync" => MarkReversed(),
            _ => throw new NotSupportedException(method)
        };
        private Task MarkReversed() { Header!.Version++; Header.PostingStatus = InboundPostingStatusEnum.Reversed; return Task.CompletedTask; }
        private Task<IReadOnlyList<InventoryPostingResultDto>> ReverseInventory(IReadOnlyList<InventoryReverseItem> items)
        {
            Assert.IsTrue(_sourceLocked);
            var delta = items.Sum(x => Facts.Single(f => f.Uid == x.OriginalLedgerId).QuantityDelta);
            if (Balance < delta) throw new InventoryPostingException("余额不足");
            var previous = Balance; var finished = Connection.Finished;
            Connection.Finished = committed => { if (!committed) Balance = previous; finished?.Invoke(committed); };
            Balance -= delta; ReverseCalls++;
            return Task.FromResult<IReadOnlyList<InventoryPostingResultDto>>([]);
        }
    }
}
