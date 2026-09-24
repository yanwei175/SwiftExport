using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Application.Services.Inventory.Posting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证入库过账的只读预览、容量、来源锁序及原命令重放；不连接数据库。</summary>
public sealed partial class InboundManagerServiceTests
{
    /// <summary>过账细权限在命令回执读取前检查；预览不新增事务、审计或回执。</summary>
    [TestMethod]
    public async Task Posting_RequiresPermissionBeforeReceipt_AndPreviewStaysReadOnly()
    {
        var f = await Prepared();
        var reads = f.ReceiptReads;
        var writes = f.ReceiptWrites;
        var commits = f.Connection.Commits;
        var logs = f.LogReads;
        Assert.IsTrue((await f.PostingService.PreviewPostAsync(f.Header!.Uid, new() { ExpectedVersion = 1 })).Success);
        Assert.AreEqual(commits, f.Connection.Commits);
        Assert.AreEqual(writes, f.ReceiptWrites);
        Assert.AreEqual(reads, f.ReceiptReads);
        f.AllowPost = false;
        var refused = await f.PostingService.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 });
        Assert.AreEqual(ServiceErrorCodes.Forbidden, refused.Error.Code);
        Assert.AreEqual(reads, f.ReceiptReads);
        Assert.AreEqual(logs, f.LogReads);
        Assert.AreEqual(0, f.PostCalls);
        Assert.AreEqual(commits, f.Connection.Commits);
    }

    /// <summary>命令格式验证先于回执；来源集合在取得来源锁后变化时拒绝过账。</summary>
    [TestMethod]
    public async Task Posting_ValidatesCommandAndLockedSourceSet()
    {
        var f = await Prepared();
        var reads = f.ReceiptReads;
        await Assert.ThrowsAsync<CommandValidationException>(() => f.PostingService.PostAsync(f.Header!.Uid,
            new PostInboundCommand { CommandId = Guid.Empty, ExpectedVersion = 1 }));
        Assert.AreEqual(reads, f.ReceiptReads);
        f.ChangeSourceOnLock = true;
        var result = await f.PostingService.PostAsync(f.Header!.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 });
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual(0, f.PostCalls);
        Assert.AreEqual(InboundPostingStatusEnum.Unposted, f.Header.PostingStatus);
    }

    /// <summary>状态与审计成功后回执写入失败须回滚整单，原 CommandId 可再次提交。</summary>
    [TestMethod]
    public async Task Posting_ReceiptFailureRollsBackAndCanRetry()
    {
        var f = await Prepared();
        var command = new PostInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1 };
        f.FailReceipt = true;
        var result = await f.PostingService.PostAsync(f.Header!.Uid, command);
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown, result.Error.Code);
        Assert.AreEqual(0m, f.Received);
        Assert.AreEqual(InboundPostingStatusEnum.Unposted, f.Header.PostingStatus);
        Assert.AreEqual(1, f.Header.Version);
        f.FailReceipt = false;
        Assert.IsTrue((await f.PostingService.PostAsync(f.Header.Uid, command)).Success);
        Assert.AreEqual(2, f.ReceiptWrites, "草稿和过账各保存一次成功回执。");
        Assert.AreEqual(0, f.LogReads, "过账不得读取旧审计日志作为机器回执。");
    }

    /// <summary>成功回执先于当前单据状态判定；后续状态变化不改变原命令的结果快照。</summary>
    [TestMethod]
    public async Task Posting_ReplaysReceiptBeforeCurrentVersionAndState()
    {
        var f = await Prepared();
        var command = new PostInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1 };
        var first = await f.PostingService.PostAsync(f.Header!.Uid, command);
        Assert.IsTrue(first.Success);
        var receiptReads = f.ReceiptReads;
        f.Header.Version = 99;
        f.Header.PostingStatus = InboundPostingStatusEnum.Reversed;
        var replay = await f.PostingService.PostAsync(f.Header.Uid, command);
        Assert.IsTrue(replay.Success);
        Assert.AreEqual(first.Data.Version, replay.Data.Version);
        Assert.AreEqual(1, f.PostCalls);
        Assert.AreEqual(receiptReads + 1, f.ReceiptReads);
    }

    /// <summary>预览不写入；独立采购可过账，库存来源只指向入库头行。</summary>
    [TestMethod]
    public async Task Posting_PreviewIsReadOnly_AndReplayDoesNotPostTwice()
    {
        var f = await Prepared();
        var preview = await f.PostingService.PreviewPostAsync(f.Header!.Uid, new() { ExpectedVersion = 1 });
        Assert.IsTrue(preview.Data.CanPost, preview.Message);
        Assert.AreEqual(0, f.PostCalls);
        var command = new PostInboundCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1 };
        var first = await f.PostingService.PostAsync(f.Header.Uid, command);
        Assert.IsTrue(first.Success, first.Message);
        Assert.AreEqual(2, first.Data.Version);
        Assert.AreEqual("InboundDocument", f.PostedItems.Single().SourceDocumentType);
        Assert.AreEqual(f.Header.Uid, f.PostedItems.Single().SourceDocumentId);
        Assert.AreEqual(f.SavedLines.Single().Uid, f.PostedItems.Single().SourceLineId);
        Assert.IsTrue((await f.PostingService.PostAsync(f.Header.Uid, command)).Success);
        Assert.AreEqual(1, f.PostCalls);
        command.ExpectedVersion++;
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, (await f.PostingService.PostAsync(f.Header.Uid, command)).Error.Code);
        Assert.IsFalse((await f.PostingService.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 2 })).Success);
    }

    /// <summary>已收净额、状态、单位、权限或历史身份不满足时不得调用库存写入。</summary>
    [TestMethod]
    public async Task Posting_RevalidatesCapacityAndBusinessGuards()
    {
        var f = await Prepared();
        f.Received = 95;
        Assert.IsFalse((await f.PostingService.PreviewPostAsync(f.Header!.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        Assert.IsFalse((await f.PostingService.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
        Assert.AreEqual(0, f.PostCalls);
        f.Received = 0; f.Receivable = false;
        Assert.IsFalse((await f.PostingService.PreviewPostAsync(f.Header.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        f.Receivable = true; f.PostingUnit = "KG";
        Assert.IsFalse((await f.PostingService.PreviewPostAsync(f.Header.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        f.PostingUnit = "PCS"; f.Unproven = true;
        Assert.IsFalse((await f.PostingService.PreviewPostAsync(f.Header.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        f.Unproven = false; f.ActiveWarehouse = false;
        Assert.IsFalse((await f.PostingService.PreviewPostAsync(f.Header.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        f.ActiveWarehouse = true; f.AllowPost = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.PostingService.PreviewPostAsync(f.Header.Uid, new() { ExpectedVersion = 1 })).Error.Code);
    }

    /// <summary>库存或日志失败回滚，不能保留过账状态。</summary>
    [TestMethod]
    public async Task Posting_FailureRollsBackStateAndInventory()
    {
        var f = await Prepared();
        f.FailLog = true;
        var result = await f.PostingService.PostAsync(f.Header!.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0m, f.Received);
        Assert.AreEqual(InboundPostingStatusEnum.Unposted, f.Header.PostingStatus);
        Assert.AreEqual(1, f.Header.Version);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        f.FailLog = false; f.FailInventory = true;
        Assert.IsFalse((await f.PostingService.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Success);
        Assert.AreEqual(2, f.Connection.Rollbacks);
    }

    /// <summary>两次请求先通过相同预览，来源锁串行后只有一张满额单可以提交。</summary>
    [TestMethod]
    public async Task Posting_ConcurrentDraftsCannotExceedSharedSource()
    {
        var shared = new SharedReceipt();
        var first = await Prepared(); var second = await Prepared();
        second.SourceId = first.SourceId;
        second.SavedLines[0].SourceLineId = first.SourceId;
        first.Shared = shared; second.Shared = shared;
        first.SavedLines[0].Quantity = 100; second.SavedLines[0].Quantity = 100;
        Assert.IsTrue((await first.PostingService.PreviewPostAsync(first.Header!.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        Assert.IsTrue((await second.PostingService.PreviewPostAsync(second.Header!.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        await shared.Gate.WaitAsync();
        var a = first.PostingService.PostAsync(first.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 });
        var b = second.PostingService.PostAsync(second.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 });
        shared.Gate.Release();
        var results = await Task.WhenAll(a, b);
        Assert.AreEqual(1, results.Count(x => x.Success));
        Assert.AreEqual(100m, shared.Quantity);
        Assert.AreEqual(1, first.PostCalls + second.PostCalls);
    }

    /// <summary>预览后版本或采购状态变化必须重新阻断；客供件仍通过同一库存内核。</summary>
    [TestMethod]
    public async Task Posting_RechecksAfterPreview_AndSupportsCustomerSupplied()
    {
        var f = await Prepared();
        Assert.IsTrue((await f.PostingService.PreviewPostAsync(f.Header!.Uid, new() { ExpectedVersion = 1 })).Data.CanPost);
        f.Header.Version = 2;
        Assert.AreEqual(ServiceErrorCodes.VersionConflict,
            (await f.PostingService.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 1 })).Error.Code);
        f.Receivable = false;
        Assert.IsFalse((await f.PostingService.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 2 })).Success);
        Assert.AreEqual(0, f.PostCalls);
        f.Receivable = true;
        f.CustomerSupplied = true;
        Assert.IsTrue((await f.PostingService.PostAsync(f.Header.Uid, new() { CommandId = Guid.NewGuid(), ExpectedVersion = 2 })).Success);
        Assert.AreEqual(InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound, f.PostedItems.Single().TransactionType);
    }

    /// <summary>建立不带需求或 Link 的采购草稿作为过账测试前置。</summary>
    private static async Task<Fixture> Prepared()
    {
        var f = new Fixture();
        Assert.IsTrue((await f.DraftService.CreateAsync(f.Create())).Success);
        return f;
    }

    private sealed class SharedReceipt
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public decimal Quantity;
    }

    private sealed partial class Fixture
    {
        public bool AllowPost = true, Receivable = true, ActiveWarehouse = true, Unproven, FailInventory, CustomerSupplied;
        public string PostingUnit = "PCS";
        public decimal Received;
        public int PostCalls;
        public IReadOnlyList<InventoryPostingItem> PostedItems = [];
        public SharedReceipt? Shared;
        private bool _sourceLocked;
        private decimal _postedDelta;
        public bool ChangeSourceOnLock;

        private object PostingRepository(string method, object?[] args) => method switch
        {
            "GetDetailAsync" => Task.FromResult<InboundManagerDetailDto?>(new()
            {
                Header = new() { Uid = Header!.Uid, Version = Header.Version, DocumentNo = Header.DocumentNo,
                    WarehouseSiteId = Header.WarehouseSiteId, ReceiptDate = Header.ReceiptDate, PostingStatus = Header.PostingStatus },
                Lines = SavedLines.Select(x => new InboundLineDetailDto { Uid = x.Uid, InboundDocumentId = x.InboundDocumentId,
                    ProductId = x.ProductId, SourceLineId = x.SourceLineId, Quantity = x.Quantity, UnitSnapshot = x.UnitSnapshot,
                    LineNumber = x.LineNumber, SkuSnapshot = x.SkuSnapshot }).ToList()
            }),
            "ReadPostingSourcesAsync" => Task.FromResult<IReadOnlyList<InboundPurchasePostingSourceDto>>([new()
            { SourceLineId = SourceId, ProductId = SavedLines[0].ProductId, Unit = PostingUnit, CurrentUnit = "PCS",
                OriginalQty = 100, PostedNetQty = Shared?.Quantity ?? Received, IsReceivable = Receivable,
                IsCustomerSupplied = CustomerSupplied, SourceNo = "PO-1" }]),
            "ReadActiveWarehouseNameAsync" => Task.FromResult(ActiveWarehouse ? "仓库" : null),
            "ReadBalanceAsync" => Task.FromResult(0m),
            "HasUnprovenReceiptAsync" => Task.FromResult(Unproven),
            "LockPurchaseSourcesAsync" => LockSource(),
            "MarkPostedAsync" => MarkPosted(),
            _ => ReverseRepository(method)
        };

        private async Task LockSource()
        {
            if (Shared is not null) await Shared.Gate.WaitAsync();
            _sourceLocked = true;
            var knownLogs = _logs.Keys.ToHashSet();
            var knownReceipts = _receipts.Keys.ToHashSet();
            var version = Header!.Version;
            var status = Header.PostingStatus;
            if (ChangeSourceOnLock) SavedLines[0].SourceLineId = Guid.NewGuid();
            Connection.Finished = committed =>
            {
                if (!committed)
                {
                    if (Shared is not null) Shared.Quantity -= _postedDelta; else Received -= _postedDelta;
                    Header.Version = version; Header.PostingStatus = status;
                    foreach (var id in _logs.Keys.Except(knownLogs).ToArray()) _logs.Remove(id);
                    foreach (var id in _receipts.Keys.Except(knownReceipts).ToArray()) _receipts.Remove(id);
                }
                _postedDelta = 0;
                if (_sourceLocked && Shared is not null) Shared.Gate.Release();
                _sourceLocked = false;
            };
        }

        private Task MarkPosted() { Header!.Version++; Header.PostingStatus = InboundPostingStatusEnum.Posted; return Task.CompletedTask; }

        private Task<IReadOnlyList<InventoryPostingResultDto>> PostInventory(IReadOnlyList<InventoryPostingItem> items)
        {
            Assert.IsTrue(_sourceLocked, "库存写入必须在来源锁之后。");
            Assert.IsNotNull(Session.Transaction);
            if (FailInventory) throw new InventoryPostingException("模拟库存拒绝");
            PostCalls++; PostedItems = items; _postedDelta = items.Sum(x => x.QuantityDelta);
            if (Shared is not null) Shared.Quantity += _postedDelta; else Received += _postedDelta;
            return Task.FromResult<IReadOnlyList<InventoryPostingResultDto>>([]);
        }
    }
}
