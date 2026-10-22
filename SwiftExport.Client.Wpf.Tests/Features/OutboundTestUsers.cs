using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>为出库测试显式提供账号和细权限，不以缺失认证依赖绕过运行时授权。</summary>
internal static class OutboundTestUsers
{
    /// <summary>创建仅用于内存测试的完整出库权限账号，权限场景自行裁剪。</summary>
    internal static CurrentUserService Create()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { UserId = "outbound-test", Permissions = [PermissionKeys.InventoryOutboundView,
            PermissionKeys.InventoryOutboundCreate, PermissionKeys.InventoryOutboundEdit, PermissionKeys.InventoryOutboundDelete,
            PermissionKeys.InventoryOutboundPost, PermissionKeys.InventoryOutboundReverse] });
        return user;
    }
}
