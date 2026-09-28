using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>用户 DataGrid 占据页面主体，行操作交由系统设置的本地 ContentDialog 承载。</summary>
public partial class UserManagementView : Page, IDisposable
{
    private readonly RbacManagementViewModel _rbac;
    private readonly Func<Task<string?>> _openRegistration;
    private readonly Func<ManagedUserDto, Task> _openProfile;
    private readonly Func<ManagedUserDto, Task> _openRoles;
    private readonly Func<ManagedUserDto, Task> _openReset;
    private bool _canManage;
    private bool _disposed;

    /// <summary>共享 RBAC 聚合状态，子窗通过窄回调返回创建用户名称或固定重置目标。</summary>
    public UserManagementView(RbacManagementViewModel rbac, Func<Task<string?>> openRegistration,
        Func<ManagedUserDto, Task> openProfile, Func<ManagedUserDto, Task> openRoles,
        Func<ManagedUserDto, Task> openReset, bool canManage)
    {
        InitializeComponent();
        _rbac = rbac;
        _openRegistration = openRegistration;
        _openProfile = openProfile;
        _openRoles = openRoles;
        _openReset = openReset;
        DataContext = rbac;
        ApplyPermissions(canManage);
        UpdateCount();
    }

    /// <summary>按完整已载入列表的当前筛选结果刷新计数。</summary>
    public void RefreshCount() => UpdateCount();

    /// <summary>同用户权限刷新后收回资料写入及模态子窗入口。</summary>
    public void ApplyPermissions(bool canManage)
    {
        _canManage = canManage;
        CreateUserButton.IsEnabled = canManage;
        UserGrid.IsEnabled = true;
    }

    /// <summary>导航页释放时不保留页面私有事件或一次性表单。</summary>
    public void Dispose() => _disposed = true;

    private async void OnCreateUser(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_canManage || _rbac.IsWriting || _rbac.IsBusy) return;
        var userName = await _openRegistration();
        if (_disposed || string.IsNullOrWhiteSpace(userName)) return;
        await _rbac.RefreshUsersPageAsync(userName);
        if (!_disposed) UpdateCount();
    }

    private async void OnEditUser(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var target)) await _openProfile(target);
    }

    private async void OnAssignRoles(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var target)) await _openRoles(target);
    }

    private async void OnResetPassword(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var target)) await _openReset(target);
    }

    private async void OnSetUserStatus(object sender, RoutedEventArgs e)
    {
        if (!CanActOnRow(sender, out var target)) return;
        await _rbac.SelectUserCommand.ExecuteAsync(target);
        if (_rbac.SelectedUser?.Uid == target.Uid && _rbac.SetUserStatusCommand.CanExecute(null))
        {
            var revision = _rbac.SuccessfulWriteRevision;
            await _rbac.SetUserStatusCommand.ExecuteAsync(null);
            // 行 DTO 没有属性通知；读回后使状态筛选和显示同步。
            if (!_disposed && revision != _rbac.SuccessfulWriteRevision)
            {
                await _rbac.RefreshUsersPageAsync(target.UserName);
                UpdateCount();
            }
        }
    }

    /// <summary>行按钮只作用于自己的 DataContext，避免复用当前选中行导致错改。</summary>
    private bool CanActOnRow(object sender, out ManagedUserDto target)
    {
        target = (sender as FrameworkElement)?.DataContext as ManagedUserDto ?? null!;
        return !_disposed && _canManage && !_rbac.IsBusy && !_rbac.IsWriting && target is not null;
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        if (_rbac is null) return;
        var view = CollectionViewSource.GetDefaultView(_rbac.Users);
        view.Filter = item => item is ManagedUserDto user
            && (string.IsNullOrWhiteSpace(SearchBox.Text)
                || user.UserName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)
                || (user.NickName?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) ?? false))
            && (StatusFilter.SelectedIndex == 0 || user.IsActive == (StatusFilter.SelectedIndex == 1));
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (_rbac is null) return;
        CountText.Text = $"{CollectionViewSource.GetDefaultView(_rbac.Users).Cast<object>().Count()} 条";
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await _rbac.RefreshUsersPageAsync();
        if (!_disposed) UpdateCount();
    }
}
