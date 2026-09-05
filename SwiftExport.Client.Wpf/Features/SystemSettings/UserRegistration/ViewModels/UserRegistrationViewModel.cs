using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;

/// <summary>创建普通用户；成功后明确提示新用户默认没有模块权限。</summary>
public sealed partial class UserRegistrationViewModel : ViewModelBaseEx
{
    private readonly IAuthApi _authApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;

    /// <summary>创建普通用户注册用例。</summary>
    public UserRegistrationViewModel(
        IAuthApi authApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService)
    {
        _authApi = authApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        Title = "创建用户";
        RegisterBusyAwareCommand(SubmitCommand);
    }

    /// <summary>请求窗口清除密码并关闭。</summary>
    public event EventHandler? RequestClose;

    /// <summary>请求窗口聚焦指定输入项。</summary>
    public event Action<string>? FocusRequested;

    /// <summary>获取或设置待创建的用户名。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string UserName { get; set; } = string.Empty;

    /// <summary>获取或设置待创建用户的显示名称。</summary>
    [ObservableProperty]
    public partial string NickName { get; set; } = string.Empty;

    /// <summary>获取或设置待创建用户的邮箱。</summary>
    [ObservableProperty]
    public partial string Email { get; set; } = string.Empty;

    /// <summary>获取或设置待创建用户的电话号码。</summary>
    [ObservableProperty]
    public partial string PhoneNumber { get; set; } = string.Empty;

    /// <summary>获取当前操作提示或错误消息。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "新用户创建后默认没有模块权限。";

    /// <summary>指示当前状态消息是否为错误。</summary>
    [ObservableProperty]
    public partial bool IsError { get; private set; }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(UserRegistrationPasswordInput? passwordInput)
    {
        if (!TryValidate(passwordInput))
        {
            return;
        }

        var succeeded = false;
        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _authApi.RegisterAsync(new RegisterRequestDto
                {
                    UserName = UserName.Trim(),
                    NickName = NickName.Trim(),
                    Email = Email.Trim(),
                    PhoneNumber = PhoneNumber.Trim(),
                    Password = passwordInput!.Password,
                    ConfirmPassword = passwordInput.ConfirmPassword
                }),
                "创建用户失败");
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            succeeded = true;
            const string successMessage = "用户已创建，默认没有模块权限，请继续在“模块权限”中分配。";
            SetStatus(successMessage, isError: false);
            _messageService.ShowSuccess("用户已创建", successMessage);
        }, "正在创建用户...");

        if (succeeded)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanSubmit(UserRegistrationPasswordInput? input)
        => IsNotBusy && input is not null;

    private bool TryValidate(UserRegistrationPasswordInput? input)
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            SetValidationError("请输入用户名。", "UserName");
            return false;
        }

        if (string.IsNullOrEmpty(input?.Password) || input.Password.Length < 6)
        {
            SetValidationError("密码至少需要 6 个字符。", "Password");
            return false;
        }

        if (!string.Equals(input.Password, input.ConfirmPassword, StringComparison.Ordinal))
        {
            SetValidationError("两次输入的密码不一致。", "ConfirmPassword");
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
