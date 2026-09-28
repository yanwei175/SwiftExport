using SwiftExportClient.Navigation;
using SwiftExportClient.Features.SystemSettings.Workspace.ViewModels;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;

/// <summary>保留个人设置，并按当前 me 权限展示唯一系统设置入口。</summary>
public sealed partial class SettingsCenterViewModel : ViewModelBaseEx, IDisposable
{
    /// <summary>设置中心唯一页签路由键。</summary>
    public const string RouteKey = "settings-center";

    private readonly ISettingsWindowService _windowService;
    private readonly INavigationService _navigation;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuthService? _authService;

    /// <summary>创建设置中心，并从当前 me 权限快照确定管理入口可见性。</summary>
    public SettingsCenterViewModel(
        ICurrentUserService currentUserService,
        ISettingsWindowService windowService,
        INavigationService navigation,
        IAuthService? authService = null)
    {
        _windowService = windowService;
        _navigation = navigation;
        _currentUser = currentUserService;
        _authService = authService;
        Title = "设置";
        currentUserService.CurrentUserChanged += OnCurrentUserChanged;
        NotifyPermissionsChanged();
        RegisterBusyAwareCommand(RefreshPermissionsCommand);
    }

    /// <summary>指示当前会话是否有任一系统管理入口。</summary>
    public bool CanManageSettings => _currentUser.HasPermission("System.Users.View")
        || _currentUser.HasPermission("System.Roles.View")
        || _currentUser.HasPermission("System.Permissions.View")
        || _currentUser.HasPermission("System.Dictionary.View");

    /// <summary>用户主动刷新权限后的结果提示。</summary>
    [ObservableProperty]
    public partial string RefreshStatus { get; private set; } = string.Empty;

    [RelayCommand]
    private void ChangePassword() => _windowService.ShowChangePassword();

    /// <summary>从服务端重新读取 me，403 后可用本入口更新当前权限快照。</summary>
    [RelayCommand]
    private async Task RefreshPermissionsAsync()
    {
        if (_authService is null) return;
        await RunBusyAsync(async () =>
        {
            var result = await _authService.RefreshCurrentUserAsync();
            RefreshStatus = result.Success ? "权限已从服务器刷新。" : result.Message;
        }, "正在刷新权限...");
    }

    /// <summary>在 Shell 打开或激活唯一系统设置页签。</summary>
    [RelayCommand(CanExecute = nameof(CanManageSettings))]
    private Task OpenSystemSettingsAsync() => _navigation.OpenAsync(SystemSettingsWorkspaceViewModel.RouteKey);

    private void OnCurrentUserChanged(object? sender, EventArgs e) => NotifyPermissionsChanged();

    /// <summary>服务端 me 变化后重算入口可见性和命令可用性。</summary>
    private void NotifyPermissionsChanged()
    {
        OnPropertyChanged(nameof(CanManageSettings));
        OpenSystemSettingsCommand.NotifyCanExecuteChanged();
    }

    /// <summary>页签释放时解除会话变化订阅。</summary>
    public void Dispose() => _currentUser.CurrentUserChanged -= OnCurrentUserChanged;
}
