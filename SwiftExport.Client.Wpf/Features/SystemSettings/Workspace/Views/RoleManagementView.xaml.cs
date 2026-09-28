using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>角色 DataGrid 占据页面主体；行操作由本地独立表单承载。</summary>
public partial class RoleManagementView : Page, IDisposable
{
    private readonly RbacManagementViewModel _model;
    private readonly Func<Task> _openCreate;
    private readonly Func<ManagedRoleDto, Task> _openProfile;
    private readonly Func<ManagedRoleDto, Task> _openPermissions;
    private bool _canManage;

    /// <summary>复用 RBAC 聚合版本和四范围修订号，不另建授权保存链。</summary>
    public RoleManagementView(RbacManagementViewModel model, Func<Task> openCreate,
        Func<ManagedRoleDto, Task> openProfile, Func<ManagedRoleDto, Task> openPermissions,
        bool canManage)
    {
        InitializeComponent();
        _model = model;
        _openCreate = openCreate;
        _openProfile = openProfile;
        _openPermissions = openPermissions;
        _canManage = canManage;
        DataContext = model;
        CreateRoleButton.IsEnabled = canManage;
    }

    /// <summary>页面无私有订阅，释放由宿主统一执行。</summary>
    public void Dispose() { }

    /// <summary>同用户权限刷新后重新计算角色写入区的准入。</summary>
    public void ApplyPermissions(bool canManage)
    {
        _canManage = canManage;
        CreateRoleButton.IsEnabled = canManage;
    }

    private async void OnCreateRole(object sender, RoutedEventArgs e)
    {
        if (_canManage && !_model.IsBusy && !_model.IsWriting) await _openCreate();
    }

    private async void OnEditRole(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var role)) await _openProfile(role);
    }

    private async void OnAssignPermissions(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var role)) await _openPermissions(role);
    }

    private async void OnSetRoleStatus(object sender, RoutedEventArgs e)
    {
        if (!CanActOnRow(sender, out var role)) return;
        await _model.SelectRoleCommand.ExecuteAsync(role);
        if (_model.SelectedRole?.Uid == role.Uid && _model.SetRoleStatusCommand.CanExecute(null))
        {
            var revision = _model.SuccessfulWriteRevision;
            await _model.SetRoleStatusCommand.ExecuteAsync(null);
            // 读回列表让非通知型 DTO 的状态立即呈现。
            if (revision != _model.SuccessfulWriteRevision)
                await _model.RefreshRolesPageAsync();
        }
    }

    private async void OnDeleteRole(object sender, RoutedEventArgs e)
    {
        if (!CanActOnRow(sender, out var role)) return;
        await _model.SelectRoleCommand.ExecuteAsync(role);
        if (_model.SelectedRole?.Uid == role.Uid && _model.DeleteRoleCommand.CanExecute(null))
            await _model.DeleteRoleCommand.ExecuteAsync(null);
    }

    /// <summary>内置角色保留只读显示，行按钮不能提交启停、删除或授权。</summary>
    private bool CanActOnRow(object sender, out ManagedRoleDto role)
    {
        role = (sender as FrameworkElement)?.DataContext as ManagedRoleDto ?? null!;
        return _canManage && !_model.IsBusy && !_model.IsWriting && role is { IsSystem: false };
    }

    private void OnRoleFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_model is null) return;
        CollectionViewSource.GetDefaultView(_model.Roles).Filter = item => item is ManagedRoleDto role
            && (string.IsNullOrWhiteSpace(SearchBox.Text)
                || role.RoleCode.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)
                || role.RoleName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase));
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await _model.RefreshRolesPageAsync();
}
