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
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>用可观察替身验证供应商反馈的授权、公共回执、重放与失败回滚。</summary>
[TestClass]
public sealed class PurchaseOrderProgressReportOrchestrationTests
{
    /// <summary>首次反馈追加一次；旧版本重放返回首次结果，异内容和撤权均不得再写。</summary>
    [TestMethod]
    public async Task ReportReplayAndConflict_KeepSingleBusinessWrite()
    {
        var f = new Fixture();
        var command = f.Command();
        var first = await f.Service.ReportAsync(f.Head.Uid, command);
        Assert.IsTrue(first.Success, first.Message);
        Assert.AreEqual(ProcurementCommandOutcomeEnum.Succeeded, first.Data.Outcome);
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.LastIsolation);
        Assert.AreEqual(1, f.Reports);
        Assert.AreEqual(1, f.Logs);
        Assert.AreEqual(1, f.Receipts.Rows.Count);

        f.Head.Version = 2; // 重放先核对公共回执，不重新核对当前版本。
        var replay = await f.Service.ReportAsync(f.Head.Uid, command);
        Assert.IsTrue(replay.Success, replay.Message);
        Assert.AreEqual(first.Data.SupplierProgressReportId, replay.Data.SupplierProgressReportId);
        Assert.AreEqual(first.Data.Outcome, replay.Data.Outcome);
        Assert.AreEqual(1, f.Reports);
        Assert.AreEqual(1, f.Logs);

        var changedNote = new ReportPurchaseOrderProgressCommand
        {
            CommandId = command.CommandId, PurchaseOrderLineId = command.PurchaseOrderLineId,
            ExpectedPurchaseOrderVersion = command.ExpectedPurchaseOrderVersion,
            ExpectedLineVersion = command.ExpectedLineVersion, ReadyQty = command.ReadyQty, Note = "改过的备注"
        };
        foreach (var changed in new[]
        {
            changedNote,
            new ReportPurchaseOrderProgressCommand { CommandId = command.CommandId, PurchaseOrderLineId = Guid.NewGuid(),
                ExpectedPurchaseOrderVersion = 1, ExpectedLineVersion = 1, ReadyQty = 3m, Note = command.Note },
            new ReportPurchaseOrderProgressCommand { CommandId = command.CommandId, PurchaseOrderLineId = command.PurchaseOrderLineId,
                ExpectedPurchaseOrderVersion = 1, ExpectedLineVersion = 1, ReadyQty = 4m, Note = command.Note },
            new ReportPurchaseOrderProgressCommand { CommandId = command.CommandId, PurchaseOrderLineId = command.PurchaseOrderLineId,
                ExpectedPurchaseOrderVersion = 1, ExpectedLineVersion = 1, ReadyQty = 3m,
                ExpectedReadyDate = new DateTime(2026, 10, 5), Note = command.Note }
        })
        {
            var conflict = await f.Service.ReportAsync(f.Head.Uid, changed);
            Assert.AreEqual(ServiceErrorCodes.CommandConflict, conflict.Error.Code);
        }
        Assert.AreEqual(1, f.Reports);
        f.User.Id = Guid.NewGuid();
        var anotherActor = await f.Service.ReportAsync(f.Head.Uid, command);
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, anotherActor.Error.Code);
        f.Allow = false;
        var forbidden = await f.Service.ReportAsync(f.Head.Uid, command);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, forbidden.Error.Code);
        Assert.AreEqual(1, f.Reports);
    }

    /// <summary>业务审计失败使写事务回滚，不产生成功回执；无权限时不申请命令锁。</summary>
    [TestMethod]
    public async Task AuthorizationAndAuditFailure_StopOrRollback()
    {
        var denied = new Fixture { Allow = false };
        var refusal = await denied.Service.ReportAsync(denied.Head.Uid, denied.Command());
        Assert.AreEqual(ServiceErrorCodes.Forbidden, refusal.Error.Code);
        Assert.AreEqual(0, denied.Connection.Begins);
        Assert.AreEqual(0, denied.Receipts.Locks);

        var failed = new Fixture { LogResult = 0 };
        var result = await failed.Service.ReportAsync(failed.Head.Uid, failed.Command());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(1, failed.Connection.Rollbacks);
        Assert.AreEqual(0, failed.Connection.Commits);
        Assert.AreEqual(0, failed.Receipts.Rows.Count);
        Assert.IsNull(failed.Session.Transaction);
    }

    /// <summary>为命令链提供仅在事务内响应的仓储与当前用户，不模拟 SQL Server 持久化。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly ReceiptStub Receipts;
        public readonly PurchaseOrderProgressReportService Service;
        public readonly UserStub User;
        public readonly PurchaseOrderHead Head = new()
        {
            Uid = Guid.NewGuid(), Version = 1, OrderNo = "PO-1",
            LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
            ConfirmationStatus = ConfirmationStatusEnum.已确认
        };
        public readonly PurchaseOrderLine Line = new() { Uid = Guid.NewGuid(), Version = 1, Quantity = 10m, LineNumber = 1 };
        public bool Allow = true;
        public int Reports, Logs, LogResult = 1;

        public Fixture()
        {
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var user = new UserStub();
            User = user;
            var correlation = new CorrelationStub();
            Receipts = new ReceiptStub(Session);
            var permissions = Proxy<IPermissionEvaluationService>.New((method, _) => method.Name == "HasPermissionAsync"
                ? Task.FromResult(Allow) : throw new NotSupportedException(method.Name));
            var orders = Proxy<IProcurementOrderDocumentRepository>.New((method, _) => method.Name switch
            {
                "GetHeadAsync" => Task.FromResult<PurchaseOrderHead?>(Head),
                "GetLinesAsync" => Task.FromResult(new List<PurchaseOrderLine> { Line }),
                _ => throw new NotSupportedException(method.Name)
            });
            var reports = Proxy<ISupplierProgressReportRepository>.New((method, _) => method.Name switch
            {
                "GetLatestForUpdateAsync" => Task.FromResult<SupplierProgressReport?>(null),
                "InsertAsync" => Task.FromResult(++Reports),
                "GetByCommandAsync" => throw new AssertFailedException("业务报告不可充当命令回执。"),
                _ => throw new NotSupportedException(method.Name)
            });
            var logs = Proxy<IBusinessOperationLogRepository>.New((method, _) => method.Name == "InsertAsync"
                ? Task.FromResult(RecordLog()) : throw new NotSupportedException(method.Name));
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<ReportPurchaseOrderProgressCommand>, ReportPurchaseOrderProgressCommandValidator>()
                .BuildServiceProvider();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), correlation, user,
                NullLogger<CommandPipeline>.Instance);
            var support = new ProcurementOrderCommandSupport(permissions, user, logs, correlation,
                NullLogger<ProcurementOrderCommandSupport>.Instance);
            Service = new PurchaseOrderProgressReportService(orders, reports, logs, correlation, user,
                new BusinessCommandExecutor(context, Receipts, user), pipeline, support);
        }

        public ReportPurchaseOrderProgressCommand Command() => new()
        {
            CommandId = Guid.NewGuid(), PurchaseOrderLineId = Line.Uid,
            ExpectedPurchaseOrderVersion = 1, ExpectedLineVersion = 1, ReadyQty = 3m, Note = "预计发货"
        };
        private int RecordLog() { Logs++; return LogResult; }
    }

    /// <summary>只转发所需的仓储方法，以捕获额外业务读取。</summary>
    public class Proxy<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[], object?> handler = default!;
        public static T New(Func<MethodInfo, object?[], object?> handler)
        { var instance = Create<T, Proxy<T>>(); ((Proxy<T>)(object)instance).handler = handler; return instance; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => handler(method!, args ?? []);
    }
    /// <summary>模拟公共回执锁与保存。</summary>
    private sealed class ReceiptStub(DbSession session) : ICommandReceiptRepository
    {
        public readonly Dictionary<Guid, CommandReceipt> Rows = [];
        public int Locks;
        public Task<int> LockAsync(Guid id, CancellationToken ct)
        { Assert.IsNotNull(session.Transaction); Locks++; return Task.FromResult(0); }
        public Task<CommandReceipt?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task InsertAsync(CommandReceipt receipt, CancellationToken ct)
        { Assert.IsNotNull(session.Transaction); Rows.Add(receipt.CommandId, receipt); return Task.CompletedTask; }
    }
    /// <summary>固定已认证的测试操作人。</summary>
    public sealed class UserStub : ICurrentUser
    { public Guid Id = new("3657c8f3-0d7e-4260-9e93-569ac0aab60a"); public Guid? UserId => Id; public string UserName => "采购测试"; public bool IsAuthenticated => true; }
    /// <summary>提供固定业务调用链身份。</summary>
    private sealed class CorrelationStub : ICorrelationContext { public string CorrelationId => "test-correlation"; }
    /// <summary>观察可串行化事务、提交和回滚。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Begins, Commits, Rollbacks;
        public IsolationLevel? LastIsolation;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "stub";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() { Begins++; return new TransactionStub(this); }
        public IDbTransaction BeginTransaction(IsolationLevel level) { Begins++; LastIsolation = level; return new TransactionStub(this); }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public void Close() { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        public void Open() { }
        public void Dispose() { }
        private sealed class TransactionStub(ConnectionStub owner) : IDbTransaction
        {
            public IDbConnection Connection => owner;
            public IsolationLevel IsolationLevel => IsolationLevel.Serializable;
            public void Commit() => owner.Commits++;
            public void Rollback() => owner.Rollbacks++;
            public void Dispose() { }
        }
    }
}
