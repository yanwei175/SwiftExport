using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Application.Models.Auth;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Application.Services.Inventory.Warehouses;
using SwiftExport.Application.Services.Inventory.Warehouses.Validation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
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

        var anonymousResult = await anonymous.Query.GetWarehousePickerPagedAsync(new PagedRequestBase());
        var noReadResult = await noRead.Query.GetWarehousePickerPagedAsync(new PagedRequestBase());
        var allowedResult = await readOnly.Query.GetWarehousePickerPagedAsync(new PagedRequestBase());

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

    /// <summary>启用命令要求独立仓库管理权限，并在开启事务前拒绝无权调用。</summary>
    [TestMethod]
    public async Task Activate_ShouldRequireManagePermissionBeforeTransaction()
    {
        var fixture = new Fixture(hasManagePermission: false);
        var target = fixture.Warehouses.AddExisting("WH-01", "测试仓", isActive: false);

        var result = await fixture.Service.ActivateWarehouseSiteAsync(target.Uid, ActivateCommand(Guid.NewGuid(), target.Version, "恢复使用"));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "管理权限");
        Assert.AreEqual(0, fixture.Connection.BeginTransactionCount);
        Assert.AreEqual(0, fixture.Warehouses.ActivateCount);
    }

    /// <summary>启用停用仓库只校验状态与唯一性，不受既有库存或流水历史阻止。</summary>
    [TestMethod]
    public async Task Activate_ShouldIgnoreInventoryHistoryAndWriteAuditLog()
    {
        var fixture = new Fixture();
        var target = fixture.Warehouses.AddExisting("WH-01", "测试仓", isActive: false);
        fixture.Warehouses.HasBalanceOrLedgerResult = true;

        var result = await fixture.Service.ActivateWarehouseSiteAsync(target.Uid, ActivateCommand(Guid.NewGuid(), target.Version, "恢复业务使用"));

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Data.IsActive);
        Assert.AreEqual(2, result.Data.Version);
        Assert.AreEqual(1, fixture.Warehouses.ActivateCount);
        Assert.AreEqual(0, fixture.Warehouses.HasBalanceOrLedgerCallCount);
        Assert.AreEqual("WarehouseSite.Activated", fixture.Logs.LastInserted?.OperationType);
        Assert.AreEqual("恢复业务使用", fixture.Logs.LastInserted?.Reason);
        Assert.AreEqual(1, fixture.Connection.Transactions.Single().Commits);
    }

    /// <summary>启用拒绝过期版本和已删除仓库，且不得把软删除记录复活。</summary>
    [TestMethod]
    public async Task Activate_ShouldRejectVersionConflictAndDeletedWarehouse()
    {
        var versionFixture = new Fixture();
        var target = versionFixture.Warehouses.AddExisting("WH-01", "测试仓", isActive: false);
        var versionResult = await versionFixture.Service.ActivateWarehouseSiteAsync(
            target.Uid, ActivateCommand(Guid.NewGuid(), target.Version + 1, "过期版本"));

        Assert.IsFalse(versionResult.Success);
        StringAssert.Contains(versionResult.Message, "已被修改");
        Assert.AreEqual(0, versionFixture.Warehouses.ActivateCount);

        var deletedFixture = new Fixture();
        target = deletedFixture.Warehouses.AddExisting("WH-02", "删除仓", isActive: false, isDeleted: true);
        var deletedResult = await deletedFixture.Service.ActivateWarehouseSiteAsync(
            target.Uid, ActivateCommand(Guid.NewGuid(), target.Version, "错误恢复"));

        Assert.IsFalse(deletedResult.Success);
        StringAssert.Contains(deletedResult.Message, "已删除");
        Assert.AreEqual(0, deletedFixture.Warehouses.ActivateCount);
    }

    /// <summary>同供应商已有另一活动虚拟仓时明确拒绝启用，不依赖数据库异常文案。</summary>
    [TestMethod]
    public async Task Activate_ShouldRejectSecondActiveSupplierWarehouse()
    {
        var fixture = new Fixture();
        var target = fixture.Warehouses.AddExisting("WH-OLD", "停用仓", isActive: false);
        fixture.Warehouses.ActiveSupplierWarehouse = new WarehouseSite
        {
            Uid = Guid.NewGuid(), Version = 1, Code = "WH-ACTIVE", Name = "活动仓",
            SupplierId = target.SupplierId, IsActive = true
        };

        var result = await fixture.Service.ActivateWarehouseSiteAsync(target.Uid, ActivateCommand(Guid.NewGuid(), target.Version, "恢复使用"));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "已存在活动虚拟仓");
        Assert.AreEqual(0, fixture.Warehouses.ActivateCount);
        Assert.AreEqual(1, fixture.Connection.Transactions.Single().Rollbacks);
    }

    /// <summary>启用响应丢失后相同意图返回首次结果，不同内容使用相同 CommandId 必须冲突。</summary>
    [TestMethod]
    public async Task Activate_ShouldReplayIdenticalIntentAndRejectDifferentContent()
    {
        var fixture = new Fixture();
        var target = fixture.Warehouses.AddExisting("WH-01", "测试仓", isActive: false);
        var commandId = Guid.NewGuid();
        var initialVersion = target.Version;

        var first = await fixture.Service.ActivateWarehouseSiteAsync(target.Uid, ActivateCommand(commandId, initialVersion, "恢复使用"));
        var replay = await fixture.Service.ActivateWarehouseSiteAsync(target.Uid, ActivateCommand(commandId, initialVersion, "恢复使用"));
        var conflict = await fixture.Service.ActivateWarehouseSiteAsync(target.Uid, ActivateCommand(commandId, initialVersion, "不同原因"));

        Assert.IsTrue(first.Success);
        Assert.IsTrue(replay.Success);
        Assert.AreEqual(first.Data.Uid, replay.Data.Uid);
        Assert.IsFalse(conflict.Success);
        StringAssert.Contains(conflict.Message, "COMMAND_CONFLICT");
        Assert.AreEqual(1, fixture.Warehouses.ActivateCount);
        Assert.AreEqual(1, fixture.Logs.InsertCount);
    }

    /// <summary>启用必须提供真实原因，空白原因在开启事务和写仓库前拒绝。</summary>
    [TestMethod]
    public async Task Activate_ShouldRejectBlankReasonBeforeTransaction()
    {
        var fixture = new Fixture();
        var target = fixture.Warehouses.AddExisting("WH-01", "测试仓", isActive: false);

        var result = await fixture.Service.ActivateWarehouseSiteAsync(
            target.Uid, ActivateCommand(Guid.NewGuid(), target.Version, "   "));

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "启用原因不能为空");
        Assert.AreEqual(0, fixture.Connection.BeginTransactionCount);
        Assert.AreEqual(0, fixture.Warehouses.ActivateCount);
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
        StringAssert.Contains(second.Message, "COMMAND_CONFLICT");
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

    /// <summary>公共回执写入失败时同一事务必须回滚仓库写入和业务审计。</summary>
    [TestMethod]
    public async Task Create_ShouldRollbackWhenReceiptInsertFails()
    {
        var fixture = new Fixture();
        fixture.Receipts.FailInsert = true;

        var result = await fixture.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(Guid.NewGuid(), "WH-01", "测试仓"));

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, fixture.Connection.Transactions.Single().Commits);
        Assert.AreEqual(1, fixture.Connection.Transactions.Single().Rollbacks);
        Assert.AreEqual(1, fixture.Warehouses.InsertCount);
        Assert.AreEqual(1, fixture.Logs.InsertCount);
        Assert.AreEqual(1, fixture.Receipts.InsertCount);
    }

    /// <summary>五种正式命令分别保存公共回执，审计只记录首次业务变更。</summary>
    [TestMethod]
    public async Task WarehouseCommands_ShouldStoreDistinctReceiptsAndSingleAuditPerCommand()
    {
        var create = new Fixture();
        var created = await create.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(Guid.NewGuid(), "WH-C", "新仓"));
        Assert.IsTrue(created.Success);
        Assert.AreEqual(1, create.Receipts.InsertCount);
        Assert.AreEqual(1, create.Logs.InsertCount);
        Assert.IsNull(create.Logs.LastInserted?.RelatedObjectsJson);
        Assert.IsFalse(create.Logs.LastInserted?.ChangedFieldsJson?.Contains("Fingerprint") ?? true);

        var update = new Fixture();
        var target = update.Warehouses.AddExisting("WH-U", "旧名");
        var renamed = await update.Service.UpdateWarehouseSiteAsync(target.Uid, new UpdateWarehouseSiteCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = target.Version, Name = "新名"
        });
        Assert.IsTrue(renamed.Success);
        Assert.AreEqual(1, update.Receipts.InsertCount);
        Assert.AreEqual("WarehouseSite.Updated", update.Logs.LastInserted?.OperationType);

        var deactivate = new Fixture();
        target = deactivate.Warehouses.AddExisting("WH-D", "停用仓");
        var stopped = await deactivate.Service.DeactivateWarehouseSiteAsync(target.Uid, new DeactivateWarehouseSiteCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = target.Version, Reason = "停用原因"
        });
        Assert.IsTrue(stopped.Success);
        Assert.AreEqual(1, deactivate.Receipts.InsertCount);
        Assert.AreEqual("WarehouseSite.Deactivated", deactivate.Logs.LastInserted?.OperationType);

        var activate = new Fixture();
        target = activate.Warehouses.AddExisting("WH-A", "启用仓", isActive: false);
        var started = await activate.Service.ActivateWarehouseSiteAsync(target.Uid, ActivateCommand(Guid.NewGuid(), target.Version, "启用原因"));
        Assert.IsTrue(started.Success);
        Assert.AreEqual(1, activate.Receipts.InsertCount);
        Assert.AreEqual("WarehouseSite.Activated", activate.Logs.LastInserted?.OperationType);

        var delete = new Fixture();
        target = delete.Warehouses.AddExisting("WH-X", "删除仓");
        var removed = await delete.Service.DeleteWarehouseSiteAsync(target.Uid, new DeleteWarehouseSiteCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = target.Version, Reason = "删除原因"
        });
        Assert.IsTrue(removed.Success);
        Assert.AreEqual(1, delete.Receipts.InsertCount);
        Assert.AreEqual("WarehouseSite.Deleted", delete.Logs.LastInserted?.OperationType);
    }

    /// <summary>公共回执把操作者计入指纹，同一命令身份不能跨用户重放。</summary>
    [TestMethod]
    public async Task Create_ShouldRejectSameCommandIdFromDifferentUser()
    {
        var fixture = new Fixture();
        var command = CreateCommand(Guid.NewGuid(), "WH-01", "测试仓");
        Assert.IsTrue((await fixture.Service.CreateSupplierVirtualWarehouseAsync(command)).Success);
        fixture.User.UserId = Guid.NewGuid();

        var conflict = await fixture.Service.CreateSupplierVirtualWarehouseAsync(command);

        Assert.IsFalse(conflict.Success);
        Assert.AreEqual("COMMAND_CONFLICT", conflict.Error?.Code);
        Assert.AreEqual(1, fixture.Warehouses.InsertCount);
        Assert.AreEqual(1, fixture.Logs.InsertCount);
    }

    /// <summary>提交结果不明时返回专用错误，不把成功或失败臆断给调用方。</summary>
    [TestMethod]
    public async Task Create_ShouldReportUnknownOutcomeWhenCommitThrows()
    {
        var fixture = new Fixture();
        fixture.Connection.ThrowOnCommit = true;

        var result = await fixture.Service.CreateSupplierVirtualWarehouseAsync(CreateCommand(Guid.NewGuid(), "WH-01", "测试仓"));

        Assert.IsFalse(result.Success);
        Assert.AreEqual("OUTCOME_UNKNOWN", result.Error?.Code);
        Assert.AreEqual(1, fixture.Connection.Transactions.Single().Rollbacks);
    }

    private static CreateSupplierVirtualWarehouseCommand CreateCommand(Guid commandId, string code, string name) => new()
    {
        CommandId = commandId, SupplierId = Guid.Parse("11111111-1111-1111-1111-111111111111"), Code = code, Name = name
    };

    private static ActivateWarehouseSiteCommand ActivateCommand(Guid commandId, int version, string reason) => new()
    {
        CommandId = commandId, ExpectedVersion = version, Reason = reason
    };

    private sealed class Fixture
    {
        public Fixture(bool hasManagePermission = true, bool hasReadPermission = true, bool authenticated = true)
        {
            Connection = new ConnectionStub();
            Warehouses = new WarehouseRepositoryStub();
            Logs = new OperationLogRepositoryStub();
            var context = new ServiceBaseContext(Session(Connection), NullLoggerFactory.Instance);
            User = new CurrentUserStub(authenticated);
            var permissions = new PermissionServiceStub(hasManagePermission, hasReadPermission);
            Receipts = new ReceiptStub();
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<CreateSupplierVirtualWarehouseCommand>, CreateSupplierVirtualWarehouseCommandValidator>()
                .AddSingleton<IValidator<UpdateWarehouseSiteCommand>, UpdateWarehouseSiteCommandValidator>()
                .AddSingleton<IValidator<DeactivateWarehouseSiteCommand>, DeactivateWarehouseSiteCommandValidator>()
                .AddSingleton<IValidator<ActivateWarehouseSiteCommand>, ActivateWarehouseSiteCommandValidator>()
                .AddSingleton<IValidator<DeleteWarehouseSiteCommand>, DeleteWarehouseSiteCommandValidator>()
                .BuildServiceProvider();
            var correlation = new CorrelationStub();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), correlation, User,
                NullLogger<CommandPipeline>.Instance);
            Query = new WarehouseQueryService(context, Warehouses, User, permissions);
            Service = new WarehouseManagementService(
                context,
                Warehouses,
                Warehouses,
                Logs,
                correlation,
                User,
                permissions,
                pipeline,
                new BusinessCommandExecutor(context, Receipts, User));
        }

        public WarehouseManagementService Service { get; }
        public WarehouseQueryService Query { get; }
        public ConnectionStub Connection { get; }
        public WarehouseRepositoryStub Warehouses { get; }
        public OperationLogRepositoryStub Logs { get; }
        public ReceiptStub Receipts { get; }
        public CurrentUserStub User { get; }
    }

    private static DbSession Session(IDbConnection connection)
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        typeof(DbSession).GetField("<Connection>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session, connection);
        return session;
    }

    private sealed class WarehouseRepositoryStub : IWarehouseQueryRepository, IWarehouseManagementRepository
    {
        private WarehouseSite? _site;
        public int InsertCount { get; private set; }
        public int ActivateCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int PickerQueryCount { get; private set; }
        public int HasBalanceOrLedgerCallCount { get; private set; }
        public IReadOnlyList<string> DeleteBlockers { get; set; } = [];
        public bool HasBalanceOrLedgerResult { get; set; }
        public WarehouseSite? ActiveSupplierWarehouse { get; set; }

        public WarehouseSite AddExisting(string code, string name, bool isActive = true, bool isDeleted = false)
        {
            _site = new WarehouseSite
            {
                Uid = Guid.NewGuid(), Version = 1, Code = code, Name = name, SupplierId = Guid.NewGuid(),
                SupplierCodeSnapshot = "S-01", SupplierNameSnapshot = "供应商甲",
                IsActive = isActive, IsDeleted = isDeleted
            };
            return _site;
        }

        public Task<WarehouseSupplierLookupDto?> GetActiveSupplierAsync(Guid supplierId, CancellationToken cancellationToken = default)
            => Task.FromResult<WarehouseSupplierLookupDto?>(new WarehouseSupplierLookupDto { Uid = supplierId, SupplierCode = "S-01", SupplierName = "供应商甲" });
        public Task<WarehouseSite?> FindByCodeForUpdateAsync(string code) => Task.FromResult<WarehouseSite?>(null);
        public Task<WarehouseSite?> FindActiveSupplierWarehouseForUpdateAsync(Guid supplierId) => Task.FromResult(
            ActiveSupplierWarehouse?.SupplierId == supplierId ? ActiveSupplierWarehouse : null);
        public Task<int> InsertAsync(WarehouseSite site) { InsertCount++; _site = site; return Task.FromResult(1); }
        public Task<WarehouseSite?> GetForUpdateAsync(Guid uid) => Task.FromResult(_site?.Uid == uid && !_site.IsDeleted ? _site : null);
        public Task<int> DeleteAsync(Guid uid, int expectedVersion) { DeleteCount++; return Task.FromResult(1); }
        public Task<IReadOnlyList<string>> GetDeleteBlockersAsync(Guid warehouseSiteId, CancellationToken cancellationToken = default) => Task.FromResult(DeleteBlockers);
        public Task<WarehouseSiteDto?> GetDtoAsync(Guid uid, CancellationToken cancellationToken = default) => Task.FromResult(_site?.Uid == uid && !_site.IsDeleted ? ToDto(_site) : null);
        public Task<int> UpdateNameAsync(WarehouseSite site, int expectedVersion) => Task.FromResult(1);
        public Task<int> DeactivateAsync(WarehouseSite site, int expectedVersion) => Task.FromResult(1);
        public Task<int> ActivateAsync(WarehouseSite site, int expectedVersion)
        {
            ActivateCount++;
            site.IsActive = true;
            site.Version++;
            return Task.FromResult(1);
        }
        public Task<bool> HasBalanceOrLedgerAsync(Guid warehouseSiteId)
        {
            HasBalanceOrLedgerCallCount++;
            return Task.FromResult(HasBalanceOrLedgerResult);
        }
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

    /// <summary>在替身事务中保存公共命令回执，验证重放不依赖业务审计。</summary>
    private sealed class ReceiptStub : ICommandReceiptRepository
    {
        private readonly Dictionary<Guid, CommandReceipt> _receipts = [];
        public bool FailInsert { get; set; }
        public int InsertCount { get; private set; }
        public Task<int> LockAsync(Guid commandId, CancellationToken token) => Task.FromResult(0);
        public Task<CommandReceipt?> FindAsync(Guid commandId, CancellationToken token) =>
            Task.FromResult(_receipts.GetValueOrDefault(commandId));
        public Task InsertAsync(CommandReceipt receipt, CancellationToken token)
        {
            InsertCount++;
            if (FailInsert) throw new InvalidOperationException("模拟回执写入失败");
            _receipts.Add(receipt.CommandId, receipt);
            return Task.CompletedTask;
        }
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
        public Guid? UserId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public bool IsAuthenticated => authenticated;
    }

    private sealed class CorrelationStub : ICorrelationContext { public string CorrelationId => "warehouse-tests"; }
    private sealed class ConnectionStub : IDbConnection
    {
        public bool ThrowOnCommit { get; set; }
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
        private readonly IDbConnection _connection = connection;
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public IDbConnection? Connection { get; private set; } = connection;
        public IsolationLevel IsolationLevel { get; } = level;
        public void Commit()
        {
            if (_connection is ConnectionStub { ThrowOnCommit: true }) throw new InvalidOperationException("提交结果不明");
            Commits++;
        }
        public void Rollback() => Rollbacks++;
        public void Dispose() => Connection = null;
    }
}
