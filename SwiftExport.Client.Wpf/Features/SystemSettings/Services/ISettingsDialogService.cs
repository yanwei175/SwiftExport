using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Features.SystemSettings.Services;

/// <summary>设置页签专用表单用例入口，每个页签持有独立实例与活动状态。</summary>
public interface ISettingsDialogService : IDisposable
{
    /// <summary>当前有表单正在初始化或展示。</summary>
    bool HasActiveForm { get; }
    /// <summary>活动状态改变时通知页签重算关闭和个人操作准入。</summary>
    event EventHandler? ActiveFormChanged;
    /// <summary>本人修改密码；成功由表单结束旧凭据会话。</summary>
    Task ShowChangePasswordAsync();
    /// <summary>创建普通用户并返回供列表定位的用户名。</summary>
    Task<SettingsDialogResult> ShowCreateUserAsync();
    /// <summary>重置指定普通用户密码。</summary>
    Task ShowResetPasswordAsync(ManagedUserDto target);
    /// <summary>编辑指定用户的非凭据资料。</summary>
    Task<SettingsDialogResult> ShowUserProfileAsync(ManagedUserDto target);
    /// <summary>按完整集合分配指定用户的角色。</summary>
    Task<SettingsDialogResult> ShowUserRolesAsync(ManagedUserDto target);
    /// <summary>创建普通角色。</summary>
    Task<SettingsDialogResult> ShowCreateRoleAsync();
    /// <summary>编辑普通角色资料。</summary>
    Task<SettingsDialogResult> ShowRoleProfileAsync(ManagedRoleDto target);
    /// <summary>按完整集合分配普通角色权限。</summary>
    Task<SettingsDialogResult> ShowRolePermissionsAsync(ManagedRoleDto target);
    /// <summary>会话或目标资格失效时立即关闭并清理当前表单。</summary>
    void ForceClose();
}
