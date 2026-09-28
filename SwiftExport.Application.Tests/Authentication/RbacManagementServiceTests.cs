using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Auth.Permissions;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auth.Permissions;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>验证管理员整组授权的业务拒绝、版本、事务与前后集合审计；SQL 并发另用隔离库验证。</summary>
[TestClass]
public sealed class RbacManagementServiceTests
{
    /// <summary>普通用户直接调用管理读写入口均被拒绝，关系与审计无变更。</summary>
    [TestMethod]
    public async Task NonAdministrator_CannotReadOrReplaceRoles()
    {
        var fixture = new Fixture { Repository = { ActorIsAdmin = false } };
        var read = await fixture.Service.GetPermissionsAsync(default);
        var write = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [fixture.RoleId] }, default);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, read.Error?.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, write.Error?.Code);
        Assert.AreEqual(0, fixture.Repository.UserWrites);
        Assert.AreEqual(0, fixture.Logs.Entries.Count);
        Assert.AreEqual(0, fixture.Connection.Transaction.Commits);
    }

    /// <summary>整组替换持久化新集合，递增聚合版本，并在同一事务追加前后集合审计。</summary>
    [TestMethod]
    public async Task ReplaceUserRoles_CommitsOneSetAndAudit()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [fixture.RoleId] }, default);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, result.Data.Version);
        CollectionAssert.AreEqual(new[] { fixture.RoleId }, fixture.Repository.UserRoles.ToArray());
        Assert.AreEqual(1, fixture.Repository.UserWrites);
        Assert.AreEqual(1, fixture.Connection.Transaction.Commits);
        Assert.AreEqual(0, fixture.Connection.Transaction.Rollbacks);
        var audit = fixture.Logs.Entries.Single();
        Assert.AreEqual("ReplaceUserRoles", audit.OperationType);
        StringAssert.Contains(audit.ChangedFieldsJson!, "Before");
        StringAssert.Contains(audit.ChangedFieldsJson!, "After");
        StringAssert.Contains(audit.ChangedFieldsJson!, fixture.RoleId.ToString());
    }

    /// <summary>用户角色整组保存后可重新读取，空集合清除旧关系并递增版本。</summary>
    [TestMethod]
    public async Task ReplaceUserRoles_ReadBackThenClearWholeSet()
    {
        var fixture = new Fixture();
        var assigned = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [fixture.RoleId] }, default);
        Assert.IsTrue(assigned.Success);
        var readAssigned = await fixture.Service.GetUserRolesAsync(fixture.UserId, default);
        Assert.IsTrue(readAssigned.Success);
        CollectionAssert.AreEqual(new[] { fixture.RoleId }, readAssigned.Data.RoleIds);
        var cleared = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = readAssigned.Data.Version, RoleIds = [] }, default);
        Assert.IsTrue(cleared.Success);
        var readCleared = await fixture.Service.GetUserRolesAsync(fixture.UserId, default);
        Assert.IsTrue(readCleared.Success);
        Assert.AreEqual(3, readCleared.Data.Version);
        Assert.HasCount(0, readCleared.Data.RoleIds);
        Assert.AreEqual(2, fixture.Repository.UserWrites);
        Assert.AreEqual(2, fixture.Logs.Entries.Count);
    }

    /// <summary>重复、无效角色及过期版本均回滚，不写关系或审计。</summary>
    [TestMethod]
    public async Task InvalidOrStaleUserRoles_RollBackWithoutPartialSave()
    {
        var fixture = new Fixture();
        var duplicate = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [fixture.RoleId, fixture.RoleId] }, default);
        var unknown = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [Guid.NewGuid()] }, default);
        var stale = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = 2, RoleIds = [fixture.RoleId] }, default);
        Assert.AreEqual(ServiceErrorCodes.Validation, duplicate.Error?.Code);
        Assert.AreEqual(ServiceErrorCodes.Validation, unknown.Error?.Code);
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, stale.Error?.Code);
        Assert.AreEqual(0, fixture.Repository.UserWrites);
        Assert.AreEqual(0, fixture.Logs.Entries.Count);
        Assert.AreEqual(3, fixture.Connection.Transaction.Rollbacks);
    }

    /// <summary>最后一位管理员不能撤销内置角色，事务回滚且保留原关系。</summary>
    [TestMethod]
    public async Task LastAdministrator_CannotWithdrawOwnRole()
    {
        var fixture = new Fixture();
        fixture.Repository.User.IsAdmin = true;
        fixture.Repository.ActiveAdministratorCount = 1;
        fixture.Repository.UserRoles = [fixture.RoleId];
        var result = await fixture.Service.ReplaceUserRolesAsync(fixture.UserId,
            new ReplaceUserRolesRequest { ExpectedVersion = 1, RoleIds = [] }, default);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error?.Code);
        CollectionAssert.AreEqual(new[] { fixture.RoleId }, fixture.Repository.UserRoles.ToArray());
        Assert.AreEqual(0, fixture.Repository.UserWrites);
        Assert.AreEqual(1, fixture.Connection.Transaction.Rollbacks);
    }

    /// <summary>普通角色只接受代码目录可分配键；成功时记录关系前后值。</summary>
    [TestMethod]
    public async Task ReplaceRolePermissions_RejectsReservedAndAuditsAssignable()
    {
        var fixture = new Fixture();
        var reservedId = Guid.NewGuid();
        fixture.Repository.PermissionDefinitions[reservedId] = new ManagedPermissionDto
        { Uid = reservedId, PermissionKey = "System.Users.Manage", IsActive = true, IsAssignable = false };
        var rejected = await fixture.Service.ReplaceRolePermissionsAsync(fixture.RoleId,
            new ReplaceRolePermissionsRequest { ExpectedVersion = 1, PermissionIds = [reservedId] }, default);
        Assert.AreEqual(ServiceErrorCodes.Validation, rejected.Error?.Code);
        Assert.AreEqual(0, fixture.Repository.RolePermissionWrites);
        var saved = await fixture.Service.ReplaceRolePermissionsAsync(fixture.RoleId,
            new ReplaceRolePermissionsRequest { ExpectedVersion = 1, PermissionIds = [fixture.PermissionId] }, default);
        Assert.IsTrue(saved.Success);
        Assert.AreEqual(2, saved.Data.Version);
        CollectionAssert.AreEqual(new[] { fixture.PermissionId }, fixture.Repository.RolePermissions.ToArray());
        Assert.AreEqual(1, fixture.Repository.RolePermissionWrites);
        var audit = fixture.Logs.Entries.Single();
        Assert.AreEqual("ReplaceRolePermissions", audit.OperationType);
        StringAssert.Contains(audit.ChangedFieldsJson!, fixture.PermissionId.ToString());
        Assert.AreEqual(1, fixture.Connection.Transaction.Commits);
        Assert.AreEqual(1, fixture.Connection.Transaction.Rollbacks);
    }

    /// <summary>角色权限保存后可读取，重复标识被拒绝，空集合清除旧权限。</summary>
    [TestMethod]
    public async Task ReplaceRolePermissions_ReadBackRejectDuplicateAndClear()
    {
        var fixture = new Fixture();
        var saved = await fixture.Service.ReplaceRolePermissionsAsync(fixture.RoleId,
            new ReplaceRolePermissionsRequest { ExpectedVersion = 1, PermissionIds = [fixture.PermissionId] }, default);
        Assert.IsTrue(saved.Success);
        var read = await fixture.Service.GetRolePermissionsAsync(fixture.RoleId, default);
        Assert.IsTrue(read.Success);
        CollectionAssert.AreEqual(new[] { fixture.PermissionId }, read.Data.PermissionIds);
        var duplicate = await fixture.Service.ReplaceRolePermissionsAsync(fixture.RoleId,
            new ReplaceRolePermissionsRequest { ExpectedVersion = read.Data.Version,
                PermissionIds = [fixture.PermissionId, fixture.PermissionId] }, default);
        Assert.AreEqual(ServiceErrorCodes.Validation, duplicate.Error?.Code);
        var cleared = await fixture.Service.ReplaceRolePermissionsAsync(fixture.RoleId,
            new ReplaceRolePermissionsRequest { ExpectedVersion = read.Data.Version, PermissionIds = [] }, default);
        Assert.IsTrue(cleared.Success);
        var readCleared = await fixture.Service.GetRolePermissionsAsync(fixture.RoleId, default);
        Assert.IsTrue(readCleared.Success);
        Assert.AreEqual(3, readCleared.Data.Version);
        Assert.HasCount(0, readCleared.Data.PermissionIds);
        Assert.AreEqual(2, fixture.Repository.RolePermissionWrites);
    }

    /// <summary>审计追加失败使授权命令返回失败并回滚事务，不能报告成功。</summary>
    [TestMethod]
    public async Task AuditFailure_RollsBackAuthorizationTransaction()
    {
        var fixture = new Fixture();
        fixture.Logs.FailInsert = true;
        var result = await fixture.Service.ReplaceRolePermissionsAsync(fixture.RoleId,
            new ReplaceRolePermissionsRequest { ExpectedVersion = 1, PermissionIds = [fixture.PermissionId] }, default);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error?.Code);
        Assert.AreEqual(0, fixture.Connection.Transaction.Commits);
        Assert.AreEqual(1, fixture.Connection.Transaction.Rollbacks);
    }

    /// <summary>组合服务、仓储、审计与事务假件，分别观察提交和回滚。</summary>
    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid RoleId { get; } = Guid.NewGuid();
        public Guid PermissionId { get; } = Guid.NewGuid();
        public ConnectionStub Connection { get; } = new();
        public RepositoryStub Repository { get; }
        public LogStub Logs { get; } = new();
        public RbacManagementService Service { get; }

        public Fixture()
        {
            Repository = new RepositoryStub(UserId, RoleId, PermissionId);
            var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(session, Connection);
            Service = new RbacManagementService(new ServiceBaseContext(session, NullLoggerFactory.Instance),
                Repository, new EvaluationStub(Repository), Logs, new CurrentUserStub(ActorId));
        }
    }

    /// <summary>模拟当前有效登录用户，不从请求体读取操作者标识。</summary>
    private sealed class CurrentUserStub(Guid userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public string UserName => "隔离测试管理员";
    }

    /// <summary>只为只读管理入口提供权限判定；管理写入由仓储的锁定重验控制。</summary>
    private sealed class EvaluationStub(RepositoryStub repository) : IPermissionEvaluationService
    {
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserPermissionProjection?>(null);
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(repository.ActorIsAdmin);
    }

    /// <summary>捕获追加式审计，模拟日志写入失败时事务必须回滚。</summary>
    private sealed class LogStub : IBusinessOperationLogRepository
    {
        public List<BusinessOperationLog> Entries { get; } = [];
        public bool FailInsert { get; set; }
        public Task<int> InsertAsync(BusinessOperationLog log)
        {
            if (FailInsert) return Task.FromResult(0);
            Entries.Add(log);
            return Task.FromResult(1);
        }
        public Task<BusinessOperationLog?> GetByModuleCommandAsync(string module, Guid commandId) => Task.FromResult<BusinessOperationLog?>(null);
        public Task<BusinessOperationLog?> GetByCommandAsync(string module, string operationType, Guid commandId) => Task.FromResult<BusinessOperationLog?>(null);
        public Task<IReadOnlyList<SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.BusinessOperationTraceItemDto>> GetByAggregateAsync(
            string module, Guid aggregateUid, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.BusinessOperationTraceItemDto>>([]);
    }

    /// <summary>只实现被测管理员命令实际用到的聚合读写；其余方法若被意外调用立即失败。</summary>
    private sealed class RepositoryStub(Guid userId, Guid roleId, Guid permissionId) : IRbacManagementRepository
    {
        public bool ActorIsAdmin { get; set; } = true;
        public int ActiveAdministratorCount { get; set; } = 2;
        public ManagedUserDto User { get; } = new() { Uid = userId, Version = 1, UserName = "target", IsActive = true };
        public ManagedRoleDto Role { get; } = new() { Uid = roleId, Version = 1, RoleCode = "ORDINARY_ROLE", IsActive = true };
        public List<Guid> UserRoles { get; set; } = [];
        public List<Guid> RolePermissions { get; set; } = [];
        public Dictionary<Guid, ManagedPermissionDto> PermissionDefinitions { get; } = new()
        {
            [permissionId] = new ManagedPermissionDto
            { Uid = permissionId, PermissionKey = "Suppliers.Supplier.View", IsActive = true, IsAssignable = true }
        };
        public int UserWrites { get; private set; }
        public int RolePermissionWrites { get; private set; }

        public Task<bool> LockAndCheckAdministratorAsync(Guid actorUserId, CancellationToken cancellationToken) => Task.FromResult(ActorIsAdmin);
        public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) => Task.FromResult(ActiveAdministratorCount);
        public Task<ManagedUserDto?> GetUserAsync(Guid id, CancellationToken token) => Task.FromResult<ManagedUserDto?>(id == userId ? User : null);
        public Task<ManagedUserDto?> GetUserForUpdateAsync(Guid id, CancellationToken token) => GetUserAsync(id, token);
        public Task<IReadOnlyList<Guid>> GetUserRoleIdsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<Guid>>(UserRoles.ToArray());
        public Task<IReadOnlyList<ManagedRoleDto>> GetActiveRolesAsync(IReadOnlyCollection<Guid> ids, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ManagedRoleDto>>(ids.Where(id => id == roleId && Role.IsActive).Select(_ => Role).ToArray());
        public Task ReplaceUserRolesAsync(Guid id, int expectedVersion, IReadOnlyCollection<Guid> ids, CancellationToken token)
        {
            UserWrites++; User.Version++; UserRoles = ids.ToList(); return Task.CompletedTask;
        }
        public Task<ManagedRoleDto?> GetRoleAsync(Guid id, CancellationToken token) => Task.FromResult<ManagedRoleDto?>(id == roleId ? Role : null);
        public Task<ManagedRoleDto?> GetRoleForUpdateAsync(Guid id, CancellationToken token) => GetRoleAsync(id, token);
        public Task<IReadOnlyList<Guid>> GetRolePermissionIdsAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<Guid>>(RolePermissions.ToArray());
        public Task<IReadOnlyList<ManagedPermissionDto>> GetActivePermissionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ManagedPermissionDto>>(ids.Where(PermissionDefinitions.ContainsKey).Select(id => PermissionDefinitions[id]).ToArray());
        public Task ReplaceRolePermissionsAsync(Guid id, int expectedVersion, IReadOnlyCollection<Guid> ids, CancellationToken token)
        {
            RolePermissionWrites++; Role.Version++; RolePermissions = ids.ToList(); return Task.CompletedTask;
        }
        public Task<PagedResponseBase<ManagedUserDto>> SearchUsersAsync(ManagedPageRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<PagedResponseBase<ManagedRoleDto>> SearchRolesAsync(ManagedPageRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ManagedPermissionDto>> GetPermissionsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<bool> UpdateUserAsync(Guid id, int version, string? name, string? email, string? phone, bool? active, CancellationToken token) => throw new NotSupportedException();
        public Task<Guid> CreateRoleAsync(string code, string name, string? description, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> UpdateRoleAsync(Guid id, int version, string? name, string? description, bool? active, bool delete, CancellationToken token) => throw new NotSupportedException();
        public Task<int> CountRoleUsersAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }

    /// <summary>记录管理事务提交与回滚次数，不假定内存仓储具有数据库回滚语义。</summary>
    private sealed class ConnectionStub : IDbConnection
    {
        public TransactionStub Transaction { get; }
        public ConnectionStub() => Transaction = new TransactionStub(this);
        public IDbTransaction BeginTransaction() => Transaction;
        public IDbTransaction BeginTransaction(IsolationLevel level) { Transaction.Level = level; return Transaction; }
        public void Close() { }
        public void Open() { }
        public void ChangeDatabase(string name) { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull] public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => ConnectionState.Open;
        public void Dispose() { }
    }

    /// <summary>提供可观测的事务结束结果；实际 SQL 原子性由隔离库验证。</summary>
    private sealed class TransactionStub(IDbConnection connection) : IDbTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public IsolationLevel Level { get; set; }
        public IDbConnection Connection => connection;
        public IsolationLevel IsolationLevel => Level;
        public void Commit() => Commits++;
        public void Rollback() => Rollbacks++;
        public void Dispose() { }
    }
}
