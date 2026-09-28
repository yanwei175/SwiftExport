using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.Profile.ViewModels;

/// <summary>独占新建或编辑角色资料草稿，编码仅允许在新建时输入。</summary>
public sealed partial class RoleProfileFormViewModel : ViewModelBaseEx, IDisposable
{
    private readonly ISystemRbacApi _api;
    private readonly IApiCallRunner _runner;
    private readonly IAuthService _auth;
    private bool _disposed;
    private bool _loading;
    private int _version;

    /// <summary>本表单的放弃与关闭状态。</summary>
    public SettingsFormState CloseState { get; }
    /// <summary>新建时为真；更新时版本和身份均由目标快照提供。</summary>
    public bool IsCreating { get; private set; }
    /// <summary>编辑模式的反向展示属性。</summary>
    public bool IsEditing => !IsCreating;
    /// <summary>成功创建或编辑后的角色标识。</summary>
    public Guid TargetId { get; private set; }
    /// <summary>服务端确认写入时为真。</summary>
    public bool Saved { get; private set; }
    /// <summary>创建时输入、编辑时只读的稳定编码。</summary>
    [ObservableProperty] public partial string RoleCode { get; set; } = string.Empty;
    /// <summary>可编辑角色名称。</summary>
    [ObservableProperty] public partial string RoleName { get; set; } = string.Empty;
    /// <summary>可编辑角色说明。</summary>
    [ObservableProperty] public partial string RoleDescription { get; set; } = string.Empty;
    /// <summary>验证与并发冲突反馈。</summary>
    [ObservableProperty] public partial string StatusMessage { get; private set; } = string.Empty;

    /// <summary>创建独立角色资料表单。</summary>
    public RoleProfileFormViewModel(ISystemRbacApi api, IApiCallRunner runner, IAuthService auth)
    {
        _api = api; _runner = runner; _auth = auth;
        CloseState = new(() => IsDirty, () => IsBusy || SaveCommand.IsRunning, () => IsDirty = false);
        RegisterBusyAwareCommand(SaveCommand);
        PropertyChanged += (_, _) => CloseState.NotifyStateChanged();
    }

    /// <summary>初始化新建草稿，不沿用先前表单输入。</summary>
    public void InitializeCreate()
    {
        _loading = true;
        try
        {
            IsCreating = true; TargetId = Guid.Empty; _version = 0;
            RoleCode = RoleName = RoleDescription = string.Empty;
            IsDirty = false; Saved = false;
            OnPropertyChanged(nameof(IsCreating)); OnPropertyChanged(nameof(IsEditing));
        }
        finally { _loading = false; }
    }

    /// <summary>以角色行身份和版本初始化编辑草稿，系统角色不能进入该模式。</summary>
    public void InitializeEdit(ManagedRoleDto target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsSystem) throw new InvalidOperationException("内置角色不能编辑资料。");
        _loading = true;
        try
        {
            IsCreating = false; TargetId = target.Uid; _version = target.Version;
            RoleCode = target.RoleCode; RoleName = target.RoleName; RoleDescription = target.Description ?? string.Empty;
            IsDirty = false; Saved = false;
            OnPropertyChanged(nameof(IsCreating)); OnPropertyChanged(nameof(IsEditing));
        }
        finally { _loading = false; }
    }

    private bool CanSave() => !_disposed && !CloseState.WasForced && IsNotBusy;

    /// <summary>分别提交创建或版本化资料更新，失败保留同一表单的输入。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        if (string.IsNullOrWhiteSpace(RoleName) || (IsCreating && string.IsNullOrWhiteSpace(RoleCode)))
        {
            StatusMessage = "请输入角色编码和名称。";
            return;
        }
        await RunWriteAsync(async () =>
        {
            if (IsCreating)
            {
                var created = await _runner.RunAsync(() => _api.CreateRoleAsync(new CreateManagedRoleRequest
                { RoleCode = RoleCode, RoleName = RoleName, Description = RoleDescription }), "创建角色失败");
                if (_disposed || CloseState.WasForced) return;
                if (!created.Success || created.Data is null) { StatusMessage = created.Message; return; }
                TargetId = created.Data.Uid; _version = created.Data.Version;
            }
            else
            {
                var updated = await _runner.RunAsync(() => _api.UpdateRoleAsync(TargetId,
                    new UpdateManagedRoleRequest { ExpectedVersion = _version, RoleName = RoleName,
                        Description = RoleDescription }), "保存角色资料失败");
                if (_disposed || CloseState.WasForced) return;
                if (!updated.Success || updated.Data is null)
                {
                    StatusMessage = updated.Error?.Code == ServiceErrorCodes.VersionConflict
                        ? "数据已被其他管理员修改，请关闭表单后刷新列表再编辑。" : updated.Message;
                    return;
                }
                _version = updated.Data.Version;
            }
            Saved = true; IsDirty = false; StatusMessage = "保存成功。";
            var refreshed = await _auth.RefreshCurrentUserAsync();
            if (!_disposed && !CloseState.WasForced && !refreshed.Success)
                StatusMessage = $"保存已完成；身份刷新失败：{refreshed.Message}";
            if (!_disposed && !CloseState.WasForced) CloseState.Complete();
        }, IsCreating ? "正在创建角色..." : "正在保存角色资料...");
    }

    partial void OnRoleCodeChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnRoleNameChanged(string value) { if (!_loading) IsDirty = true; }
    partial void OnRoleDescriptionChanged(string value) { if (!_loading) IsDirty = true; }

    /// <summary>令迟到结果失效。</summary>
    public void Dispose() => _disposed = true;
}
