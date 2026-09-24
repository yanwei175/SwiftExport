using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>人工入库整单反向的数量、幂等与事实证明；不冒充真实数据库锁验证。</summary>
public sealed partial class InboundManagerServiceTests
{
    /// <summary>反向细权限先于回执检查，预览不新增写事务、回执或库存事实。</summary>
    [TestMethod]
    public async Task Reverse_RequiresPermissionBeforeReceipt_AndPreviewStaysReadOnly()
    {
        var f = await Fixture.Reversible();
        var reads = f.ReceiptReads;
        var writes = f.ReceiptWrites;
        var commits = f.Connection.Commits;
        Assert.IsTrue((await f.ReversalService.PreviewReverseAsync(f.Header!.Uid, new() { ExpectedVersion = 2 })).Success);
        Assert.AreEqual(reads, f.ReceiptReads);
        Assert.AreEqual(writes, f.ReceiptWrites);
        Assert.AreEqual(commits, f.Connection.Commits);
        f.AllowReverse = false;
        var result = await f.ReversalService.ReverseAsync(f.Header.Uid,
            new() { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "纠错" });
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.AreEqual(reads, f.ReceiptReads);
        Assert.AreEqual(0, f.ReverseCalls);
    }

    /// <summary>原因按 Trim 后语义参与指纹；等价重试只返回原结果，改原因或目标冲突。</summary>
    [TestMethod]
    public async Task Reverse_UsesNormalizedReasonForReplayAndConflict()
    {
        var f = await Fixture.Reversible();
        var commandId = Guid.NewGuid();
        var first = new ReverseInboundCommand { CommandId = commandId, ExpectedVersion = 2, Reason = "  录错数量  " };
        Assert.IsTrue((await f.ReversalService.ReverseAsync(f.Header!.Uid, first)).Success);
        Assert.AreEqual("  录错数量  ", first.Reason, "服务不改写调用方 DTO。");
        var equivalent = await f.ReversalService.ReverseAsync(f.Header.Uid,
            new() { CommandId = commandId, ExpectedVersion = 2, Reason = "录错数量" });
        Assert.IsTrue(equivalent.Success);
        Assert.AreEqual("录错数量", equivalent.Data.ReverseReason);
        Assert.AreEqual(1, f.ReverseCalls);
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.ReversalService.ReverseAsync(f.Header.Uid,
            new() { CommandId = commandId, ExpectedVersion = 2, Reason = "另一原因" })).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.ReversalService.ReverseAsync(Guid.NewGuid(),
            new() { CommandId = commandId, ExpectedVersion = 2, Reason = "录错数量" })).Error.Code);
        Assert.AreEqual(0, f.LogReads, "新反向命令不得读取旧审计日志作为机器回执。");
    }

    /// <summary>验证按 Trim 后的长度判断，格式错误在读取回执前拒绝。</summary>
    [TestMethod]
    public async Task Reverse_ValidatesTrimmedReasonBeforeReceipt()
    {
        var f = await Fixture.Reversible();
        var reads = f.ReceiptReads;
        await Assert.ThrowsAsync<CommandValidationException>(() => f.ReversalService.ReverseAsync(f.Header!.Uid,
            new() { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "   " }));
        Assert.AreEqual(reads, f.ReceiptReads);
        var longButTrimmed = "   " + new string('文', 500) + "   ";
        var result = await f.ReversalService.ReverseAsync(f.Header!.Uid,
            new() { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = longButTrimmed });
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(500, result.Data.ReverseReason.Length);
    }

    /// <summary>回执失败时反向余额、状态和审计一起回滚，原命令可重新执行。</summary>
    [TestMethod]
    public async Task Reverse_ReceiptFailureRollsBackAndCanRetry()
    {
        var f = await Fixture.Reversible();
        var command = new ReverseInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "纠错" };
        f.FailReceipt = true;
        var failed = await f.ReversalService.ReverseAsync(f.Header!.Uid, command);
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, failed.Error.Code);
        Assert.AreEqual(100m, f.Balance);
        Assert.AreEqual(InboundPostingStatusEnum.Posted, f.Header.PostingStatus);
        Assert.AreEqual(2, f.Header.Version);
        f.FailReceipt = false;
        Assert.IsTrue((await f.ReversalService.ReverseAsync(f.Header.Uid, command)).Success);
        Assert.AreEqual(90m, f.Balance);
        Assert.AreEqual(0, f.LogReads);
    }

    /// <summary>锁定来源后若完整来源集合变化，反向不得写入部分库存事实。</summary>
    [TestMethod]
    public async Task Reverse_RechecksLockedSourceSet()
    {
        var f = await Fixture.Reversible();
        f.ChangeSourceOnLock = true;
        var result = await f.ReversalService.ReverseAsync(f.Header!.Uid,
            new() { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "纠错" });
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual(0, f.ReverseCalls);
        Assert.AreEqual(100m, f.Balance);
    }

    /// <summary>库存端口与状态写入失败均回滚反向余额和单据，原命令仍可重试。</summary>
    [TestMethod]
    public async Task Reverse_InventoryAndStateFailuresRollBack()
    {
        var f = await Fixture.Reversible();
        var command = new ReverseInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "纠错" };
        f.FailReverseInventory = true;
        Assert.IsFalse((await f.ReversalService.ReverseAsync(f.Header!.Uid, command)).Success);
        Assert.AreEqual(100m, f.Balance);
        Assert.AreEqual(InboundPostingStatusEnum.Posted, f.Header.PostingStatus);
        f.FailReverseInventory = false;
        f.FailMarkReverse = true;
        Assert.IsFalse((await f.ReversalService.ReverseAsync(f.Header.Uid, command)).Success);
        Assert.AreEqual(100m, f.Balance);
        Assert.AreEqual(2, f.Header.Version);
        Assert.IsNull(f.Session.Transaction);
        f.FailMarkReverse = false;
        Assert.IsTrue((await f.ReversalService.ReverseAsync(f.Header.Uid, command)).Success);
        Assert.AreEqual(90m, f.Balance);
    }

    /// <summary>余额足够时扣减一次，不重新要求采购可收；重放不重复扣库存。</summary>
    [TestMethod]
    public async Task Reverse_DeductsOnceWithoutPurchaseEligibility()
    {
        var f = await Fixture.Reversible();
        f.Receivable = false;
        Assert.IsTrue((await f.ReversalService.PreviewReverseAsync(f.Header!.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        var cmd = new ReverseInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "  录错数量  " };
        var result = await f.ReversalService.ReverseAsync(f.Header.Uid, cmd);
        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual("录错数量", result.Data.ReverseReason);
        Assert.AreEqual(90m, f.Balance);
        Assert.AreEqual(10m, f.SavedLines.Single().Quantity);
        Assert.AreEqual(InboundPostingStatusEnum.Reversed, f.Header.PostingStatus);
        Assert.IsTrue((await f.ReversalService.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(1, f.ReverseCalls);
        Assert.IsFalse((await f.ReversalService.ReverseAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 3, Reason = "重复" })).Success);
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
        var preview = await f.ReversalService.PreviewReverseAsync(f.Header.Uid, new() { ExpectedVersion = 2 });
        Assert.IsFalse(preview.Data.CanReverse);
        Assert.AreEqual(20m, preview.Data.StockImpacts.Single().ReverseQuantity);
        f.Balance = 25;
        Assert.IsTrue((await f.ReversalService.PreviewReverseAsync(f.Header.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        f.Facts[0].ReplacementOfLedgerId = Guid.NewGuid();
        Assert.IsFalse((await f.ReversalService.PreviewReverseAsync(f.Header.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
    }

    /// <summary>预览后余额变化重新阻断；日志失败回滚原状态和余额。</summary>
    [TestMethod]
    public async Task Reverse_RechecksBalanceAndRollsBackLogFailure()
    {
        var f = await Fixture.Reversible();
        Assert.IsTrue((await f.ReversalService.PreviewReverseAsync(f.Header!.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        f.Balance = 5;
        var cmd = new ReverseInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 2, Reason = "错误" };
        Assert.IsFalse((await f.ReversalService.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(0, f.ReverseCalls);
        f.Balance = 100; f.FailLog = true;
        Assert.IsFalse((await f.ReversalService.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(100m, f.Balance);
        Assert.AreEqual(InboundPostingStatusEnum.Posted, f.Header.PostingStatus);
        Assert.AreEqual(2, f.Header.Version);
    }

    private sealed partial class Fixture
    {
        public decimal Balance = 100;
        public int ReverseCalls;
        public bool FailReverseInventory, FailMarkReverse;
        public List<InventoryLedgerEntry> Facts = [];
        public static async Task<Fixture> Reversible()
        {
            var f = await Prepared();
            Assert.IsTrue((await f.PostingService.PostAsync(f.Header!.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
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
        private Task MarkReversed()
        {
            if (FailMarkReverse) throw new IOException("模拟入库状态写入失败");
            Header!.Version++; Header.PostingStatus = InboundPostingStatusEnum.Reversed;
            return Task.CompletedTask;
        }
        private Task<IReadOnlyList<InventoryPostingResultDto>> ReverseInventory(IReadOnlyList<InventoryReverseItem> items)
        {
            Assert.IsTrue(_sourceLocked);
            if (FailReverseInventory) throw new InventoryPostingException("模拟反向库存失败");
            var delta = items.Sum(x => Facts.Single(f => f.Uid == x.OriginalLedgerId).QuantityDelta);
            if (Balance < delta) throw new InventoryPostingException("余额不足");
            var previous = Balance; var finished = Connection.Finished;
            Connection.Finished = committed => { if (!committed) Balance = previous; finished?.Invoke(committed); };
            Balance -= delta; ReverseCalls++;
            return Task.FromResult<IReadOnlyList<InventoryPostingResultDto>>([]);
        }
    }
}
