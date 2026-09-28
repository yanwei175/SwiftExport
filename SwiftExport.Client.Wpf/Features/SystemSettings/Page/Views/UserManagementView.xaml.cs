using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Query;

namespace SwiftExportClient.Features.SystemSettings.Page.Views;

/// <summary>呈现用户 DataGrid 和控件筛选，业务命令归用户列表模型。</summary>
public partial class UserManagementView : System.Windows.Controls.Page, IDisposable
{
    private readonly UsersQueryViewModel _model;
    private bool _canManage;
    private bool _disposed;

    /// <summary>绑定页签拥有的用户列表，视图不创建或持有业务表单。</summary>
    public UserManagementView(UsersQueryViewModel model, bool canManage)
    {
        InitializeComponent();
        _model = model; DataContext = model;
        ApplyPermissions(canManage);
        UpdateCount();
    }

    /// <summary>按当前筛选视图更新结果计数。</summary>
    public void RefreshCount() => UpdateCount();

    /// <summary>权限刷新后同步写入口可用性。</summary>
    public void ApplyPermissions(bool canManage)
    {
        _canManage = canManage;
        CreateUserButton.IsEnabled = canManage;
        UserGrid.IsEnabled = true;
    }

    /// <summary>视觉页释放后停止响应迟到命令回调。</summary>
    public void Dispose() => _disposed = true;

    private async void OnCreateUser(object sender, RoutedEventArgs e)
    {
        if (_disposed || !_canManage) return;
        await _model.CreateUserCommand.ExecuteAsync(null);
        if (!_disposed) UpdateCount();
    }

    private async void OnEditUser(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var target)) await _model.EditProfileCommand.ExecuteAsync(target);
    }

    private async void OnAssignRoles(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var target)) await _model.AssignRolesCommand.ExecuteAsync(target);
    }

    private async void OnResetPassword(object sender, RoutedEventArgs e)
    {
        if (CanActOnRow(sender, out var target)) await _model.ResetPasswordCommand.ExecuteAsync(target);
    }

    private async void OnSetUserStatus(object sender, RoutedEventArgs e)
    {
        if (!CanActOnRow(sender, out var target)) return;
        await _model.SetUserStatusCommand.ExecuteAsync(target);
        if (!_disposed) UpdateCount();
    }

    /// <summary>行操作只传本行 DTO，不能误用当前选中项。</summary>
    private bool CanActOnRow(object sender, out ManagedUserDto target)
    {
        target = (sender as FrameworkElement)?.DataContext as ManagedUserDto ?? null!;
        return !_disposed && _canManage && !_model.IsBusy && !_model.IsWriting && target is not null;
    }

    private void OnFilterChanged(object sender, EventArgs e)
    {
        if (_model is null) return;
        var view = CollectionViewSource.GetDefaultView(_model.Users);
        view.Filter = item => item is ManagedUserDto user
            && (string.IsNullOrWhiteSpace(SearchBox.Text)
                || user.UserName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase)
                || (user.NickName?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) ?? false))
            && (StatusFilter.SelectedIndex == 0 || user.IsActive == (StatusFilter.SelectedIndex == 1));
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (_model is null) return;
        CountText.Text = $"{CollectionViewSource.GetDefaultView(_model.Users).Cast<object>().Count()} 条";
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await _model.RefreshCommand.ExecuteAsync(null);
        if (!_disposed) UpdateCount();
    }
}
