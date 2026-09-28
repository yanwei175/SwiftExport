using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Models;
using SwiftExportClient.Features.SystemSettings.Services;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Query;

/// <summary>用户列表、选择和启停命令；表单草稿由 Dialog 用例独立创建。</summary>
public sealed partial class UsersQueryViewModel : SettingsQueryViewModel
{
    private readonly ISystemRbacApi _api;
    private readonly IAuthService _auth;
    private readonly ISettingsDialogService _forms;
    private bool _loaded;

    /// <inheritdoc />
    protected override string ReadPermission => "System.Users.View";
    /// <summary>服务端已载入用户，包括停用账号。</summary>
    public ObservableCollection<ManagedUserDto> Users { get; } = [];
    /// <summary>列表当前行；表单持有自身目标快照。</summary>
    [ObservableProperty] public partial ManagedUserDto? SelectedUser { get; set; }

    /// <summary>注入用户 API、当前会话与页签本地表单服务。</summary>
    public UsersQueryViewModel(ISystemRbacApi api, IApiCallRunner runner, IAuthService auth,
        ICurrentUserService currentUser, ISettingsDialogService forms) : base(runner, currentUser)
    {
        _api = api; _auth = auth; _forms = forms;
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(SetUserStatusCommand);
    }

    /// <summary>首次进入时读取用户列表；重复呈现不会覆盖选择。</summary>
    public Task InitializeAsync() => _loaded ? Task.CompletedTask : RefreshUsersPageAsync();

    /// <summary>显式刷新或写入成功后重读并按身份或用户名定位。</summary>
    public async Task RefreshUsersPageAsync(string? userName = null, Guid? userId = null)
    {
        if (!CanAccept || IsWriting) return;
        var previous = userId ?? SelectedUser?.Uid;
        await RunBusyAsync(async () =>
        {
            var rows = await ReadPagesAsync(_api.SearchUsersAsync);
            if (rows is null || !CanAccept) return;
            Users.Clear();
            foreach (var row in rows) Users.Add(row);
            _loaded = true;
            SelectedUser = Users.FirstOrDefault(item => item.Uid == previous)
                ?? Users.FirstOrDefault(item => string.Equals(item.UserName, userName,
                    StringComparison.OrdinalIgnoreCase)) ?? Users.FirstOrDefault();
            StatusMessage = $"已载入 {Users.Count} 位用户。";
        }, "正在加载用户...", skipIfBusy: false);
    }

    /// <summary>用户显式刷新命令。</summary>
    [RelayCommand]
    private Task RefreshAsync() => RefreshUsersPageAsync();

    private bool CanManage(ManagedUserDto? target = null) => CanAccept && CurrentUser.IsAdmin
        && !IsBusy && !IsWriting && !_forms.HasActiveForm && (target is null || target.Uid != Guid.Empty);

    /// <summary>创建成功后才重读列表并定位用户名。</summary>
    [RelayCommand]
    private async Task CreateUserAsync()
    {
        if (!CanManage()) return;
        var result = await _forms.ShowCreateUserAsync();
        if (result.Saved && CanAccept) await RefreshUsersPageAsync(result.CreatedUserName, result.TargetId);
    }

    /// <summary>资料保存成功后重读，失败和取消保留列表原状态。</summary>
    [RelayCommand]
    private async Task EditProfileAsync(ManagedUserDto? target)
    {
        if (!CanManage(target)) return;
        var result = await _forms.ShowUserProfileAsync(target!);
        if (result.Saved && CanAccept) await RefreshUsersPageAsync(target!.UserName, result.TargetId);
    }

    /// <summary>角色关系保存成功后重读用户版本。</summary>
    [RelayCommand]
    private async Task AssignRolesAsync(ManagedUserDto? target)
    {
        if (!CanManage(target)) return;
        var result = await _forms.ShowUserRolesAsync(target!);
        if (result.Saved && CanAccept) await RefreshUsersPageAsync(target!.UserName, result.TargetId);
    }

    /// <summary>重置指定用户密码，管理员目标由表单继续校验。</summary>
    [RelayCommand]
    private Task ResetPasswordAsync(ManagedUserDto? target)
        => CanManage(target) ? _forms.ShowResetPasswordAsync(target!) : Task.CompletedTask;

    /// <summary>按行版本启停用户，后端负责最后管理员保护。</summary>
    [RelayCommand]
    private async Task SetUserStatusAsync(ManagedUserDto? target)
    {
        if (!CanManage(target)) return;
        IsWriting = true;
        try
        {
            var result = await Runner.RunAsync(() => _api.SetUserStatusAsync(target!.Uid,
                new SetManagedStatusRequest { ExpectedVersion = target.Version, IsActive = !target.IsActive }),
                "修改用户状态失败");
            if (!CanAccept) return;
            if (!result.Success || result.Data is null)
            {
                StatusMessage = result.Error?.Code == ServiceErrorCodes.VersionConflict
                    ? "数据已被其他管理员修改，请刷新列表。" : result.Message;
                return;
            }
            var refreshed = await _auth.RefreshCurrentUserAsync();
            if (!CanAccept) return;
            IsWriting = false;
            await RefreshUsersPageAsync(target!.UserName, target.Uid);
            if (!refreshed.Success) StatusMessage = $"状态已修改；身份刷新失败：{refreshed.Message}";
        }
        finally { IsWriting = false; }
    }

    /// <inheritdoc />
    protected override void ClearData()
    {
        _loaded = false; Users.Clear(); SelectedUser = null;
    }
}
