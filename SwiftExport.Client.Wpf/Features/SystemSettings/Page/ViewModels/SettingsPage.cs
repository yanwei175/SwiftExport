namespace SwiftExportClient.Features.SystemSettings.Page.ViewModels;

/// <summary>系统设置内部独立页面的稳定标识。</summary>
public enum SettingsPage
{
    /// <summary>所有登录用户的个人概览。</summary>
    Personal,
    /// <summary>用户管理。</summary>
    Users,
    /// <summary>角色管理。</summary>
    Roles,
    /// <summary>只读权限目录。</summary>
    Permissions,
    /// <summary>字典管理。</summary>
    Dictionary
}
