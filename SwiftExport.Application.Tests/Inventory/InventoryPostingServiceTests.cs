using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证库存内核的幂等、负库存、完整反向与原子纠错边界。</summary>
[TestClass]
public sealed class InventoryPostingServiceTests
{
    /// <summary>类型 22 只扣减库存，同一明细重放不重复扣减。</summary>
    [TestMethod]
    public async Task StockIssue_RequiresNegativeDelta_AndReplays()
    {
        var f = new Fixture();
        await f.Service.PostAsync(Guid.NewGuid(), [f.Item(10)]);
        var issue = f.Item(-4);
        issue.TransactionType = InventoryTransactionTypeEnum.StockIssueOutbound;
        var command = Guid.NewGuid();
        await f.Service.PostAsync(command, [issue]);
        await f.Service.PostAsync(command, [issue]);
        Assert.AreEqual(6m, f.Repository.Balance!.OnHandQty);
        Assert.AreEqual(2, f.Repository.Ledgers.Count);
        var invalid = new Fixture();
        var positive = invalid.Item(1);
        positive.TransactionType = InventoryTransactionTypeEnum.StockIssueOutbound;
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => invalid.Service.PostAsync(Guid.NewGuid(), [positive]));
        Assert.AreEqual(0, invalid.Repository.Ledgers.Count);
    }

    /// <summary>普通库存出库不能借类型 22 放行不足或超过四位小数的数量。</summary>
    [TestMethod]
    public async Task StockIssue_RejectsShortageAndPrecision()
    {
        foreach (var quantity in new[] { -1m, -0.00001m })
        {
            var f = new Fixture();
            var issue = f.Item(quantity);
            issue.TransactionType = InventoryTransactionTypeEnum.StockIssueOutbound;
            await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => f.Service.PostAsync(Guid.NewGuid(), [issue]));
            Assert.AreEqual(0, f.Repository.Ledgers.Count);
        }
    }
    /// <summary>同一外层命令可以按调用链分批追加不同明细，而每条明细仍只入账一次。</summary>
    [TestMethod]
    public async Task PostAsync_SameCommandDifferentItems_ShouldAppendAndReplayPerItem()
    {
        var fixture = new Fixture();
        var commandId = Guid.NewGuid();
        var first = fixture.Item(6);
        var second = fixture.Item(4);

        await fixture.Service.PostAsync(commandId, [first]);
        await fixture.Service.PostAsync(commandId, [second]);
        var replay = await fixture.Service.PostAsync(commandId, [first]);

        Assert.AreEqual(2, fixture.Repository.Ledgers.Count);
        Assert.AreEqual(10m, fixture.Repository.Balance!.OnHandQty);
        Assert.IsTrue(replay.Single().AlreadyApplied);
    }

    /// <summary>相同 CommandItemId 重放不同事实必须冲突，并回滚调用方事务。</summary>
    [TestMethod]
    public async Task PostAsync_ChangedReplay_ShouldConflictAndRollback()
    {
        var fixture = new Fixture();
        var commandId = Guid.NewGuid();
        var item = fixture.Item(5);
        await fixture.Service.PostAsync(commandId, [item]);
        item.QuantityDelta = 7;

        var exception = await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.PostAsync(commandId, [item]));

        StringAssert.Contains(exception.Message, "IDEMPOTENCY_CONFLICT");
        Assert.AreEqual(1, fixture.Transaction.Rollbacks);
    }

    /// <summary>普通出库不能形成负库存，失败不得留下流水。</summary>
    [TestMethod]
    public async Task PostAsync_NegativeStock_ShouldRollbackWithoutLedger()
    {
        var fixture = new Fixture();
        var item = fixture.Item(-1);
        item.TransactionType = InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound;

        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.PostAsync(Guid.NewGuid(), [item]));

        Assert.AreEqual(0, fixture.Repository.Ledgers.Count);
        Assert.AreEqual(1, fixture.Transaction.Rollbacks);
    }

    /// <summary>反向的仓库、产品、单位和数量完全来自原流水，并且同一原流水只能反向一次。</summary>
    [TestMethod]
    public async Task ReverseAsync_ShouldDeriveOriginalFactAndRejectSecondReverse()
    {
        var fixture = new Fixture();
        var original = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(8)])).Single();
        var reverse = fixture.Reverse(original.LedgerId);

        await fixture.Service.ReverseAsync(Guid.NewGuid(), [reverse]);
        var second = fixture.Reverse(original.LedgerId);
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.ReverseAsync(Guid.NewGuid(), [second]));

        var ledger = fixture.Repository.Ledgers.Single(x => x.ReversalOfLedgerId == original.LedgerId);
        Assert.AreEqual(-8m, ledger.QuantityDelta);
        Assert.AreEqual(fixture.WarehouseId, ledger.WarehouseSiteId);
        Assert.AreEqual(fixture.ProductId, ledger.ProductId);
        Assert.AreEqual(0m, fixture.Repository.Balance!.OnHandQty);
    }

    /// <summary>纠错允许反向后暂时为负，但替代事实必须使整组最终余额恢复为非负。</summary>
    [TestMethod]
    public async Task CorrectAsync_ShouldApplyReverseAndReplacementAsFrozenGroup()
    {
        var fixture = new Fixture();
        var original = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(10)])).Single();
        await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(-7)]);
        var commandId = Guid.NewGuid();
        var correction = fixture.Correction(original.LedgerId, 8);

        var first = await fixture.Service.CorrectAsync(commandId, [correction]);
        var replay = await fixture.Service.CorrectAsync(commandId, [correction]);

        Assert.AreEqual(1m, fixture.Repository.Balance!.OnHandQty);
        Assert.AreEqual(2, fixture.Repository.Ledgers.Count(x => x.CorrectionGroupId == commandId));
        Assert.IsTrue(replay.Reversals.Single().AlreadyApplied);
        Assert.IsTrue(replay.Replacements.Single().AlreadyApplied);
        Assert.AreEqual(original.LedgerId, fixture.Repository.Ledgers.Single(x => x.ReplacementOfLedgerId.HasValue).ReplacementOfLedgerId);
        Assert.AreEqual(-10m, first.Reversals.Single().Delta);
        Assert.AreEqual(-7m, first.Reversals.Single().AfterQty);
        Assert.AreEqual(1m, first.Replacements.Single().AfterQty);
    }

    /// <summary>纠错最终余额为负时，外层事务必须恢复余额和本次已追加的流水。</summary>
    [TestMethod]
    public async Task CorrectAsync_FinalNegative_ShouldRollbackBalanceAndGroupLedgers()
    {
        var fixture = new Fixture();
        var original = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(10)])).Single();
        await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(-7)]);
        var ledgerIds = fixture.Repository.Ledgers.Select(x => x.Uid).ToArray();
        fixture.BeginTransactionSnapshot();

        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() =>
            fixture.Service.CorrectAsync(Guid.NewGuid(), [fixture.Correction(original.LedgerId, 6)]));

        Assert.AreEqual(3m, fixture.Repository.Balance!.OnHandQty);
        CollectionAssert.AreEqual(ledgerIds, fixture.Repository.Ledgers.Select(x => x.Uid).ToArray());
        Assert.IsFalse(fixture.Repository.Ledgers.Any(x => x.CorrectionGroupId.HasValue));
        Assert.AreEqual(1, fixture.Transaction.Rollbacks);
    }

    /// <summary>纠错命令只允许精确集合重放，不能漏项、增项、换原流水或重复原流水。</summary>
    [TestMethod]
    public async Task CorrectAsync_ReplayMustMatchExactOriginalSet()
    {
        var fixture = new Fixture();
        var first = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(10)])).Single();
        var second = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(5)])).Single();
        var third = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(2)])).Single();
        var commandId = Guid.NewGuid();
        var firstCorrection = fixture.Correction(first.LedgerId, 8);
        var secondCorrection = fixture.Correction(second.LedgerId, 4);
        await fixture.Service.CorrectAsync(commandId, [firstCorrection, secondCorrection]);

        fixture.BeginTransactionSnapshot();
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.CorrectAsync(commandId, [firstCorrection]));

        fixture.BeginTransactionSnapshot();
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.CorrectAsync(commandId, [firstCorrection, secondCorrection, fixture.Correction(third.LedgerId, 1)]));

        fixture.BeginTransactionSnapshot();
        var originalId = firstCorrection.OriginalLedgerId;
        firstCorrection.OriginalLedgerId = third.LedgerId;
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.CorrectAsync(commandId, [firstCorrection, secondCorrection]));
        firstCorrection.OriginalLedgerId = originalId;

        fixture.BeginTransactionSnapshot();
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.CorrectAsync(Guid.NewGuid(), [firstCorrection, firstCorrection]));

        Assert.AreEqual(4, fixture.Repository.Ledgers.Count(x => x.CorrectionGroupId == commandId));
    }

    /// <summary>纠错替代项携带关系语义，不能退化成普通过账重放。</summary>
    [TestMethod]
    public async Task PostAsync_CannotReplayCorrectionReplacementAsNormalPosting()
    {
        var fixture = new Fixture();
        var original = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(10)])).Single();
        var commandId = Guid.NewGuid();
        var correction = fixture.Correction(original.LedgerId, 8);
        await fixture.Service.CorrectAsync(commandId, [correction]);
        fixture.BeginTransactionSnapshot();

        var exception = await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.PostAsync(commandId, [correction.Replacement]));

        StringAssert.Contains(exception.Message, "IDEMPOTENCY_CONFLICT");
        Assert.AreEqual(3, fixture.Repository.Ledgers.Count);
    }

    /// <summary>纠错组重放时替代事实的缺省语义变化必须按内容冲突处理。</summary>
    [TestMethod]
    public async Task CorrectAsync_ChangedReplacementSnapshot_ShouldConflict()
    {
        var fixture = new Fixture();
        var original = (await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(10)])).Single();
        var commandId = Guid.NewGuid();
        var correction = fixture.Correction(original.LedgerId, 4);
        await fixture.Service.CorrectAsync(commandId, [correction]);
        correction.Replacement.UnitSnapshot = null;

        var exception = await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.CorrectAsync(commandId, [correction]));

        StringAssert.Contains(exception.Message, "IDEMPOTENCY_CONFLICT");
    }

    /// <summary>产品停用不改变库存身份，已确认业务仍可按调用方规则履约。</summary>
    [TestMethod]
    public async Task PostAsync_InactiveButExistingProduct_ShouldRemainPostable()
    {
        var fixture = new Fixture();
        fixture.Repository.Product.IsActive = false;

        var result = await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(2)]);

        Assert.AreEqual(2m, result.Single().AfterQty);
    }

    /// <summary>首次建账和余额归零后都不能把产品基础单位从 PCS 改为 BOX。</summary>
    [TestMethod]
    public async Task PostAsync_UnitMismatch_ShouldFailForNewAndZeroBalance()
    {
        var first = new Fixture();
        var mismatched = first.Item(2);
        mismatched.UnitSnapshot = "BOX";
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => first.Service.PostAsync(Guid.NewGuid(), [mismatched]));

        var zero = new Fixture();
        await zero.Service.PostAsync(Guid.NewGuid(), [zero.Item(2)]);
        await zero.Service.PostAsync(Guid.NewGuid(), [zero.Item(-2)]);
        var changed = zero.Item(1);
        changed.UnitSnapshot = "BOX";
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => zero.Service.PostAsync(Guid.NewGuid(), [changed]));
    }

    /// <summary>幂等指纹区分显式快照和缺省快照，同时不受后续主数据改名影响。</summary>
    [TestMethod]
    public async Task PostAsync_Fingerprint_ShouldPreserveMissingSemanticsAndIgnoreMasterRename()
    {
        var explicitFixture = new Fixture();
        var explicitItem = explicitFixture.Item(3);
        var commandId = Guid.NewGuid();
        await explicitFixture.Service.PostAsync(commandId, [explicitItem]);
        explicitItem.UnitSnapshot = null;
        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => explicitFixture.Service.PostAsync(commandId, [explicitItem]));

        var missingFixture = new Fixture();
        var missing = missingFixture.Item(3);
        missing.SkuSnapshot = null;
        missing.ProductNameSnapshot = null;
        missing.UnitSnapshot = null;
        var missingCommandId = Guid.NewGuid();
        await missingFixture.Service.PostAsync(missingCommandId, [missing]);
        missingFixture.Repository.Product.Sku = "SKU-RENAMED";
        missingFixture.Repository.Product.DescriptionZh = "已改名产品";
        var replay = await missingFixture.Service.PostAsync(missingCommandId, [missing]);
        Assert.IsTrue(replay.Single().AlreadyApplied);
    }

    /// <summary>库存流水必须携带非空来源单据和来源行身份。</summary>
    [TestMethod]
    public async Task PostAsync_MissingSourceIdentity_ShouldFail()
    {
        var fixture = new Fixture();
        var item = fixture.Item(1);
        item.SourceLineId = null;

        await Assert.ThrowsExactlyAsync<InventoryPostingException>(() => fixture.Service.PostAsync(Guid.NewGuid(), [item]));
    }

    /// <summary>合法 300 字符仓名必须完整写入流水快照，不允许截断。</summary>
    [TestMethod]
    public async Task PostAsync_LongWarehouseName_ShouldKeepFullSnapshot()
    {
        var fixture = new Fixture();
        fixture.Warehouse.Name = new string('仓', 300);

        await fixture.Service.PostAsync(Guid.NewGuid(), [fixture.Item(1)]);

        Assert.AreEqual(300, fixture.Repository.Ledgers.Single().WarehouseNameSnapshot!.Length);
    }

    private sealed class Fixture
    {
        private readonly DbSession _session;

        public Guid WarehouseId { get; } = Guid.NewGuid();
        public Guid ProductId { get; } = Guid.NewGuid();
        public InventoryRepositoryStub Repository { get; }
        public WarehouseSite Warehouse { get; }
        public TransactionStub Transaction { get; private set; }
        public InventoryPostingService Service { get; }

        public Fixture()
        {
            Repository = new InventoryRepositoryStub(ProductId);
            Warehouse = new WarehouseSite { Uid = WarehouseId, Code = "WH-1", Name = "供应商仓", IsActive = true };
            _session = Session(new ConnectionStub());
            Transaction = new TransactionStub(_session.Connection);
            _session.Transaction = Transaction;
            Service = new InventoryPostingService(new ServiceBaseContext(_session, NullLoggerFactory.Instance), Repository, new WarehouseRepositoryStub(Warehouse));
        }

        /// <summary>为预期失败的调用建立可恢复快照，模拟数据库事务回滚。</summary>
        public void BeginTransactionSnapshot()
        {
            Repository.CaptureSnapshot();
            Transaction = new TransactionStub(_session.Connection, Repository.RestoreSnapshot);
            _session.Transaction = Transaction;
        }

        public InventoryPostingItem Item(decimal quantity) => new() { CommandItemId = Guid.NewGuid(), WarehouseSiteId = WarehouseId, ProductId = ProductId, SkuSnapshot = "SKU-1", ProductNameSnapshot = "产品一", UnitSnapshot = "PCS", QuantityDelta = quantity, TransactionType = quantity > 0 ? InventoryTransactionTypeEnum.PurchaseReceiptInbound : InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound, SourceDocumentType = "Test", SourceDocumentId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceDocumentNoSnapshot = "DOC-1", OccurredDate = new DateTime(2026, 9, 13) };
        public InventoryReverseItem Reverse(Guid originalId) => new() { CommandItemId = Guid.NewGuid(), OriginalLedgerId = originalId, SourceDocumentType = "TestReverse", SourceDocumentId = Guid.NewGuid(), SourceLineId = Guid.NewGuid(), SourceDocumentNoSnapshot = "RV-1", Reason = "测试反向", OccurredDate = new DateTime(2026, 9, 14) };
        public InventoryCorrectionItem Correction(Guid originalId, decimal quantity) => new() { OriginalLedgerId = originalId, ReverseCommandItemId = Guid.NewGuid(), ReverseSourceDocumentType = "TestCorrection", ReverseSourceDocumentId = Guid.NewGuid(), ReverseSourceLineId = Guid.NewGuid(), ReverseSourceDocumentNoSnapshot = "CR-1", ReverseOccurredDate = new DateTime(2026, 9, 15), Reason = "测试纠错", Replacement = Item(quantity) };
    }

    private sealed class InventoryRepositoryStub(Guid productId) : IInventoryRepository
    {
        private long _sequence;
        private long _snapshotSequence;
        private int _snapshotLedgerCount;
        private InventoryBalance? _snapshotBalance;
        public List<InventoryLedgerEntry> Ledgers { get; } = [];
        public Product Product { get; } = new() { Uid = productId, Sku = "SKU-1", Unit = "PCS", DescriptionZh = "产品一" };
        public InventoryBalance? Balance { get; private set; }
        public Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<InventoryLedgerEntry?> FindLedgerByCommandItemAsync(Guid commandItemId) => Task.FromResult(Ledgers.SingleOrDefault(x => x.CommandItemId == commandItemId));
        public Task<InventoryLedgerEntry?> LockLedgerByCommandItemAsync(Guid commandItemId) => FindLedgerByCommandItemAsync(commandItemId);
        public Task<InventoryLedgerEntry?> LockLedgerByIdAsync(Guid ledgerId) => Task.FromResult(Ledgers.SingleOrDefault(x => x.Uid == ledgerId));
        public Task<InventoryLedgerEntry?> LockReversalByOriginalAsync(Guid originalLedgerId) => Task.FromResult(Ledgers.SingleOrDefault(x => x.ReversalOfLedgerId == originalLedgerId));
        public Task<InventoryLedgerEntry?> LockReplacementByOriginalAsync(Guid originalLedgerId) => Task.FromResult(Ledgers.SingleOrDefault(x => x.ReplacementOfLedgerId == originalLedgerId));
        public Task<IReadOnlyList<InventoryLedgerEntry>> LockCorrectionGroupAsync(Guid correctionGroupId) => Task.FromResult<IReadOnlyList<InventoryLedgerEntry>>(Ledgers.Where(x => x.CorrectionGroupId == correctionGroupId).ToList());
        public Task<InventoryBalance?> LockBalanceAsync(Guid warehouseSiteId, Guid requestedProductId) => Task.FromResult(Balance?.WarehouseSiteId == warehouseSiteId && Balance.ProductId == requestedProductId ? Balance : null);
        public Task<Product?> GetProductForPostingAsync(Guid requestedProductId) => Task.FromResult<Product?>(requestedProductId == productId ? Product : null);
        public Task<int> InsertBalanceAsync(InventoryBalance balance) { Balance = balance; return Task.FromResult(1); }
        public Task<int> UpdateBalanceAsync(InventoryBalance balance, int expectedVersion) { balance.Version++; return Task.FromResult(1); }
        public Task<long> NextLedgerSequenceAsync() => Task.FromResult(++_sequence);
        public Task<int> InsertLedgerAsync(InventoryLedgerEntry entry) { Ledgers.Add(entry); return Task.FromResult(1); }
        /// <summary>保存当前投影与流水边界，供事务桩在失败时恢复。</summary>
        public void CaptureSnapshot()
        {
            _snapshotSequence = _sequence;
            _snapshotLedgerCount = Ledgers.Count;
            _snapshotBalance = Balance is null ? null : CopyBalance(Balance);
        }

        /// <summary>撤销快照之后的流水追加与余额变更。</summary>
        public void RestoreSnapshot()
        {
            if (Ledgers.Count > _snapshotLedgerCount) Ledgers.RemoveRange(_snapshotLedgerCount, Ledgers.Count - _snapshotLedgerCount);
            _sequence = _snapshotSequence;
            Balance = _snapshotBalance is null ? null : CopyBalance(_snapshotBalance);
        }

        private static InventoryBalance CopyBalance(InventoryBalance source) => new()
        {
            Uid = source.Uid,
            WarehouseSiteId = source.WarehouseSiteId,
            ProductId = source.ProductId,
            OnHandQty = source.OnHandQty,
            SkuSnapshot = source.SkuSnapshot,
            ProductNameSnapshot = source.ProductNameSnapshot,
            UnitSnapshot = source.UnitSnapshot,
            LastLedgerSequence = source.LastLedgerSequence,
            Version = source.Version,
            IsDeleted = source.IsDeleted
        };
        public Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponseBase<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, int pageIndex, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryReconciliationDto?> GetReconciliationAsync(Guid balanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class WarehouseRepositoryStub(WarehouseSite warehouse) : IWarehouseSiteRepository
    {
        public Task<WarehouseSite?> GetForUpdateAsync(Guid uid) => Task.FromResult<WarehouseSite?>(uid == warehouse.Uid ? warehouse : null);
        public Task<PagedResponseBase<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto>> GetPickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponseBase<WarehouseSupplierLookupDto>> SearchSupplierLookupAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WarehouseSupplierLookupDto?> GetActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WarehouseSite?> FindByCodeForUpdateAsync(string code) => throw new NotSupportedException();
        public Task<WarehouseSite?> FindActiveSupplierWarehouseForUpdateAsync(Guid supplierId) => throw new NotSupportedException();
        public Task<int> InsertAsync(WarehouseSite site) => throw new NotSupportedException();
        public Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion) => throw new NotSupportedException();
        public Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion) => throw new NotSupportedException();
        public Task<int> DeleteAsync(Guid uid, int expectedVersion) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId) => throw new NotSupportedException();
    }

    private static DbSession Session(IDbConnection connection) { var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession)); typeof(DbSession).GetField("<Connection>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(session, connection); return session; }
    private sealed class ConnectionStub : IDbConnection { public IDbTransaction BeginTransaction() => new TransactionStub(this); public IDbTransaction BeginTransaction(IsolationLevel il) => new TransactionStub(this); public void Close() { } public void Open() { } public void ChangeDatabase(string databaseName) { } public IDbCommand CreateCommand() => throw new NotSupportedException(); [AllowNull] public string ConnectionString { get; set; } = string.Empty; public int ConnectionTimeout => 0; public string Database => "test"; public ConnectionState State => ConnectionState.Open; public void Dispose() { } }
    public sealed class TransactionStub(IDbConnection connection, Action? rollback = null) : IDbTransaction { public int Rollbacks { get; private set; } public IDbConnection Connection { get; } = connection; public IsolationLevel IsolationLevel => IsolationLevel.Serializable; public void Commit() { } public void Rollback() { Rollbacks++; rollback?.Invoke(); } public void Dispose() { } }
}
