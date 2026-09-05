using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;

namespace SwiftExport.Client.Wpf.Tests.Authentication;

/// <summary>验证客户端只消费稳定 Permissions，不把角色名作为权限兜底。</summary>
[TestClass]
public sealed class CurrentUserPermissionTests
{
    /// <summary>权限查询精确匹配 Permissions，Admin 角色本身不能隐式授权。</summary>
    [TestMethod]
    public void HasPermission_uses_permissions_without_role_fallback()
    {
        var service = new CurrentUserService();
        service.SetCurrentUser(new MeDto
        {
            Roles = new List<string> { "Admin", PermissionKeys.ProcurementOrderEdit },
            Permissions = new List<string> { PermissionKeys.ProcurementOrderRead }
        });

        Assert.IsTrue(service.HasPermission(PermissionKeys.ProcurementOrderRead));
        Assert.IsFalse(service.HasPermission(PermissionKeys.ProcurementOrderEdit));
        Assert.IsFalse(service.HasPermission(PermissionKeys.ProcurementOrderCreate));
        Assert.IsFalse(service.HasPermission(PermissionKeys.ProcurementOrderRead.ToLowerInvariant()));
    }

    /// <summary>清除会话后权限集合为空，所有权限查询均拒绝。</summary>
    [TestMethod]
    public void Clear_removes_permission_snapshot()
    {
        var service = new CurrentUserService();
        service.SetCurrentUser(new MeDto
        {
            Permissions = new List<string> { PermissionKeys.ProcurementOrderConfirm }
        });

        service.Clear();

        Assert.IsFalse(service.IsAuthenticated);
        Assert.AreEqual(0, service.Permissions.Count);
        Assert.IsFalse(service.HasPermission(PermissionKeys.ProcurementOrderConfirm));
    }

    /// <summary>登录恢复和用户替换均发布快照变更，供 Shell 与导航清除上一用户状态。</summary>
    [TestMethod]
    public void User_snapshot_changes_raise_notifications()
    {
        var service = new CurrentUserService();
        var changeCount = 0;
        service.CurrentUserChanged += (_, _) => changeCount++;

        service.SetCurrentUser(new MeDto
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = "first",
            Permissions = new List<string> { PermissionKeys.ModuleProductsAccess }
        });
        service.SetCurrentUser(new MeDto
        {
            UserId = Guid.NewGuid().ToString(),
            UserName = "second",
            Permissions = new List<string> { PermissionKeys.ModuleSalesAccess }
        });
        service.Clear();
        service.Clear();

        Assert.AreEqual(3, changeCount);
        Assert.IsFalse(service.IsAuthenticated);
    }
}
