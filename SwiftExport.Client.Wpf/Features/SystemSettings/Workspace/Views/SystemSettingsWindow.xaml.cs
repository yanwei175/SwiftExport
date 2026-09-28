using System.ComponentModel;
using System.Windows;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Views;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Services.UI;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>单实例系统设置宿主：按权限延迟创建四页，统一管理本地对话框、会话和未保存关闭。</summary>
public partial class SystemSettingsWindow : FluentWindow, IManagedSettingsWindow
{
    private readonly Func<RbacManagementViewModel> _rbacFactory;
    private readonly Func<DictionaryManagerViewModel> _dictionaryFactory;
    private readonly Func<UserRegistrationViewModel> _registrationFactory;
    private readonly Func<ResetPasswordViewModel> _resetFactory;
    private readonly ICurrentUserService _currentUser;
    private readonly IContentDialogService _contentDialogs;
    private readonly WindowMessageService _messages;
    private readonly string? _sessionUserId;
    private RbacManagementViewModel? _rbac;
    private DictionaryManagerViewModel? _dictionary;
    private UserManagementView? _users;
    private RoleManagementView? _roles;
    private PermissionCatalogView? _permissions;
    private DictionaryManagementPage? _dictionaryPage;
    private DictionaryManagerView? _dictionaryView;
    private SettingsFormDialogSession? _activeForm;
    private SettingsPage? _currentPage;
    private bool _navigating;
    private bool _pendingPermissionNavigation;
    private bool _checkingClose;
    private bool _allowClose;
    private bool _disposed;

    /// <summary>仅保存页面工厂；访问字典页时不构造或请求用户、角色管理接口。</summary>
    public SystemSettingsWindow(Func<RbacManagementViewModel> rbacFactory,
        Func<DictionaryManagerViewModel> dictionaryFactory,
        Func<UserRegistrationViewModel> registrationFactory,
        Func<ResetPasswordViewModel> resetFactory,
        ICurrentUserService currentUser, IContentDialogService contentDialogs,
        WindowMessageService messages)
    {
        InitializeComponent();
        _rbacFactory = rbacFactory;
        _dictionaryFactory = dictionaryFactory;
        _registrationFactory = registrationFactory;
        _resetFactory = resetFactory;
        _currentUser = currentUser;
        _contentDialogs = contentDialogs;
        _messages = messages;
        _sessionUserId = currentUser.CurrentUser?.UserId;
        _currentUser.CurrentUserChanged += OnCurrentUserChanged;
        _messages.MessageChanged += OnMessageChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>窗口当前页面，供 STA 验证权限导航和切页结果。</summary>
    public string? CurrentPageName => _currentPage?.ToString();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ConstrainToWorkArea();
        _contentDialogs.SetDialogHost(LocalDialogHost);
        ApplyPermissionMenu();
        if (FirstAccessiblePage() is { } first) await NavigateAsync(first, force: true);
        else ForceClose();
    }

    /// <summary>窗口首次显示时限制在当前工作区，窄屏下缩小最小尺寸以保持操作区可达。</summary>
    private void ConstrainToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width);
        Height = Math.Min(Height, area.Height);
        Left = Math.Clamp(double.IsFinite(Left) ? Left : area.Left, area.Left, area.Right - Width);
        Top = Math.Clamp(double.IsFinite(Top) ? Top : area.Top, area.Top, area.Bottom - Height);
    }

    private async void OnUsersNavigation(object sender, RoutedEventArgs e)
    { await NavigateAsync(SettingsPage.Users); SynchronizeSelection(); }
    private async void OnRolesNavigation(object sender, RoutedEventArgs e)
    { await NavigateAsync(SettingsPage.Roles); SynchronizeSelection(); }
    private async void OnPermissionsNavigation(object sender, RoutedEventArgs e)
    { await NavigateAsync(SettingsPage.Permissions); SynchronizeSelection(); }
    private async void OnDictionaryNavigation(object sender, RoutedEventArgs e)
    { await NavigateAsync(SettingsPage.Dictionary); SynchronizeSelection(); }

    /// <summary>使 NavigationView 的可见选中项始终对应已通过切页门禁的 Page。</summary>
    private void SynchronizeSelection()
    {
        UsersNavigation.IsActive = _currentPage == SettingsPage.Users;
        RolesNavigation.IsActive = _currentPage == SettingsPage.Roles;
        PermissionsNavigation.IsActive = _currentPage == SettingsPage.Permissions;
        DictionaryNavigation.IsActive = _currentPage == SettingsPage.Dictionary;
    }

    /// <summary>先完成当前页的编辑提交与放弃确认，再按权限初始化目标页。</summary>
    private async Task NavigateAsync(SettingsPage page, bool force = false)
    {
        if (_disposed || _navigating || _currentPage == page || !CanRead(page)) return;
        // 避免活动表单与导航确认争用同一 ContentDialogHost。
        if (!force && _activeForm is not null) return;
        _navigating = true;
        try
        {
            if (!force)
            {
                if (_rbac?.IsWriting == true || _rbac?.IsBusy == true
                    || _dictionary?.IsWriting == true || _dictionary?.IsBusy == true) return;
                if (_currentPage == SettingsPage.Users)
                {
                    if (_rbac is not null && !await _rbac.CanLeaveUsersPageAsync()) return;
                }
                if (_currentPage == SettingsPage.Roles
                    && _rbac is not null && !await _rbac.CanLeaveRolesPageAsync()) return;
                if (_currentPage == SettingsPage.Dictionary
                    && (_dictionaryView?.CommitPendingEdits() == false
                        || (_dictionary is not null && !await _dictionary.CanLeavePageAsync(CancellationToken.None))))
                    return;
            }

            switch (page)
            {
                case SettingsPage.Users:
                    _rbac ??= _rbacFactory();
                    _users ??= new UserManagementView(_rbac, ShowRegistrationDialogAsync,
                        ShowUserProfileDialogAsync, ShowUserRolesDialogAsync,
                        ShowResetPasswordDialogAsync, _currentUser.IsAdmin);
                    SettingsNavigation.ReplaceContent(_users, null);
                    _currentPage = page;
                    SynchronizeSelection();
                    await _rbac.InitializeUsersPageAsync();
                    if (!_disposed && CanRead(page) && _users is not null)
                        _users.RefreshCount();
                    break;
                case SettingsPage.Roles:
                    _rbac ??= _rbacFactory();
                    _roles ??= new RoleManagementView(_rbac, ShowCreateRoleDialogAsync,
                        ShowRoleProfileDialogAsync, ShowRolePermissionsDialogAsync,
                        _currentUser.IsAdmin);
                    SettingsNavigation.ReplaceContent(_roles, null);
                    _currentPage = page;
                    SynchronizeSelection();
                    await _rbac.InitializeRolesPageAsync();
                    break;
                case SettingsPage.Permissions:
                    _rbac ??= _rbacFactory();
                    _permissions ??= new PermissionCatalogView(_rbac);
                    SettingsNavigation.ReplaceContent(_permissions, null);
                    _currentPage = page;
                    SynchronizeSelection();
                    await _rbac.InitializePermissionsPageAsync();
                    if (!_disposed && CanRead(page) && _permissions is not null)
                        _permissions.RefreshGroups();
                    break;
                case SettingsPage.Dictionary:
                    _dictionary ??= _dictionaryFactory();
                    _dictionaryPage ??= new DictionaryManagementPage(_dictionary);
                    _dictionaryView = _dictionaryPage.Editor;
                    SettingsNavigation.ReplaceContent(_dictionaryPage, null);
                    _currentPage = page;
                    SynchronizeSelection();
                    await _dictionary.InitializeAsync();
                    break;
            }
        }
        finally
        {
            _navigating = false;
            if (_pendingPermissionNavigation && !_disposed)
            {
                _pendingPermissionNavigation = false;
                // 当前加载释放导航门禁后，重新按最新权限选择页面。
                _ = Dispatcher.BeginInvoke(new Action(() => OnCurrentUserChanged(this, EventArgs.Empty)));
            }
        }
    }

    private bool CanRead(SettingsPage page) => page switch
    {
        SettingsPage.Users => _currentUser.HasPermission("System.Users.View"),
        SettingsPage.Roles => _currentUser.HasPermission("System.Roles.View"),
        SettingsPage.Permissions => _currentUser.HasPermission("System.Permissions.View"),
        SettingsPage.Dictionary => _currentUser.HasPermission("System.Dictionary.View"),
        _ => false
    };

    /// <summary>新建用户成功后把用户名交还列表刷新定位；密码始终留在表单 PasswordBox。</summary>
    private async Task<string?> ShowRegistrationDialogAsync()
    {
        if (!CanOpenForm(SettingsPage.Users)) return null;
        var model = _registrationFactory();
        var form = new UserRegistrationView(model);
        string? createdName = null;
        var session = NewFormSession("创建用户", form, () => form.HasDraft, () => model.IsBusy,
            form.ClearSensitiveInput, form.Dispose);
        void OnComplete(object? sender, EventArgs e)
        {
            if (session.WasForced) return;
            createdName = model.UserName;
            session.Complete();
        }
        void OnCancel(object? sender, EventArgs e) => session.RequestCancel();
        model.RequestClose += OnComplete;
        form.CancelRequested += OnCancel;
        try { await ShowFormAsync(session); return createdName; }
        finally { model.RequestClose -= OnComplete; form.CancelRequested -= OnCancel; }
    }

    /// <summary>固定父页行目标，不让重置弹窗再查询或选择其他用户。</summary>
    private async Task ShowResetPasswordDialogAsync(ManagedUserDto target)
    {
        if (!CanOpenForm(SettingsPage.Users)) return;
        var model = _resetFactory();
        model.SelectedUser = new UserSummaryDto { Uid = target.Uid, UserName = target.UserName,
            NickName = target.NickName, IsAdmin = target.IsAdmin };
        var form = new ResetPasswordView(model);
        var session = NewFormSession("重置密码", form, () => form.HasDraft, () => model.IsBusy,
            form.ClearSensitiveInput, () => { form.Dispose(); model.SelectedUser = null; model.Dispose(); });
        void OnComplete(object? sender, EventArgs e) => session.Complete();
        void OnCancel(object? sender, EventArgs e) => session.RequestCancel();
        model.RequestClose += OnComplete;
        form.CancelRequested += OnCancel;
        try { await ShowFormAsync(session); }
        finally { model.RequestClose -= OnComplete; form.CancelRequested -= OnCancel; }
    }

    /// <summary>资料弹窗只保存当前行的非凭据资料，保存成功后重新定位该用户。</summary>
    private async Task ShowUserProfileDialogAsync(ManagedUserDto target)
    {
        if (!CanOpenForm(SettingsPage.Users) || _rbac is null) return;
        await _rbac.SelectUserCommand.ExecuteAsync(target);
        if (_rbac.SelectedUser?.Uid != target.Uid || !CanOpenForm(SettingsPage.Users)) return;
        var form = new UserProfileFormView(_rbac);
        var session = NewFormSession("编辑用户资料", form, () => _rbac.HasUserProfileDraft,
            () => _rbac.IsWriting, _rbac.DiscardUserDraft, () => { });
        void OnSaved(object? sender, EventArgs e) => session.Complete();
        form.Saved += OnSaved;
        try { await ShowFormAsync(session); }
        finally { form.Saved -= OnSaved; }
        if (!_disposed && _currentUser.HasPermission("System.Users.View")
            && _rbac.SelectedUser?.Uid == target.Uid)
            await _rbac.RefreshUsersPageAsync(target.UserName);
    }

    /// <summary>角色授权弹窗只保存用户角色的完整集合，失败时保留勾选。</summary>
    private async Task ShowUserRolesDialogAsync(ManagedUserDto target)
    {
        if (!CanOpenForm(SettingsPage.Users) || _rbac is null) return;
        await _rbac.SelectUserCommand.ExecuteAsync(target);
        if (_rbac.SelectedUser?.Uid != target.Uid || !CanOpenForm(SettingsPage.Users)) return;
        var form = new UserRolesFormView(_rbac);
        var session = NewFormSession("分配用户角色", form, () => _rbac.HasUserRolesDraft,
            () => _rbac.IsWriting, _rbac.DiscardUserDraft, () => { });
        void OnSaved(object? sender, EventArgs e) => session.Complete();
        form.Saved += OnSaved;
        try { await ShowFormAsync(session); }
        finally { form.Saved -= OnSaved; }
    }

    /// <summary>新增普通角色仅写角色资料；权限分配由后续独立弹窗执行。</summary>
    private async Task ShowCreateRoleDialogAsync()
    {
        if (!CanOpenForm(SettingsPage.Roles) || _rbac is null) return;
        var form = new RoleProfileFormView(_rbac, creating: true);
        var session = NewFormSession("新增角色", form, () => _rbac.HasNewRoleDraft,
            () => _rbac.IsWriting || _rbac.IsBusy, _rbac.DiscardRoleDraft, () => { });
        void OnSaved(object? sender, EventArgs e) => session.Complete();
        form.Saved += OnSaved;
        try { await ShowFormAsync(session); }
        finally { form.Saved -= OnSaved; }
    }

    /// <summary>普通角色资料弹窗与完整权限集合弹窗使用不同保存范围。</summary>
    private async Task ShowRoleProfileDialogAsync(ManagedRoleDto target)
    {
        if (!CanOpenForm(SettingsPage.Roles) || _rbac is null) return;
        await _rbac.SelectRoleCommand.ExecuteAsync(target);
        if (_rbac.SelectedRole?.Uid != target.Uid || !CanOpenForm(SettingsPage.Roles)) return;
        var form = new RoleProfileFormView(_rbac, creating: false);
        var writeRevision = _rbac.SuccessfulWriteRevision;
        var session = NewFormSession("编辑角色资料", form, () => _rbac.HasRoleProfileDraft,
            () => _rbac.IsWriting, _rbac.DiscardRoleDraft, () => { });
        void OnSaved(object? sender, EventArgs e) => session.Complete();
        form.Saved += OnSaved;
        try { await ShowFormAsync(session); }
        finally { form.Saved -= OnSaved; }
        // DTO 不发属性通知，资料保存后读回列表以更新名称和当前行。
        if (!_disposed && _currentUser.HasPermission("System.Roles.View")
            && _rbac.SuccessfulWriteRevision != writeRevision)
            await _rbac.RefreshRolesPageAsync();
    }

    /// <summary>保存角色权限始终提交未被筛选裁剪的完整集合。</summary>
    private async Task ShowRolePermissionsDialogAsync(ManagedRoleDto target)
    {
        if (!CanOpenForm(SettingsPage.Roles) || _rbac is null || target.IsSystem) return;
        await _rbac.SelectRoleCommand.ExecuteAsync(target);
        if (_rbac.SelectedRole?.Uid != target.Uid || !CanOpenForm(SettingsPage.Roles)) return;
        var form = new RolePermissionsFormView(_rbac);
        var session = NewFormSession("分配角色权限", form, () => _rbac.HasRolePermissionsDraft,
            () => _rbac.IsWriting, _rbac.DiscardRoleDraft, () => { });
        void OnSaved(object? sender, EventArgs e) => session.Complete();
        form.Saved += OnSaved;
        try { await ShowFormAsync(session); }
        finally { form.Saved -= OnSaved; }
    }

    private bool CanOpenForm(SettingsPage page) => !_disposed && _activeForm is null
        && _currentUser.IsAdmin && CanRead(page) && _rbac?.IsWriting != true && _rbac?.IsBusy != true;

    private SettingsFormDialogSession NewFormSession(string title, FrameworkElement form,
        Func<bool> hasDraft, Func<bool> isBusy, Action discard, Action cleanup)
        => new(_contentDialogs, title, form, hasDraft, isBusy, discard, cleanup);

    private async Task ShowFormAsync(SettingsFormDialogSession session)
    {
        _activeForm = session;
        try { await session.ShowAsync(); }
        finally { if (ReferenceEquals(_activeForm, session)) _activeForm = null; }
    }

    private SettingsPage? FirstAccessiblePage()
    {
        foreach (var page in Enum.GetValues<SettingsPage>())
            if (CanRead(page)) return page;
        return null;
    }

    private void ApplyPermissionMenu()
    {
        UsersNavigation.Visibility = CanRead(SettingsPage.Users) ? Visibility.Visible : Visibility.Collapsed;
        RolesNavigation.Visibility = CanRead(SettingsPage.Roles) ? Visibility.Visible : Visibility.Collapsed;
        PermissionsNavigation.Visibility = CanRead(SettingsPage.Permissions) ? Visibility.Visible : Visibility.Collapsed;
        DictionaryNavigation.Visibility = CanRead(SettingsPage.Dictionary) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>同用户撤权时立即移除不可读页面；无入口或账号切换则强制关闭。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnCurrentUserChanged(sender, e));
            return;
        }
        if (_currentUser.CurrentUser?.UserId != _sessionUserId) { ForceClose(); return; }
        _ = Dispatcher.BeginInvoke(async () =>
        {
            if (_disposed) return;
            ApplyPermissionMenu();
            _users?.ApplyPermissions(_currentUser.IsAdmin);
            _roles?.ApplyPermissions(_currentUser.IsAdmin);
            if ((!CanRead(SettingsPage.Users) && _currentPage == SettingsPage.Users
                    || !CanRead(SettingsPage.Roles) && _currentPage == SettingsPage.Roles
                    || !_currentUser.IsAdmin) && _activeForm is { } activeForm)
                activeForm.ForceClose();
            if (!CanRead(SettingsPage.Dictionary) && _dictionary is not null)
            {
                _dictionary.Dispose();
                _dictionaryPage = null;
                _dictionaryView = null;
                _dictionary = null;
            }
            if (!CanRead(SettingsPage.Users) && _users is not null)
            {
                _users.Dispose();
                _users = null;
            }
            if (FirstAccessiblePage() is not { } first) { ForceClose(); return; }
            if (_currentPage is { } active && CanRead(active)) return;
            SettingsNavigation.ReplaceContent(new System.Windows.Controls.Page(), null);
            _currentPage = null;
            SynchronizeSelection();
            if (_rbac?.IsWriting == true || _dictionary?.IsWriting == true)
            {
                ForceClose();
                return;
            }
            if (_navigating)
            {
                _pendingPermissionNavigation = true;
                return;
            }
            await NavigateAsync(first, force: true);
        });
    }

    private void OnMessageChanged(object? sender, StatusMessageChangedEventArgs e)
    {
        WindowInfoBar.Title = e.Title ?? string.Empty;
        WindowInfoBar.Message = e.Message ?? string.Empty;
        WindowInfoBar.Severity = e.Severity switch
        {
            UiMessageSeverity.Success => InfoBarSeverity.Success,
            UiMessageSeverity.Warning => InfoBarSeverity.Warning,
            UiMessageSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        WindowInfoBar.IsOpen = e.IsOpen;
    }

    /// <summary>普通关闭先提交字典单元格，再确认各页草稿；写入中拒绝关闭。</summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_checkingClose || _activeForm is not null
            || _rbac?.IsWriting == true || _dictionary?.IsWriting == true) return;
        _checkingClose = true;
        try
        {
            if (_dictionaryView?.CommitPendingEdits() == false) return;
            if (_rbac is not null && !await _rbac.CanCloseAsync()) return;
            if (_dictionary is not null && !await _dictionary.CanCloseAsync(CancellationToken.None)) return;
            _allowClose = true;
            _ = Dispatcher.BeginInvoke(new Action(Close));
        }
        finally { _checkingClose = false; }
    }

    private void OnClosed(object? sender, EventArgs e) => DisposeContent();

    /// <inheritdoc />
    public void ForceClose()
    {
        if (_disposed) return;
        _activeForm?.ForceClose();
        _allowClose = true;
        DisposeContent();
        Close();
    }

    private void DisposeContent()
    {
        if (_disposed) return;
        _disposed = true;
        _activeForm?.ForceClose();
        _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
        _messages.MessageChanged -= OnMessageChanged;
        _users?.Dispose();
        _roles?.Dispose();
        _rbac?.Dispose();
        _dictionary?.Dispose();
        SettingsNavigation.ReplaceContent(new System.Windows.Controls.Page(), null);
    }

    private enum SettingsPage { Users, Roles, Permissions, Dictionary }
}
