using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.UserPermissions.Models;

/// <summary>表示设置窗口中的一个固定一级模块选项及其勾选状态。</summary>
public sealed partial class ModulePermissionOptionModel : ObservableObject
{
    /// <summary>创建一个稳定模块权限选项。</summary>
    public ModulePermissionOptionModel(string permissionKey, string displayName)
    {
        PermissionKey = permissionKey;
        DisplayName = displayName;
    }

    /// <summary>服务端识别的稳定模块权限键。</summary>
    public string PermissionKey { get; }

    /// <summary>面向用户的模块名称。</summary>
    public string DisplayName { get; }

    /// <summary>获取或设置当前模块是否被授予。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
