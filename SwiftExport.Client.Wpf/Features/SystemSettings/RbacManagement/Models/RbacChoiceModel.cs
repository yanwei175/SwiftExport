using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Models;

/// <summary>保存用户角色或角色权限页面的一项可勾选目录事实。</summary>
public sealed partial class RbacChoiceModel(Guid id, string displayName, bool isEnabled = true) : ObservableObject
{
    /// <summary>服务端角色或权限定义标识。</summary>
    public Guid Id { get; } = id;

    /// <summary>可读的角色名称或权限分组与名称。</summary>
    public string DisplayName { get; } = displayName;

    /// <summary>是否允许勾选，保留管理权限不可由普通角色分配。</summary>
    public bool IsEnabled { get; } = isEnabled;

    /// <summary>当前编辑集合是否包含本项。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
