using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Progress;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证采购跟进读取使用独立 View 键，不要求采购订单 Read。</summary>
[TestClass]
public sealed class PurchaseOrderProgressViewPermissionTests
{
    /// <summary>仅有跟进 View 可进入业务参数校验，仅有订单 Read 在授权处拒绝。</summary>
    [TestMethod]
    public async Task Detail_UsesProgressViewBeforeQuery()
    {
        var session = (DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
        var permission = new PermissionStub();
        var reads = new ProcurementOrderReadSupport(new ServiceBaseContext(session, NullLoggerFactory.Instance),
            permission, new UserStub());
        var service = new PurchaseOrderProgressQueryService(reads, null!);

        permission.Allowed = [PermissionKeys.ProcurementOrderRead];
        var denied = await service.GetDetailAsync(Guid.Empty);
        Assert.AreEqual(ServiceErrorCodes.Forbidden, denied.Error.Code);

        permission.Allowed = [PermissionKeys.ProcurementProgressView];
        var reachedValidation = await service.GetDetailAsync(Guid.Empty);
        Assert.AreEqual(ServiceErrorCodes.Validation, reachedValidation.Error.Code);
        CollectionAssert.AreEqual(new[] { PermissionKeys.ProcurementProgressView,
            PermissionKeys.ProcurementProgressView }, permission.Keys);
    }

    private sealed class UserStub : ICurrentUser
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string UserName => "跟进查看测试";
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
