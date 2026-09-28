using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.ViewModels;

/// <summary>独占用户角色完整集合、目标版本与保存生命周期。</summary>
public sealed partial class UserRolesFormViewModel : ViewModelBaseEx, IDisposable
{
    private readonly ISystemRbacApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IAuthService _auth;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private int _version;

    /// <summary>关闭、放弃与强制失效状态。</summary>
    public SettingsFormState CloseState { get; }
    /// <summary>当前目标用户标识。</summary>
    public Guid TargetId { get; private set; }
    /// <summary>服务端已确认整组替换时为真。</summary>
    public bool Saved { get; private set; }
    /// <summary>目标用户名，只读展示。</summary>
    [ObservableProperty] public partial string UserName { get; private set; } = string.Empty;
    /// <summary>服务端全部可选角色，保存不依赖视觉筛选。</summary>
    public ObservableCollection<RbacChoiceModel> UserRoleChoices { get; } = [];
    /// <summary>当前有效权限键，只读参考。</summary>
    public ObservableCollection<string> EffectivePermissions { get; } = [];
    /// <summary>读取、保存和冲突反馈。</summary>
    [ObservableProperty] public partial string StatusMessage { get; private set; } = string.Empty;

    /// <summary>创建独立用户角色表单，不复用列表选中对象的编辑集合。</summary>
    public UserRolesFormViewModel(ISystemRbacApi api, IApiCallRunner runner, IAuthService auth)
    {
        _api = api; _runner = runner; _auth = auth;
        CloseState = new(() => IsDirty, () => IsBusy || SaveCommand.IsRunning, () => IsDirty = false);
        RegisterBusyAwareCommand(SaveCommand);
        PropertyChanged += (_, _) => CloseState.NotifyStateChanged();
    }

    /// <summary>读取目标的权威版本、完整角色目录和有效权限；失效后不接纳迟到结果。</summary>
    public async Task<bool> InitializeAsync(ManagedUserDto target)
    {
        ArgumentNullException.ThrowIfNull(target);
        TargetId = target.Uid; UserName = target.UserName;
        var token = _lifetime.Token;
        var relation = await _runner.RunAsync(() => _api.GetUserRolesAsync(target.Uid).WaitAsync(token), token,
            "读取用户角色失败");
        if (_disposed || CloseState.WasForced || token.IsCancellationRequested) return false;
        if (!relation.Success || relation.Data is null) { StatusMessage = relation.Message; return false; }
        var roles = new List<ManagedRoleDto>();
        for (var page = 1; ; page++)
        {
            var response = await _runner.RunAsync(() => _api.SearchRolesAsync(new ManagedPageRequest
                { PageIndex = page, PageSize = 100 }).WaitAsync(token), token, "读取角色候选失败");
            if (_disposed || CloseState.WasForced || token.IsCancellationRequested) return false;
            if (!response.Success || response.Data is null) { StatusMessage = response.Message; return false; }
            roles.AddRange(response.Data.Items);
            if (roles.Count >= response.Data.TotalCount || response.Data.Items.Count == 0) break;
        }
        var effective = await _runner.RunAsync(() => _api.GetEffectivePermissionsAsync(target.Uid).WaitAsync(token), token,
            "读取有效权限失败");
        if (_disposed || CloseState.WasForced || token.IsCancellationRequested) return false;
        if (!effective.Success) { StatusMessage = effective.Message; return false; }
        _version = relation.Data.Version;
        var selected = relation.Data.RoleIds.ToHashSet();
        // 已绑定但停用的角色仍属于完整关系集，显示并保留它们以免保存时误删。
        foreach (var role in roles.Where(item => item.IsActive || selected.Contains(item.Uid)))
        {
            var choice = new RbacChoiceModel(role.Uid, $"{role.RoleCode} · {role.RoleName}")
                { IsSelected = selected.Contains(role.Uid) };
            choice.PropertyChanged += OnChoiceChanged;
            UserRoleChoices.Add(choice);
        }
        foreach (var key in effective.Data ?? []) EffectivePermissions.Add(key);
        IsDirty = false;
        StatusMessage = $"用户角色已读取，当前有效权限 {EffectivePermissions.Count} 项。";
        return true;
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RbacChoiceModel.IsSelected)) IsDirty = true;
    }
    private bool CanSave() => !_disposed && !CloseState.WasForced && TargetId != Guid.Empty && IsNotBusy;

    /// <summary>始终提交未裁剪的完整角色集合，版本冲突时保留勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        await RunWriteAsync(async () =>
        {
            var request = new ReplaceUserRolesRequest
            {
                ExpectedVersion = _version,
                RoleIds = UserRoleChoices.Where(item => item.IsSelected).Select(item => item.Id).ToList()
            };
            var result = await _runner.RunAsync(() => _api.ReplaceUserRolesAsync(TargetId, request),
                "保存用户角色失败");
            if (_disposed || CloseState.WasForced) return;
            if (!result.Success || result.Data is null)
            {
                StatusMessage = result.Error?.Code == ServiceErrorCodes.VersionConflict
                    ? "数据已被其他管理员修改，请关闭表单后刷新列表再编辑。" : result.Message;
                return;
            }
            _version = result.Data.Version;
            Saved = true; IsDirty = false; StatusMessage = "保存成功。";
            var refreshed = await _auth.RefreshCurrentUserAsync();
            if (!_disposed && !CloseState.WasForced && !refreshed.Success)
                StatusMessage = $"保存已完成；身份刷新失败：{refreshed.Message}";
            if (!_disposed && !CloseState.WasForced) CloseState.Complete();
        }, "正在保存用户角色...");
    }

    /// <summary>取消初始化并解绑选择事件，防止后续编辑污染已结束表单。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel(); _lifetime.Dispose();
        foreach (var choice in UserRoleChoices) choice.PropertyChanged -= OnChoiceChanged;
        UserRoleChoices.Clear(); EffectivePermissions.Clear();
    }
}
