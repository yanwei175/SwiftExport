using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.Workspace.ViewModels;

/// <summary>负责系统设置页签的选择、懒加载与关闭保护，不持有视图。</summary>
public sealed partial class SystemSettingsWorkspaceViewModel : ObservableObject, IDocumentTabViewModel, IDisposable
{
    /// <summary>单实例路由键。</summary>
    public const string RouteKey = "system-settings";
    /// <summary>任一读取权限即可进入；操作仍由各页检查。</summary>
    public static IReadOnlyList<string> EntryPermissions { get; } = Array.AsReadOnly(new[] {
        "System.Users.View", "System.Roles.View", "System.Permissions.View", "System.Dictionary.View" });
    private readonly Func<RbacManagementViewModel> _rbacFactory;
    private readonly Func<DictionaryManagerViewModel> _dictionaryFactory;
    private readonly string? _userId;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _navigating, _pendingPermissions, _checkingClose;
    internal RbacManagementViewModel? Rbac { get; private set; }
    internal DictionaryManagerViewModel? Dictionary { get; private set; }
    internal ICurrentUserService CurrentUser { get; }
    internal IDialogService Dialogs { get; }
    internal Func<UserRegistrationViewModel> RegistrationFactory { get; }
    internal Func<ResetPasswordViewModel> ResetFactory { get; }
    internal Func<bool>? CommitEdits { get; set; }
    internal bool HasActiveForm { get; set; }
    internal bool IsDisposed { get; private set; }
    internal event EventHandler? PresentationChanged;
    internal event EventHandler? FormsInvalidated;
    internal event EventHandler? Disposed;

    /// <summary>注入页面工厂和 Shell 服务；仅在访问对应页面时创建子模型。</summary>
    public SystemSettingsWorkspaceViewModel(Func<RbacManagementViewModel> rbacFactory,
        Func<DictionaryManagerViewModel> dictionaryFactory, Func<UserRegistrationViewModel> registrationFactory,
        Func<ResetPasswordViewModel> resetFactory, ICurrentUserService currentUser, IDialogService dialogs)
    {
        _rbacFactory = rbacFactory; _dictionaryFactory = dictionaryFactory;
        RegistrationFactory = registrationFactory; ResetFactory = resetFactory;
        CurrentUser = currentUser; Dialogs = dialogs; _userId = currentUser.CurrentUser?.UserId;
        currentUser.CurrentUserChanged += OnCurrentUserChanged;
    }
    /// <inheritdoc />
    public string DocumentTitle => "系统设置";
    /// <inheritdoc />
    public bool HasUnsavedChanges => Rbac?.HasUnsavedChanges == true || Dictionary?.HasUnsavedChanges == true;
    /// <summary>实际选中页，空值表示尚未选择或已撤权。</summary>
    [ObservableProperty]
    public partial SettingsPage? CurrentPage { get; private set; }
    /// <summary>检查当前读取权限。</summary>
    public bool CanRead(SettingsPage page) => CurrentUser.IsAuthenticated && CurrentUser.HasPermission(page switch {
        SettingsPage.Users => "System.Users.View", SettingsPage.Roles => "System.Roles.View",
        SettingsPage.Permissions => "System.Permissions.View", _ => "System.Dictionary.View" });
    /// <summary>首次挂载才选择页面，重复挂载保留草稿。</summary>
    public Task InitializeAsync() => CurrentPage is null ? NavigateFirstAsync() : Task.CompletedTask;
    /// <summary>仅选择具有读取权限的第一个页面。</summary>
    private Task NavigateFirstAsync()
    {
        var first = Enum.GetValues<SettingsPage>().Where(CanRead).Cast<SettingsPage?>().FirstOrDefault();
        return first is { } page ? NavigateAsync(page, true) : Task.CompletedTask;
    }
    /// <summary>切页前提交编辑并确认草稿；加载中的撤权延后选择新页。</summary>
    public async Task NavigateAsync(SettingsPage page, bool force = false)
    {
        if (IsDisposed || _navigating || CurrentPage == page || !CanRead(page)) return;
        if (!force && HasActiveForm) return;
        _navigating = true;
        try
        {
            if (!force)
            {
                if (Rbac?.IsBusy == true || Rbac?.IsWriting == true || Dictionary?.IsBusy == true || Dictionary?.IsWriting == true) return;
                if (CurrentPage == SettingsPage.Users && Rbac is not null && !await Rbac.CanLeaveUsersPageAsync()) return;
                if (CurrentPage == SettingsPage.Roles && Rbac is not null && !await Rbac.CanLeaveRolesPageAsync()) return;
                if (CurrentPage == SettingsPage.Dictionary && (CommitEdits?.Invoke() == false ||
                    (Dictionary is not null && !await Dictionary.CanLeavePageAsync(CancellationToken.None)))) return;
            }
            if (IsDisposed || !CanRead(page)) return;
            if (page == SettingsPage.Dictionary)
            {
                if (Dictionary is null) { Dictionary = _dictionaryFactory(); Dictionary.PropertyChanged += OnChildChanged; }
            }
            else if (Rbac is null) { Rbac = _rbacFactory(); Rbac.PropertyChanged += OnChildChanged; }
            CurrentPage = page;
            PresentationChanged?.Invoke(this, EventArgs.Empty);
            switch (page)
            {
                case SettingsPage.Users: await Rbac!.InitializeUsersPageAsync(); break;
                case SettingsPage.Roles: await Rbac!.InitializeRolesPageAsync(); break;
                case SettingsPage.Permissions: await Rbac!.InitializePermissionsPageAsync(); break;
                case SettingsPage.Dictionary: await Dictionary!.InitializeAsync(); break;
            }
            if (!IsDisposed && CanRead(page)) PresentationChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _navigating = false;
            if (_pendingPermissions && !IsDisposed) { _pendingPermissions = false; await RefreshPermissionsAsync(); }
        }
    }
    /// <inheritdoc />
    public async Task<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed) return true;
        if (_checkingClose || HasActiveForm || Rbac?.IsWriting == true || Dictionary?.IsWriting == true) return false;
        _checkingClose = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CommitEdits?.Invoke() == false) return false;
            if (Rbac is not null && !await Rbac.CanCloseAsync()) return false;
            return Dictionary is null || await Dictionary.CanCloseAsync(cancellationToken);
        }
        finally { _checkingClose = false; }
    }
    private void OnChildChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(HasUnsavedChanges));
    /// <summary>将跨线程身份通知送回创建线程，避免 WPF 上下文副本引起重复投递。</summary>
    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        if (_context is not null && Environment.CurrentManagedThreadId != _ownerThreadId)
        { _context.Post(_ => OnCurrentUserChanged(sender, e), null); return; }
        if (IsDisposed) return;
        if (CurrentUser.CurrentUser?.UserId != _userId || !Enum.GetValues<SettingsPage>().Any(CanRead))
        { Dispose(); return; }
        // 等子模型接收同一权限事件、更新请求版本后再发起新页面读取。
        if (_context is not null) _context.Post(async _ => await RefreshPermissionsAsync(), null);
        else _ = RefreshPermissionsAsync();
    }
    /// <summary>撤权立即清理表单和不可读页面；加载期间延后选择新页。</summary>
    private async Task RefreshPermissionsAsync()
    {
        if (IsDisposed) return;
        if (CurrentUser.CurrentUser?.UserId != _userId || !Enum.GetValues<SettingsPage>().Any(CanRead)) { Dispose(); return; }
        if (!CurrentUser.IsAdmin || (CurrentPage is { } active && !CanRead(active))) FormsInvalidated?.Invoke(this, EventArgs.Empty);
        if (!CanRead(SettingsPage.Dictionary) && Dictionary is not null)
        { Dictionary.PropertyChanged -= OnChildChanged; Dictionary.Dispose(); Dictionary = null; }
        if (CurrentPage is { } page && !CanRead(page)) CurrentPage = null;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
        if (CurrentPage is not null) return;
        if (_navigating) { _pendingPermissions = true; return; }
        await NavigateFirstAsync();
    }
    /// <summary>真正关闭页签或会话结束时释放模型；视觉卸载不销毁草稿。</summary>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        FormsInvalidated?.Invoke(this, EventArgs.Empty);
        CurrentUser.CurrentUserChanged -= OnCurrentUserChanged;
        if (Rbac is not null) { Rbac.PropertyChanged -= OnChildChanged; Rbac.Dispose(); }
        if (Dictionary is not null) { Dictionary.PropertyChanged -= OnChildChanged; Dictionary.Dispose(); }
        CommitEdits = null;
        Disposed?.Invoke(this, EventArgs.Empty);
    }
}

