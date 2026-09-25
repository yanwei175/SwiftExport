using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.SalesOrders.Document;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Reception.Models;
using SwiftExport.Application.Services.SalesOrders.Confirmation;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>以内存事务与端口核对销售确认的权限、回执、来源参与及失败回滚；不连接 SQL Server。</summary>
[TestClass]
public sealed class SalesOrderConfirmationCommandTests
{
    /// <summary>同一命令重放原结果，旧版本不再读取销售事实，异输入、目标和操作人均冲突。</summary>
    [TestMethod]
    public async Task Receipt_ReplaysOriginalAndRejectsConflictingIntent()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var first = await f.Service.ConfirmAsync(f.Header.Uid, f.Command(id));
        Assert.IsTrue(first.Success, first.Message);
        Assert.AreEqual(1, first.Data.GrossDemandCount);
        f.Header.Version = 8;
        var replay = await f.Service.ConfirmAsync(f.Header.Uid, f.Command(id));
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(2, replay.Data.Version);
        Assert.AreEqual(1, f.HeadReads);
        Assert.AreEqual(1, f.SourceCalls);
        Assert.AreEqual(1, f.AuditWrites);
        Assert.AreEqual(1, f.ReceiptWrites);
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Service.ConfirmAsync(f.Header.Uid, f.Command(id, 2))).Error.Code);
        var otherUid = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Service.ConfirmAsync(otherUid, new ConfirmSalesOrderCommand
            { Uid = otherUid, Version = 1, CommandId = id })).Error.Code);
        f.User.UserIdValue = Guid.NewGuid();
        Assert.AreEqual(ServiceErrorCodes.CommandConflict,
            (await f.Service.ConfirmAsync(f.Header.Uid, f.Command(id))).Error.Code);
    }

    /// <summary>登录和模块权限先于回执与事务；已确认单据使用新 ID 仍沿用当前重验路径。</summary>
    [TestMethod]
    public async Task AuthorizationAndConfirmedNewCommand()
    {
        var f = new Fixture();
        var command = f.Command(Guid.NewGuid());
        f.User.UserIdValue = null;
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated,
            (await f.Service.ConfirmAsync(f.Header.Uid, command)).Error.Code);
        f.User.UserIdValue = Guid.NewGuid();
        f.Allow = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden,
            (await f.Service.ConfirmAsync(f.Header.Uid, command)).Error.Code);
        Assert.AreEqual(0, f.Connection.Begins);
        f.Allow = true;
        Assert.IsTrue((await f.Service.ConfirmAsync(f.Header.Uid, command)).Success);
        f.Allow = false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden,
            (await f.Service.ConfirmAsync(f.Header.Uid, command)).Error.Code);
        Assert.AreEqual(1, f.ReceiptReads);
        f.Allow = true;
        f.Header.Version = 2;
        f.Header.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        Assert.IsTrue((await f.Service.ConfirmAsync(f.Header.Uid, f.Command(Guid.NewGuid(), 1))).Success);
        Assert.AreEqual(1, f.StatusWrites);
        Assert.AreEqual(2, f.SourceCalls);
    }

    /// <summary>来源、销售审计、公共回执失败或提交未知都不得被伪装成确定成功。</summary>
    [TestMethod]
    public async Task Failures_RollBackAndPreserveUnknownOutcome()
    {
        var source = new Fixture { FailSource = true };
        Assert.IsFalse((await source.Service.ConfirmAsync(source.Header.Uid, source.Command(Guid.NewGuid()))).Success);
        Assert.AreEqual(1, source.Connection.Rollbacks);
        Assert.IsNull(source.Session.Transaction);

        var audit = new Fixture { FailAudit = true };
        Assert.IsFalse((await audit.Service.ConfirmAsync(audit.Header.Uid, audit.Command(Guid.NewGuid()))).Success);
        Assert.AreEqual(1, audit.Connection.Rollbacks);

        var receipt = new Fixture { FailReceipt = true };
        Assert.AreEqual(ServiceErrorCodes.Unexpected,
            (await receipt.Service.ConfirmAsync(receipt.Header.Uid, receipt.Command(Guid.NewGuid()))).Error.Code);
        Assert.AreEqual(1, receipt.Connection.Rollbacks);

        var unknown = new Fixture();
        unknown.Connection.FailCommit = true;
        Assert.AreEqual(ServiceErrorCodes.OutcomeUnknown,
            (await unknown.Service.ConfirmAsync(unknown.Header.Uid, unknown.Command(Guid.NewGuid()))).Error.Code);
        Assert.IsNull(unknown.Session.Transaction);
    }

    /// <summary>仅替代确认路径真实调用的仓储方法，并观察事务与回执次数。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly UserStub User = new();
        public readonly SalesOrderHeader Header = new()
        { Uid = Guid.NewGuid(), Version = 1, OrderNo = "SO-1", CustomerId = Guid.NewGuid() };
        public readonly SalesOrderConfirmationService Service;
        public bool Allow = true, FailSource, FailAudit, FailReceipt;
        public int HeadReads, StatusWrites, SourceCalls, AuditWrites, ReceiptWrites, ReceiptReads;
        private readonly Dictionary<Guid, CommandReceipt> _receipts = [];

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var heads = Proxy<ISalesOrderHeadRepository>((method, args) => method.Name switch
            {
                "GetByIdAsync" => ReadHead(),
                "SetConfirmationStatusAsync" => SetConfirmed(),
                _ => throw new AssertFailedException(method.Name)
            });
            var lines = Proxy<ISalesOrderLineRepository>((method, args) => method.Name switch
            {
                "GetLinesByOrderIdAsync" => Task.FromResult<IReadOnlyList<SalesOrderLine>>(
                    [new SalesOrderLine { Uid = Guid.NewGuid(), SalesOrderId = Header.Uid,
                        Sku = "NS-1", Qty = 1, IsNonStandard = true, LineNumber = 1 }]),
                _ => throw new AssertFailedException(method.Name)
            });
            var products = Proxy<IProductRepository>((method, args) => method.Name switch
            {
                "GetByIdListAsync" => Task.FromResult<IReadOnlyList<Product>>([]),
                _ => throw new AssertFailedException(method.Name)
            });
            var reception = Proxy<ISourceDemandReceptionService>((method, args) => method.Name switch
            {
                "ReceiveAsync" => Receive((SourceDemandReceptionCommand)args![0]!),
                _ => throw new AssertFailedException(method.Name)
            });
            var logs = Proxy<IBusinessOperationLogRepository>((method, args) => method.Name switch
            {
                "InsertAsync" => WriteAudit(),
                _ => throw new AssertFailedException(method.Name)
            });
            var permissions = Proxy<IUserPermissionService>((method, args) => method.Name switch
            {
                "HasPermissionAsync" => (string)args![1]! == PermissionKeys.SalesOrderConfirm
                    ? Task.FromResult(Allow) : throw new AssertFailedException("权限键错误"),
                _ => throw new AssertFailedException(method.Name)
            });
            var receipts = Proxy<ICommandReceiptRepository>((method, args) => method.Name switch
            {
                "LockAsync" => Task.FromResult(0),
                "FindAsync" => ReadReceipt((Guid)args![0]!),
                "InsertAsync" => WriteReceipt((CommandReceipt)args![0]!),
                _ => throw new AssertFailedException(method.Name)
            });
            var correlation = Proxy<ICorrelationContext>((method, args) => method.Name == "get_CorrelationId"
                ? "sales-confirm-test" : throw new AssertFailedException(method.Name));
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<ConfirmSalesOrderCommand>, ConfirmSalesOrderCommandValidator>()
                .BuildServiceProvider();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), correlation, User,
                NullLogger<CommandPipeline>.Instance);
            Service = new SalesOrderConfirmationService(context, heads, lines, products, reception,
                logs, correlation, User, permissions, pipeline, new BusinessCommandExecutor(context, receipts, User));
        }

        public ConfirmSalesOrderCommand Command(Guid id, int version = 1)
            => new() { Uid = Header.Uid, Version = version, CommandId = id };
        private Task<SalesOrderHeader?> ReadHead() { HeadReads++; return Task.FromResult<SalesOrderHeader?>(Header); }
        private Task<RepositoryWriteResult> SetConfirmed()
        { StatusWrites++; return Task.FromResult(RepositoryWriteResult.Ok(Header.Uid, 1, 2)); }
        private Task<SourceDemandReceptionResult> Receive(SourceDemandReceptionCommand command)
        {
            Assert.IsNotNull(Session.Transaction);
            SourceCalls++;
            if (FailSource) throw new InvalidOperationException("模拟来源失败");
            return Task.FromResult(new SourceDemandReceptionResult { SourceVersion = command.SourceVersion, CreatedCount = 1 });
        }
        private Task<int> WriteAudit() { AuditWrites++; return Task.FromResult(FailAudit ? 0 : 1); }
        private Task<CommandReceipt?> ReadReceipt(Guid id)
        { ReceiptReads++; return Task.FromResult(_receipts.GetValueOrDefault(id)); }
        private Task WriteReceipt(CommandReceipt receipt)
        {
            if (FailReceipt) throw new InvalidOperationException("模拟回执失败");
            ReceiptWrites++; _receipts.Add(receipt.CommandId, receipt);
            return Task.CompletedTask;
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MethodProxy>();
        ((MethodProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    public class MethodProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
    private sealed class UserStub : ICurrentUser
    {
        public Guid? UserIdValue = Guid.NewGuid();
        public Guid? UserId => UserIdValue;
        public bool IsAuthenticated => UserIdValue.HasValue;
        public string UserName => "销售测试";
    }
    /// <summary>记录默认隔离入口及提交、回滚；无实际数据库连接。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Begins, Rollbacks;
        public bool FailCommit;
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "memory";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() { Begins++; return new Tx(this); }
        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
            => throw new AssertFailedException("销售确认不得更改默认隔离入口");
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class Tx(ConnectionStub owner) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public void Commit() { if (owner.FailCommit) throw new IOException("模拟提交未知"); }
            public void Rollback() => owner.Rollbacks++;
            public void Dispose() { }
        }
    }
}
