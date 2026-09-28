using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Query;

namespace SwiftExportClient.Features.SystemSettings.Page.Views;

/// <summary>呈现角色 DataGrid 和本地筛选，业务动作归角色列表模型。</summary>
public partial class RoleManagementView : System.Windows.Controls.Page, IDisposable
{
    private readonly RolesQueryViewModel _model;
    private bool _canManage;
    private bool _disposed;

    /// <summary>绑定页签拥有的角色列表，不在 code-behind 编排表单。</summary>
    public RoleManagementView(RolesQueryViewModel model, bool canManage)
    {
        InitializeComponent();
        _model = model; DataContext = model;
        ApplyPermissions(canManage);
    }

    /// <summary>视觉页释放后停止响应事件。</summary>
    public void Dispose() => _disposed = true;

    /// <summary>权限刷新后同步角色写入入口。</summary>
    public void ApplyPermissions(bool canManage)
    {
        _canManage = canManage;
        CreateRoleButton.IsEnabled = canManage;
    }

    private async void OnCreateRole(object sender, RoutedEventArgs e)
    {
        if (!_disposed && _canManage) await _model.CreateRoleCommand.ExecuteAsync(null);
    }

    private async void OnEditRole(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var role)) await _model.EditProfileCommand.ExecuteAsync(role);
    }

    private async void OnAssignPermissions(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var role)) await _model.AssignPermissionsCommand.ExecuteAsync(role);
    }

    private async void OnSetRoleStatus(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var role)) await _model.SetRoleStatusCommand.ExecuteAsync(role);
    }

    private async void OnDeleteRole(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var role)) await _model.DeleteRoleCommand.ExecuteAsync(role);
    }

    /// <summary>行按钮只传当前普通角色；系统角色保持只读。</summary>
    private bool CanActOnRow(object sender, out ManagedRoleDto role)
    {
        role = (sender as FrameworkElement)?.DataContext as ManagedRoleDto ?? null!;
        return !_disposed && _canManage && !_model.IsBusy && !_model.IsWriting && role is { IsSystem: false };
    }

    private void OnRoleFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_model is null) return;
        CollectionViewSource.GetDefaultView(_model.Roles).Filter = item => item is ManagedRoleDto role
            && (string.IsNullOrWhiteSpace(SearchBox.Text)
                || role.RoleCode.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)
                || role.RoleName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
        => await _model.RefreshCommand.ExecuteAsync(null);
}
