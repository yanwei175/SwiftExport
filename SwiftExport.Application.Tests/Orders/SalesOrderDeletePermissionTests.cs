using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.SalesOrders.Draft;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;

namespace SwiftExport.Application.Tests.Orders;

/// <summary>验证销售草稿删除的 Application 授权独立于保存维护能力。</summary>
[TestClass]
public sealed class SalesOrderDeletePermissionTests
{
    /// <summary>仅有 Delete 键允许删除入口，只有 Maintain 键仍拒绝，且实际只请求 Delete。</summary>
    [TestMethod]
    public async Task DeleteAuthorization_RequiresIndependentDeleteKey()
    {
        var evaluator = new PermissionStub();
        var service = new SalesOrderDraftService(null!, null!, null!, null!, null!, null!, null!, null!,
            null!, new UserStub(), evaluator, null!, null!);
        var method = typeof(SalesOrderDraftService).GetMethod("AuthorizeDeleteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        evaluator.Allowed = [PermissionKeys.SalesOrderMaintain];
        var denied = await (Task<ServiceResult<bool>?>)method.Invoke(service, [CancellationToken.None])!;
        Assert.AreEqual(ServiceErrorCodes.Forbidden, denied?.Error?.Code);

        evaluator.Allowed = [PermissionKeys.SalesOrderDelete];
        var allowed = await (Task<ServiceResult<bool>?>)method.Invoke(service, [CancellationToken.None])!;
        Assert.IsNull(allowed);
        CollectionAssert.AreEqual(new[] { PermissionKeys.SalesOrderDelete, PermissionKeys.SalesOrderDelete }, evaluator.Keys);
    }

    private sealed class UserStub : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "销售删除测试";
        public bool IsAuthenticated => true;
    }

    private sealed class PermissionStub : IPermissionEvaluationService
    {
        public IReadOnlyCollection<string> Allowed { get; set; } = [];
        public List<string> Keys { get; } = [];
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(Guid userId, string permissionKey, CancellationToken cancellationToken = default)
        {
            Keys.Add(permissionKey);
            return Task.FromResult(Allowed.Contains(permissionKey));
        }
    }
}
