using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Permissions.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Query;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Query;
using SwiftExportClient.Features.SystemSettings.Services;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Features.SystemSettings.SystemParameters.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Page.ViewModels;

/// <summary>设置单页签的内部导航、子页面生命周期与关闭保护。</summary>
public sealed partial class SettingsPageViewModel : ObservableObject, IDocumentTabViewModel, IDisposable
{
    /// <summary>Shell 中稳定的设置路由键。</summary>
    public const string RouteKey = "system-settings";
    /// <summary>管理菜单读取权限集合；个人设置不依赖这些权限。</summary>
    public static IReadOnlyList<string> EntryPermissions { get; } = Array.AsReadOnly(new[]
        { "System.Users.View", "System.Roles.View", "System.Permissions.View", "System.Dictionary.View", "System.Parameters.Manage" });

    private readonly Func<UsersQueryViewModel> _usersFactory;
    private readonly Func<RolesQueryViewModel> _rolesFactory;
    private readonly Func<PermissionsQueryViewModel> _permissionsFactory;
    private readonly Func<DictionaryManagerViewModel> _dictionaryFactory;
    private readonly Func<SystemParametersViewModel>? _parametersFactory;
    private readonly ISystemParameterCacheService? _parameterCache;
    private readonly ISettingsDialogService _dialogs;
    private readonly IAuthService? _authService;
    private readonly IMessageService? _messages;
    private readonly string? _userId;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _navigating, _pendingPermissions, _checkingClose;

    /// <summary>用户列表按首次访问创建。</summary>
    internal UsersQueryViewModel? Users { get; private set; }
    /// <summary>角色列表按首次访问创建。</summary>
    internal RolesQueryViewModel? Roles { get; private set; }
    /// <summary>权限目录按首次访问创建。</summary>
    internal PermissionsQueryViewModel? Permissions { get; private set; }
    /// <summary>字典编辑模型按首次访问创建。</summary>
    internal DictionaryManagerViewModel? Dictionary { get; private set; }
    /// <summary>参数编辑模型按首次访问创建，草稿由本页签保护。</summary>
    internal SystemParametersViewModel? SystemParameters { get; private set; }
    /// <summary>当前身份，供 View 显示权限入口。</summary>
    internal ICurrentUserService CurrentUser { get; }
    /// <summary>只允许控件层提交当前 DataGrid 编辑；卸载时解绑。</summary>
    internal Func<bool>? CommitEdits { get; set; }
    /// <summary>活动表单由本页签 Dialog 服务维护。</summary>
    public bool HasActiveForm => _dialogs.HasActiveForm;
    /// <summary>个人刷新结果，仅对仍存活的当前会话显示。</summary>
    [ObservableProperty] public partial string RefreshStatus { get; private set; } = string.Empty;
    /// <summary>所有登录用户可见的系统参数手动刷新结果。</summary>
    [ObservableProperty] public partial string ParameterRefreshStatus { get; private set; } = string.Empty;
    /// <summary>当前内页；空值仅在销毁或权限变化短暂存在。</summary>
    [ObservableProperty] public partial SettingsPage? CurrentPage { get; private set; }
    /// <summary>页签已经销毁，迟到查询不得重新呈现。</summary>
    internal bool IsDisposed { get; private set; }
    /// <summary>内部页面或权限可见性需要重新呈现。</summary>
    internal event EventHandler? PresentationChanged;
    /// <summary>页签释放时通知 View 解除控件事件桥接。</summary>
    internal event EventHandler? Disposed;

    /// <summary>创建页签局部列表及 Dialog 服务；表单不跨页签复用。</summary>
    public SettingsPageViewModel(Func<UsersQueryViewModel> usersFactory,
        Func<RolesQueryViewModel> rolesFactory, Func<PermissionsQueryViewModel> permissionsFactory,
        Func<DictionaryManagerViewModel> dictionaryFactory, ICurrentUserService currentUser,
        ISettingsDialogService dialogs, IAuthService? authService = null, IMessageService? messages = null,
        Func<SystemParametersViewModel>? parametersFactory = null, ISystemParameterCacheService? parameterCache = null)
    {
        _usersFactory = usersFactory; _rolesFactory = rolesFactory;
        _permissionsFactory = permissionsFactory; _dictionaryFactory = dictionaryFactory;
        _parametersFactory = parametersFactory; _parameterCache = parameterCache;
        CurrentUser = currentUser; _dialogs = dialogs; _authService = authService; _messages = messages;
        _userId = currentUser.CurrentUser?.UserId;
        currentUser.CurrentUserChanged += OnCurrentUserChanged;
        dialogs.ActiveFormChanged += OnActiveFormChanged;
    }

    /// <inheritdoc />
    public string DocumentTitle => "设置";
    /// <inheritdoc />
    public bool HasUnsavedChanges => Dictionary?.HasUnsavedChanges == true || SystemParameters?.HasUnsavedChanges == true;
    private bool CanUsePersonal() => !IsDisposed && CurrentUser.IsAuthenticated && !HasActiveForm;

    /// <summary>由模块 Dialog 用例直接打开本人改密表单。</summary>
    [RelayCommand(CanExecute = nameof(CanUsePersonal))]
    private Task ChangePasswordAsync() => CanUsePersonal() ? _dialogs.ShowChangePasswordAsync() : Task.CompletedTask;

    /// <summary>刷新当前账号权限；旧会话结果不反馈到新页签。</summary>
    [RelayCommand(CanExecute = nameof(CanUsePersonal))]
    private async Task RefreshPermissionsAsync()
    {
        if (!CanUsePersonal() || _authService is null) return;
        var result = await _authService.RefreshCurrentUserAsync();
        if (IsDisposed || CurrentUser.CurrentUser?.UserId != _userId) return;
        RefreshStatus = result.Success ? "权限已从服务器刷新。" : result.Message;
        if (result.Success) _messages?.ShowSuccess("刷新权限", RefreshStatus);
        else _messages?.ShowError("刷新权限失败", RefreshStatus);
    }

    /// <summary>手动刷新当前会话参数缓存，不覆盖参数维护页的未保存草稿。</summary>
    [RelayCommand(CanExecute = nameof(CanUsePersonal))]
    private async Task RefreshSystemParametersAsync()
    {
        if (!CanUsePersonal() || _parameterCache is null) return;
        var result = await _parameterCache.RefreshAsync();
        if (IsDisposed || CurrentUser.CurrentUser?.UserId != _userId) return;
        ParameterRefreshStatus = result.Message;
    }

    private void OnActiveFormChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasActiveForm));
        ChangePasswordCommand.NotifyCanExecuteChanged();
        RefreshPermissionsCommand.NotifyCanExecuteChanged();
        RefreshSystemParametersCommand.NotifyCanExecuteChanged();
    }

    /// <summary>按当前权限判定内部页面可读性，个人概览只要求登录。</summary>
    public bool CanRead(SettingsPage page) => CurrentUser.IsAuthenticated && (page == SettingsPage.SystemParameters
        ? CurrentUser.IsAdmin : (page == SettingsPage.Personal
        || CurrentUser.HasPermission(page switch
        {
            SettingsPage.Users => "System.Users.View", SettingsPage.Roles => "System.Roles.View",
            SettingsPage.Permissions => "System.Permissions.View",
            _ => "System.Dictionary.View"
        })));

    /// <summary>首次挂载选个人概览，重复挂载保留当前页。</summary>
    public Task InitializeAsync() => CurrentPage is null ? NavigateAsync(SettingsPage.Personal, true) : Task.CompletedTask;

    /// <summary>切页前检查活动表单和字典编辑；子列表仅在对应页面按需创建。</summary>
    public async Task NavigateAsync(SettingsPage page, bool force = false)
    {
        if (IsDisposed || _navigating || CurrentPage == page || !CanRead(page)) return;
        if (!force && HasActiveForm) return;
        _navigating = true;
        try
        {
            if (!force)
            {
                if (Users?.IsWriting == true || Roles?.IsWriting == true || Dictionary?.IsWriting == true
                    || Dictionary?.IsBusy == true || SystemParameters?.IsBusy == true) return;
                if (CurrentPage == SettingsPage.SystemParameters && SystemParameters is not null
                    && !await SystemParameters.CanLeaveAsync(CancellationToken.None)) return;
                if (CurrentPage == SettingsPage.Dictionary && (CommitEdits?.Invoke() == false
                    || (Dictionary is not null && !await Dictionary.CanLeavePageAsync(CancellationToken.None)))) return;
            }
            if (IsDisposed || !CanRead(page)) return;
            switch (page)
            {
                case SettingsPage.Users: Users ??= _usersFactory(); break;
                case SettingsPage.Roles: Roles ??= _rolesFactory(); break;
                case SettingsPage.Permissions: Permissions ??= _permissionsFactory(); break;
                case SettingsPage.SystemParameters:
                    if (SystemParameters is null && _parametersFactory is not null)
                    {
                        SystemParameters = _parametersFactory();
                        SystemParameters.PropertyChanged += OnDictionaryChanged;
                    }
                    break;
                case SettingsPage.Dictionary:
                    if (Dictionary is null)
                    {
                        Dictionary = _dictionaryFactory();
                        Dictionary.PropertyChanged += OnDictionaryChanged;
                    }
                    break;
            }
            CurrentPage = page;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
            switch (page)
            {
                case SettingsPage.Users: await Users!.InitializeAsync(); break;
                case SettingsPage.Roles: await Roles!.InitializeAsync(); break;
                case SettingsPage.Permissions: await Permissions!.InitializeAsync(); break;
                case SettingsPage.SystemParameters:
                    if (SystemParameters is not null) await SystemParameters.InitializeAsync();
                    break;
                case SettingsPage.Dictionary: await Dictionary!.InitializeAsync(); break;
            }
            if (!IsDisposed && CanRead(page)) PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _navigating = false;
            if (_pendingPermissions && !IsDisposed)
            {
                _pendingPermissions = false;
                await ReconcilePermissionsAsync();
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed) return true;
        if (_checkingClose || HasActiveForm || Users?.IsWriting == true || Roles?.IsWriting == true
            || Dictionary?.IsWriting == true || SystemParameters?.IsBusy == true) return false;
        _checkingClose = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CommitEdits?.Invoke() == false) return false;
            if (SystemParameters is not null && !await SystemParameters.CanLeaveAsync(cancellationToken)) return false;
            return Dictionary is null || await Dictionary.CanCloseAsync(cancellationToken);
        }
        finally { _checkingClose = false; }
    }

    private void OnDictionaryChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(nameof(HasUnsavedChanges));

    /// <summary>跨线程身份通知回到创建线程，账号切换立即销毁旧页签。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_context is not null && Environment.CurrentManagedThreadId != _ownerThreadId)
        { _context.Post(_ => OnCurrentUserChanged(sender, e), null); return; }
        if (IsDisposed) return;
        if (CurrentUser.CurrentUser?.UserId != _userId || !CurrentUser.IsAuthenticated)
        { Dispose(); return; }
        if (_context is not null) _context.Post(async _ => await ReconcilePermissionsAsync(), null);
        else _ = ReconcilePermissionsAsync();
    }

    /// <summary>管理入口被撤销时清理各列表并回个人概览，个人表单仍由其自身资格决定。</summary>
    private async Task ReconcilePermissionsAsync()
    {
        if (IsDisposed) return;
        if (CurrentUser.CurrentUser?.UserId != _userId || !CurrentUser.IsAuthenticated)
        { Dispose(); return; }
        if (!CanRead(SettingsPage.Users) && Users is not null) { Users.Dispose(); Users = null; }
        if (!CanRead(SettingsPage.Roles) && Roles is not null) { Roles.Dispose(); Roles = null; }
        if (!CanRead(SettingsPage.Permissions) && Permissions is not null)
        { Permissions.Dispose(); Permissions = null; }
        if (!CanRead(SettingsPage.Dictionary) && Dictionary is not null)
        {
            Dictionary.PropertyChanged -= OnDictionaryChanged;
            Dictionary.Dispose(); Dictionary = null;
        }
        if (!CanRead(SettingsPage.SystemParameters) && SystemParameters is not null)
        {
            SystemParameters.PropertyChanged -= OnDictionaryChanged;
            SystemParameters.Dispose(); SystemParameters = null;
        }
        OnPropertyChanged(nameof(HasUnsavedChanges));
        if (CurrentPage is { } current && !CanRead(current)) CurrentPage = null;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
        if (CurrentPage is not null) return;
        if (_navigating) { _pendingPermissions = true; return; }
        await NavigateAsync(SettingsPage.Personal, true);
    }

    /// <summary>真正关闭页签时释放表单、列表及字典；视觉卸载不销毁模型。</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        _dialogs.ActiveFormChanged -= OnActiveFormChanged;
        _dialogs.Dispose();
        CurrentUser.CurrentUserChanged -= OnCurrentUserChanged;
        Users?.Dispose(); Roles?.Dispose(); Permissions?.Dispose();
        if (SystemParameters is not null)
        {
            SystemParameters.PropertyChanged -= OnDictionaryChanged;
            SystemParameters.Dispose();
        }
        if (Dictionary is not null)
        {
            Dictionary.PropertyChanged -= OnDictionaryChanged;
            Dictionary.Dispose();
        }
        CommitEdits = null;
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}
