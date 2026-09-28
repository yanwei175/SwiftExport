using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.ViewModels;

/// <summary>独占指定用户的资料草稿、版本和保存结果，不修改列表行对象。</summary>
public sealed partial class UserProfileFormViewModel : ViewModelBaseEx, IDisposable
{
    private readonly ISystemRbacApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IAuthService _auth;
    private bool _disposed;
    private bool _loading;
    private int _version;

    /// <summary>本次表单关闭保护与放弃意图。</summary>
    public SettingsFormState CloseState { get; }
    /// <summary>本次目标用户标识。</summary>
    public Guid TargetId { get; private set; }
    /// <summary>仅在服务端确认写入后为真。</summary>
    public bool Saved { get; private set; }
    /// <summary>用户名只读展示。</summary>
    [ObservableProperty] public partial string UserName { get; private set; } = string.Empty;
    /// <summary>可编辑显示名称。</summary>
    [ObservableProperty] public partial string UserNickName { get; set; } = string.Empty;
    /// <summary>可编辑邮箱。</summary>
    [ObservableProperty] public partial string UserEmail { get; set; } = string.Empty;
    /// <summary>可编辑电话。</summary>
    [ObservableProperty] public partial string UserPhone { get; set; } = string.Empty;
    /// <summary>保存和并发冲突提示。</summary>
    [ObservableProperty] public partial string StatusMessage { get; private set; } = string.Empty;

    /// <summary>注入当前用例的 API、错误归一化及本人权限刷新能力。</summary>
    public UserProfileFormViewModel(ISystemRbacApi api, IApiCallRunner runner, IAuthService auth)
    {
        _api = api; _runner = runner; _auth = auth;
        CloseState = new(() => IsDirty, () => IsBusy || SaveCommand.IsRunning,
            () => { IsDirty = false; });
        RegisterBusyAwareCommand(SaveCommand);
        PropertyChanged += (_, _) => CloseState.NotifyStateChanged();
    }

    /// <summary>以目标行快照建立独立资料草稿和预期版本。</summary>
    public void Initialize(ManagedUserDto target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _loading = true;
        try
        {
            TargetId = target.Uid; _version = target.Version;
            UserName = target.UserName;
            UserNickName = target.NickName ?? string.Empty;
            UserEmail = target.Email ?? string.Empty;
            UserPhone = target.PhoneNumber ?? string.Empty;
            IsDirty = false; Saved = false;
        }
        finally { _loading = false; }
    }

    private bool CanSave() => !_disposed && !CloseState.WasForced && TargetId != Guid.Empty && IsNotBusy;

    /// <summary>以目标版本保存资料；失败保留全部输入供用户核对。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        await RunWriteAsync(async () =>
        {
            var request = new UpdateManagedUserRequest
            {
                ExpectedVersion = _version, NickName = UserNickName,
                Email = UserEmail, PhoneNumber = UserPhone
            };
            var result = await _runner.RunAsync(() => _api.UpdateUserAsync(TargetId, request), "保存用户资料失败");
            if (_disposed || CloseState.WasForced) return;
            if (!result.Success || result.Data is null)
            {
                StatusMessage = result.Error?.Code == ServiceErrorCodes.VersionConflict
                    ? "数据已被其他管理员修改，请关闭表单后刷新列表再编辑。" : result.Message;
                return;
            }
            _version = result.Data.Version;
            Saved = true; IsDirty = false;
            StatusMessage = "保存成功。";
            var refreshed = await _auth.RefreshCurrentUserAsync();
            if (!_disposed && !CloseState.WasForced && !refreshed.Success)
                StatusMessage = $"保存已完成；身份刷新失败：{refreshed.Message}";
            if (!_disposed && !CloseState.WasForced) CloseState.Complete();
        }, "正在保存用户资料...");
    }

    partial void OnUserNickNameChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnUserEmailChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnUserPhoneChanged(string value) { if (!_loading) IsDirty = true; }

    /// <summary>阻止强制关闭后迟到的写入结果更新表单。</summary>
    public void Dispose() => _disposed = true;
}
