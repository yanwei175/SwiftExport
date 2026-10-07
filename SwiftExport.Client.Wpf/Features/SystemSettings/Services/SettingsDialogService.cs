using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;
using SwiftExportClient.Features.SystemSettings.Forms.Views;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.Views;
using SwiftExportClient.Features.SystemSettings.Passwords.Reset.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Reset.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Profile.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Profile.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.Views;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Views;
using SwiftExportClient.Services.UI;
using System.Windows;

namespace SwiftExportClient.Features.SystemSettings.Services;

/// <summary>按页签实例创建设置表单、调用 Shell 公共 Dialog，并在任何结束路径清理一次性状态。</summary>
public sealed class SettingsDialogService : ISettingsDialogService
{
    private readonly IDialogService _dialogs;
    private readonly ICurrentUserService _currentUser;
    private readonly string? _sessionUserId;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Func<ChangePasswordViewModel> _changeFactory;
    private readonly Func<UserRegistrationViewModel> _registrationFactory;
    private readonly Func<ResetPasswordViewModel> _resetFactory;
    private readonly Func<UserProfileFormViewModel> _userProfileFactory;
    private readonly Func<UserRolesFormViewModel> _userRolesFactory;
    private readonly Func<RoleProfileFormViewModel> _roleProfileFactory;
    private readonly Func<RolePermissionsFormViewModel> _rolePermissionsFactory;
    private ActiveForm? _active;
    private bool _disposed;

    /// <inheritdoc />
    public bool HasActiveForm => _active is not null;
    /// <inheritdoc />
    public event EventHandler? ActiveFormChanged;

    /// <summary>绑定 Shell 公共弹窗和当前页签会话；活动状态不在全局服务间共享。</summary>
    public SettingsDialogService(IDialogService dialogs, ICurrentUserService currentUser,
        Func<ChangePasswordViewModel> changeFactory, Func<UserRegistrationViewModel> registrationFactory,
        Func<ResetPasswordViewModel> resetFactory, Func<UserProfileFormViewModel> userProfileFactory,
        Func<UserRolesFormViewModel> userRolesFactory, Func<RoleProfileFormViewModel> roleProfileFactory,
        Func<RolePermissionsFormViewModel> rolePermissionsFactory)
    {
        _dialogs = dialogs; _currentUser = currentUser;
        _changeFactory = changeFactory; _registrationFactory = registrationFactory; _resetFactory = resetFactory;
        _userProfileFactory = userProfileFactory; _userRolesFactory = userRolesFactory;
        _roleProfileFactory = roleProfileFactory; _rolePermissionsFactory = rolePermissionsFactory;
        _sessionUserId = currentUser.CurrentUser?.UserId;
        currentUser.CurrentUserChanged += OnCurrentUserChanged;
    }

    /// <inheritdoc />
    public async Task ShowChangePasswordAsync()
    {
        if (!CanOpen()) return;
        var model = _changeFactory();
        var active = Begin(model.CloseState, null, false, model.Dispose);
        if (active is null) { model.Dispose(); return; }
        try
        {
            if (!CanContinue(active)) return;
            var view = new ChangePasswordView(model);
            active.Cleanup = Once(() => view.Dispose());
            await ShowFormAsync(active, view, "修改我的密码", "修改密码", model.SaveCommand);
        }
        finally { End(active); }
    }

    /// <inheritdoc />
    public async Task<SettingsDialogResult> ShowCreateUserAsync()
    {
        if (!CanOpen("System.Users.View", true)) return default;
        var model = _registrationFactory();
        var active = Begin(model.CloseState, "System.Users.View", true, model.Dispose);
        if (active is null) { model.Dispose(); return default; }
        try
        {
            if (!CanContinue(active)) return default;
            var view = new UserRegistrationView(model);
            active.Cleanup = Once(view.Dispose);
            var accepted = await ShowFormAsync(active, view, "创建用户", "创建用户", model.SaveCommand);
            return accepted && model.CreatedUserId is { } id
                ? new(true, id, model.CreatedUserName) : default;
        }
        finally { End(active); }
    }

    /// <inheritdoc />
    public async Task ShowResetPasswordAsync(ManagedUserDto target)
    {
        if (target is null || !CanOpen("System.Users.View", true)) return;
        var model = _resetFactory();
        var active = Begin(model.CloseState, "System.Users.View", true, model.Dispose);
        if (active is null) { model.Dispose(); return; }
        try
        {
            model.SelectedUser = new UserSummaryDto
            {
                Uid = target.Uid,
                UserName = target.UserName,
                NickName = target.NickName,
                IsAdmin = target.IsAdmin
            };
            if (!CanContinue(active)) return;
            var view = new ResetPasswordView(model);
            active.Cleanup = Once(() => { view.Dispose(); model.SelectedUser = null; model.Dispose(); });
            await ShowFormAsync(active, view, "重置密码", "重置密码", model.SaveCommand);
        }
        finally { End(active); }
    }

    /// <inheritdoc />
    public async Task<SettingsDialogResult> ShowUserProfileAsync(ManagedUserDto target)
    {
        if (target is null || !CanOpen("System.Users.View", true)) return default;
        var model = _userProfileFactory();
        var active = Begin(model.CloseState, "System.Users.View", true, model.Dispose);
        if (active is null) { model.Dispose(); return default; }
        try
        {
            model.Initialize(target);
            if (!CanContinue(active)) return default;
            var view = new UserProfileFormView(model);
            var accepted = await ShowFormAsync(active, view, "编辑用户资料", "保存资料", model.SaveCommand);
            return accepted && model.Saved ? new(true, model.TargetId) : default;
        }
        finally { End(active); }
    }

    /// <inheritdoc />
    public async Task<SettingsDialogResult> ShowUserRolesAsync(ManagedUserDto target)
    {
        if (target is null || !CanOpen("System.Users.View", true)
            || !_currentUser.HasPermission("System.Roles.View")) return default;
        var model = _userRolesFactory();
        var active = Begin(model.CloseState, "System.Users.View", true, model.Dispose, "System.Roles.View");
        if (active is null) { model.Dispose(); return default; }
        try
        {
            if (!await model.InitializeAsync(target))
            {
                if (CanContinue(active) && !string.IsNullOrWhiteSpace(model.StatusMessage))
                    await _dialogs.ShowErrorAsync("读取用户角色失败", model.StatusMessage);
                return default;
            }
            if (!CanContinue(active)) return default;
            var view = new UserRolesFormView(model);
            var accepted = await ShowFormAsync(active, view, "分配用户角色", "保存角色", model.SaveCommand);
            return accepted && model.Saved ? new(true, model.TargetId) : default;
        }
        finally { End(active); }
    }

    /// <inheritdoc />
    public async Task<SettingsDialogResult> ShowCreateRoleAsync()
    {
        if (!CanOpen("System.Roles.View", true)) return default;
        var model = _roleProfileFactory();
        var active = Begin(model.CloseState, "System.Roles.View", true, model.Dispose);
        if (active is null) { model.Dispose(); return default; }
        try
        {
            model.InitializeCreate();
            if (!CanContinue(active)) return default;
            var view = new RoleProfileFormView(model);
            var accepted = await ShowFormAsync(active, view, "新增角色", "创建角色", model.SaveCommand);
            return accepted && model.Saved ? new(true, model.TargetId) : default;
        }
        finally { End(active); }
    }

    /// <inheritdoc />
    public async Task<SettingsDialogResult> ShowRoleProfileAsync(ManagedRoleDto target)
    {
        if (target is null || target.IsSystem || !CanOpen("System.Roles.View", true)) return default;
        var model = _roleProfileFactory();
        var active = Begin(model.CloseState, "System.Roles.View", true, model.Dispose);
        if (active is null) { model.Dispose(); return default; }
        try
        {
            model.InitializeEdit(target);
            if (!CanContinue(active)) return default;
            var view = new RoleProfileFormView(model);
            var accepted = await ShowFormAsync(active, view, "编辑角色资料", "保存资料", model.SaveCommand);
            return accepted && model.Saved ? new(true, model.TargetId) : default;
        }
        finally { End(active); }
    }

    /// <inheritdoc />
    public async Task<SettingsDialogResult> ShowRolePermissionsAsync(ManagedRoleDto target)
    {
        if (target is null || target.IsSystem || !CanOpen("System.Roles.View", true)
            || !_currentUser.HasPermission("System.Permissions.View")) return default;
        var model = _rolePermissionsFactory();
        var active = Begin(model.CloseState, "System.Roles.View", true, model.Dispose, "System.Permissions.View");
        if (active is null) { model.Dispose(); return default; }
        try
        {
            if (!await model.InitializeAsync(target))
            {
                if (CanContinue(active) && !string.IsNullOrWhiteSpace(model.StatusMessage))
                    await _dialogs.ShowErrorAsync("读取角色权限失败", model.StatusMessage);
                return default;
            }
            if (!CanContinue(active)) return default;
            var view = new RolePermissionsFormView(model);
            var accepted = await ShowFormAsync(active, view, "分配角色权限", "保存权限", model.SaveCommand);
            return accepted && model.Saved ? new(true, model.TargetId) : default;
        }
        finally { End(active); }
    }

    /// <summary>仅将现有公共 Dialog 的按钮、关闭状态和 XAML 正文包装组合一次。</summary>
    private async Task<bool> ShowFormAsync(ActiveForm active, FrameworkElement form, string title,
        string primaryText, IAsyncRelayCommand command)
    {
        if (!CanContinue(active)) return false;
        using var content = new SettingsFormContent(form, active.State);

        var options = new DialogOptions(title)
        {
            PrimaryButtonText = primaryText,
            PrimaryButtonCommand = command,
            CloseButtonText = "关闭",
            DefaultButton = DialogButton.Primary
        };

        var result = await _dialogs.ShowAsync(content, options, active.State);
        return result == DialogResult.Primary && CanContinue(active);
    }

    private bool CanOpen(string? permission = null, bool requireAdmin = false) => !_disposed && _active is null
        && _currentUser.IsAuthenticated && _currentUser.CurrentUser?.UserId == _sessionUserId
        && (!requireAdmin || _currentUser.IsAdmin)
        && (permission is null || _currentUser.HasPermission(permission));

    /// <summary>初始化开始即登记活动状态，权限事件可在首个 API 响应前强制失效。</summary>
    private ActiveForm? Begin(SettingsFormState state, string? permission, bool requireAdmin, Action cleanup,
        string? secondaryPermission = null)
    {
        if (!CanOpen(permission, requireAdmin)
            || (secondaryPermission is not null && !_currentUser.HasPermission(secondaryPermission))) return null;
        _active = new ActiveForm(state, permission, secondaryPermission, requireAdmin, Once(cleanup));
        ActiveFormChanged?.Invoke(this, EventArgs.Empty);
        return _active;
    }

    private bool CanContinue(ActiveForm active) => ReferenceEquals(_active, active) && !active.State.WasForced
        && CanOpenForActive(active);

    private bool CanOpenForActive(ActiveForm active) => !_disposed && _currentUser.IsAuthenticated
        && _currentUser.CurrentUser?.UserId == _sessionUserId
        && (!active.RequireAdmin || _currentUser.IsAdmin)
        && (active.Permission is null || _currentUser.HasPermission(active.Permission))
        && (active.SecondaryPermission is null || _currentUser.HasPermission(active.SecondaryPermission));

    private void End(ActiveForm active)
    {
        active.Cleanup();
        if (!ReferenceEquals(_active, active)) return;
        _active = null;
        ActiveFormChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Action Once(Action cleanup)
    {
        var called = 0;
        return () => { if (Interlocked.Exchange(ref called, 1) == 0) cleanup(); };
    }

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_context is not null && Environment.CurrentManagedThreadId != _ownerThreadId)
        { _context.Post(_ => OnCurrentUserChanged(sender, e), null); return; }
        if (_active is { } active && !CanOpenForActive(active)) ForceClose();
    }

    /// <inheritdoc />
    public void ForceClose()
    {
        if (_active is not { } active) return;
        active.State.ForceClose();
        active.Cleanup();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        ForceClose();
    }

    private sealed class ActiveForm(SettingsFormState state, string? permission, string? secondaryPermission,
        bool requireAdmin, Action cleanup)
    {
        public SettingsFormState State { get; } = state;
        public string? Permission { get; } = permission;
        public string? SecondaryPermission { get; } = secondaryPermission;
        public bool RequireAdmin { get; } = requireAdmin;
        public Action Cleanup { get; set; } = cleanup;
    }
}
