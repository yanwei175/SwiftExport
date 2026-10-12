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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.ViewModels;

/// <summary>独占普通角色的权限完整集合，搜索只影响显示，不裁剪提交数据。</summary>
public sealed partial class RolePermissionsFormViewModel : ViewModelBaseEx, IDisposable
{
    private readonly ISystemRbacApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IAuthService _auth;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private int _version;

    /// <summary>当前表单关闭状态。</summary>
    public SettingsFormState CloseState { get; }
    /// <summary>本次目标普通角色标识。</summary>
    public Guid TargetId { get; private set; }
    /// <summary>服务端确认写入时为真。</summary>
    public bool Saved { get; private set; }
    /// <summary>角色名称只读展示。</summary>
    [ObservableProperty] public partial string RoleName { get; private set; } = string.Empty;
    /// <summary>权限完整集合；保存始终从此集合取值。</summary>
    public ObservableCollection<RbacChoiceModel> RolePermissionChoices { get; } = [];
    /// <summary>仅用于当前表单显示的独立筛选视图。</summary>
    public ListCollectionView Choices { get; }
    /// <summary>显示筛选文字。</summary>
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    /// <summary>读取、保存及版本冲突提示。</summary>
    [ObservableProperty] public partial string StatusMessage { get; private set; } = string.Empty;

    /// <summary>创建独立权限表单和集合筛选视图。</summary>
    public RolePermissionsFormViewModel(ISystemRbacApi api, IApiCallRunner runner, IAuthService auth)
    {
        _api = api; _runner = runner; _auth = auth;
        Choices = new ListCollectionView(RolePermissionChoices);
        CloseState = new(() => IsDirty, () => IsBusy || SaveCommand.IsRunning, () => IsDirty = false);
        RegisterBusyAwareCommand(SaveCommand);
        PropertyChanged += (_, _) => CloseState.NotifyStateChanged();
    }

    /// <summary>读取服务端目标版本和完整目录，系统角色保留只读边界。</summary>
    public async Task<bool> InitializeAsync(ManagedRoleDto target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsSystem) return false;
        TargetId = target.Uid; RoleName = target.RoleName;
        var token = _lifetime.Token;
        var relation = await _runner.RunAsync(() => _api.GetRolePermissionsAsync(target.Uid).WaitAsync(token), token,
            "读取角色权限失败");
        if (_disposed || CloseState.WasForced || token.IsCancellationRequested) return false;
        if (!relation.Success || relation.Data is null) { StatusMessage = relation.Message; return false; }
        var permissions = await _runner.RunAsync(() => _api.GetPermissionsAsync().WaitAsync(token), token,
            "读取权限目录失败");
        if (_disposed || CloseState.WasForced || token.IsCancellationRequested) return false;
        if (!permissions.Success || permissions.Data is null) { StatusMessage = permissions.Message; return false; }
        _version = relation.Data.Version;
        var selected = relation.Data.PermissionIds.ToHashSet();
        foreach (var permission in permissions.Data)
        {
            var choice = new RbacChoiceModel(permission.Uid,
                $"{permission.GroupKey} · {permission.DisplayName} ({permission.PermissionKey})",
                permission.IsAssignable && permission.IsActive)
            { IsSelected = selected.Contains(permission.Uid) };
            choice.PropertyChanged += OnChoiceChanged;
            RolePermissionChoices.Add(choice);
        }
        IsDirty = false; StatusMessage = "角色权限已读取。";
        return true;
    }

    private void OnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RbacChoiceModel.IsSelected)) IsDirty = true;
    }
    partial void OnSearchTextChanged(string value) => Choices.Filter = item =>
        item is RbacChoiceModel choice && (string.IsNullOrWhiteSpace(value)
            || choice.DisplayName.Contains(value, StringComparison.OrdinalIgnoreCase));
    private bool CanSave() => !_disposed && !CloseState.WasForced && TargetId != Guid.Empty && IsNotBusy;

    /// <summary>按目标版本提交未筛选的全部可分配权限，失败保留勾选。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        await RunWriteAsync(async () =>
        {
            var request = new ReplaceRolePermissionsRequest
            {
                ExpectedVersion = _version,
                PermissionIds = RolePermissionChoices.Where(item => item.IsSelected && item.IsEnabled)
                    .Select(item => item.Id).ToList()
            };
            var result = await _runner.RunAsync(() => _api.ReplaceRolePermissionsAsync(TargetId, request),
                "保存角色权限失败");
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
        }, "正在保存角色权限...");
    }

    /// <summary>取消初始化并解除集合订阅，迟到结果不能重建已关闭的编辑集合。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose();
        foreach (var choice in RolePermissionChoices) choice.PropertyChanged -= OnChoiceChanged;
        RolePermissionChoices.Clear();
    }
}
