using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Application.Tests.Inventory;
/// <summary>验证整单反向的事实证明、幂等、授权和事务边界；不模拟真实 SQL 锁实现。</summary>
public sealed partial class OutboundManagerServiceTests
{
    /// <summary>完整恢复只发生一次，保留原原因和身份；不同命令不能再次恢复。</summary>
    [TestMethod]
    public async Task Reverse_RestoresOnceAndPreservesOriginal()
    {
        var f = await Fixture.Posted(); f.Header!.IssueReason = null; // 历史原因缺失不阻断合法纠错。
        var preview = await f.Service.PreviewReverseAsync(f.Header.Uid, new() { ExpectedVersion = 2 });
        Assert.IsTrue(preview.Data.CanReverse, string.Join(";", preview.Data.Blockers));
        Assert.AreEqual(100m, preview.Data.StockImpacts.Single().ExpectedQuantity);
        var cmd = f.ReverseCommand();
        var first = await f.Service.ReverseAsync(f.Header.Uid, cmd);
        Assert.IsTrue(first.Success, first.Message);
        Assert.IsTrue((await f.Service.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(1, f.ReverseCalls); Assert.AreEqual(100m, f.Balance);
        Assert.AreEqual(3, f.LogCount, "草稿、过账和首次反向各写一条审计；重放不追加。");
        Assert.AreEqual(OutboundPostingStatusEnum.Reversed, f.Header.PostingStatus);
        Assert.IsNull(f.Header.IssueReason);
        Assert.AreEqual(f.Facts.Single().Uid, f.ReverseItems.Single().OriginalLedgerId);
        Assert.AreEqual(f.SavedLines.Single().Uid, f.ReverseItems.Single().SourceLineId);
        Assert.AreNotEqual(f.SavedLines.Single().CommandItemId, f.ReverseItems.Single().CommandItemId);
        cmd.Reason = "不同原因";
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.Service.ReverseAsync(f.Header.Uid, cmd)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.Service.PostAsync(f.Header.Uid,
            new() { CommandId = cmd.CommandId, ExpectedVersion = 3 })).Error.Code);
        Assert.IsFalse((await f.Service.ReverseAsync(f.Header.Uid, f.ReverseCommand())).Success);
        Assert.IsFalse((await f.Service.UpdateAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 3, Draft = f.Create().Draft })).Success);
        Assert.IsFalse((await f.Service.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 3 })).Success);
    }

    /// <summary>原流水任何身份或数量不一致、缺失、附加或纠错关系都阻断整单。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)] [DataRow(6)] [DataRow(7)] [DataRow(8)] [DataRow(9)]
    public async Task Reverse_RejectsUnprovenFacts(int fault)
    {
        var f = await Fixture.Posted(); var fact = f.Facts[0];
        switch (fault)
        {
            case 0: f.Facts.Clear(); break;
            case 1: f.Facts.Add(new() { Uid = Guid.NewGuid(), ReversalOfLedgerId = fact.Uid }); break;
            case 2: fact.CommandItemId = Guid.NewGuid(); break;
            case 3: fact.WarehouseSiteId = Guid.NewGuid(); break;
            case 4: fact.ProductId = Guid.NewGuid(); break;
            case 5: fact.QuantityDelta = -9; break;
            case 6: fact.UnitSnapshot = "KG"; break;
            case 7: fact.SourceDocumentId = Guid.NewGuid(); break;
            case 8: fact.CorrectionGroupId = Guid.NewGuid(); break;
            case 9: fact.TransactionType = InventoryTransactionTypeEnum.Reversal; break;
        }
        Assert.IsFalse((await f.Service.PreviewReverseAsync(f.Header!.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        Assert.IsFalse((await f.Service.ReverseAsync(f.Header.Uid, f.ReverseCommand())).Success);
        Assert.AreEqual(0, f.ReverseCalls); Assert.AreEqual(90m, f.Balance);
    }

    /// <summary>独立权限、自动单、缺失余额、原因和锁繁忙不能绕过。</summary>
    [TestMethod]
    public async Task Reverse_GuardsPermissionsStateReasonAndProjection()
    {
        var f = await Fixture.Posted(); var id = f.Header!.Uid;
        f.Allowed = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.ReverseAsync(id, f.ReverseCommand())).Error.Code);
        f.Allowed = true;
        f.AllowReverse = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.PreviewReverseAsync(id, new() { ExpectedVersion = 2 })).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Service.ReverseAsync(id, f.ReverseCommand())).Error.Code);
        f.AllowReverse = true; f.AllowPost = false;
        f.Header.TriggerPackingListId = Guid.NewGuid();
        Assert.IsFalse((await f.Service.ReverseAsync(id, f.ReverseCommand())).Success);
        f.Header.TriggerPackingListId = null; f.MissingBalance = true;
        Assert.IsFalse((await f.Service.ReverseAsync(id, f.ReverseCommand())).Success);
        f.MissingBalance = false;
        foreach (var reason in new[] { "", " \t\n", new string('因',501) })
        {
            var cmd = f.ReverseCommand(); cmd.Reason = reason;
            await Assert.ThrowsAsync<CommandValidationException>(() => f.Service.ReverseAsync(id, cmd));
        }
        f.CommandLockResult = -1;
        Assert.AreEqual(OutboundErrorCodes.CommandBusy, (await f.Service.ReverseAsync(id, f.ReverseCommand())).Error.Code);
        f.CommandLockResult = 0; var valid = f.ReverseCommand(); valid.Reason = new string('因',500);
        Assert.IsTrue((await f.Service.ReverseAsync(id, valid)).Success);
        Assert.AreEqual("试制领用", f.Header.IssueReason);
    }

    /// <summary>草稿、终态、删除、非活动、非手工类型及过期版本均不可反向。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)] [DataRow(4)] [DataRow(5)]
    public async Task Reverse_RejectsInvalidDocumentState(int fault)
    {
        var f = await Fixture.Posted(); var h = f.Header!;
        switch (fault)
        {
            case 0: h.PostingStatus = OutboundPostingStatusEnum.Unposted; break;
            case 1: h.PostingStatus = OutboundPostingStatusEnum.Reversed; break;
            case 2: h.IsDeleted = true; break;
            case 3: h.LifecycleStatus = (InventoryDocumentLifecycleStatusEnum)8; break;
            case 4: h.DocumentType = (OutboundDocumentTypeEnum)20; break;
            case 5: h.Version++; break;
        }
        Assert.IsFalse((await f.Service.PreviewReverseAsync(h.Uid, new() { ExpectedVersion = 2 })).Data.CanReverse);
        var command = f.ReverseCommand(); command.ExpectedVersion = 2;
        Assert.IsFalse((await f.Service.ReverseAsync(h.Uid, command)).Success);
        Assert.AreEqual(0, f.ReverseCalls);
        Assert.AreEqual(90m, f.Balance);
    }

    /// <summary>库存或日志失败回滚状态和恢复量，同一原命令可在失败消除后重试。</summary>
    [TestMethod]
    public async Task Reverse_FailureRollsBack()
    {
        var f = await Fixture.Posted(); var cmd = f.ReverseCommand(); f.FailInventory = true;
        Assert.IsFalse((await f.Service.ReverseAsync(f.Header!.Uid, cmd)).Success);
        f.FailInventory = false; f.FailLog = true;
        Assert.IsFalse((await f.Service.ReverseAsync(f.Header.Uid, cmd)).Success);
        Assert.AreEqual(90m, f.Balance); Assert.AreEqual(2, f.Header.Version);
        Assert.AreEqual(OutboundPostingStatusEnum.Posted, f.Header.PostingStatus);
        Assert.IsNull(f.Header.ReverseReason);
        f.FailLog = false;
        Assert.IsTrue((await f.Service.ReverseAsync(f.Header.Uid, cmd)).Success);
    }

    private sealed partial class Fixture
    {
        public bool AllowReverse = true, MissingBalance;
        public int ReverseCalls;
        public List<InventoryLedgerEntry> Facts = [];
        public IReadOnlyList<InventoryReverseItem> ReverseItems = [];
        public ReverseOutboundCommand ReverseCommand() => new() { CommandId = Guid.NewGuid(), ExpectedVersion = Header!.Version, Reason = "错误出库" };
        public static async Task<Fixture> Posted()
        {
            var f = new Fixture(); await f.Service.CreateAsync(f.Create());
            await f.Service.PostAsync(f.Header!.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 });
            f.Facts = f.PostedItems.Select(x => new InventoryLedgerEntry { Uid = Guid.NewGuid(), CommandItemId = x.CommandItemId,
                SourceDocumentId = x.SourceDocumentId, SourceLineId = x.SourceLineId, SourceDocumentType = x.SourceDocumentType,
                ProductId = x.ProductId, WarehouseSiteId = x.WarehouseSiteId, UnitSnapshot = x.UnitSnapshot, QuantityDelta = x.QuantityDelta,
                TransactionType = x.TransactionType, SkuSnapshot = x.SkuSnapshot }).ToList();
            return f;
        }
        private object ReverseRepository(string method) => method switch
        {
            "ReadReverseLedgersAsync" => Task.FromResult<IReadOnlyList<InventoryLedgerEntry>>(Facts),
            "ReadReverseBalanceAsync" => Task.FromResult<InventoryBalance?>(MissingBalance ? null : new() { OnHandQty = Balance, UnitSnapshot = "PCS" }),
            "ReverseWarehouseExistsAsync" => Task.FromResult(true),
            "MarkReversedAsync" => MarkReversed(),
            _ => throw new NotSupportedException(method)
        };
        private Task MarkReversed() { Header!.Version++; Header.PostingStatus = OutboundPostingStatusEnum.Reversed; return Task.CompletedTask; }
        private Task<IReadOnlyList<InventoryPostingResultDto>> ReverseInventory(IReadOnlyList<InventoryReverseItem> items)
        {
            Assert.IsNotNull(Session.Transaction);
            if (FailInventory) throw new InventoryPostingException("模拟库存反向失败");
            var before = Balance; var version = Header!.Version;
            ReverseCalls++; ReverseItems = items; Balance -= Facts.Sum(x => x.QuantityDelta);
            Connection.Finished = commit => { if (!commit) { Balance = before; Header.Version = version;
                Header.PostingStatus = OutboundPostingStatusEnum.Posted; Header.ReverseReason = null; Header.ReversedBy = null; Header.ReversedDate = null; } };
            return Task.FromResult<IReadOnlyList<InventoryPostingResultDto>>([]);
        }
    }
}
