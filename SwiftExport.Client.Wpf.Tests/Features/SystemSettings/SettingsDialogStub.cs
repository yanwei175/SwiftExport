using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.Services;

namespace SwiftExport.Client.Wpf.Tests.Features.SystemSettings;

/// <summary>只用于页签导航测试的表单服务替身，不访问任何 WPF 弹窗宿主。</summary>
internal sealed class SettingsDialogStub : ISettingsDialogService
{
    /// <inheritdoc />
    public bool HasActiveForm { get; private set; }
    /// <inheritdoc />
    public event EventHandler? ActiveFormChanged;
    /// <summary>供列表测试注入一次成功返回。</summary>
    public SettingsDialogResult Result { get; set; }
    /// <summary>记录个人改密入口调用。</summary>
    public int ChangePasswordCalls { get; private set; }
    /// <summary>模拟活动表单以核对页签关闭保护。</summary>
    public void SetActive(bool active)
    {
        HasActiveForm = active;
        ActiveFormChanged?.Invoke(this, EventArgs.Empty);
    }
    /// <inheritdoc />
    public Task ShowChangePasswordAsync()
    { ChangePasswordCalls++; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<SettingsDialogResult> ShowCreateUserAsync() => Task.FromResult(Result);
    /// <inheritdoc />
    public Task ShowResetPasswordAsync(ManagedUserDto target) => Task.CompletedTask;
    /// <inheritdoc />
    public Task<SettingsDialogResult> ShowUserProfileAsync(ManagedUserDto target) => Task.FromResult(Result);
    /// <inheritdoc />
    public Task<SettingsDialogResult> ShowUserRolesAsync(ManagedUserDto target) => Task.FromResult(Result);
    /// <inheritdoc />
    public Task<SettingsDialogResult> ShowCreateRoleAsync() => Task.FromResult(Result);
    /// <inheritdoc />
    public Task<SettingsDialogResult> ShowRoleProfileAsync(ManagedRoleDto target) => Task.FromResult(Result);
    /// <inheritdoc />
    public Task<SettingsDialogResult> ShowRolePermissionsAsync(ManagedRoleDto target) => Task.FromResult(Result);
    /// <inheritdoc />
    public void ForceClose() => SetActive(false);
    /// <inheritdoc />
    public void Dispose() => SetActive(false);
}
