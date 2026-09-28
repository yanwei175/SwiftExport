using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;
using SwiftExportClient.Features.SystemSettings.Passwords.Views;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Views;
using SwiftExportClient.Features.SystemSettings.Workspace.ViewModels;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>系统设置页签的视觉适配器：保留内部导航，表单使用 Shell 公共弹窗。</summary>
public partial class SystemSettingsView : UserControl
{
    private SystemSettingsWorkspaceViewModel Model => _model!;
    private SystemSettingsWorkspaceViewModel? _model;
    private SystemSettingsWorkspaceViewModel? _attached;
    private SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels.RbacManagementViewModel? _rbac => Model.Rbac;
    private SwiftExportClient.Authentication.ICurrentUserService _currentUser => Model.CurrentUser;
    private bool _disposed => _attached is null || Model.IsDisposed;
    private UserManagementView? _users;
    private RoleManagementView? _roles;
    private PermissionCatalogView? _permissions;
    private DictionaryManagementPage? _dictionaryPage;
    private DictionaryManagerView? _dictionaryView;
    private SettingsFormDialogSession? _activeForm;

    /// <summary>由页签 DataTemplate 创建；卸载仅解绑视图，不释放页签模型。</summary>
    public SystemSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { Detach(); if (IsLoaded) Attach(); };
    }
    /// <summary>当前页面名称，供宿主验收与辅助功能检查。</summary>
    public string? CurrentPageName => _model?.CurrentPage?.ToString();
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        if (_attached is not null) await _attached.InitializeAsync();
    }
    /// <summary>挂载视图回调；模型持有业务草稿，视图只保留控件。</summary>
    private void Attach()
    {
        if (_attached is not null || DataContext is not SystemSettingsWorkspaceViewModel model || model.IsDisposed) return;
        _attached = model;
        _model = model;
        model.CommitEdits = CommitEdits;
        model.PresentationChanged += OnPresentationChanged;
        model.FormsInvalidated += OnFormsInvalidated;
        model.Disposed += OnDisposed;
        RenderPage();
    }
    private bool CommitEdits() => _dictionaryView?.CommitPendingEdits() != false;
    /// <summary>视图卸载时提交单元格并解除回调；草稿保存在仍存活的页签模型。</summary>
    private void Detach()
    {
        if (_attached is null) return;
        CommitEdits();
        _activeForm?.ForceClose();
        if (_attached.CommitEdits == CommitEdits) _attached.CommitEdits = null;
        _attached.PresentationChanged -= OnPresentationChanged;
        _attached.FormsInvalidated -= OnFormsInvalidated;
        _attached.Disposed -= OnDisposed;
        _attached = null;
        ClearViews();
    }
    private void OnDisposed(object? sender, EventArgs e) => Detach();
    private void OnFormsInvalidated(object? sender, EventArgs e) => _activeForm?.ForceClose();
    private void OnPresentationChanged(object? sender, EventArgs e) => RenderPage();
    private bool CanRead(SettingsPage page) => Model.CanRead(page);
    private async void OnUsersNavigation(object sender, RoutedEventArgs e) { await Model.NavigateAsync(SettingsPage.Users); SynchronizeSelection(); }
    private async void OnRolesNavigation(object sender, RoutedEventArgs e) { await Model.NavigateAsync(SettingsPage.Roles); SynchronizeSelection(); }
    private async void OnPermissionsNavigation(object sender, RoutedEventArgs e) { await Model.NavigateAsync(SettingsPage.Permissions); SynchronizeSelection(); }
    private async void OnDictionaryNavigation(object sender, RoutedEventArgs e) { await Model.NavigateAsync(SettingsPage.Dictionary); SynchronizeSelection(); }
    private void SynchronizeSelection()
    {
        UsersNavigation.IsActive = Model.CurrentPage == SettingsPage.Users;
        RolesNavigation.IsActive = Model.CurrentPage == SettingsPage.Roles;
        PermissionsNavigation.IsActive = Model.CurrentPage == SettingsPage.Permissions;
        DictionaryNavigation.IsActive = Model.CurrentPage == SettingsPage.Dictionary;
    }
    /// <summary>按模型选择呈现页面，不因重新加载视觉树重复请求数据。</summary>
    private void RenderPage()
    {
        UsersNavigation.Visibility = CanRead(SettingsPage.Users) ? Visibility.Visible : Visibility.Collapsed;
        RolesNavigation.Visibility = CanRead(SettingsPage.Roles) ? Visibility.Visible : Visibility.Collapsed;
        PermissionsNavigation.Visibility = CanRead(SettingsPage.Permissions) ? Visibility.Visible : Visibility.Collapsed;
        DictionaryNavigation.Visibility = CanRead(SettingsPage.Dictionary) ? Visibility.Visible : Visibility.Collapsed;
        if (!CanRead(SettingsPage.Users)) { _users?.Dispose(); _users = null; }
        if (!CanRead(SettingsPage.Roles)) { _roles?.Dispose(); _roles = null; }
        if (!CanRead(SettingsPage.Permissions)) _permissions = null;
        if (!CanRead(SettingsPage.Dictionary)) { _dictionaryPage = null; _dictionaryView = null; }
        _users?.ApplyPermissions(_currentUser.IsAdmin);
        _roles?.ApplyPermissions(_currentUser.IsAdmin);
        Page page;
        switch (Model.CurrentPage)
        {
            case SettingsPage.Users:
                _users ??= new UserManagementView(_rbac!, ShowRegistrationDialogAsync, ShowUserProfileDialogAsync,
                    ShowUserRolesDialogAsync, ShowResetPasswordDialogAsync, _currentUser.IsAdmin);
                _users.RefreshCount(); page = _users; break;
            case SettingsPage.Roles:
                _roles ??= new RoleManagementView(_rbac!, ShowCreateRoleDialogAsync, ShowRoleProfileDialogAsync,
                    ShowRolePermissionsDialogAsync, _currentUser.IsAdmin);
                page = _roles; break;
            case SettingsPage.Permissions:
                _permissions ??= new PermissionCatalogView(_rbac!);
                _permissions.RefreshGroups(); page = _permissions; break;
            case SettingsPage.Dictionary:
                _dictionaryPage ??= new DictionaryManagementPage(Model.Dictionary!);
                _dictionaryView = _dictionaryPage.Editor; page = _dictionaryPage; break;
            default: page = new Page(); break;
        }
        SettingsNavigation.ReplaceContent(page, null);
        SynchronizeSelection();
    }
    /// <summary>解除页面控件引用，不销毁页签拥有的子模型。</summary>
    private void ClearViews()
    {
        _users?.Dispose(); _roles?.Dispose();
        _users = null; _roles = null; _permissions = null; _dictionaryPage = null; _dictionaryView = null;
        SettingsNavigation.ReplaceContent(new Page(), null);
    }
    /// <summary>新建用户成功后把用户名交还列表刷新定位；密码始终留在表单 PasswordBox。</summary>
    private async Task<string?> ShowRegistrationDialogAsync()
    {
        if (!CanOpenForm(SettingsPage.Users)) return null;
        var model = Model.RegistrationFactory();
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
        var model = Model.ResetFactory();
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
        => new(Model.Dialogs, title, form, hasDraft, isBusy, discard, cleanup);

    /// <summary>活动表单期间阻止页签关闭，结束后释放关闭门禁。</summary>
    private async Task ShowFormAsync(SettingsFormDialogSession session)
    {
        var owner = Model;
        _activeForm = session;
        owner.HasActiveForm = true;
        try { await session.ShowAsync(); }
        finally { if (ReferenceEquals(_activeForm, session)) { _activeForm = null; owner.HasActiveForm = false; } }
    }

}
