using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Auth.Permissions;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Core.Interfaces.Commands;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>用独立无网络夹具验证 RBAC 可选事务准备、每次命令身份和事务先释放再释放租约；不替代真实 PG 并发验收。</summary>
[TestClass]
public sealed class RbacTransactionPreparationTests
{
    /// <summary>成功写入先事务外准备，再 Serializable/管理员锁/写入/审计，审计沿用准备身份，最后释放事务及租约。</summary>
    [TestMethod]
    public async Task Success_PreparesBeforeSerializableAndSharesAuditIdentity()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        var result = await f.WriteAsync(cancellation.Token);
        Assert.IsTrue(result.Success);
        Assert.HasCount(1, f.Prepared!.Acquisitions); var acquired = f.Prepared.Acquisitions.Single();
        Assert.AreNotEqual(Guid.Empty, acquired.Id); Assert.AreEqual(cancellation.Token, acquired.Token);
        Assert.AreEqual(acquired.Id, f.Logs.Entries.Single().CommandId);
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.Transactions.Single().IsolationLevel);
        Assert.AreEqual(1, f.Connection.Transactions.Single().Commits);
        AssertOrder(f, "Acquire", "Begin", "CheckAdmin", "Write", "Audit", "Commit");
        AssertReleased(f);
    }

    /// <summary>管理员拒绝和业务校验拒绝仍结束已准备事务并释放租约，不能写审计或提交。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Rejection_RollsBackAndReleasesPreparedLease(bool businessRejection)
    {
        var f = new Fixture(); f.Repository.ActorIsAdmin = businessRejection;
        var result = await f.WriteAsync(default, businessRejection ? 2 : 1);
        Assert.AreEqual(businessRejection ? ServiceErrorCodes.VersionConflict : ServiceErrorCodes.Forbidden, result.Error?.Code);
        Assert.AreEqual(0, f.Repository.Writes); Assert.HasCount(0, f.Logs.Entries);
        Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks);
        Assert.AreEqual(0, f.Connection.Transactions.Single().Commits); AssertReleased(f);
    }

    /// <summary>审计抛出异常或零行均返回既有 Unexpected，回滚后释放租约且不能宣称内存假仓储具备数据库回滚。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AuditFailure_RollsBackAndReleasesLease(bool zeroRows)
    {
        var f = new Fixture(); f.Logs.FailAudit = !zeroRows; f.Logs.AuditRows = zeroRows ? 0 : 1;
        var result = await f.WriteAsync();
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error?.Code);
        Assert.AreEqual(0, f.Connection.Transactions.Single().Commits);
        Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks); AssertReleased(f);
    }

    /// <summary>准备阶段取消原样传播原令牌，未开始事务，未创建的租约不能被释放。</summary>
    [TestMethod]
    public async Task CancellationDuringPreparation_PropagatesWithoutTransaction()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Prepared!.OnAcquire = (_, token) => { cancellation.Cancel(); throw new OperationCanceledException(token); };
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.WriteAsync(cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.HasCount(1, f.Prepared.Acquisitions); Assert.AreEqual(0, f.Connection.BeginAttempts);
        Assert.HasCount(0, f.Prepared.Released); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>获取租约后取消或事务内取消均保留原令牌；已建事务先回滚释放，租约最后释放。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAfterLease_PropagatesAndCleansUp(bool duringTransaction)
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        if (duringTransaction) f.Repository.BeforeCheck = _ => cancellation.Cancel();
        else f.Prepared!.OnAcquire = (id, _) => { cancellation.Cancel(); return Task.FromResult<IAsyncDisposable?>(f.Prepared.Lease(id)); };
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.WriteAsync(cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken);
        Assert.HasCount(1, f.Prepared!.Released); Assert.IsNull(f.Session.Transaction);
        Assert.IsTrue(f.Connection.Transactions.All(tx => tx.Disposed));
        if (duringTransaction) Assert.AreEqual(1, f.Connection.Transactions.Single().Rollbacks);
        Assert.HasCount(0, f.Logs.Entries);
    }

    /// <summary>空租约表示准备超时，返回 Unexpected 且零事务、零管理员锁及零写入。</summary>
    [TestMethod]
    public async Task EmptyLease_ReturnsUnexpectedWithoutStartingTransaction()
    {
        var f = new Fixture(); f.Prepared!.OnAcquire = (_, _) => Task.FromResult<IAsyncDisposable?>(null);
        var result = await f.WriteAsync();
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error?.Code);
        Assert.HasCount(1, f.Prepared.Acquisitions); Assert.AreEqual(0, f.Connection.BeginAttempts);
        Assert.HasCount(0, f.Prepared.Released); Assert.IsFalse(f.Events.Contains("CheckAdmin"));
        Assert.AreEqual(0, f.Repository.Writes); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>准备异常和准备超时异常使用既有 Unexpected，不创建事务或虚构租约释放。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparationFailure_ReturnsUnexpectedWithoutTransaction(bool timeout)
    {
        var f = new Fixture(); f.Prepared!.OnAcquire = (_, _) => throw (timeout ? new TimeoutException("模拟准备超时") : new InvalidOperationException("模拟准备失败"));
        var result = await f.WriteAsync();
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error?.Code);
        Assert.AreEqual(0, f.Connection.BeginAttempts); Assert.AreEqual(0, f.Repository.Writes);
        Assert.HasCount(0, f.Prepared.Released); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>事务开启失败时也释放已获得租约，返回 Unexpected，不留下会话事务。</summary>
    [TestMethod]
    public async Task BeginTransactionFailure_ReleasesAcquiredLease()
    {
        var f = new Fixture(); f.Connection.FailBegin = true;
        var result = await f.WriteAsync();
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error?.Code);
        Assert.AreEqual(1, f.Connection.BeginAttempts); Assert.HasCount(0, f.Connection.Transactions);
        Assert.HasCount(1, f.Prepared!.Released); Assert.IsNull(f.Session.Transaction);
        AssertOrder(f, "Acquire", "Begin", "LeaseDispose");
    }

    /// <summary>无准备接口的日志保留原 SQL Server 路径，仍使用 Serializable、成功审计及会话清理。</summary>
    [TestMethod]
    public async Task LegacyLogs_KeepExistingBehaviorWithoutPreparation()
    {
        var f = new Fixture(prepared: false); var result = await f.WriteAsync();
        Assert.IsTrue(result.Success); Assert.HasCount(1, f.Logs.Entries);
        Assert.AreNotEqual(Guid.Empty, f.Logs.Entries.Single().CommandId);
        Assert.IsFalse(f.Events.Contains("Acquire")); Assert.IsFalse(f.Events.Contains("LeaseDispose"));
        Assert.AreEqual(IsolationLevel.Serializable, f.Connection.Transactions.Single().IsolationLevel);
        Assert.IsTrue(f.Connection.Transactions.Single().Disposed); Assert.IsNull(f.Session.Transaction);
    }

    /// <summary>登录失效或调用方已有事务先拒绝，不获取租约、不接管或释放调用方事务。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingTransactionOrUnauthenticatedActor_RejectsBeforePreparation(bool existingTransaction)
    {
        var f = new Fixture();
        TransactionStub? external = null;
        if (existingTransaction) { external = new TransactionStub(f.Connection, IsolationLevel.ReadCommitted); f.Session.Transaction = external; }
        else f.CurrentUser.Authenticated = false;
        var result = await f.WriteAsync();
        Assert.AreEqual(existingTransaction ? ServiceErrorCodes.BusinessBlocked : ServiceErrorCodes.Unauthenticated, result.Error?.Code);
        Assert.HasCount(0, f.Prepared!.Acquisitions); Assert.AreEqual(0, f.Connection.BeginAttempts);
        Assert.AreSame(external, f.Session.Transaction);
        if (external is not null) { Assert.IsFalse(external.Disposed); Assert.AreEqual(0, external.Rollbacks); }
    }

    /// <summary>事务释放、租约释放或回滚清理异常均不能掩盖已确定成功或 Forbidden 结果。</summary>
    [TestMethod]
    [DataRow(true, true, false, false)]
    [DataRow(true, false, true, false)]
    [DataRow(false, true, true, false)]
    [DataRow(false, false, true, true)]
    public async Task CleanupExceptions_DoNotMaskDeterminedResult(bool success, bool failDispose, bool failLease, bool failRollback)
    {
        var f = new Fixture(); f.Repository.ActorIsAdmin = success;
        f.Connection.FailDispose = failDispose; f.Connection.FailRollback = failRollback; f.Prepared!.FailDispose = failLease;
        var result = await f.WriteAsync();
        if (success) Assert.IsTrue(result.Success); else Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error?.Code);
        AssertReleased(f);
    }

    /// <summary>取消主异常不能被回滚、事务释放或租约释放的次生异常取代。</summary>
    [TestMethod]
    public async Task CleanupExceptions_DoNotMaskCancellation()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Repository.BeforeCheck = _ => cancellation.Cancel(); f.Connection.FailRollback = true; f.Connection.FailDispose = true; f.Prepared!.FailDispose = true;
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => f.WriteAsync(cancellation.Token));
        Assert.AreEqual(cancellation.Token, error.CancellationToken); AssertReleased(f);
    }

    /// <summary>事务 Dispose 失败时关闭连接，关闭再失败也继续清空会话及释放租约，保留成功或拒绝结果。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TransactionDisposeFailure_ClosesConnectionBeforeLeaseEvenWhenCloseThrows(bool success)
    {
        var f = new Fixture(); f.Repository.ActorIsAdmin = success;
        f.Connection.FailDispose = true; f.Connection.FailConnectionDispose = true;
        var result = await f.WriteAsync();
        if (success) Assert.IsTrue(result.Success); else Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error?.Code);
        AssertOrder(f, "TransactionDispose", "ConnectionDispose", "LeaseDispose");
        AssertReleased(f);
    }

    /// <summary>准备等待期间同一服务另一调用完成后，第一调用仍审计自己的身份，防止把 CommandId 保存到可变服务字段。</summary>
    [TestMethod]
    public async Task InterleavedPreparation_KeepsPerInvocationCommandIdentity()
    {
        var f = new Fixture(); var firstLease = new TaskCompletionSource<IAsyncDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Prepared!.OnAcquire = (id, _) => f.Prepared.Acquisitions.Count == 1 ? firstLease.Task : Task.FromResult<IAsyncDisposable?>(f.Prepared.Lease(id));
        var first = f.WriteAsync();
        Assert.HasCount(1, f.Prepared.Acquisitions); Assert.AreEqual(0, f.Connection.BeginAttempts);
        var firstId = f.Prepared.Acquisitions[0].Id;
        try
        {
            Assert.IsTrue((await f.WriteAsync()).Success);
            Assert.HasCount(2, f.Prepared.Acquisitions); var secondId = f.Prepared.Acquisitions[1].Id;
            Assert.AreNotEqual(firstId, secondId); Assert.AreNotEqual(Guid.Empty, secondId);
            firstLease.SetResult(f.Prepared.Lease(firstId)); Assert.IsTrue((await first).Success);
            CollectionAssert.AreEqual(new[] { secondId, firstId }, f.Logs.Entries.Select(log => log.CommandId).ToArray());
            CollectionAssert.AreEquivalent(new[] { firstId, secondId }, f.Prepared.Released.ToArray());
            Assert.IsNull(f.Session.Transaction); Assert.IsTrue(f.Connection.Transactions.All(tx => tx.Disposed));
        }
        finally
        {
            firstLease.TrySetResult(f.Prepared.Lease(firstId)); await first;
        }
    }

    /// <summary>验证事件严格先后，仅比较生命周期关键点，不依赖内部日志或私有方法实现。</summary>
    private static void AssertOrder(Fixture f, params string[] names)
    {
        var previous = -1;
        foreach (var name in names) { var index = f.Events.IndexOf(name); Assert.IsTrue(index > previous, $"生命周期顺序错误：{name}；实际 {string.Join(',', f.Events)}"); previous = index; }
    }

    /// <summary>确认事务释放及清空会话均发生在租约释放前，且未留下事务引用。</summary>
    private static void AssertReleased(Fixture f)
    {
        Assert.IsNull(f.Session.Transaction); Assert.HasCount(1, f.Prepared!.Released);
        Assert.IsTrue(f.Connection.Transactions.All(tx => tx.Disposed));
        AssertOrder(f, "TransactionDispose", "LeaseDispose");
        AssertOrder(f, "SessionClear", "LeaseDispose");
    }

    /// <summary>独立组装核心会话、仓储及可选准备日志，不反射共享夹具、不访问运行配置或网络。</summary>
    private sealed class Fixture
    {
        public readonly List<string> Events = [];
        public readonly SessionStub Session;
        public readonly ConnectionStub Connection;
        public readonly RepositoryStub Repository;
        public readonly LogStub Logs;
        public readonly PreparedLogStub? Prepared;
        public readonly CurrentUserStub CurrentUser = new();
        public readonly RbacManagementService Service;
        /// <summary>按用例选择带准备能力或旧日志契约，其余组件独立且不共享运行状态。</summary>
        public Fixture(bool prepared = true)
        {
            Connection = new ConnectionStub(Events); Session = new SessionStub(Connection, Events);
            Repository = new RepositoryStub(Events);
            Logs = prepared ? Prepared = new PreparedLogStub(Session, Events, Connection) : new LogStub(Session, Events);
            Service = new RbacManagementService(new ServiceBaseContext(Session, NullLoggerFactory.Instance), Repository,
                new EvaluationStub(), Logs, CurrentUser);
        }
        /// <summary>走真实公开管理员命令，用空角色集合减少与生命周期无关的业务依赖。</summary>
        public Task<ServiceResult<UserRolesDto>> WriteAsync(CancellationToken token = default, int version = 1) =>
            Service.ReplaceUserRolesAsync(Repository.UserId, new ReplaceUserRolesRequest { ExpectedVersion = version, RoleIds = [] }, token);
    }

    /// <summary>提供真实核心会话契约并记录本层何时清空事务，异常分类保持未知而不猜测数据库失败。</summary>
    private sealed class SessionStub(IDbConnection connection, List<string> events) : IDbSession, IDatabaseExceptionClassifier
    {
        private IDbTransaction? _transaction;
        public IDbConnection Connection => connection;
        public IDatabaseExceptionClassifier Errors => this;
        public IDbTransaction? Transaction
        {
            get => _transaction;
            set { _transaction = value; if (value is null) events.Add("SessionClear"); }
        }
        /// <summary>本独立夹具注入技术异常均为未知，测试服务既有 Unexpected 分类。</summary>
        public DatabaseFailureKind Classify(Exception exception) => DatabaseFailureKind.Unknown;
    }

    /// <summary>仅模拟公开命令所需角色聚合读取和写入，其余接口调用立即拒绝，避免假通过。</summary>
    private sealed class RepositoryStub(List<string> events) : IRbacManagementRepository
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public bool ActorIsAdmin = true;
        public int Writes;
        public Action<CancellationToken>? BeforeCheck;
        /// <summary>记录实际管理员锁定位置并可在事务内注入取消。</summary>
        public Task<bool> LockAndCheckAdministratorAsync(Guid actorUserId, CancellationToken token)
        { events.Add("CheckAdmin"); BeforeCheck?.Invoke(token); token.ThrowIfCancellationRequested(); return Task.FromResult(ActorIsAdmin); }
        /// <summary>返回稳定版本目标，版本拒绝不依赖假仓储回滚。</summary>
        public Task<ManagedUserDto?> GetUserForUpdateAsync(Guid id, CancellationToken token) => Task.FromResult<ManagedUserDto?>(new() { Uid = id, Version = 1, UserName = "目标", IsActive = true });
        /// <summary>空角色集合的既有关系读取。</summary>
        public Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<Guid>>([]);
        /// <summary>本命令输入空集合，返回相同空集合，不新增管理员资格逻辑。</summary>
        public Task<IReadOnlyList<ManagedRoleDto>> GetActiveRolesAsync(IReadOnlyCollection<Guid> ids, CancellationToken token) => Task.FromResult<IReadOnlyList<ManagedRoleDto>>([]);
        /// <summary>记录写入调用，不声称模拟数据库回滚。</summary>
        public Task ReplaceUserRolesAsync(Guid id, int version, IReadOnlyCollection<Guid> ids, CancellationToken token) { Writes++; events.Add("Write"); return Task.CompletedTask; }
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<PagedResponseBase<ManagedUserDto>> SearchUsersAsync(ManagedPageRequest request, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<ManagedUserDto?> GetUserAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<PagedResponseBase<ManagedRoleDto>> SearchRolesAsync(ManagedPageRequest request, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<ManagedRoleDto?> GetRoleAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<ManagedRoleDto?> GetRoleForUpdateAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<IReadOnlyList<Guid>> GetRolePermissionIdsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<IReadOnlyList<ManagedPermissionDto>> GetPermissionsAsync(CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<IReadOnlyList<ManagedPermissionDto>> GetActivePermissionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<int> CountActiveAdministratorsAsync(CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<bool> UpdateUserAsync(Guid id, int version, string? name, string? email, string? phone, bool? active, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<Guid> CreateRoleAsync(string code, string name, string? description, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<bool> UpdateRoleAsync(Guid id, int version, string? name, string? description, bool? active, bool delete, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<int> CountRoleUsersAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task ReplaceRolePermissionsAsync(Guid id, int version, IReadOnlyCollection<Guid> ids, CancellationToken token) => throw new NotSupportedException();
    }

    /// <summary>原日志契约不实现准备接口，用于验证 SQL Server 兼容路径及事务内审计。</summary>
    private class LogStub(SessionStub session, List<string> events) : IBusinessOperationLogRepository
    {
        /// <summary>向可选准备派生假件共享同一会话。</summary>
        protected SessionStub Session { get; } = session;
        /// <summary>向可选准备派生假件共享同一事件序列。</summary>
        protected List<string> Events { get; } = events;
        public readonly List<BusinessOperationLog> Entries = [];
        public bool FailAudit;
        public int AuditRows = 1;
        /// <summary>审计必须持有 Serializable 事务；可以注入异常或零行，而不触碰业务数据库。</summary>
        public Task<int> InsertAsync(BusinessOperationLog log)
        {
            Events.Add("Audit"); Assert.IsNotNull(Session.Transaction); Assert.AreEqual(IsolationLevel.Serializable, Session.Transaction.IsolationLevel);
            if (FailAudit) throw new InvalidOperationException("模拟审计失败");
            if (AuditRows == 1) Entries.Add(log); return Task.FromResult(AuditRows);
        }
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module, Guid id, CancellationToken token = default) => throw new NotSupportedException();
    }

    /// <summary>只给独立日志添加现有可选准备能力，记录每次身份、原令牌及租约释放条件。</summary>
    private sealed class PreparedLogStub(SessionStub session, List<string> events, ConnectionStub connection)
        : LogStub(session, events), ICommandTransactionPreparation
    {
        public readonly List<(Guid Id, CancellationToken Token)> Acquisitions = [];
        public readonly List<Guid> Released = [];
        public Func<Guid, CancellationToken, Task<IAsyncDisposable?>>? OnAcquire;
        public bool FailDispose;
        /// <summary>事务外取得租约，任何准备失败在 BeginTransaction 前可观察。</summary>
        public Task<IAsyncDisposable?> AcquireBeforeTransactionAsync(Guid commandId, CancellationToken token)
        {
            Events.Add("Acquire"); Assert.IsNull(Session.Transaction); Assert.AreNotEqual(Guid.Empty, commandId);
            Acquisitions.Add((commandId, token));
            return OnAcquire?.Invoke(commandId, token) ?? Task.FromResult<IAsyncDisposable?>(Lease(commandId));
        }
        /// <summary>创建属于本次调用的独立租约，不用可变当前命令身份。</summary>
        public IAsyncDisposable Lease(Guid id) => new LeaseStub(this, id, Session, Events, connection);
        /// <summary>释放前确认事务释放且会话清空，并可注入次生清理异常。</summary>
        private sealed class LeaseStub(PreparedLogStub owner, Guid id, SessionStub session, List<string> events, ConnectionStub connection) : IAsyncDisposable
        {
            /// <summary>捕获清理顺序，故障注入发生在可观察释放之后，不能被误当成原业务失败。</summary>
            public ValueTask DisposeAsync()
            {
                events.Add("LeaseDispose"); owner.Released.Add(id);
                Assert.IsNull(session.Transaction); Assert.IsTrue(connection.Transactions.All(tx => tx.Disposed));
                if (owner.FailDispose) throw new InvalidOperationException("模拟租约释放失败");
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>模拟事务开启与释放，不创建 SQL 命令或连接网络。</summary>
    private sealed class ConnectionStub(List<string> events) : IDbConnection
    {
        public readonly List<TransactionStub> Transactions = [];
        public int BeginAttempts;
        public bool FailBegin, FailDispose, FailRollback, FailConnectionDispose;
        /// <summary>管理写入必须显式选择隔离，防止默默退回默认事务。</summary>
        public IDbTransaction BeginTransaction() => throw new NotSupportedException();
        /// <summary>记录开启位置、隔离及可恢复的开启失败。</summary>
        public IDbTransaction BeginTransaction(IsolationLevel level)
        {
            events.Add("Begin"); BeginAttempts++;
            if (FailBegin) throw new InvalidOperationException("模拟事务开启失败");
            var tx = new TransactionStub(this, level); Transactions.Add(tx); return tx;
        }
        /// <summary>仅供本例事务记录生命周期事件。</summary>
        public void Record(string name) => events.Add(name);
        public void Close() { }
        public void Open() { }
        public void ChangeDatabase(string name) => throw new NotSupportedException();
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "无网络事务准备夹具";
        public ConnectionState State => ConnectionState.Open;
        /// <summary>记录服务在事务释放失败后隔离连接的尝试，可注入关闭次生异常。</summary>
        public void Dispose() { events.Add("ConnectionDispose"); if (FailConnectionDispose) throw new InvalidOperationException("模拟连接关闭失败"); }
    }

    /// <summary>记录提交/回滚/释放并允许清理故障，不能把假仓储状态宣称为真实 SQL 原子性。</summary>
    private sealed class TransactionStub(ConnectionStub connection, IsolationLevel level) : IDbTransaction
    {
        public int Commits, Rollbacks;
        public bool Disposed;
        public IDbConnection Connection => connection;
        public IsolationLevel IsolationLevel => level;
        /// <summary>记录确定的成功提交。</summary>
        public void Commit() { connection.Record("Commit"); Commits++; }
        /// <summary>记录回滚尝试并可注入次生异常。</summary>
        public void Rollback() { connection.Record("Rollback"); Rollbacks++; if (connection.FailRollback) throw new InvalidOperationException("模拟回滚失败"); }
        /// <summary>记录资源释放并可注入清理异常，租约必须最后释放。</summary>
        public void Dispose() { connection.Record("TransactionDispose"); Disposed = true; if (connection.FailDispose) throw new InvalidOperationException("模拟事务释放失败"); }
    }

    /// <summary>固定已认证操作者，只为身份拒绝场景切换认证状态。</summary>
    private sealed class CurrentUserStub : ICurrentUser
    {
        public bool Authenticated = true;
        public bool IsAuthenticated => Authenticated;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "事务准备测试管理员";
    }

    /// <summary>本组只测写路径；若误调用只读权限入口立即失败，避免掩盖行为改变。</summary>
    private sealed class EvaluationStub : IPermissionEvaluationService
    {
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken token = default) => throw new NotSupportedException();
        /// <summary>拒绝本生命周期用例不使用的接口，防止意外扩展夹具导致假通过。</summary>
        public Task<bool> HasPermissionAsync(Guid userId, string key, CancellationToken token = default) => throw new NotSupportedException();
    }
}
