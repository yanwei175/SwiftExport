using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Files;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Tests.Files;

/// <summary>验证文件权限只信任活动账号实时投影，普通用户可读而仅真实 Admin 可写。</summary>
[TestClass]
public sealed class FileAccessPolicyTests
{
    /// <summary>未认证或活动投影失效时读写均返回未认证，不按 token 角色继续放行。</summary>
    [TestMethod]
    public async Task MissingIdentityOrProjection_ShouldRejectReadAndWriteAsUnauthenticated()
    {
        var unauthenticated = new FileAccessPolicy(
            new CurrentUserStub(false, null),
            new PermissionStub(new UserPermissionProjection { IsAdmin = true }));
        Assert.AreEqual(
            ServiceErrorCodes.Unauthenticated,
            (await unauthenticated.AuthorizeReadAsync()).Error.Code);

        var inactive = new FileAccessPolicy(
            new CurrentUserStub(true, Guid.NewGuid()),
            new PermissionStub(null));
        Assert.AreEqual(
            ServiceErrorCodes.Unauthenticated,
            (await inactive.AuthorizeReadAsync()).Error.Code);
        Assert.AreEqual(
            ServiceErrorCodes.Unauthenticated,
            (await inactive.AuthorizeWriteAsync()).Error.Code);
    }

    /// <summary>活动普通用户允许读取但禁止写入，Admin 的实时投影允许两种操作。</summary>
    [TestMethod]
    public async Task ActiveProjection_ShouldAllowReadAndRequireAdminForWrite()
    {
        var userId = Guid.NewGuid();
        var normal = new FileAccessPolicy(
            new CurrentUserStub(true, userId),
            new PermissionStub(new UserPermissionProjection { IsAdmin = false }));
        Assert.IsTrue((await normal.AuthorizeReadAsync()).Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, (await normal.AuthorizeWriteAsync()).Error.Code);

        var admin = new FileAccessPolicy(
            new CurrentUserStub(true, userId),
            new PermissionStub(new UserPermissionProjection { IsAdmin = true }));
        Assert.IsTrue((await admin.AuthorizeReadAsync()).Success);
        Assert.IsTrue((await admin.AuthorizeWriteAsync()).Success);
    }

    private sealed class CurrentUserStub(bool isAuthenticated, Guid? userId) : ICurrentUser
    {
        public string UserName => "test";
        public Guid? UserId => userId;
        public bool IsAuthenticated => isAuthenticated;
    }

    private sealed class PermissionStub(UserPermissionProjection? projection) : IPermissionEvaluationService
    {
        public Task<UserPermissionProjection?> GetProjectionAsync(
            Guid userId,
            CancellationToken cancellationToken = default) => Task.FromResult(projection);

        public Task<bool> HasPermissionAsync(
            Guid userId,
            string permissionKey,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
