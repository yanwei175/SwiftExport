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
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Commands;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>以事务和仓储替身验证采购反向接入公共回执后的权限、重放与失败回滚。</summary>
[TestClass]
public sealed class ProcurementOrderReversalCommandOrchestrationTests
{
    /// <summary>细权限拒绝与格式错误均在命令锁前停止，不读取或写入采购事实。</summary>
    [TestMethod]
    public async Task PermissionAndValidation_PrecedeCommandLock()
    {
        var f = new Fixture { Allow = false };
        var rejected = await f.Service.VoidAsync(f.Head.Uid, new VoidProcurementOrderCommand());
        Assert.AreEqual(ServiceErrorCodes.Forbidden, rejected.Error.Code);
        Assert.AreEqual(0, f.Connection.Begins);
        Assert.AreEqual(0, f.Receipts.Locks);
        f.Allow = true;
        await Assert.ThrowsAsync<CommandValidationException>(() => f.Service.VoidAsync(f.Head.Uid,
            new VoidProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = " " }));
        Assert.AreEqual(0, f.Connection.Begins);
        Assert.AreEqual(0, f.Applies);
    }

    /// <summary>作废成功后旧版本请求可重放，改变原因、操作或权限均不得重复执行。</summary>
    [TestMethod]
    public async Task VoidSuccessReplayAndConflicts_WriteOnce()
    {
        var f = new Fixture();
        var command = new VoidProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = " 作废 " };
        var first = await f.Service.VoidAsync(f.Head.Uid, command);
        var repeat = await f.Service.VoidAsync(f.Head.Uid, command);
        Assert.IsTrue(first.Success, first.Message);
        Assert.IsTrue(repeat.Success, repeat.Message);
        Assert.AreEqual(first.Data.Version, repeat.Data.Version);
        Assert.AreEqual(1, f.Applies);
        Assert.AreEqual(1, f.Logs);
        Assert.AreEqual(1, f.Receipts.Rows.Count);
        Assert.AreEqual(2, f.Connection.Commits);
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.LastIsolation);
        Assert.AreEqual("test-correlation", f.LastLog!.CorrelationId);
        Assert.AreEqual("作废", f.LastLog.Reason);

        var changed = await f.Service.VoidAsync(f.Head.Uid,
            new VoidProcurementOrderCommand { CommandId = command.CommandId, ExpectedVersion = 1, Reason = "不同原因" });
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, changed.Error.Code);
        var another = await f.Service.CancelConfirmationAsync(f.Head.Uid,
            new CancelProcurementOrderConfirmationCommand { CommandId = command.CommandId, ExpectedVersion = 1, Reason = "作废" });
        Assert.AreEqual(ServiceErrorCodes.CommandConflict, another.Error.Code);
        f.Allow = false;
        var forbidden = await f.Service.VoidAsync(f.Head.Uid, command);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, forbidden.Error.Code);
        Assert.AreEqual(1, f.Applies);
    }

    /// <summary>取消确认不删行，删行只传选定行，整单作废保持既有策略入口。</summary>
    [TestMethod]
    public async Task Operations_KeepTheirSelectedBusinessFacts()
    {
        var cancel = new Fixture();
        cancel.Head.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        var cancelled = await cancel.Service.CancelConfirmationAsync(cancel.Head.Uid,
            new CancelProcurementOrderConfirmationCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = "取消确认" });
        Assert.IsTrue(cancelled.Success, cancelled.Message);
        Assert.AreEqual(ProcurementReversalOperation.CancelConfirmation, cancel.LastOperation);
        Assert.AreEqual(1, cancel.Facts.Lines.Count(x => !x.IsDeleted));
        Assert.AreEqual(0, cancel.Facts.Links.Count);

        var deletion = new Fixture();
        deletion.Facts.Lines.Add(new PurchaseOrderLine { Uid = Guid.NewGuid(), Version = 1, Quantity = 1, UnlinkedQty = 1 });
        var selected = deletion.Facts.Lines[0];
        var deleted = await deletion.Service.DeleteLinesAsync(deletion.Head.Uid,
            new DeleteProcurementDraftLinesCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1,
                Reason = "仅删选中行", Lines = [new ProcurementLineVersionDto { PurchaseOrderLineId = selected.Uid, Version = 1 }] });
        Assert.IsTrue(deleted.Success, deleted.Message);
        Assert.AreEqual(ProcurementReversalOperation.DeleteLines, deletion.LastOperation);
        CollectionAssert.AreEqual(new[] { selected.Uid }, deletion.LastLineIds);
        Assert.IsTrue(selected.IsDeleted);
        Assert.IsFalse(deletion.Facts.Lines[1].IsDeleted);
    }

    /// <summary>旧版本和未退出入库在业务锁后拒绝；审计或回执失败不能提交部分反向。</summary>
    [TestMethod]
    public async Task StaleVersionDownstreamAndFailures_RollBack()
    {
        var stale = new Fixture();
        stale.Head.Version = 2;
        var old = await stale.Service.VoidAsync(stale.Head.Uid,
            new VoidProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = "旧版本" });
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, old.Error.Code);
        Assert.AreEqual(0, stale.Applies);
        Assert.AreEqual(1, stale.Connection.Rollbacks);

        var downstream = new Fixture();
        var inbound = new InboundDocument { Uid = Guid.NewGuid(), PostingStatus = InboundPostingStatusEnum.Posted };
        downstream.Facts.Inbounds.Add(inbound);
        downstream.Facts.InboundLines.Add(new InboundDocumentLine { InboundDocumentId = inbound.Uid, SourceLineId = downstream.Facts.Lines[0].Uid });
        var blocked = await downstream.Service.VoidAsync(downstream.Head.Uid,
            new VoidProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = "仍有入库" });
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, blocked.Error.Code);
        Assert.AreEqual(0, downstream.Applies);

        foreach (var receiptFailure in new[] { false, true })
        {
            var f = new Fixture { LogResult = receiptFailure ? 1 : 0 };
            f.Receipts.FailInsert = receiptFailure;
            var failed = await f.Service.VoidAsync(f.Head.Uid,
                new VoidProcurementOrderCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = "失败回滚" });
            Assert.IsFalse(failed.Success);
            Assert.AreEqual(1, f.Applies);
            Assert.AreEqual(1, f.Connection.Rollbacks);
            Assert.AreEqual(0, f.Connection.Commits);
            Assert.AreEqual(0, f.Receipts.Rows.Count);
            Assert.IsNull(f.Session.Transaction);
        }
    }

    /// <summary>三种反向命令的锁定或写入冲突均回滚，不能留下成功审计或回执。</summary>
    [TestMethod]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, false)]
    [DataRow(ProcurementReversalOperation.DeleteLines, false)]
    [DataRow(ProcurementReversalOperation.Void, false)]
    [DataRow(ProcurementReversalOperation.CancelConfirmation, true)]
    [DataRow(ProcurementReversalOperation.DeleteLines, true)]
    [DataRow(ProcurementReversalOperation.Void, true)]
    public async Task RepositoryConflict_RollsBackEveryReversal(ProcurementReversalOperation operation, bool duringLock)
    {
        var f = new Fixture { FailApply = !duringLock,
            LockError = duringLock ? new ServiceErrorDto { Code = "CONCURRENCY_CONFLICT", Message = "引用变化" } : null };
        if (operation == ProcurementReversalOperation.CancelConfirmation) f.Head.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        f.Facts.Lines.Add(new PurchaseOrderLine { Uid = Guid.NewGuid(), Version = 1, Quantity = 1, UnlinkedQty = 1 });
        var commandId = Guid.NewGuid();
        var result = operation switch
        {
            ProcurementReversalOperation.CancelConfirmation => await f.Service.CancelConfirmationAsync(f.Head.Uid, new() { CommandId = commandId, ExpectedVersion = 1, Reason = "测试冲突" }),
            ProcurementReversalOperation.DeleteLines => await f.Service.DeleteLinesAsync(f.Head.Uid, new() { CommandId = commandId, ExpectedVersion = 1, Reason = "测试冲突", Lines = [new() { PurchaseOrderLineId = f.Facts.Lines[0].Uid, Version = 1 }] }),
            _ => await f.Service.VoidAsync(f.Head.Uid, new() { CommandId = commandId, ExpectedVersion = 1, Reason = "测试冲突" })
        };
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual("ExpectedVersion", result.Error.Field);
        Assert.AreEqual(duringLock ? 0 : 1, f.Applies);
        Assert.AreEqual(0, f.Logs);
        Assert.AreEqual(0, f.Receipts.Rows.Count);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>反向预览的锁定冲突也采用版本错误，事务安全释放且不执行写入。</summary>
    [TestMethod]
    public async Task LockConflict_BlocksReversalPreview()
    {
        var f = new Fixture { LockError = new ServiceErrorDto { Code = "CONCURRENCY_CONFLICT", Message = "引用变化" } };
        var result = await f.Service.PreviewAsync(f.Head.Uid, ProcurementReversalOperation.Void, new() { ExpectedVersion = 1 });
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.AreEqual("ExpectedVersion", result.Error.Field);
        Assert.AreEqual(1, f.Connection.Rollbacks);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Applies);
        Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>仅模拟 Application 编排与回执，不模拟 SQL Server 持久化和锁。</summary>
    private sealed class Fixture
    {
        public readonly ConnectionStub Connection = new();
        public readonly DbSession Session;
        public readonly ReceiptStub Receipts;
        public readonly ProcurementOrderReversalService Service;
        public readonly PurchaseOrderHead Head = new() { Uid = Guid.NewGuid(), Version = 1, OriginKind = 2 };
        public readonly ProcurementReversalFacts Facts;
        public bool Allow = true;
        public bool FailApply;
        public ServiceErrorDto? LockError;
        public int Applies, Logs, LogResult = 1;
        public ProcurementReversalOperation? LastOperation;
        public Guid[] LastLineIds = [];
        public BusinessOperationLog? LastLog;

        public Fixture()
        {
            Facts = new ProcurementReversalFacts { Head = Head, Lines = [new PurchaseOrderLine
            { Uid = Guid.NewGuid(), Version = 1, Quantity = 1, UnlinkedQty = 1 }] };
            Session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Session, Connection);
            var context = new ServiceBaseContext(Session, NullLoggerFactory.Instance);
            var user = new UserStub();
            var correlation = new CorrelationStub();
            Receipts = new ReceiptStub(Session);
            var executor = new BusinessCommandExecutor(context, Receipts, user);
            var permissions = Proxy<IPermissionEvaluationService>.New((method, _) => method.Name == "HasPermissionAsync"
                ? Task.FromResult(Allow) : throw new NotSupportedException(method.Name));
            var logs = Proxy<IBusinessOperationLogRepository>.New((method, args) => method.Name == "InsertAsync"
                ? RecordLog((BusinessOperationLog)args[0]!) : throw new NotSupportedException(method.Name));
            var reversals = Proxy<IProcurementReversalRepository>.New((method, args) => method.Name switch
            {
                "LockAsync" => Task.FromResult<(ProcurementReversalFacts?, ServiceErrorDto?)>((LockError is null ? Facts : null, LockError)),
                "ApplyAsync" => Apply((ProcurementReversalOperation)args[1]!, (IReadOnlyCollection<Guid>)args[2]!),
                _ => throw new NotSupportedException(method.Name)
            });
            var validators = new ServiceCollection()
                .AddSingleton<IValidator<CancelProcurementOrderConfirmationCommand>, CancelProcurementOrderConfirmationCommandValidator>()
                .AddSingleton<IValidator<DeleteProcurementDraftLinesCommand>, DeleteProcurementDraftLinesCommandValidator>()
                .AddSingleton<IValidator<VoidProcurementOrderCommand>, VoidProcurementOrderCommandValidator>()
                .BuildServiceProvider();
            var pipeline = new CommandPipeline(new CommandRequestValidator(validators), correlation, user,
                NullLogger<CommandPipeline>.Instance);
            var support = new ProcurementOrderCommandSupport(permissions, user, logs, correlation,
                NullLogger<ProcurementOrderCommandSupport>.Instance);
            Service = new ProcurementOrderReversalService(new ProcurementOrderReadSupport(context, permissions, user),
                reversals, logs, user, correlation, executor, pipeline, support);
        }

        private Task<int> RecordLog(BusinessOperationLog log) { Logs++; LastLog = log; return Task.FromResult(LogResult); }
        /// <summary>模拟反向仓储的结果；冲突时不改事实，让测试观察 Application 是否继续执行。</summary>
        private Task<RepositoryWriteResult> Apply(ProcurementReversalOperation operation, IReadOnlyCollection<Guid> lineIds)
        {
            Applies++;
            if (FailApply) return Task.FromResult(RepositoryWriteResult.Fail(Head.Uid, Head.Version, "CONCURRENCY_CONFLICT", "反向冲突"));
            LastOperation = operation; LastLineIds = lineIds.ToArray(); Head.Version++;
            if (operation == ProcurementReversalOperation.CancelConfirmation)
            { Head.ConfirmationStatus = ConfirmationStatusEnum.待确认; Head.ActiveConfirmationId = null; }
            else if (operation == ProcurementReversalOperation.Void) Head.LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Voided;
            else foreach (var line in Facts.Lines.Where(x => lineIds.Contains(x.Uid))) { line.IsDeleted = true; line.Version++; }
            return Task.FromResult(RepositoryWriteResult.Ok(Head.Uid, Head.Version - 1, Head.Version));
        }
    }

    /// <summary>为测试提供只响应指定方法的接口替身。</summary>
    public class Proxy<T> : DispatchProxy where T : class
    {
        private Func<MethodInfo, object?[], object?> handler = default!;
        public static T New(Func<MethodInfo, object?[], object?> handler)
        { var instance = Create<T, Proxy<T>>(); ((Proxy<T>)(object)instance).handler = handler; return instance; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) => handler(method!, args ?? []);
    }

    /// <summary>保存成功结果并可注入回执写入失败。</summary>
    private sealed class ReceiptStub(DbSession session) : ICommandReceiptRepository
    {
        public readonly Dictionary<Guid, CommandReceipt> Rows = [];
        public int Locks;
        public bool FailInsert;
        public Task<int> LockAsync(Guid id, CancellationToken ct)
        { Assert.IsNotNull(session.Transaction); Locks++; return Task.FromResult(0); }
        public Task<CommandReceipt?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task InsertAsync(CommandReceipt receipt, CancellationToken ct)
        { Assert.IsNotNull(session.Transaction); if (FailInsert) throw new InvalidOperationException("模拟回执写入失败"); Rows.Add(receipt.CommandId, receipt); return Task.CompletedTask; }
    }

    /// <summary>提供固定认证人，不使用真实账号或权限库。</summary>
    private sealed class UserStub : ICurrentUser
    { public Guid? UserId => new Guid("3657c8f3-0d7e-4260-9e93-569ac0aab60a"); public string UserName => "采购测试"; public bool IsAuthenticated => true; }
    /// <summary>提供与 CommandId 独立的调用链身份。</summary>
    private sealed class CorrelationStub : ICorrelationContext { public string CorrelationId => "test-correlation"; }

    /// <summary>观察可串行化事务与提交回滚次数。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public int Begins, Commits, Rollbacks;
        public IsolationLevel? LastIsolation;
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "stub";
        public ConnectionState State => ConnectionState.Open;
        public IDbTransaction BeginTransaction() { Begins++; LastIsolation = null; return new TransactionStub(this); }
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
