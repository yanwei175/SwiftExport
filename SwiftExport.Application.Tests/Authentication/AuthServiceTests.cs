using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Models.Auth;
using SwiftExport.Application.Services.Auth;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Tests.Authentication;

/// <summary>验证账号创建、密码维护及凭据版本的应用层安全边界。</summary>
[TestClass]
public sealed class AuthServiceTests
{
    /// <summary>注册契约返回 Guid，且不存在匿名重置旧契约。</summary>
    [TestMethod]
    public void RefitContractUsesGuidAndOnlyProtectedPasswordCommands()
    {
        var register = typeof(IAuthApi).GetMethod(nameof(IAuthApi.RegisterAsync))!;
        Assert.AreEqual(typeof(Task<ServiceResult<Guid>>), register.ReturnType);
        Assert.IsNull(typeof(IAuthApi).GetMethod("ResetPasswordAsync"));
        Assert.IsNotNull(typeof(IAuthApi).GetMethod(nameof(IAuthApi.ChangePasswordAsync)));
        Assert.IsNotNull(typeof(IAuthApi).GetMethod(nameof(IAuthApi.ResetUserPasswordAsync)));
        Assert.AreEqual(typeof(Task<ServiceResult<List<UserSummaryDto>>>),
            typeof(IAuthApi).GetMethod(nameof(IAuthApi.GetUsersAsync))!.ReturnType);
        Assert.AreEqual(typeof(Task<ServiceResult<UserModulePermissionsDto>>),
            typeof(IAuthApi).GetMethod(nameof(IAuthApi.GetUserModulesAsync))!.ReturnType);
        Assert.AreEqual(typeof(Task<ServiceResult<UserModulePermissionsDto>>),
            typeof(IAuthApi).GetMethod(nameof(IAuthApi.SaveUserModulesAsync))!.ReturnType);
    }

    /// <summary>只有真实 Admin 能创建用户；用户名规范化且结果保持预生成 Guid。</summary>
    [TestMethod]
    public async Task RegisterRequiresAdminAndReturnsCreatedGuid()
    {
        var actorId = Guid.NewGuid();
        var repository = new AuthRepositoryStub { Admins = { actorId } };
        var service = CreateService(repository, actorId);
        var result = await service.RegisterAsync(new RegisterRequestDto
        {
            UserName = "  new-user  ", Password = " secret ", ConfirmPassword = " secret ", NickName = "  新用户  "
        });
        Assert.IsTrue(result.Success);
        Assert.AreNotEqual(Guid.Empty, result.Data);
        Assert.AreEqual("new-user", repository.CreatedUser!.UserName);
        Assert.AreEqual("新用户", repository.CreatedUser.NickName);
        Assert.AreEqual("hash: secret ", repository.CreatedUser.PasswordHash);

        repository = new AuthRepositoryStub();
        result = await CreateService(repository, actorId).RegisterAsync(new RegisterRequestDto
        {
            UserName = "blocked", Password = "secret", ConfirmPassword = "secret"
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.IsNull(repository.CreatedUser);
    }

    /// <summary>并发窗口内由仓储唯一约束识别的重复用户名仍返回字段级业务失败。</summary>
    [TestMethod]
    public async Task RegisterNormalizesLateUniqueConflict()
    {
        var actorId = Guid.NewGuid();
        var repository = new AuthRepositoryStub { CreateStatus = AuthUserCreateStatus.UserNameInUse };
        repository.Admins.Add(actorId);
        var result = await CreateService(repository, actorId).RegisterAsync(new RegisterRequestDto
        {
            UserName = "duplicate", Password = "secret", ConfirmPassword = "secret"
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Validation, result.Error.Code);
        Assert.AreEqual("UserName", result.Error.Field);
    }

    /// <summary>错误旧密码是业务失败，不更新凭据也不伪装成会话 401。</summary>
    [TestMethod]
    public async Task ChangeOwnPasswordRejectsWrongCurrentPasswordWithoutMutation()
    {
        var user = User("user", "hash:old-pass", credentialVersion: 3);
        var repository = new AuthRepositoryStub(user);
        var result = await CreateService(repository, user.Uid).ChangeOwnPasswordAsync(new ChangePasswordRequestDto
        {
            CurrentPassword = "wrong", NewPassword = "new-pass", ConfirmPassword = "new-pass"
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.BusinessBlocked, result.Error.Code);
        Assert.AreEqual(0, repository.OwnPasswordUpdates);
        Assert.AreEqual(3, user.CredentialVersion);
    }

    /// <summary>本人改密只取当前身份，成功后旧凭据版本立即失效。</summary>
    [TestMethod]
    public async Task ChangeOwnPasswordInvalidatesPreviousCredentialVersion()
    {
        var user = User("user", "hash:old-pass", credentialVersion: 4);
        var repository = new AuthRepositoryStub(user);
        var service = CreateService(repository, user.Uid);
        var result = await service.ChangeOwnPasswordAsync(new ChangePasswordRequestDto
        {
            CurrentPassword = "old-pass", NewPassword = "new-pass", ConfirmPassword = "new-pass"
        });
        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, repository.OwnPasswordUpdates);
        Assert.IsFalse(await service.IsCredentialVersionValidAsync(user.Uid, 4));
        Assert.IsTrue(await service.IsCredentialVersionValidAsync(user.Uid, 5));
    }

    /// <summary>管理重置同时拒绝非管理员操作者和管理员目标。</summary>
    [TestMethod]
    public async Task AdminResetRejectsUnauthorizedActorAndAdminTarget()
    {
        var actorId = Guid.NewGuid();
        var target = User("target", "hash:old-pass", credentialVersion: 2);
        var repository = new AuthRepositoryStub(target);
        var request = new AdminResetPasswordRequestDto
        {
            UserId = target.Uid, NewPassword = "new-pass", ConfirmPassword = "new-pass"
        };
        var result = await CreateService(repository, actorId).ResetUserPasswordAsync(request);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.AreEqual(0, repository.AdminPasswordUpdates);

        repository.Admins.Add(actorId);
        repository.Admins.Add(target.Uid);
        result = await CreateService(repository, actorId).ResetUserPasswordAsync(request);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
        Assert.AreEqual(0, repository.AdminPasswordUpdates);
    }

    /// <summary>仓储在最终写入时再次撤销操作者权限，服务不得把预检当作授权。</summary>
    [TestMethod]
    public async Task AdminResetHonorsAtomicRepositoryRecheck()
    {
        var actorId = Guid.NewGuid();
        var target = User("target", "hash:old-pass", credentialVersion: 2);
        var repository = new AuthRepositoryStub(target) { ResetStatus = AdminPasswordResetStatus.ActorNotAdmin };
        repository.Admins.Add(actorId);
        var result = await CreateService(repository, actorId).ResetUserPasswordAsync(new AdminResetPasswordRequestDto
        {
            UserId = target.Uid, NewPassword = "new-pass", ConfirmPassword = "new-pass"
        });
        Assert.IsFalse(result.Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, result.Error.Code);
    }

    /// <summary>管理员重置普通用户后，目标用户旧凭据版本立即失效。</summary>
    [TestMethod]
    public async Task AdminResetInvalidatesTargetCredentialVersion()
    {
        var actorId = Guid.NewGuid();
        var target = User("target", "hash:old-pass", credentialVersion: 9);
        var repository = new AuthRepositoryStub(target);
        repository.Admins.Add(actorId);
        var service = CreateService(repository, actorId);
        var result = await service.ResetUserPasswordAsync(new AdminResetPasswordRequestDto
        {
            UserId = target.Uid, NewPassword = "new-pass", ConfirmPassword = "new-pass"
        });
        Assert.IsTrue(result.Success);
        Assert.IsFalse(await service.IsCredentialVersionValidAsync(target.Uid, 9));
        Assert.IsTrue(await service.IsCredentialVersionValidAsync(target.Uid, 10));
    }

    /// <summary>me 直接采用统一服务端投影，不再从认证仓储重复展开权限。</summary>
    [TestMethod]
    public async Task MeUsesUnifiedPermissionProjection()
    {
        var user = User("buyer", "hash:pass", credentialVersion: 1);
        var permissions = new PermissionServiceStub
        {
            Projection = new UserPermissionProjection
            {
                Roles = ["Buyer"],
                Permissions = [PermissionKeys.ModuleProcurementAccess, PermissionKeys.ProcurementOrderRead]
            }
        };
        var service = new SwiftExport.Application.Services.Auth.AuthService(
            new AuthRepositoryStub(user), new CurrentUserStub(user.Uid), new PasswordHasherStub(), permissions);
        var result = await service.GetCurrentUserAsync();

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(permissions.Projection.Roles.ToArray(), result.Data.Roles);
        CollectionAssert.AreEqual(permissions.Projection.Permissions.ToArray(), result.Data.Permissions);
    }

    /// <summary>生产哈希器可验证自身哈希，损坏哈希按认证失败处理而不泄露解析异常。</summary>
    [TestMethod]
    public void BcryptHasherIsCompatibleAndRejectsMalformedHash()
    {
        var hasher = new BcryptPasswordHasher();
        var hash = hasher.Hash(" secret ");
        Assert.IsTrue(hasher.Verify(" secret ", hash));
        Assert.IsFalse(hasher.Verify("secret", hash));
        Assert.IsFalse(hasher.Verify("secret", "not-a-bcrypt-hash"));
    }

    private static SwiftExport.Application.Services.Auth.AuthService CreateService(
        AuthRepositoryStub repository,
        Guid userId) => new(repository, new CurrentUserStub(userId), new PasswordHasherStub(), new PermissionServiceStub());

    private static SysUser User(string name, string hash, int credentialVersion) => new()
    {
        Uid = Guid.NewGuid(), Version = 1, UserName = name, PasswordHash = hash,
        CredentialVersion = credentialVersion, IsActive = true
    };

    private sealed class CurrentUserStub(Guid userId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => userId;
        public string UserName => "测试操作者";
    }

    private sealed class PasswordHasherStub : IPasswordHasher
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string password, string passwordHash) => passwordHash == "hash:" + password;
    }

    private sealed class PermissionServiceStub : IUserPermissionService
    {
        public UserPermissionProjection Projection { get; set; } = new();
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<UserPermissionProjection?>(Projection);
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<List<UserSummaryDto>>> GetUsersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<UserModulePermissionsDto>> GetUserModulesAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<UserModulePermissionsDto>> SaveUserModulesAsync(Guid userId, SaveUserModulePermissionsRequestDto request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class AuthRepositoryStub : IAuthRepository
    {
        private readonly Dictionary<Guid, SysUser> _users = [];
        public AuthRepositoryStub(params SysUser[] users)
        {
            foreach (var user in users) _users[user.Uid] = user;
        }

        public HashSet<Guid> Admins { get; } = [];
        public SysUser? CreatedUser { get; private set; }
        public int OwnPasswordUpdates { get; private set; }
        public int AdminPasswordUpdates { get; private set; }
        public AdminPasswordResetStatus ResetStatus { get; set; } = AdminPasswordResetStatus.Updated;
        public AuthUserCreateStatus CreateStatus { get; set; } = AuthUserCreateStatus.Created;
        public Task<SysUser?> GetActiveUserByNameAsync(string userName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.Values.SingleOrDefault(x => x.UserName == userName));
        public Task<SysUser?> GetActiveUserByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.GetValueOrDefault(userId));
        public Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Admins.Contains(userId));
        public Task<bool> IsUserNameInUseAsync(string userName, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.Values.Any(x => x.UserName == userName));
        public Task<AuthUserCreateStatus> CreateUserByAdminAsync(Guid actorUserId, SysUser user, CancellationToken cancellationToken = default)
        {
            if (!Admins.Contains(actorUserId)) return Task.FromResult(AuthUserCreateStatus.ActorNotAdmin);
            if (_users.Values.Any(x => x.UserName == user.UserName)) return Task.FromResult(AuthUserCreateStatus.UserNameInUse);
            if (CreateStatus != AuthUserCreateStatus.Created) return Task.FromResult(CreateStatus);
            CreatedUser = user; _users[user.Uid] = user;
            return Task.FromResult(AuthUserCreateStatus.Created);
        }
        public Task<bool> UpdateOwnPasswordAsync(SysUser user, CancellationToken cancellationToken = default)
        {
            OwnPasswordUpdates++; user.Version++; user.CredentialVersion++;
            return Task.FromResult(true);
        }
        public Task<AdminPasswordResetStatus> ResetNonAdminPasswordAsync(Guid actorUserId, SysUser targetUser, CancellationToken cancellationToken = default)
        {
            AdminPasswordUpdates++;
            if (ResetStatus == AdminPasswordResetStatus.Updated)
            {
                targetUser.Version++; targetUser.CredentialVersion++;
            }
            return Task.FromResult(ResetStatus);
        }
        public Task<bool> IsCredentialVersionValidAsync(Guid userId, int credentialVersion, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.TryGetValue(userId, out var user) && user.CredentialVersion == credentialVersion && user.IsActive);
    }
}
