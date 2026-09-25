using SwiftExport.Core.Interfaces.Suppliers.Management;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.DemandPlanning.Materials.SupplierSuggestion;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>验证拆分后推荐供应商的同值、清空、版本和事务语义，使用内存端口。</summary>
[TestClass]
public sealed class MaterialDemandSupplierSuggestionServiceTests
{
    /// <summary>提交相同供应商时返回原版本，不执行物料事实更新。</summary>
    [TestMethod]
    public async Task SameSupplierKeepsVersionWithoutUpdate()
    {
        using var fixture = new Fixture();
        var supplierId = Guid.NewGuid();
        fixture.Row.SuggestedPurchaseSupplierId = supplierId;
        fixture.Supplier = new Supplier { Uid = supplierId, SupplierCode = "S1", SupplierName = "供应商", DocRelateNo = "S1" };

        var result = await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(new()
        {
            CommandId = Guid.NewGuid(),
            NetDemandId = fixture.Row.Uid, Version = fixture.Row.Version,
            SuggestedPurchaseSupplierId = supplierId
        });

        Assert.IsTrue(result.Success);
        Assert.AreEqual(fixture.Row.Version, result.Data!.Version);
        Assert.AreEqual(0, fixture.NetDemandPort.UpdateCount);
        Assert.AreEqual(1, fixture.Connection.LastTransaction!.CommitCount);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>null 表示清空建议，并且只更新物料需求的局部版本。</summary>
    [TestMethod]
    public async Task NullClearsSuggestionAndIncrementsVersion()
    {
        using var fixture = new Fixture();
        fixture.Row.SuggestedPurchaseSupplierId = Guid.NewGuid();

        var result = await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(new()
        {
            CommandId = Guid.NewGuid(),
            NetDemandId = fixture.Row.Uid, Version = fixture.Row.Version,
            SuggestedPurchaseSupplierId = null
        });

        Assert.IsTrue(result.Success);
        Assert.AreEqual(fixture.Row.Version + 1, result.Data!.Version);
        Assert.IsNull(result.Data.SuggestedPurchaseSupplierId);
        Assert.AreEqual(1, fixture.NetDemandPort.UpdateCount);
        Assert.IsNull(fixture.NetDemandPort.UpdatedSupplierId);
        Assert.AreEqual(1, fixture.Connection.LastTransaction!.CommitCount);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>旧版本阻断更新，回滚并清理本服务创建的事务。</summary>
    [TestMethod]
    public async Task OldVersionRollsBackWithoutUpdate()
    {
        using var fixture = new Fixture();

        var result = await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(new()
        {
            CommandId = Guid.NewGuid(),
            NetDemandId = fixture.Row.Uid, Version = fixture.Row.Version - 1,
            SuggestedPurchaseSupplierId = null
        });

        Assert.IsFalse(result.Success);
        Assert.AreEqual("CONCURRENCY_CONFLICT", result.Error?.Code);
        Assert.AreEqual(0, fixture.NetDemandPort.UpdateCount);
        Assert.AreEqual(1, fixture.Connection.LastTransaction!.RollbackCount);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>同一行同一身份返回首次完整结果；不同建议及操作人占用原 ID 时冲突。</summary>
    [TestMethod]
    public async Task ReplayAndConflict_PrecedeChangedVersion()
    {
        using var fixture = new Fixture();
        var command = new UpdateSuggestedPurchaseSupplierCommand
        {
            CommandId = Guid.NewGuid(), NetDemandId = fixture.Row.Uid,
            Version = fixture.Row.Version, SuggestedPurchaseSupplierId = null
        };
        Assert.IsTrue((await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(command)).Success);
        fixture.Row.Version++;
        var replay = await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(command.Version, replay.Data!.Version);
        Assert.AreEqual(0, fixture.NetDemandPort.UpdateCount);
        Assert.AreEqual(1, fixture.Receipt.InsertCount);
        var readsBeforeDenial = fixture.Receipt.ReadCount;
        fixture.Permission.Allow = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden,
            (await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(command)).Error.Code);
        Assert.AreEqual(readsBeforeDenial, fixture.Receipt.ReadCount);
        fixture.Permission.Allow = true;

        command.SuggestedPurchaseSupplierId = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(command)).Error.Code);
        command.SuggestedPurchaseSupplierId = null;
        fixture.User.UserId = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(command)).Error.Code);
    }

    /// <summary>权限在事务及回执前检查；首次无权不占用命令身份。</summary>
    [TestMethod]
    public async Task PermissionDenied_DoesNotStartTransaction()
    {
        using var fixture = new Fixture();
        fixture.Permission.Allow = false;
        var result = await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(new()
        {
            CommandId = Guid.NewGuid(), NetDemandId = fixture.Row.Uid, Version = fixture.Row.Version
        });
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.IsNull(fixture.Connection.LastTransaction);
        Assert.AreEqual(0, fixture.Receipt.InsertCount);
    }

    /// <summary>回执写入失败使该行事实事务回滚，不能报告已保存。</summary>
    [TestMethod]
    public async Task ReceiptFailure_RollsBackSuggestionUpdate()
    {
        using var fixture = new Fixture();
        fixture.Receipt.FailInsert = true;
        fixture.Row.SuggestedPurchaseSupplierId = Guid.NewGuid();
        var result = await fixture.Service.UpdateSuggestedPurchaseSupplierAsync(new()
        {
            CommandId = Guid.NewGuid(), NetDemandId = fixture.Row.Uid, Version = fixture.Row.Version,
            SuggestedPurchaseSupplierId = null
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, fixture.Connection.LastTransaction!.RollbackCount);
        Assert.IsNull(fixture.Session.Transaction);
    }

    /// <summary>只构造内存仓储与连接标记，避免 DbSession 正式构造打开 SQL 连接。</summary>
    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Session, Connection);
            var demands = DispatchProxy.Create<IMaterialDemandDocumentRepository, NetDemandPort>();
            NetDemandPort = (NetDemandPort)(object)demands;
            NetDemandPort.Row = Row;
            var suppliers = DispatchProxy.Create<ISupplierRepository, SupplierPort>();
            ((SupplierPort)(object)suppliers).GetSupplier = () => Supplier;
            var user = DispatchProxy.Create<ICurrentUser, UserPort>();
            User = (UserPort)(object)user;
            var permission = DispatchProxy.Create<IPermissionEvaluationService, PermissionPort>();
            Permission = (PermissionPort)(object)permission;
            var receipt = DispatchProxy.Create<ICommandReceiptRepository, ReceiptPort>();
            Receipt = (ReceiptPort)(object)receipt;
            var correlation = DispatchProxy.Create<ICorrelationContext, CorrelationPort>();
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<UpdateSuggestedPurchaseSupplierCommand>, UpdateSuggestedPurchaseSupplierCommandValidator>()
                .BuildServiceProvider();
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            Service = new MaterialDemandSupplierSuggestionService(
                context, suppliers, demands, user, permission,
                new CommandPipeline(new CommandRequestValidator(validators), correlation, user, NullLogger<CommandPipeline>.Instance),
                new BusinessCommandExecutor(context, receipt, user));
        }

        public DbSession Session { get; }
        public FakeConnection Connection { get; } = new();
        public NetDemand Row { get; } = new() { Uid = Guid.NewGuid(), Version = 4 };
        public Supplier? Supplier { get; set; }
        public NetDemandPort NetDemandPort { get; }
        public UserPort User { get; }
        public PermissionPort Permission { get; }
        public ReceiptPort Receipt { get; }
        public MaterialDemandSupplierSuggestionService Service { get; }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>提供已登录操作人身份，不访问认证服务。</summary>
    public class UserPort : DispatchProxy
    {
        public Guid? UserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_IsAuthenticated" => UserId.HasValue,
            "get_UserId" => UserId,
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    /// <summary>仅授予本测试命令的独立维护权限。</summary>
    public class PermissionPort : DispatchProxy
    {
        public bool Allow { get; set; } = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "HasPermissionAsync" => Task.FromResult(Allow && (string)args![1]! == PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage),
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    /// <summary>内存回执端口用于证明命令事务内插入成功。</summary>
    public class ReceiptPort : DispatchProxy
    {
        private readonly Dictionary<Guid, CommandReceipt> _items = [];
        public int InsertCount { get; private set; }
        public int ReadCount { get; private set; }
        public bool FailInsert { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "LockAsync" => Task.FromResult(0),
            "FindAsync" => Find((Guid)args![0]!),
            "InsertAsync" => Insert((CommandReceipt)args![0]!),
            _ => throw new NotSupportedException(method?.Name)
        };

        private Task<CommandReceipt?> Find(Guid id)
        {
            ReadCount++;
            return Task.FromResult(_items.GetValueOrDefault(id));
        }

        private Task Insert(CommandReceipt receipt)
        {
            if (FailInsert) throw new InvalidOperationException("模拟回执写入失败");
            InsertCount++;
            _items.Add(receipt.CommandId, receipt);
            return Task.CompletedTask;
        }
    }

    /// <summary>提供固定链路身份。</summary>
    public class CorrelationPort : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_CorrelationId" => "supplier-test",
            _ => throw new NotSupportedException(method?.Name)
        };
    }

    /// <summary>仅模拟局部更新所需的物料仓储方法。</summary>
    public class NetDemandPort : DispatchProxy
    {
        public NetDemand Row { get; set; } = null!;
        public int UpdateCount { get; private set; }
        public Guid? UpdatedSupplierId { get; private set; }

        /// <summary>记录读取和写入，任何新增依赖均要求测试显式处理。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IMaterialDemandDocumentRepository.GetActiveFamilyForUpdateAsync) =>
                Task.FromResult<IReadOnlyList<NetDemand>>([Row]),
            nameof(IMaterialDemandDocumentRepository.UpdateSuggestedPurchaseSupplierAsync) => Update(args),
            _ => throw new NotSupportedException(method?.Name)
        };

        private Task<int> Update(object?[]? args)
        {
            UpdateCount++;
            UpdatedSupplierId = (Guid?)args![2];
            return Task.FromResult(1);
        }
    }

    /// <summary>仅按身份返回现有供应商，清空建议时不应调用此端口。</summary>
    public class SupplierPort : DispatchProxy
    {
        public Func<Supplier?> GetSupplier { get; set; } = () => null;

        /// <summary>模拟有效供应商读取。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(ISupplierRepository.GetByIdAsync)
                ? Task.FromResult(GetSupplier())
                : throw new NotSupportedException(method?.Name);
    }

    /// <summary>提供请求内连接和可观察事务，不连接数据库。</summary>
    private sealed class FakeConnection : IDbConnection
    {
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State { get; private set; } = ConnectionState.Open;
        public FakeTransaction? LastTransaction { get; private set; }
        public IDbTransaction BeginTransaction() => LastTransaction = new FakeTransaction(this);
        public IDbTransaction BeginTransaction(IsolationLevel level) => BeginTransaction();
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() => State = ConnectionState.Closed;
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() => State = ConnectionState.Open;
        public void Dispose() => Close();
    }

    /// <summary>计数提交与回滚，验证命令的事务清理路径。</summary>
    private sealed class FakeTransaction(IDbConnection connection) : IDbTransaction
    {
        public IDbConnection Connection { get; } = connection;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public void Commit() => CommitCount++;
        public void Rollback() => RollbackCount++;
        public void Dispose() { }
    }
}
