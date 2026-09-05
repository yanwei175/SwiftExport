using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.SettingsCenter.ViewModels;

/// <summary>提供已登录用户的个人设置入口，并按管理能力展示管理员工具。</summary>
public sealed partial class SettingsCenterViewModel : ViewModelBaseEx
{
    /// <summary>设置中心唯一页签路由键。</summary>
    public const string RouteKey = "settings-center";

    private readonly ISettingsWindowService _windowService;

    /// <summary>创建设置中心，并从当前 me 权限快照确定管理入口可见性。</summary>
    public SettingsCenterViewModel(
        ICurrentUserService currentUserService,
        ISettingsWindowService windowService)
    {
        _windowService = windowService;
        Title = "设置";
        CanManageSettings = currentUserService.HasPermission(PermissionKeys.SystemSettingsManage);
    }

    /// <summary>指示是否显示账号、权限和字典管理入口。</summary>
    public bool CanManageSettings { get; }

    [RelayCommand]
    private void ChangePassword() => _windowService.ShowChangePassword();

    [RelayCommand(CanExecute = nameof(CanOpenManagementWindow))]
    private void RegisterUser() => _windowService.ShowUserRegistration();

    [RelayCommand(CanExecute = nameof(CanOpenManagementWindow))]
    private void ResetPassword() => _windowService.ShowResetPassword();

    [RelayCommand(CanExecute = nameof(CanOpenManagementWindow))]
    private void ManageUserModules() => _windowService.ShowUserModules();

    [RelayCommand(CanExecute = nameof(CanOpenManagementWindow))]
    private void ManageDictionaries() => _windowService.ShowDictionaries();

    private bool CanOpenManagementWindow() => CanManageSettings;
}
