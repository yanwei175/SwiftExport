using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Models.Auth;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证供应商虚拟仓应用服务的权限、原因边界、幂等重放、删除阻断和日志事务。</summary>
[TestClass]
public sealed class WarehouseSiteManagementServiceTests
{
    /// <summary>验证选择器查询仅依赖库存模块读权限，未认证和无读权限均在访问仓储前拒绝。</summary>
    [TestMethod]
    public async Task Picker_ShouldRequireInventoryReadPermissionWithoutStartingTransaction()
    {
        var anonymous = new Fixture(authenticated: false);
        var noRead = new Fixture(hasReadPermission: false);
        var readOnly = new Fixture(hasManagePermission: false, hasReadPermission: true);

        var anonymousResult = await anonymous.Service.GetWarehousePickerPagedAsync(new PagedRequestBase());
        var noReadResult = await noRead.Service.GetWarehousePickerPagedAsync(new PagedRequestBase());
        var allowedResult = await readOnly.Service.GetWarehousePickerPagedAsync(new PagedRequestBase());

        Assert.IsFalse(anonymousResult.Success);
        Assert.IsFalse(noReadResult.Success);
        Assert.IsTrue(allowedResult.Success);
        Assert.AreEqual(0, anonymous.Warehouses.PickerQueryCount);
        Assert.AreEqual(0, noRead.Warehouses.PickerQueryCount);
        Assert.AreEqual(1, readOnly.Warehouses.PickerQueryCount);
        Assert.AreEqual(0, readOnly.Connection.BeginTransactionCount);
    }

    /// <summary>验证没有独立管理权限时在开启事务和访问仓储前拒绝写命令。</summary>
    [TestMethod]
    public async Task Create_ShouldRejectUserWithoutManagePermissionBeforeTransaction()
    {
        var fixture = new Fixture(hasManagePermission: false);

        var result = await fixture.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(Guid.NewGuid(), "WH-01", "测试仓"));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "管理权限");
        Assert.AreEqual(0, fixture.Connection.BeginTransactionCount);
        Assert.AreEqual(0, fixture.Warehouses.InsertCount);
    }

    /// <summary>验证停用和删除原因按 BusinessOperationLog.Reason 的真实 1000 字符列宽拒绝越界。</summary>
    [TestMethod]
    public async Task Reason_ShouldAccept1000AndReject1001Characters()
    {
        var accepted = new Fixture();
        var target = accepted.Warehouses.AddExisting("WH-01", "测试仓");
        var acceptedResult = await accepted.Service.DeleteWarehouseSiteAsync(target.Uid, new DeleteWarehouseSiteCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = target.Version, Reason = new string('原', 1000)
        });

        Assert.IsTrue(acceptedResult.Success);
        Assert.AreEqual(1000, accepted.Logs.LastInserted?.Reason?.Length);

        var rejected = new Fixture();
        target = rejected.Warehouses.AddExisting("WH-02", "测试仓二");
        var rejectedResult = await rejected.Service.DeleteWarehouseSiteAsync(target.Uid, new DeleteWarehouseSiteCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = target.Version, Reason = new string('原', 1001)
        });

        Assert.IsFalse(rejectedResult.Success);
        StringAssert.Contains(rejectedResult.Message, "1000");
        Assert.AreEqual(0, rejected.Connection.BeginTransactionCount);
        Assert.AreEqual(0, rejected.Warehouses.DeleteCount);
    }

    /// <summary>验证结构化指纹不会把含旧分隔符但字段边界不同的请求误判为相同重放。</summary>
    [TestMethod]
    public async Task Create_ShouldRejectDifferentFieldBoundariesWithSameLegacyConcatenation()
    {
        var fixture = new Fixture();
        var commandId = Guid.NewGuid();
        var first = await fixture.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(commandId, "A\u001fB", "C"));
        var second = await fixture.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(commandId, "A", "B\u001fC"));

        Assert.IsTrue(first.Success);
        Assert.IsFalse(second.Success);
        StringAssert.Contains(second.Message, "IDEMPOTENCY_CONFLICT");
        Assert.AreEqual(1, fixture.Warehouses.InsertCount);
    }

    /// <summary>验证相同 CommandId 和相同内容返回首次完整结果，不重复建仓或写日志。</summary>
    [TestMethod]
    public async Task Create_ShouldReturnOriginalResultForIdenticalReplay()
    {
        var fixture = new Fixture();
        var command = CreateCommand(Guid.NewGuid(), "WH-01", "测试仓");

        var first = await fixture.Service.CreateSupplierVirtualWarehouseAsync(command);
        var replay = await fixture.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(command.CommandId, command.Code, command.Name));

        Assert.IsTrue(first.Success);
        Assert.IsTrue(replay.Success);
        Assert.AreEqual(first.Data.Uid, replay.Data.Uid);
        Assert.AreEqual(1, fixture.Warehouses.InsertCount);
        Assert.AreEqual(1, fixture.Logs.InsertCount);
        Assert.AreEqual(2, fixture.Connection.Transactions.Sum(transaction => transaction.Commits));
    }

    /// <summary>验证任一删除引用类别都会阻断软删；库存余额类别同时代表零余额记录也不可删。</summary>
    [TestMethod]
    public async Task Delete_ShouldBlockEveryProtectedReferenceCategory()
    {
        var blockers = new[]
        {
            "库存余额", "库存流水", "采购订单行", "采购确认节点", "正式入库单", "正式出库单", "PackingList贡献行"
        };

        foreach (var blocker in blockers)
        {
            var fixture = new Fixture();
            var target = fixture.Warehouses.AddExisting("WH-01", "测试仓");
            fixture.Warehouses.DeleteBlockers = [blocker];

            var result = await fixture.Service.DeleteWarehouseSiteAsync(target.Uid, new DeleteWarehouseSiteCommand
            {
                CommandId = Guid.NewGuid(), ExpectedVersion = target.Version, Reason = "存在业务引用"
            });

            Assert.IsFalse(result.Success, blocker);
            StringAssert.Contains(result.Message, blocker);
            Assert.AreEqual(0, fixture.Warehouses.DeleteCount, blocker);
            Assert.AreEqual(1, fixture.Connection.Transactions.Single().Rollbacks, blocker);
        }
    }

    /// <summary>验证业务日志追加失败时仓库命令不提交，并由同一事务执行回滚。</summary>
    [TestMethod]
    public async Task Create_ShouldRollbackWhenBusinessLogInsertFails()
    {
        var fixture = new Fixture();
        fixture.Logs.FailInsert = true;

        var result = await fixture.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(Guid.NewGuid(), "WH-01", "测试仓"));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Connection.Transactions.Single().Commits);
        Assert.AreEqual(1, fixture.Connection.Transactions.Single().Rollbacks);
        Assert.AreEqual(1, fixture.Warehouses.InsertCount);
        Assert.AreEqual(1, fixture.Logs.InsertCount);
    }

    private static CreateSupplierVirtualWarehouseCommand CreateCommand(Guid commandId, string code, string name) => new()
    {
        CommandId = commandId, SupplierId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Code = code, Name = name
    };

    private sealed class Fixture
    {
        public Fixture(bool hasManagePermission = true, bool hasReadPermission = true, bool authenticated = true)
        {
            Connection = new ConnectionStub();
            Warehouses = new WarehouseRepositoryStub();
            Logs = new OperationLogRepositoryStub();
            Service = new InventoryService(
                new ServiceBaseContext(Session(Connection), NullLoggerFactory.Instance),
                Warehouses,
                new InventoryRepositoryStub(),
                Logs,
                new CorrelationStub(),
                new CurrentUserStub(authenticated),
                new PermissionServiceStub(hasManagePermission, hasReadPermission));
        }

        public InventoryService Service { get; }
        public ConnectionStub Connection { get; }
        public WarehouseRepositoryStub Warehouses { get; }
        public OperationLogRepositoryStub Logs { get; }
    }

    private static DbSession Session(IDbConnection connection)
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        typeof(DbSession).GetField("<Connection>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session, connection);
        return session;
    }

    private sealed class WarehouseRepositoryStub : IWarehouseSiteRepository
    {
        private WarehouseSite? _site;
        public int InsertCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int PickerQueryCount { get; private set; }
        public IReadOnlyList<string> DeleteBlockers { get; set; } = [];

        public WarehouseSite AddExisting(string code, string name)
        {
            _site = new WarehouseSite
            {
                Uid = Guid.NewGuid(), Version = 1, Code = code, Name = name, SupplierId = Guid.NewGuid(),
                SupplierCodeSnapshot = "S-01", SupplierNameSnapshot = "供应商甲", IsActive = true
            };
            return _site;
        }

        public Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<WarehouseSupplierLookupDto?> GetActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default)
            => Task.FromResult<WarehouseSupplierLookupDto?>(new WarehouseSupplierLookupDto { Uid = supplierId, SupplierCode = "S-01", SupplierName = "供应商甲" });
        public Task<WarehouseSite?> FindByCodeForUpdateAsync(string code) => Task.FromResult<WarehouseSite?>(null);
        public Task<WarehouseSite?> FindActiveSupplierWarehouseForUpdateAsync(Guid supplierId) => Task.FromResult<WarehouseSite?>(null);
        public Task<int> InsertAsync(WarehouseSite site) { InsertCount++; _site = site; return Task.FromResult(1); }
        public Task<WarehouseSite?> GetForUpdateAsync(Guid uid) => Task.FromResult(_site?.Uid == uid ? _site : null);
        public Task<int> DeleteAsync(Guid uid, int expectedVersion) { DeleteCount++; return Task.FromResult(1); }
        public Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default) => Task.FromResult(DeleteBlockers);
        public Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default) => Task.FromResult(_site?.Uid == uid ? ToDto(_site) : null);
        public Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion) => Task.FromResult(1);
        public Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion) => Task.FromResult(1);
        public Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId) => Task.FromResult(false);
        public Task<PagedResponseBase<WarehouseSitePickerDto>> GetPickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
        {
            PickerQueryCount++;
            return Task.FromResult(new PagedResponseBase<WarehouseSitePickerDto>
            {
                Items = [], TotalCount = 0, PageIndex = request.PageIndex, PageSize = request.PageSize
            });
        }
        public Task<PagedResponseBase<WarehouseSupplierLookupDto>> SearchSupplierLookupAsync(PagedRequestBase request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponseBase<WarehouseSiteDto>> SearchAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private static WarehouseSiteDto ToDto(WarehouseSite site) => new()
        {
            Uid = site.Uid, Version = site.Version, Code = site.Code, Name = site.Name, SupplierId = site.SupplierId,
            SupplierCode = site.SupplierCodeSnapshot ?? string.Empty, SupplierName = site.SupplierNameSnapshot ?? string.Empty,
            IsActive = site.IsActive
        };
    }

    private sealed class OperationLogRepositoryStub : IBusinessOperationLogRepository
    {
        private BusinessOperationLog? _existing;
        public bool FailInsert { get; set; }
        public int InsertCount { get; private set; }
        public BusinessOperationLog? LastInserted { get; private set; }
        public Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId) => Task.FromResult(_existing?.CommandId == commandId ? _existing : null);
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId) => Task.FromResult<BusinessOperationLog?>(null);
        public Task<int> InsertAsync(BusinessOperationLog log)
        {
            InsertCount++;
            LastInserted = log;
            if (!FailInsert) _existing = log;
            return Task.FromResult(FailInsert ? 0 : 1);
        }
        public Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid aggregateUid, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BusinessOperationTraceItemDto>>([]);
    }

    private sealed class PermissionServiceStub(bool manageAllowed, bool readAllowed) : IUserPermissionService
    {
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
            => Task.FromResult(permissionKey switch
            {
                PermissionKeys.InventoryWarehouseManage => manageAllowed,
                PermissionKeys.ModuleInventoryAccess => readAllowed,
                _ => false
            });
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<UserModulePermissionsDto>> GetUserModulesAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<UserModulePermissionsDto>> SaveUserModulesAsync(Guid userId, SaveUserModulePermissionsRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CurrentUserStub(bool authenticated) : ICurrentUser
    {
        public string UserName => "仓库测试用户";
        public Guid? UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public bool IsAuthenticated => authenticated;
    }

    private sealed class CorrelationStub : ICorrelationContext { public string CorrelationId => "warehouse-tests"; }
    private sealed class InventoryRepositoryStub : IInventoryRepository
    {
        public Task<PagedResponseBase<InventoryBalanceDto>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponseBase<InventoryLedgerItemDto>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryBalanceDto?> GetBalanceDtoAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResponseBase<InventoryLedgerItemDto>> GetLedgerByBalanceAsync(Guid balanceId, int pageIndex, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryReconciliationDto?> GetReconciliationAsync(Guid balanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InventoryBalance?> LockBalanceAsync(Guid warehouseSiteId, Guid productId) => throw new NotSupportedException();
        public Task<InventoryLedgerEntry?> FindLedgerByCommandItemAsync(Guid commandItemId) => throw new NotSupportedException();
        public Task<InventoryLedgerEntry?> LockLedgerByCommandItemAsync(Guid commandItemId) => throw new NotSupportedException();
        public Task<InventoryLedgerEntry?> LockLedgerByIdAsync(Guid ledgerId) => throw new NotSupportedException();
        public Task<InventoryLedgerEntry?> LockReversalByOriginalAsync(Guid originalLedgerId) => throw new NotSupportedException();
        public Task<InventoryLedgerEntry?> LockReplacementByOriginalAsync(Guid originalLedgerId) => throw new NotSupportedException();
        public Task<IReadOnlyList<InventoryLedgerEntry>> LockCorrectionGroupAsync(Guid correctionGroupId) => throw new NotSupportedException();
        public Task<Product?> GetProductForPostingAsync(Guid productId) => throw new NotSupportedException();
        public Task<int> AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> InsertBalanceAsync(InventoryBalance balance) => throw new NotSupportedException();
        public Task<int> UpdateBalanceAsync(InventoryBalance balance, int expectedVersion) => throw new NotSupportedException();
        public Task<long> NextLedgerSequenceAsync() => throw new NotSupportedException();
        public Task<int> InsertLedgerAsync(InventoryLedgerEntry entry) => throw new NotSupportedException();
    }

    private sealed class ConnectionStub : IDbConnection
    {
        public List<TransactionStub> Transactions { get; } = [];
        public int BeginTransactionCount => Transactions.Count;
        public IDbTransaction BeginTransaction() => BeginTransaction(IsolationLevel.Unspecified);
        public IDbTransaction BeginTransaction(IsolationLevel il)
        {
            var transaction = new TransactionStub(this, il);
            Transactions.Add(transaction);
            return transaction;
        }
        public void Close() { }
        public void Open() { }
        public void ChangeDatabase(string databaseName) { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => ConnectionState.Open;
        public void Dispose() { }
    }

    private sealed class TransactionStub(IDbConnection connection, IsolationLevel level) : IDbTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public IDbConnection? Connection { get; private set; } = connection;
        public IsolationLevel IsolationLevel { get; } = level;
        public void Commit() => Commits++;
        public void Rollback() => Rollbacks++;
        public void Dispose() => Connection = null;
    }
}
