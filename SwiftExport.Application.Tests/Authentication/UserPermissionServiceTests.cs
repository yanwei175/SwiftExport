using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.Auth;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Models.Auth;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>验证模块目录、统一权限投影及管理员整组保存安全边界。</summary>
[TestClass]
public sealed class UserPermissionServiceTests
{
    /// <summary>一级模块目录保持不变，普通用户可分配目录额外包含虚拟仓独立管理能力。</summary>
    [TestMethod]
    public void ModuleCatalogContainsOnlyStableAssignableKeys()
    {
        CollectionAssert.AreEqual(new[]
        {
            PermissionKeys.ModuleProductsAccess,
            PermissionKeys.ModuleCustomersAccess,
            PermissionKeys.ModuleSuppliersAccess,
            PermissionKeys.ModuleSalesAccess,
            PermissionKeys.ModuleDemandPlanningAccess,
            PermissionKeys.ModuleProcurementAccess,
            PermissionKeys.ModuleInventoryAccess,
            PermissionKeys.ModuleShippingAccess,
            PermissionKeys.ModuleQualityControlAccess
        }, PermissionKeys.Modules.ToArray());
        CollectionAssert.DoesNotContain(PermissionKeys.Modules.ToList(), PermissionKeys.SystemSettingsManage);
        CollectionAssert.Contains(PermissionKeys.UserAssignable.ToList(), PermissionKeys.InventoryWarehouseManage);
        CollectionAssert.Contains(PermissionKeys.UserAssignable.ToList(), PermissionKeys.DemandPlanningMaterialEstablish);
        CollectionAssert.Contains(PermissionKeys.UserAssignable.ToList(), PermissionKeys.DemandPlanningMaterialRollback);
        CollectionAssert.Contains(PermissionKeys.UserAssignable.ToList(), PermissionKeys.DemandPlanningSourceManualManage);
        CollectionAssert.Contains(PermissionKeys.UserAssignable.ToList(), PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage);
        CollectionAssert.DoesNotContain(PermissionKeys.Modules.ToList(), PermissionKeys.DemandPlanningMaterialEstablish);
        CollectionAssert.DoesNotContain(PermissionKeys.Modules.ToList(), PermissionKeys.DemandPlanningMaterialRollback);
        CollectionAssert.DoesNotContain(PermissionKeys.Modules.ToList(), PermissionKeys.DemandPlanningSourceManualManage);
        CollectionAssert.DoesNotContain(PermissionKeys.Modules.ToList(), PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage);
        CollectionAssert.DoesNotContain(PermissionKeys.UserAssignable.ToList(), PermissionKeys.SystemSettingsManage);
    }

    /// <summary>验证管理员可保存虚拟仓独立管理能力，普通用户投影随后真实包含该权限。</summary>
    [TestMethod]
    public async Task WarehouseManagementPermission_ShouldBeAssignableAndProjected()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = AdminRepository(actorId);
        repository.Facts[targetId] = Facts(targetId, false, [], [PermissionKeys.InventoryWarehouseManage]);
        var connection = new ConnectionStub();
        var service = Service(repository, actorId, connection);

        var saved = await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto
        {
            ModulePermissions = [PermissionKeys.ModuleInventoryAccess, PermissionKeys.InventoryWarehouseManage]
        });
        var projection = await service.GetProjectionAsync(targetId);

        Assert.IsTrue(saved.Success);
        CollectionAssert.Contains(repository.LastModules.ToList(), PermissionKeys.InventoryWarehouseManage);
        Assert.IsNotNull(projection);
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.InventoryWarehouseManage);
    }

    /// <summary>两个物料操作键可分别保存和读取，需求模块访问不会自动展开为操作权限。</summary>
    [TestMethod]
    public async Task MaterialOperations_AreSavedReadAndIndependentOfModuleAccess()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = AdminRepository(actorId);
        repository.Facts[targetId] = Facts(targetId, false, [], []);
        var service = Service(repository, actorId);
        var saved = await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto
        {
            ModulePermissions = [PermissionKeys.ModuleDemandPlanningAccess, PermissionKeys.DemandPlanningMaterialEstablish]
        });
        var loaded = await service.GetUserModulesAsync(targetId);
        var projection = await service.GetProjectionAsync(targetId);
        Assert.IsTrue(saved.Success);
        Assert.IsTrue(loaded.Success);
        CollectionAssert.Contains(loaded.Data.ModulePermissions, PermissionKeys.DemandPlanningMaterialEstablish);
        Assert.IsTrue(await service.HasPermissionAsync(targetId, PermissionKeys.DemandPlanningMaterialEstablish));
        Assert.IsFalse(await service.HasPermissionAsync(targetId, PermissionKeys.DemandPlanningMaterialRollback));
        CollectionAssert.DoesNotContain(projection!.Permissions.ToList(), PermissionKeys.DemandPlanningMaterialRollback);

        await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto
        {
            ModulePermissions = [PermissionKeys.ModuleDemandPlanningAccess, PermissionKeys.DemandPlanningMaterialRollback]
        });
        Assert.IsFalse(await service.HasPermissionAsync(targetId, PermissionKeys.DemandPlanningMaterialEstablish));
        Assert.IsTrue(await service.HasPermissionAsync(targetId, PermissionKeys.DemandPlanningMaterialRollback));
    }

    /// <summary>两项维护权限可独立保存读取；单独模块访问不授予写入能力。</summary>
    [TestMethod]
    public async Task MaintenancePermissions_AreIndependentAndProjected()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = AdminRepository(actorId);
        repository.Facts[targetId] = Facts(targetId, false, [], []);
        var service = Service(repository, actorId);
        await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto
        {
            ModulePermissions = [PermissionKeys.ModuleDemandPlanningAccess]
        });
        Assert.IsFalse(await service.HasPermissionAsync(targetId, PermissionKeys.DemandPlanningSourceManualManage));
        Assert.IsFalse(await service.HasPermissionAsync(targetId, PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage));
        var saved = await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto
        {
            ModulePermissions = [PermissionKeys.ModuleDemandPlanningAccess,
                PermissionKeys.DemandPlanningSourceManualManage,
                PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage]
        });
        var loaded = await service.GetUserModulesAsync(targetId);
        var projection = await service.GetProjectionAsync(targetId);
        Assert.IsTrue(saved.Success);
        CollectionAssert.Contains(loaded.Data.ModulePermissions, PermissionKeys.DemandPlanningSourceManualManage);
        CollectionAssert.Contains(loaded.Data.ModulePermissions, PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage);
        CollectionAssert.Contains(projection!.Permissions.ToList(), PermissionKeys.DemandPlanningSourceManualManage);
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage);
    }

    /// <summary>普通用户保留历史细权限，采购模块只在唯一投影点展开为四个既有操作键。</summary>
    [TestMethod]
    public async Task NormalProjectionCombinesModulesLegacyPermissionsAndProcurementCompatibility()
    {
        var userId = Guid.NewGuid();
        var repository = new RepositoryStub();
        repository.Facts[userId] = Facts(userId, false,
            ["Buyer", PermissionKeys.ProcurementOrderRead],
            [PermissionKeys.ModuleProductsAccess, PermissionKeys.ModuleProcurementAccess, "Unknown.Module"]);
        var projection = await Service(repository, userId).GetProjectionAsync(userId);

        Assert.IsNotNull(projection);
        Assert.IsFalse(projection.IsAdmin);
        CollectionAssert.AreEqual(new[] { "Buyer" }, projection.Roles.ToArray());
        CollectionAssert.IsSubsetOf(PermissionKeys.ProcurementOperations.ToArray(), projection.Permissions.ToArray());
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.ModuleProductsAccess);
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.ModuleProcurementAccess);
        CollectionAssert.DoesNotContain(projection.Permissions.ToList(), "Unknown.Module");
        CollectionAssert.DoesNotContain(projection.Permissions.ToList(), PermissionKeys.SystemSettingsManage);

        var readOnlyUserId = Guid.NewGuid();
        repository.Facts[readOnlyUserId] = Facts(
            readOnlyUserId, false, [PermissionKeys.ProcurementOrderRead], []);
        var service = Service(repository, readOnlyUserId);
        Assert.IsTrue(await service.HasPermissionAsync(readOnlyUserId, PermissionKeys.ProcurementOrderRead));
        Assert.IsFalse(await service.HasPermissionAsync(readOnlyUserId, PermissionKeys.ProcurementOrderCreate));
    }

    /// <summary>Admin 权限完全由服务端固定生成，包含全部模块、设置管理和采购兼容权限。</summary>
    [TestMethod]
    public async Task AdminProjectionAlwaysReturnsAllServerPermissions()
    {
        var userId = Guid.NewGuid();
        var repository = new RepositoryStub();
        repository.Facts[userId] = Facts(userId, true, ["Admin"], []);
        var projection = await Service(repository, userId).GetProjectionAsync(userId);

        Assert.IsNotNull(projection);
        Assert.IsTrue(projection.IsAdmin);
        CollectionAssert.AreEqual(PermissionKeys.All.ToArray(), projection.Permissions.ToArray());
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.SystemSettingsManage);
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.DemandPlanningMaterialEstablish);
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.DemandPlanningMaterialRollback);
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.DemandPlanningSourceManualManage);
        CollectionAssert.Contains(projection.Permissions.ToList(), PermissionKeys.DemandPlanningMaterialSupplierSuggestionManage);
    }

    /// <summary>非管理员不能读取或保存，未知键和设置管理键在进入事务前即被拒绝。</summary>
    [TestMethod]
    public async Task ManagementRejectsNonAdminAndUnknownOrPrivilegedKeys()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = new RepositoryStub();
        repository.Facts[actorId] = Facts(actorId, false, ["Buyer"], []);
        var service = Service(repository, actorId);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await service.GetUsersAsync()).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await service.GetUserModulesAsync(targetId)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden,
            (await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto())).Error.Code);

        repository.Facts[actorId] = Facts(actorId, true, ["Admin"], []);
        foreach (var key in new[] { "Module.Unknown.Access", PermissionKeys.SystemSettingsManage })
        {
            var result = await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto
            {
                ModulePermissions = [key]
            });
            Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        }
        Assert.AreEqual(0, repository.ReplaceCalls);
    }

    /// <summary>目标成为 Admin 时仓储最终重验拒绝写入，并回滚调用方事务。</summary>
    [TestMethod]
    public async Task AdminTargetCannotBeModifiedAndTransactionRollsBack()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = AdminRepository(actorId);
        repository.ReplaceStatus = UserModuleReplaceStatus.TargetIsAdmin;
        var connection = new ConnectionStub();
        var result = await Service(repository, actorId, connection).SaveUserModulesAsync(
            targetId,
            new SaveUserModulePermissionsRequestDto { ModulePermissions = [PermissionKeys.ModuleSalesAccess] });

        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.AreEqual(0, connection.Transaction.Commits);
        Assert.AreEqual(1, connection.Transaction.Rollbacks);
    }

    /// <summary>完整集合只调用一次仓储并提交；仓储失败时回滚且不返回部分成功。</summary>
    [TestMethod]
    public async Task ReplacementCommitsWholeSetAndRollsBackFailures()
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var repository = AdminRepository(actorId);
        var connection = new ConnectionStub();
        var service = Service(repository, actorId, connection);
        var result = await service.SaveUserModulesAsync(targetId, new SaveUserModulePermissionsRequestDto
        {
            ModulePermissions = [PermissionKeys.ModuleSalesAccess, PermissionKeys.ModuleProductsAccess,
                PermissionKeys.ModuleSalesAccess]
        });
        Assert.IsTrue(result.Success);
        CollectionAssert.AreEquivalent(new[] { PermissionKeys.ModuleSalesAccess, PermissionKeys.ModuleProductsAccess },
            repository.LastModules.ToArray());
        Assert.AreEqual(1, repository.ReplaceCalls);
        Assert.AreEqual(1, connection.Transaction.Commits);
        Assert.AreEqual(0, connection.Transaction.Rollbacks);

        repository.ThrowOnReplace = true;
        connection = new ConnectionStub();
        result = await Service(repository, actorId, connection).SaveUserModulesAsync(
            targetId, new SaveUserModulePermissionsRequestDto());
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Unexpected, result.Error.Code);
        Assert.AreEqual(0, connection.Transaction.Commits);
        Assert.AreEqual(1, connection.Transaction.Rollbacks);
    }

    private static RepositoryStub AdminRepository(Guid actorId)
    {
        var repository = new RepositoryStub();
        repository.Facts[actorId] = Facts(actorId, true, ["Admin"], []);
        return repository;
    }

    private static UserPermissionFacts Facts(
        Guid userId,
        bool isAdmin,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> modules) => new()
    {
        UserId = userId,
        IsActive = true,
        IsAdmin = isAdmin,
        RoleNames = roles,
        ModulePermissions = modules
    };

    private static UserPermissionService Service(
        RepositoryStub repository,
        Guid actorId,
        ConnectionStub? connection = null)
    {
        var session = Session(connection ?? new ConnectionStub());
        return new UserPermissionService(
            new ServiceBaseContext(session, NullLoggerFactory.Instance), repository, new CurrentUserStub(actorId));
    }

    private static DbSession Session(IDbConnection connection)
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        typeof(DbSession).GetField("<Connection>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(session, connection);
        return session;
    }

    private sealed class CurrentUserStub(Guid userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public string UserName => "权限测试管理员";
    }

    private sealed class RepositoryStub : IUserPermissionRepository
    {
        public Dictionary<Guid, UserPermissionFacts> Facts { get; } = [];
        public int ReplaceCalls { get; private set; }
        public UserModuleReplaceStatus ReplaceStatus { get; set; } = UserModuleReplaceStatus.Updated;
        public bool ThrowOnReplace { get; set; }
        public IReadOnlyCollection<string> LastModules { get; private set; } = [];
        public Task<UserPermissionFacts?> GetPermissionFactsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Facts.GetValueOrDefault(userId));
        public Task<IReadOnlyList<UserSummaryDto>> GetActiveUsersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSummaryDto>>([]);
        public Task<UserModuleReplaceStatus> ReplaceModulesAsync(
            Guid actorUserId, Guid targetUserId, IReadOnlyCollection<string> modulePermissions,
            CancellationToken cancellationToken = default)
        {
            ReplaceCalls++;
            LastModules = modulePermissions.ToArray();
            if (ThrowOnReplace) throw new InvalidOperationException("模拟整组写入失败");
            if (ReplaceStatus == UserModuleReplaceStatus.Updated && Facts.TryGetValue(targetUserId, out var current))
            {
                Facts[targetUserId] = new UserPermissionFacts
                {
                    UserId = current.UserId, IsActive = current.IsActive, IsAdmin = current.IsAdmin,
                    RoleNames = current.RoleNames, ModulePermissions = modulePermissions.ToArray()
                };
            }
            return Task.FromResult(ReplaceStatus);
        }
    }

    private sealed class ConnectionStub : IDbConnection
    {
        public ConnectionStub()
        {
            Transaction = new TransactionStub(this);
        }

        public TransactionStub Transaction { get; }
        public IDbTransaction BeginTransaction() => Transaction;
        public IDbTransaction BeginTransaction(IsolationLevel il) { Transaction.Level = il; return Transaction; }
        public void Close() { }
        public void Open() { }
        public void ChangeDatabase(string databaseName) { }
        public IDbCommand CreateCommand() => throw new NotSupportedException();
        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => ConnectionState.Open;
        public void Dispose() { }
    }

    private sealed class TransactionStub(IDbConnection connection) : IDbTransaction
    {
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public IsolationLevel Level { get; set; }
        public IDbConnection? Connection { get; private set; } = connection;
        public IsolationLevel IsolationLevel => Level;
        public void Commit() => Commits++;
        public void Rollback() => Rollbacks++;
        public void Dispose() { }
    }
}
