using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.MasterData;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.MasterData.ExecutionEligibility;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.MasterData;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>独立无网络夹具验证执行资格服务的准备身份、原业务/异常边界与清理顺序，不替代真实数据库原子性或授权锁验收。</summary>
[TestClass]
public sealed class ExecutionEligibilityTransactionPreparationTests
{
    /// <summary>审计准备先于 Serializable，事务内目标准备先于授权及业务步骤，日志与租约同 ID 且保留成功投影。</summary>
    [TestMethod]
    public async Task Success_UsesPreparedIdentityAndOriginalResult()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var result = await f.Write(cancellation.Token);
        Assert.IsTrue(result.Success); Assert.AreEqual(f.Command.Uid, result.Data.Uid); Assert.AreEqual(2, result.Data.Version);
        Assert.AreEqual(f.Command.Kind, result.Data.Kind); Assert.IsFalse(result.Data.IsActive);
        var acquired = f.Prepared!.Acquisitions.Single(); Assert.AreNotEqual(Guid.Empty, acquired.Id); Assert.AreEqual(cancellation.Token, acquired.Token);
        var log = f.Logs.Entries.Single(); Assert.AreEqual(acquired.Id, log.CommandId); Assert.AreEqual(f.Command.Uid, log.AggregateUid);
        Assert.AreEqual("MasterData", log.Module); Assert.AreEqual("SetExecutionEligibility", log.OperationType); Assert.AreEqual("暂停资格", log.Reason);
        Assert.AreEqual(f.User.UserId, log.UserUid); Assert.AreEqual(f.User.UserName, log.UserNameSnapshot); Assert.AreEqual("测试关联", log.CorrelationId);
        using var changes = JsonDocument.Parse(log.ChangedFieldsJson!);
        Assert.IsTrue(changes.RootElement.GetProperty("IsActive").GetProperty("Before").GetBoolean());
        Assert.IsFalse(changes.RootElement.GetProperty("IsActive").GetProperty("After").GetBoolean());
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.Transactions.Single().IsolationLevel);
        Assert.AreEqual(1, f.Connection.Transactions.Single().Commits); Assert.AreEqual(0, f.Connection.Transactions.Single().Rollbacks);
        var targetPreparation = f.Repository.TargetPreparations.Single();
        Assert.AreEqual(f.Command.Kind, targetPreparation.Kind); Assert.AreEqual(cancellation.Token, targetPreparation.Token);
        Assert.AreSame(f.Connection.Transactions.Single(), targetPreparation.Transaction);
        Order(f, "Acquire", "Begin", "TargetPrepare", "Permission", "Target", "Update", "Audit", "Commit"); Released(f);
    }

    /// <summary>没有可选准备能力时保留原 SQL Server 服务路径和服务器日志身份，不擅自强制所有日志提供者准备。</summary>
    [TestMethod]
    public async Task LegacyLog_StillUsesSerializableWithoutLease()
    {
        var f = new Fixture(prepared: false); Assert.IsTrue((await f.Write()).Success);
        Assert.IsFalse(f.Events.Contains("Acquire")); Assert.IsFalse(f.Events.Contains("LeaseDispose"));
        Assert.AreNotEqual(Guid.Empty, f.Logs.Entries.Single().CommandId);
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.Transactions.Single().IsolationLevel);
        Assert.HasCount(1, f.Repository.TargetPreparations);
        Order(f, "Begin", "TargetPrepare", "Permission", "Target", "Update", "Audit", "Commit");
        Assert.IsTrue(f.Connection.Transactions.Single().Disposed); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>三种对象均显式准备当前范围，原令牌和本次 Serializable 事务不因传统日志路径而丢失。</summary>
    [TestMethod]
    [DataRow(ExecutionEligibilityKind.Product, true)] [DataRow(ExecutionEligibilityKind.Product, false)]
    [DataRow(ExecutionEligibilityKind.Customer, true)] [DataRow(ExecutionEligibilityKind.Customer, false)]
    [DataRow(ExecutionEligibilityKind.Supplier, true)] [DataRow(ExecutionEligibilityKind.Supplier, false)]
    public async Task TargetPreparation_UsesCurrentKindTokenAndTransaction(ExecutionEligibilityKind kind, bool prepared)
    {
        var f = new Fixture(prepared); f.Command.Kind = kind; using var cancellation = new CancellationTokenSource();
        Assert.IsTrue((await f.Write(cancellation.Token)).Success);
        var preparation = f.Repository.TargetPreparations.Single();
        Assert.AreEqual(kind, preparation.Kind); Assert.AreEqual(cancellation.Token, preparation.Token);
        Assert.AreSame(f.Connection.Transactions.Single(), preparation.Transaction);
        Assert.AreEqual(IsolationLevel.Serializable, preparation.Transaction.IsolationLevel);
        Order(f, "Begin", "TargetPrepare", "Permission", "Target", "Update", "Audit", "Commit");
        if (prepared) { Order(f, "Acquire", "Begin"); Released(f); }
        else
        {
            Assert.IsFalse(f.Events.Contains("Acquire")); Assert.IsFalse(f.Events.Contains("LeaseDispose"));
            Assert.IsTrue(f.Connection.Transactions.Single().Disposed); Assert.IsNull(f.Session.Transaction);
        }
    }

    /// <summary>原四类拒绝保持错误码及消息，更新零行仍是原版本冲突；均不审计、不提交且结束租约。</summary>
    [TestMethod]
    [DataRow("permission", ServiceErrorCodes.Forbidden, "账号或执行资格授权已失效。")]
    [DataRow("missing", ServiceErrorCodes.NotFound, "主数据不存在或已删除。")]
    [DataRow("version", ServiceErrorCodes.VersionConflict, "资料版本已变化，请刷新后重试。")]
    [DataRow("state", ServiceErrorCodes.StateBlocked, "目标资格与当前一致，无需重复变更。")]
    [DataRow("zero", ServiceErrorCodes.VersionConflict, "资格更新失败，请刷新后重试。")]
    public async Task Rejection_PreservesCodeMessageAndNoAudit(string rejection, string code, string message)
    {
        var f = new Fixture(); f.Repository.Rejection = rejection; var result = await f.Write();
        Assert.AreEqual(code, result.Error.Code); Assert.AreEqual(message, result.Message);
        Assert.AreEqual(rejection == "zero" ? 1 : 0, f.Repository.Updates); Assert.HasCount(0, f.Logs.Entries);
        Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks); Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); Released(f);
        Assert.HasCount(1, f.Repository.TargetPreparations); Order(f, "Begin", "TargetPrepare", "Permission", "Rollback");
    }

    /// <summary>准备异常、超时和空租约不能变成错误码映射；原异常保留且没有事务或业务访问。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public async Task PreparationFailure_PropagatesWithoutTransaction(int mode)
    {
        var f = new Fixture(); var original = mode == 1 ? new TimeoutException("准备超时") : new InvalidOperationException("准备技术失败") as Exception;
        f.Prepared!.OnAcquire = (_, _) => mode == 2 ? Task.FromResult<IAsyncDisposable?>(null) : throw original;
        if (mode == 2) await Assert.ThrowsAsync<TimeoutException>(() => f.Write());
        else Assert.AreSame(original, await Assert.ThrowsAsync<Exception>(() => f.Write()));
        Assert.AreEqual(0, f.Connection.BeginAttempts); Assert.AreEqual(0, f.Repository.Updates); Assert.HasCount(0, f.Prepared.Released);
        Assert.IsFalse(f.Events.Contains("Permission")); Assert.IsNull(f.Session.Transaction);
        Assert.HasCount(0, f.Repository.TargetPreparations);
    }

    /// <summary>准备等待中的取消原样保留同令牌，不创建不存在的事务或租约。</summary>
    [TestMethod]
    public async Task PreparationCancellation_KeepsOriginalExceptionAndToken()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var original = new OperationCanceledException(cancellation.Token);
        f.Prepared!.OnAcquire = (_, token) => { Assert.AreEqual(cancellation.Token, token); cancellation.Cancel(); throw original; };
        Assert.AreSame(original, await Assert.ThrowsAsync<OperationCanceledException>(() => f.Write(cancellation.Token)));
        Assert.AreEqual(0, f.Connection.BeginAttempts); Assert.HasCount(0, f.Prepared.Released); Assert.IsNull(f.Session.Transaction);
        Assert.HasCount(0, f.Repository.TargetPreparations);
    }

    /// <summary>租约取得后取消应在 Begin 前检查并释放租约；有效命令预取消也不能开始事务或业务写入。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task CancellationBeforeBegin_DoesNotWrite(bool afterLease)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (afterLease) f.Prepared!.OnAcquire = (id, _) => { cancellation.Cancel(); return Task.FromResult<IAsyncDisposable?>(f.Prepared.Lease(id)); };
        else cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Write(cancellation.Token)); Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(0, f.Connection.BeginAttempts); Assert.AreEqual(0, f.Repository.Updates); Assert.IsNull(f.Session.Transaction);
        Assert.HasCount(0, f.Repository.TargetPreparations);
        if (afterLease) Assert.HasCount(1, f.Prepared!.Released);
    }

    /// <summary>事务开启失败抛原异常并释放本次准备租约，不触发授权和更新。</summary>
    [TestMethod]
    public async Task BeginFailure_ReleasesLeaseAndThrowsOriginal()
    {
        var f = new Fixture(); var original = new InvalidOperationException("事务开启失败"); f.Connection.BeginError = original;
        Assert.AreSame(original, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Write()));
        Assert.HasCount(0, f.Connection.Transactions); Assert.HasCount(1, f.Prepared!.Released); Assert.IsNull(f.Session.Transaction);
        Assert.IsFalse(f.Events.Contains("Permission")); Order(f, "Acquire", "Begin", "LeaseDispose");
        Assert.HasCount(0, f.Repository.TargetPreparations);
    }

    /// <summary>目标准备、授权、目标、更新、审计或提交异常均抛原对象，不新增业务映射；事务回滚后清理。</summary>
    [TestMethod]
    [DataRow("TargetPrepare")] [DataRow("Permission")] [DataRow("Target")] [DataRow("Update")] [DataRow("Audit")] [DataRow("Commit")]
    public async Task TransactionFailure_ThrowsOriginalAndCleansUp(string stage)
    {
        var f = new Fixture(); var original = new InvalidOperationException("原" + stage + "异常");
        f.ErrorStage = stage; f.OriginalError = original;
        Assert.AreSame(original, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Write()));
        Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks); Released(f);
        if (stage != "Commit") Assert.AreEqual(0, f.Connection.Transactions.Single().Commits);
        if (stage == "TargetPrepare")
        { Assert.IsFalse(f.Events.Contains("Permission")); Assert.AreEqual(0, f.Repository.Updates); Assert.HasCount(0, f.Logs.Entries); }
    }

    /// <summary>日志零行保留原异常和消息，不提交资格变更。</summary>
    [TestMethod]
    public async Task AuditZero_KeepsOriginalFailureBoundary()
    {
        var f = new Fixture(); f.Logs.Rows = 0;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Write()); Assert.AreEqual("执行资格变更日志写入失败。", error.Message);
        Assert.AreEqual(1, f.Repository.Updates); Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks); Released(f);
    }

    /// <summary>事务内目标准备/权限/更新/审计取消保留令牌；审计后取消由提交前检查捕获，不能提交。</summary>
    [TestMethod]
    [DataRow("TargetPrepare")] [DataRow("Permission")] [DataRow("Update")] [DataRow("Audit")]
    public async Task TransactionCancellation_RollsBackBeforeCommit(string stage)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.BeforeStep = name => { if (name == stage) cancellation.Cancel(); };
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Write(cancellation.Token)); Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks); Released(f);
        if (stage == "TargetPrepare")
        { Assert.IsFalse(f.Events.Contains("Permission")); Assert.AreEqual(0, f.Repository.Updates); Assert.HasCount(0, f.Logs.Entries); }
    }

    /// <summary>Begin 完成时已取消，目标准备收到原取消令牌，禁止继续授权且回滚本次事务、最后释放审计租约。</summary>
    [TestMethod]
    public async Task CancellationBeforeTargetPreparation_DoesNotAuthorize()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Connection.AfterBegin = cancellation.Cancel;
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Write(cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.AreEqual(cancellation.Token, f.Repository.TargetPreparations.Single().Token);
        Assert.IsFalse(f.Events.Contains("Permission")); Assert.AreEqual(0, f.Repository.Updates); Assert.HasCount(0, f.Logs.Entries);
        Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks);
        Order(f, "Acquire", "Begin", "TargetPrepare", "Rollback", "TransactionDispose", "LeaseDispose"); Released(f);
    }

    /// <summary>异步等待目标准备期间取消，真实服务不能提前授权或写入，等待结束后清空事务并释放租约。</summary>
    [TestMethod]
    public async Task CancellationWhileTargetPreparationWaits_CleansUp()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Repository.OnTargetPrepare = token => Task.Delay(Timeout.Infinite, token);
        var pending = f.Write(cancellation.Token);
        try
        {
            Assert.IsFalse(pending.IsCompleted); Assert.HasCount(1, f.Repository.TargetPreparations);
            Assert.IsFalse(f.Events.Contains("Permission")); Assert.IsNotNull(f.Session.Transaction);
        }
        finally { cancellation.Cancel(); }
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsFalse(f.Events.Contains("Permission")); Assert.AreEqual(0, f.Repository.Updates); Assert.HasCount(0, f.Logs.Entries);
        Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks); Released(f);
    }

    /// <summary>目标准备取得后令牌取消，即使准备正常返回，后续授权必须收到原令牌并阻止业务更新。</summary>
    [TestMethod]
    public async Task CancellationAfterTargetPreparation_DoesNotWrite()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Repository.OnTargetPrepare = token => { Assert.AreEqual(cancellation.Token, token); cancellation.Cancel(); return Task.CompletedTask; };
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.Write(cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.IsFalse(f.Events.Contains("Target")); Assert.AreEqual(0, f.Repository.Updates); Assert.HasCount(0, f.Logs.Entries);
        Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks);
        Order(f, "Begin", "TargetPrepare", "Permission", "Rollback"); Released(f);
    }

    /// <summary>目标准备技术异常或取消不能被回滚、事务/连接/租约释放和诊断的次生故障覆盖。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task TargetPreparationFailure_CleanupKeepsOriginalOutcome(bool cancelled)
    {
        var f = new Fixture(failDiagnostics: true); using var cancellation = new CancellationTokenSource();
        f.ErrorStage = "TargetPrepare";
        f.OriginalError = cancelled ? new OperationCanceledException(cancellation.Token) : new InvalidOperationException("原目标准备失败");
        f.Connection.FailRollback = true; f.Connection.FailTransactionDispose = true; f.Connection.FailConnectionDispose = true; f.Prepared!.FailDispose = true;
        Assert.AreSame(f.OriginalError, await Assert.ThrowsAsync<Exception>(() => f.Write(cancellation.Token)));
        Assert.IsFalse(f.Events.Contains("Permission")); Assert.AreEqual(0, f.Repository.Updates); Assert.HasCount(0, f.Logs.Entries);
        Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks);
        Order(f, "TargetPrepare", "Rollback", "TransactionDispose", "ConnectionDispose", "LeaseDispose"); Released(f);
    }

    /// <summary>次生回滚、事务/连接/租约释放及诊断异常不能掩盖成功、原拒绝、原数据库异常或取消。</summary>
    [TestMethod]
    [DataRow("success")] [DataRow("reject")] [DataRow("exception")] [DataRow("cancel")]
    public async Task CleanupFailure_DoesNotMaskOutcome(string outcome)
    {
        var f = new Fixture(failDiagnostics: true); f.Connection.FailTransactionDispose = true; f.Connection.FailConnectionDispose = true;
        f.Connection.FailRollback = true; f.Prepared!.FailDispose = true;
        using var cancellation = new CancellationTokenSource();
        if (outcome == "reject") f.Repository.Rejection = "permission";
        if (outcome == "exception") { f.ErrorStage = "Update"; f.OriginalError = new InvalidOperationException("原更新失败"); }
        if (outcome == "cancel") f.BeforeStep = name => { if (name == "Permission") cancellation.Cancel(); };
        if (outcome == "success") Assert.IsTrue((await f.Write()).Success);
        else if (outcome == "reject") Assert.AreEqual(ServiceErrorCodes.Forbidden, (await f.Write()).Error.Code);
        else if (outcome == "exception") Assert.AreSame(f.OriginalError, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Write()));
        else Assert.AreEqual(cancellation.Token, (await Assert.ThrowsAsync<OperationCanceledException>(() => f.Write(cancellation.Token))).CancellationToken);
        Order(f, "TransactionDispose", "ConnectionDispose", "LeaseDispose"); Released(f);
    }

    /// <summary>缺失或无效身份、非法命令先返回原业务错误；即使已有事务及预取消也不进入准备或接管事务。</summary>
    [TestMethod]
    [DataRow("unauthenticated")] [DataRow("nullUser")] [DataRow("emptyUser")]
    [DataRow("nullCommand")] [DataRow("kind")] [DataRow("uid")] [DataRow("version")] [DataRow("maxVersion")]
    [DataRow("reason")] [DataRow("longReason")]
    public async Task EarlyGuards_KeepOriginalOrderAndDoNotPrepare(string invalid)
    {
        var f = new Fixture(); var external = new TransactionStub(f, IsolationLevel.ReadCommitted); f.Session.Transaction = external;
        if (invalid == "unauthenticated") f.User.Authenticated = false;
        if (invalid == "nullUser") f.User.Id = null;
        if (invalid == "emptyUser") f.User.Id = Guid.Empty;
        if (invalid == "kind") f.Command.Kind = (ExecutionEligibilityKind)99;
        if (invalid == "uid") f.Command.Uid = Guid.Empty;
        if (invalid == "version") f.Command.ExpectedVersion = 0;
        if (invalid == "maxVersion") f.Command.ExpectedVersion = int.MaxValue;
        if (invalid == "reason") f.Command.Reason = " ";
        if (invalid == "longReason") f.Command.Reason = new string('中', 501);
        var result = await f.Service.SetStatusAsync(invalid == "nullCommand" ? null! : f.Command, new CancellationToken(true));
        var identity = invalid is "unauthenticated" or "nullUser" or "emptyUser";
        Assert.AreEqual(identity ? ServiceErrorCodes.Unauthenticated : ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual(identity ? "需要真实有效的登录身份。" : "对象、版本和不超过500字的变更原因必填。", result.Message);
        Assert.HasCount(0, f.Prepared!.Acquisitions); Assert.AreEqual(0, f.Connection.BeginAttempts); Assert.AreSame(external, f.Session.Transaction);
        Assert.IsFalse(external.Disposed); Assert.AreEqual(0, external.Rollbacks);
        Assert.HasCount(0, f.Repository.TargetPreparations);
    }

    /// <summary>有效命令遇调用方事务继续抛原异常，不准备、不清空、不回滚外层事务。</summary>
    [TestMethod]
    public async Task ExistingTransaction_IsNotTakenOver()
    {
        var f = new Fixture(); var external = new TransactionStub(f, IsolationLevel.ReadCommitted); f.Session.Transaction = external;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Write(new CancellationToken(true)));
        Assert.AreEqual("执行资格命令必须独立拥有业务事务。", error.Message);
        Assert.AreSame(external, f.Session.Transaction); Assert.IsFalse(external.Disposed); Assert.AreEqual(0, external.Rollbacks);
        Assert.HasCount(0, f.Prepared!.Acquisitions); Assert.AreEqual(0, f.Connection.BeginAttempts);
        Assert.HasCount(0, f.Repository.TargetPreparations);
    }

    /// <summary>同一服务第一调用准备等待期间第二调用完成，服务器身份各自局部保存，不能串写审计。</summary>
    [TestMethod]
    public async Task InterleavedCalls_KeepInvocationLocalAuditIdentity()
    {
        var f = new Fixture(); var pending = new TaskCompletionSource<IAsyncDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Prepared!.OnAcquire = (id, _) => f.Prepared.Acquisitions.Count == 1 ? pending.Task : Task.FromResult<IAsyncDisposable?>(f.Prepared.Lease(id));
        var first = f.Write();
        try
        {
            Assert.HasCount(1, f.Prepared.Acquisitions); Assert.AreEqual(0, f.Connection.BeginAttempts); var firstId = f.Prepared.Acquisitions[0].Id;
            Assert.IsTrue((await f.Write()).Success); var secondId = f.Prepared.Acquisitions[1].Id; Assert.AreNotEqual(firstId, secondId);
            pending.SetResult(f.Prepared.Lease(firstId)); Assert.IsTrue((await first.WaitAsync(TimeSpan.FromSeconds(3))).Success);
            CollectionAssert.AreEqual(new[] { secondId, firstId }, f.Logs.Entries.Select(log => log.CommandId).ToArray());
            CollectionAssert.AreEquivalent(new[] { firstId, secondId }, f.Prepared.Released.ToArray()); Assert.IsNull(f.Session.Transaction);
        }
        finally { if (f.Prepared.Acquisitions.Count > 0) pending.TrySetResult(f.Prepared.Lease(f.Prepared.Acquisitions[0].Id)); await first.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    /// <summary>只比较可观察的生命周期关键顺序，不检查私有实现文本。</summary>
    private static void Order(Fixture f, params string[] names)
    { var previous = -1; foreach (var name in names) { var index = f.Events.IndexOf(name); Assert.IsTrue(index > previous, $"顺序错误{name}：{string.Join(',', f.Events)}"); previous = index; } }

    /// <summary>确认本层事务释放和会话清空均先于租约释放。</summary>
    private static void Released(Fixture f)
    {
        Assert.IsNull(f.Session.Transaction); Assert.HasCount(1, f.Prepared!.Released); Assert.IsTrue(f.Connection.Transactions.All(tx => tx.Disposed));
        Order(f, "TransactionDispose", "LeaseDispose"); Order(f, "SessionClear", "LeaseDispose");
    }

    /// <summary>独立组装真实服务与核心接口，不访问共享 SQL 夹具、配置或网络；故障只在明确步骤注入。</summary>
    private sealed class Fixture
    {
        public readonly List<string> Events = [];
        public readonly SessionStub Session;
        public readonly ConnectionStub Connection;
        public readonly RepositoryStub Repository;
        public readonly LogStub Logs;
        public readonly PreparedLogStub? Prepared;
        public readonly UserStub User = new();
        public readonly ExecutionEligibilityService Service;
        public readonly SetExecutionEligibilityStatusCommand Command = new() { Kind = ExecutionEligibilityKind.Product, Uid = Guid.NewGuid(), ExpectedVersion = 1, IsActive = false, Reason = " 暂停资格 " };
        public string? ErrorStage;
        public Exception? OriginalError;
        public Action<string>? BeforeStep;
        /// <summary>选择准备或传统日志，诊断故障只用于证明清理不覆盖业务结果。</summary>
        public Fixture(bool prepared = true, bool failDiagnostics = false)
        {
            Connection = new(this); Session = new(Connection, Events); Repository = new(this);
            Logs = prepared ? Prepared = new PreparedLogStub(this) : new LogStub(this);
            Service = new(new ServiceBaseContext(Session, failDiagnostics ? new FailingLoggerFactory() : NullLoggerFactory.Instance), Repository, Logs, User, new CorrelationStub());
        }
        /// <summary>调用唯一公开资格命令，保留真实服务判断和日志构造。</summary>
        public Task<ServiceResult<ExecutionEligibilityStatusResult>> Write(CancellationToken token = default) => Service.SetStatusAsync(Command, token);
        /// <summary>记录步骤及令牌，并要求所有业务仓储工作都在同一 Serializable 事务。</summary>
        public void Step(string name, CancellationToken token)
        {
            Events.Add(name); Assert.IsNotNull(Session.Transaction); Assert.AreEqual(IsolationLevel.Serializable, Session.Transaction.IsolationLevel);
            BeforeStep?.Invoke(name); if (ErrorStage == name) throw OriginalError!; token.ThrowIfCancellationRequested();
        }
    }

    /// <summary>真实核心会话替身，显式记录服务清空事务时点，不将错误自动分类为业务冲突。</summary>
    private sealed class SessionStub(IDbConnection connection, List<string> events) : IDbSession, IDatabaseExceptionClassifier
    {
        private IDbTransaction? _transaction;
        public IDbConnection Connection => connection;
        public IDatabaseExceptionClassifier Errors => this;
        public IDbTransaction? Transaction { get => _transaction; set { _transaction = value; if (value is null) events.Add("SessionClear"); } }
        /// <summary>注入的技术异常保持未知，服务不得新增分类映射。</summary>
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
    }

    /// <summary>显式实现事务内目标准备及原资格接口，保留真实服务的资格和版本判断。</summary>
    private sealed class RepositoryStub(Fixture f) : IExecutionEligibilityRepository
    {
        public string? Rejection;
        public int Updates;
        /// <summary>记录每次范围准备的对象、原令牌和本次事务，供公开服务调用验证。</summary>
        public readonly List<(ExecutionEligibilityKind Kind, CancellationToken Token, IDbTransaction Transaction)> TargetPreparations = [];
        /// <summary>仅在目标准备阶段注入异步等待或取得后取消，不替代服务授权和业务判断。</summary>
        public Func<CancellationToken, Task>? OnTargetPrepare;
        /// <summary>记录准备阶段并要求授权或业务读取尚未发生，所有工作借用外层 Serializable 事务。</summary>
        public Task PrepareTargetBeforeSnapshotAsync(ExecutionEligibilityKind kind, CancellationToken token = default)
        {
            Assert.IsNotNull(f.Session.Transaction);
            Assert.IsFalse(TargetPreparations.Any(item => ReferenceEquals(item.Transaction, f.Session.Transaction)));
            Assert.IsFalse(f.Events.Skip(f.Events.LastIndexOf("Begin") + 1)
                .Any(stage => stage is "Permission" or "Target" or "Update" or "Audit"));
            TargetPreparations.Add((kind, token, f.Session.Transaction));
            f.Step("TargetPrepare", token);
            return OnTargetPrepare?.Invoke(token) ?? Task.CompletedTask;
        }
        /// <summary>记录事务授权重验，并按用例提供拒绝或原异常。</summary>
        public Task<bool> HasExecutionEligibilityPermissionAsync(Guid userId, CancellationToken token = default)
        { Assert.AreEqual(f.User.UserId, userId); f.Step("Permission", token); return Task.FromResult(Rejection != "permission"); }
        /// <summary>返回独立版本事实，资格、版本及缺失拒绝仍由服务决定。</summary>
        public Task<ExecutionEligibilityStatusResult?> GetForUpdateAsync(ExecutionEligibilityKind kind, Guid uid, CancellationToken token = default)
        {
            f.Step("Target", token); Assert.AreEqual(f.Command.Kind, kind); Assert.AreEqual(f.Command.Uid, uid);
            return Task.FromResult<ExecutionEligibilityStatusResult?>(Rejection == "missing" ? null : new() { Kind = kind, Uid = uid, Version = Rejection == "version" ? 2 : 1, IsActive = Rejection != "state" });
        }
        /// <summary>记录更新尝试，不声称假仓储具有数据库回滚功能。</summary>
        public Task<int> SetStatusAsync(SetExecutionEligibilityStatusCommand command, CancellationToken token = default)
        { f.Step("Update", token); Updates++; Assert.AreSame(f.Command, command); return Task.FromResult(Rejection == "zero" ? 0 : 1); }
    }

    /// <summary>无准备接口的原日志契约，记录真实服务生成的日志及事务状态。</summary>
    private class LogStub(Fixture f) : IBusinessOperationLogRepository
    {
        /// <summary>派生准备假件与原日志共用本例上下文，避免重复捕获主构造参数。</summary>
        protected Fixture Owner { get; } = f;
        public readonly List<BusinessOperationLog> Entries = [];
        public int Rows = 1;
        /// <summary>记录审计；现有接口没有令牌，审计后取消由真实服务提交前检查。</summary>
        public Task<int> InsertAsync(BusinessOperationLog log)
        { Owner.Step("Audit", CancellationToken.None); if (Rows == 1) Entries.Add(log); return Task.FromResult(Rows); }
        /// <summary>本生命周期测试不调用审计查询，误调用立即失败。</summary>
        public Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId) => throw new NotSupportedException();
        /// <summary>本生命周期测试不调用回执查询，误调用立即失败。</summary>
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId) => throw new NotSupportedException();
        /// <summary>本生命周期测试不调用追溯查询，误调用立即失败。</summary>
        public Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid id, CancellationToken token = default) => throw new NotSupportedException();
    }

    /// <summary>仅给日志添加已有可选能力，租约各自绑定本次局部 ID。</summary>
    private sealed class PreparedLogStub(Fixture f) : LogStub(f), ICommandTransactionPreparation
    {
        public readonly List<(Guid Id, CancellationToken Token)> Acquisitions = [];
        public readonly List<Guid> Released = [];
        public Func<Guid, CancellationToken, Task<IAsyncDisposable?>>? OnAcquire;
        public bool FailDispose;
        /// <summary>准备必须在业务事务外调用，记录原令牌及身份。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid id, CancellationToken token)
        { Owner.Events.Add("Acquire"); Assert.IsNull(Owner.Session.Transaction); Assert.AreNotEqual(Guid.Empty, id); Acquisitions.Add((id, token)); token.ThrowIfCancellationRequested(); return OnAcquire?.Invoke(id, token) ?? Task.FromResult<IAsyncDisposable?>(Lease(id)); }
        /// <summary>每次租约独立绑定身份，不共享当前命令字段。</summary>
        public IAsyncDisposable Lease(Guid id) => new LeaseStub(Owner, this, id);
        /// <summary>释放记录验证事务已结束且会话清空，可注入次生故障。</summary>
        private sealed class LeaseStub(Fixture f, PreparedLogStub owner, Guid id) : IAsyncDisposable
        {
            /// <summary>租约最后释放，不能在仍持有本层事务时假通过。</summary>
            public ValueTask DisposeAsync()
            {
                f.Events.Add("LeaseDispose"); owner.Released.Add(id); Assert.IsNull(f.Session.Transaction); Assert.IsTrue(f.Connection.Transactions.All(tx => tx.Disposed));
                if (owner.FailDispose) throw new InvalidOperationException("次生租约释放失败"); return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>无网络连接替身，仅支持显式事务及资源隔离，不允许创建 SQL 命令。</summary>
    private sealed class ConnectionStub(Fixture f) : IDbConnection
    {
        public readonly List<TransactionStub> Transactions = [];
        public int BeginAttempts;
        public Exception? BeginError;
        public bool FailRollback, FailTransactionDispose, FailConnectionDispose;
        /// <summary>事务对象返回前注入取消，验证 Begin 后进入目标准备的令牌边界。</summary>
        public Action? AfterBegin;
        /// <summary>禁止降为默认隔离级别。</summary>
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        /// <summary>记录事务开启及原异常；没有创建的事务不能由清理逻辑虚构释放。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel level)
        { f.Events.Add("Begin"); BeginAttempts++; if (BeginError is not null) throw BeginError; var tx = new TransactionStub(f, level); Transactions.Add(tx); AfterBegin?.Invoke(); return tx; }
        public void Close() { }
        public void Open() { }
        public void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull] public string ConnectionString { get; set; } = "";
        public int ConnectionTimeout => 0;
        public string Database => "执行资格独立离线夹具";
        public ConnectionState State => ConnectionState.Open;
        /// <summary>事务释放失败后服务应隔离连接，关闭失败也不得遮蔽原结果。</summary>
        public void Dispose() { f.Events.Add("ConnectionDispose"); if (FailConnectionDispose) throw new InvalidOperationException("次生连接关闭失败"); }
    }

    /// <summary>仅记录提交、回滚和释放顺序，故障均可独立注入。</summary>
    private sealed class TransactionStub(Fixture f, IsolationLevel level) : IDbTransaction
    {
        public int Commits, Rollbacks;
        public bool Disposed;
        public IDbConnection Connection => f.Connection;
        public IsolationLevel IsolationLevel => level;
        /// <summary>提交尝试可以失败，不能把提交失败当作成功。</summary>
        public void Commit() { f.Events.Add("Commit"); Commits++; if (f.ErrorStage == "Commit") throw f.OriginalError!; }
        /// <summary>回滚清理错误用独立异常表示，便于验证原异常对象仍被保留。</summary>
        public void Rollback() { f.Events.Add("Rollback"); Rollbacks++; if (f.Connection.FailRollback) throw new InvalidOperationException("次生回滚失败"); }
        /// <summary>记录释放尝试，不将异常吞进假件以冒充服务安全清理。</summary>
        public void Dispose() { f.Events.Add("TransactionDispose"); Disposed = true; if (f.Connection.FailTransactionDispose) throw new InvalidOperationException("次生事务释放失败"); }
    }

    /// <summary>身份守卫用例可独立调整登录标志及真实用户 ID。</summary>
    private sealed class UserStub : ICurrentUser
    {
        public bool Authenticated = true;
        public Guid? Id = Guid.NewGuid();
        public bool IsAuthenticated => Authenticated;
        public Guid? UserId => Id;
        public string UserName => "资格准备测试用户";
    }

    /// <summary>原关联标识仅作为日志投影，不引入命令重放身份。</summary>
    private sealed class CorrelationStub : ICorrelationContext
    { public string CorrelationId => "测试关联"; }

    /// <summary>故意让清理诊断创建日志器失败，验证日志故障不能掩盖确定结果。</summary>
    private sealed class FailingLoggerFactory : ILoggerFactory
    {
        /// <summary>清理诊断失败必须由服务保护，假件不吞掉故障。</summary>
        public ILogger CreateLogger(string categoryName) => throw new InvalidOperationException("次生诊断失败");
        /// <summary>不允许用例添加额外日志提供者。</summary>
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        /// <summary>本假件不持有任何资源。</summary>
        public void Dispose() { }
    }
}
