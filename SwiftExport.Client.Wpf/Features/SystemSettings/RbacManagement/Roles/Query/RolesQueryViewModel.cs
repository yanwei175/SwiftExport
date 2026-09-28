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
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Query;

/// <summary>角色列表、选择、启停和删除；角色资料及权限草稿由独立表单持有。</summary>
public sealed partial class RolesQueryViewModel : SettingsQueryViewModel
{
    private readonly ISystemRbacApi _api;
    private readonly IAuthService _auth;
    private readonly ISettingsDialogService _forms;
    private readonly IDialogService _dialogs;
    private bool _loaded;

    /// <inheritdoc />
    protected override string ReadPermission => "System.Roles.View";
    /// <summary>服务端全部角色，包括停用和内置角色。</summary>
    public ObservableCollection<ManagedRoleDto> Roles { get; } = [];
    /// <summary>列表当前行，编辑表单不与其共享草稿。</summary>
    [ObservableProperty] public partial ManagedRoleDto? SelectedRole { get; set; }

    /// <summary>注入角色 API、删除确认和页签专用表单服务。</summary>
    public RolesQueryViewModel(ISystemRbacApi api, IApiCallRunner runner, IAuthService auth,
        ICurrentUserService currentUser, ISettingsDialogService forms, IDialogService dialogs)
        : base(runner, currentUser)
    {
        _api = api; _auth = auth; _forms = forms; _dialogs = dialogs;
        RegisterBusyAwareCommand(RefreshCommand);
        RegisterBusyAwareCommand(SetRoleStatusCommand);
        RegisterBusyAwareCommand(DeleteRoleCommand);
    }

    /// <summary>首次进入才读取角色列表。</summary>
    public Task InitializeAsync() => _loaded ? Task.CompletedTask : RefreshRolesPageAsync();

    /// <summary>重读角色列表并按目标标识定位，读取失败不误报写入失败。</summary>
    public async Task RefreshRolesPageAsync(Guid? roleId = null)
    {
        if (!CanAccept || IsWriting) return;
        var previous = roleId ?? SelectedRole?.Uid;
        await RunBusyAsync(async () =>
        {
            var rows = await ReadPagesAsync(_api.SearchRolesAsync);
            if (rows is null || !CanAccept) return;
            Roles.Clear();
            foreach (var row in rows) Roles.Add(row);
            _loaded = true;
            SelectedRole = Roles.FirstOrDefault(item => item.Uid == previous) ?? Roles.FirstOrDefault();
            StatusMessage = $"已载入 {Roles.Count} 个角色。";
        }, "正在加载角色...", skipIfBusy: false);
    }

    /// <summary>显式刷新角色列表。</summary>
    [RelayCommand]
    private Task RefreshAsync() => RefreshRolesPageAsync();

    private bool CanManage(ManagedRoleDto? target = null) => CanAccept && CurrentUser.IsAdmin
        && !IsBusy && !IsWriting && !_forms.HasActiveForm && (target is null || !target.IsSystem);

    /// <summary>创建成功后按新角色标识读回。</summary>
    [RelayCommand]
    private async Task CreateRoleAsync()
    {
        if (!CanManage()) return;
        var result = await _forms.ShowCreateRoleAsync();
        if (result.Saved && CanAccept) await RefreshRolesPageAsync(result.TargetId);
    }

    /// <summary>资料更新成功后读回最新名称和版本。</summary>
    [RelayCommand]
    private async Task EditProfileAsync(ManagedRoleDto? target)
    {
        if (!CanManage(target) || target is null) return;
        var result = await _forms.ShowRoleProfileAsync(target);
        if (result.Saved && CanAccept) await RefreshRolesPageAsync(result.TargetId);
    }

    /// <summary>权限整组替换成功后读回最新角色版本。</summary>
    [RelayCommand]
    private async Task AssignPermissionsAsync(ManagedRoleDto? target)
    {
        if (!CanManage(target) || target is null) return;
        var result = await _forms.ShowRolePermissionsAsync(target);
        if (result.Saved && CanAccept) await RefreshRolesPageAsync(result.TargetId);
    }

    /// <summary>按当前行版本启停普通角色。</summary>
    [RelayCommand]
    private async Task SetRoleStatusAsync(ManagedRoleDto? target)
    {
        if (!CanManage(target) || target is null) return;
        IsWriting = true;
        try
        {
            var result = await Runner.RunAsync(() => _api.SetRoleStatusAsync(target.Uid,
                new SetManagedStatusRequest { ExpectedVersion = target.Version, IsActive = !target.IsActive }),
                "修改角色状态失败");
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
            await RefreshRolesPageAsync(target.Uid);
            if (!refreshed.Success) StatusMessage = $"状态已修改；身份刷新失败：{refreshed.Message}";
        }
        finally { IsWriting = false; }
    }

    /// <summary>确认后删除无关联普通角色，失败保留服务端业务原因。</summary>
    [RelayCommand]
    private async Task DeleteRoleAsync(ManagedRoleDto? target)
    {
        if (!CanManage(target) || target is null) return;
        if (!await _dialogs.ConfirmAsync("删除角色", $"确定删除 {target.RoleName}？", "删除")) return;
        if (!CanManage(target)) return;
        IsWriting = true;
        try
        {
            var result = await Runner.RunAsync(() => _api.DeleteRoleAsync(target.Uid, target.Version), "删除角色失败");
            if (!CanAccept) return;
            if (!result.Success || result.Data != true)
            {
                StatusMessage = result.Error?.Code == ServiceErrorCodes.VersionConflict
                    ? "数据已被其他管理员修改，请刷新列表。" : result.Message;
                return;
            }
            var refreshed = await _auth.RefreshCurrentUserAsync();
            if (!CanAccept) return;
            IsWriting = false;
            SelectedRole = null;
            await RefreshRolesPageAsync();
            if (!refreshed.Success) StatusMessage = $"角色已删除；身份刷新失败：{refreshed.Message}";
        }
        finally { IsWriting = false; }
    }

    /// <inheritdoc />
    protected override void ClearData()
    {
        _loaded = false; Roles.Clear(); SelectedRole = null;
    }
}
