using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Passwords.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;

/// <summary>验证并提交当前用户密码修改；成功后清除敏感输入并结束旧凭据会话。</summary>
public sealed partial class ChangePasswordViewModel : ViewModelBaseEx
{
    private readonly IAuthApi _authApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly ISessionService _sessionService;

    /// <summary>创建本人改密用例，并在成功后结束持有旧凭据的当前会话。</summary>
    public ChangePasswordViewModel(
        IAuthApi authApi,
        IApiCallRunner apiCallRunner,
        ISessionService sessionService)
    {
        _authApi = authApi;
        _apiCallRunner = apiCallRunner;
        _sessionService = sessionService;
        Title = "修改我的密码";
        RegisterBusyAwareCommand(SubmitCommand);
    }

    /// <summary>请求窗口清空 PasswordBox 并关闭。</summary>
    public event EventHandler? RequestClose;

    /// <summary>请求窗口把焦点移动到指定输入项。</summary>
    public event Action<string>? FocusRequested;

    /// <summary>获取当前操作提示或错误消息。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "修改成功后需要重新登录。";

    /// <summary>指示当前状态消息是否为错误。</summary>
    [ObservableProperty]
    public partial bool IsError { get; private set; }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(ChangePasswordInput? input)
    {
        if (!TryValidate(input))
        {
            return;
        }

        var succeeded = false;
        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _authApi.ChangePasswordAsync(new ChangePasswordRequestDto
                {
                    CurrentPassword = input!.CurrentPassword,
                    NewPassword = input.NewPassword,
                    ConfirmPassword = input.ConfirmPassword
                }),
                "修改密码失败");
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            succeeded = true;
            SetStatus("密码已修改，正在返回登录窗口。", isError: false);
        }, "正在修改密码...");

        if (succeeded)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            _sessionService.EndSession(SessionEndReason.CredentialsChanged);
        }
    }

    private bool CanSubmit(ChangePasswordInput? input) => IsNotBusy && input is not null;

    private bool TryValidate(ChangePasswordInput? input)
    {
        if (string.IsNullOrEmpty(input?.CurrentPassword))
        {
            SetValidationError("请输入当前密码。", "CurrentPassword");
            return false;
        }

        if (string.IsNullOrEmpty(input.NewPassword) || input.NewPassword.Length < 6)
        {
            SetValidationError("新密码至少需要 6 个字符。", "NewPassword");
            return false;
        }

        if (!string.Equals(input.NewPassword, input.ConfirmPassword, StringComparison.Ordinal))
        {
            SetValidationError("两次输入的新密码不一致。", "ConfirmPassword");
            return false;
        }

        return true;
    }

    private void SetValidationError(string message, string field)
    {
        SetStatus(message, isError: true);
        FocusRequested?.Invoke(field);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsError = isError;
        ValidationSummary = isError ? message : null;
    }
}
