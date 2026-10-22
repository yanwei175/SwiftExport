using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>为入库测试显式提供当前账号权限，不依赖缺省依赖绕过运行时授权。</summary>
internal static class InboundTestUsers
{
    /// <summary>创建具有完整入库权限的内存账号；权限边界测试自行撤销对应细权限。</summary>
    internal static CurrentUserService Create()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { UserId = "inbound-test", Permissions = [PermissionKeys.InventoryInboundView,
            PermissionKeys.InventoryInboundCreate, PermissionKeys.InventoryInboundEdit, PermissionKeys.InventoryInboundDelete,
            PermissionKeys.InventoryInboundPost, PermissionKeys.InventoryInboundReverse] });
        return user;
    }
}
