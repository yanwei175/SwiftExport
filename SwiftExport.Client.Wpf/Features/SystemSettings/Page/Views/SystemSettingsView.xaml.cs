using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;
using SwiftExportClient.Features.SystemSettings.Personal.Views;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.SystemParameters.Views;

namespace SwiftExportClient.Features.SystemSettings.Page.Views;

/// <summary>设置页签的 NavigationView 视觉适配器，仅桥接控件事件和字典编辑提交。</summary>
public partial class SystemSettingsView : UserControl
{
    private SettingsPageViewModel Model => _model!;
    private SettingsPageViewModel? _model;
    private SettingsPageViewModel? _attached;
    private UserManagementView? _users;
    private RoleManagementView? _roles;
    private PermissionCatalogView? _permissions;
    private DictionaryManagementPage? _dictionaryPage;
    private DictionaryManagerView? _dictionaryView;

    /// <summary>由页签 DataTemplate 创建；视觉卸载只解除桥接，不销毁模型。</summary>
    public SystemSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { Detach(); if (IsLoaded) Attach(); };
    }

    /// <summary>当前显示的内部页面名称，供辅助功能和验收读取。</summary>
    public string? CurrentPageName => _model?.CurrentPage?.ToString();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Attach();
        if (_attached is not null) await _attached.InitializeAsync();
    }

    private void Attach()
    {
        if (_attached is not null || DataContext is not SettingsPageViewModel model || model.IsDisposed) return;
        _attached = model; _model = model;
        model.CommitEdits = CommitEdits;
        model.PresentationChanged += OnPresentationChanged;
        model.Disposed += OnDisposed;
        RenderPage();
    }

    private bool CommitEdits() => _dictionaryView?.CommitPendingEdits() != false;

    /// <summary>解除页签事件订阅；表单与草稿生命周期仍归页签模型。</summary>
    private void Detach()
    {
        if (_attached is null) return;
        CommitEdits();
        if (_attached.CommitEdits == CommitEdits) _attached.CommitEdits = null;
        _attached.PresentationChanged -= OnPresentationChanged;
        _attached.Disposed -= OnDisposed;
        _attached = null;
        ClearViews();
    }

    private void OnDisposed(object? sender, EventArgs e) => Detach();
    private void OnPresentationChanged(object? sender, EventArgs e) => RenderPage();
    private bool CanRead(SettingsPage page) => Model.CanRead(page);
    private async void OnUsersNavigation(object sender, RoutedEventArgs e)
    { await Model.NavigateAsync(SettingsPage.Users); SynchronizeSelection(); }
    private async void OnRolesNavigation(object sender, RoutedEventArgs e)
    { await Model.NavigateAsync(SettingsPage.Roles); SynchronizeSelection(); }
    private async void OnPermissionsNavigation(object sender, RoutedEventArgs e)
    { await Model.NavigateAsync(SettingsPage.Permissions); SynchronizeSelection(); }
    private async void OnDictionaryNavigation(object sender, RoutedEventArgs e)
    { await Model.NavigateAsync(SettingsPage.Dictionary); SynchronizeSelection(); }
    /// <summary>参数页切换仍由页签模型检查草稿与保存状态。</summary>
    private async void OnSystemParametersNavigation(object sender, RoutedEventArgs e)
    { await Model.NavigateAsync(SettingsPage.SystemParameters); SynchronizeSelection(); }

    /// <summary>个人菜单子操作冒泡时保持当前管理页面。</summary>
    private async void OnPersonalNavigation(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, PersonalNavigation)) return;
        await Model.NavigateAsync(SettingsPage.Personal);
        SynchronizeSelection();
    }

    private void SynchronizeSelection()
    {
        PersonalNavigation.IsActive = Model.CurrentPage == SettingsPage.Personal;
        UsersNavigation.IsActive = Model.CurrentPage == SettingsPage.Users;
        RolesNavigation.IsActive = Model.CurrentPage == SettingsPage.Roles;
        PermissionsNavigation.IsActive = Model.CurrentPage == SettingsPage.Permissions;
        DictionaryNavigation.IsActive = Model.CurrentPage == SettingsPage.Dictionary;
        SystemParametersNavigation.IsActive = Model.CurrentPage == SettingsPage.SystemParameters;
    }

    /// <summary>按页签模型呈现对应页面，权限撤销时丢弃不可读列表视图。</summary>
    private void RenderPage()
    {
        if (_attached is null) return;
        var currentUser = Model.CurrentUser;
        ManagementNavigation.Visibility = currentUser.IsAdmin || SettingsPageViewModel.EntryPermissions.Any(currentUser.HasPermission)
            ? Visibility.Visible : Visibility.Collapsed;
        UsersNavigation.Visibility = CanRead(SettingsPage.Users) ? Visibility.Visible : Visibility.Collapsed;
        RolesNavigation.Visibility = CanRead(SettingsPage.Roles) ? Visibility.Visible : Visibility.Collapsed;
        PermissionsNavigation.Visibility = CanRead(SettingsPage.Permissions) ? Visibility.Visible : Visibility.Collapsed;
        DictionaryNavigation.Visibility = CanRead(SettingsPage.Dictionary) ? Visibility.Visible : Visibility.Collapsed;
        SystemParametersNavigation.Visibility = CanRead(SettingsPage.SystemParameters) ? Visibility.Visible : Visibility.Collapsed;
        if (!CanRead(SettingsPage.Users)) { _users?.Dispose(); _users = null; }
        if (!CanRead(SettingsPage.Roles)) { _roles?.Dispose(); _roles = null; }
        if (!CanRead(SettingsPage.Permissions)) _permissions = null;
        if (!CanRead(SettingsPage.Dictionary)) { _dictionaryPage = null; _dictionaryView = null; }
        _users?.ApplyPermissions(currentUser.IsAdmin);
        _roles?.ApplyPermissions(currentUser.IsAdmin);
        System.Windows.Controls.Page page;
        switch (Model.CurrentPage)
        {
            case SettingsPage.Personal:
                page = new PersonalSettingsPage { DataContext = Model }; break;
            case SettingsPage.Users:
                _users ??= new UserManagementView(Model.Users!, currentUser.IsAdmin);
                _users.RefreshCount(); page = _users; break;
            case SettingsPage.Roles:
                _roles ??= new RoleManagementView(Model.Roles!, currentUser.IsAdmin);
                page = _roles; break;
            case SettingsPage.Permissions:
                _permissions ??= new PermissionCatalogView(Model.Permissions!);
                _permissions.RefreshGroups(); page = _permissions; break;
            case SettingsPage.Dictionary:
                _dictionaryPage ??= new DictionaryManagementPage(Model.Dictionary!);
                _dictionaryView = _dictionaryPage.Editor; page = _dictionaryPage; break;
            case SettingsPage.SystemParameters:
                page = new SystemParametersPage { DataContext = Model.SystemParameters }; break;
            default: page = new System.Windows.Controls.Page(); break;
        }
        SettingsNavigation.ReplaceContent(page, null);
        SynchronizeSelection();
    }

    /// <summary>清理一次视觉树引用；页签仍拥有列表和字典模型。</summary>
    private void ClearViews()
    {
        _users?.Dispose(); _roles?.Dispose();
        _users = null; _roles = null; _permissions = null; _dictionaryPage = null; _dictionaryView = null;
        SettingsNavigation.ReplaceContent(new System.Windows.Controls.Page(), null);
    }
}
