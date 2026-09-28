using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;

/// <summary>创建普通用户；成功后提示需显式分配角色才能获得业务权限。</summary>
public sealed partial class UserRegistrationViewModel : ViewModelBaseEx, IDisposable
{
    private readonly IAuthApi _authApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private bool _isDisposed;

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
        CloseState = new(() => HasInputDraft?.Invoke() == true || !string.IsNullOrEmpty(UserName) || !string.IsNullOrEmpty(NickName) || !string.IsNullOrEmpty(Email) || !string.IsNullOrEmpty(PhoneNumber),
            () => IsBusy || SaveCommand.IsRunning, () => ClearInput?.Invoke());
        RegisterBusyAwareCommand(SaveCommand);
        PropertyChanged += (_, _) => CloseState.NotifyStateChanged();
    }

    /// <summary>关闭意图与草稿保护，供 Dialog 或其他宿主复用。</summary>
    public SettingsFormState CloseState { get; }
    /// <summary>视图在提交时提供一次性密码，不把明文保存在绑定属性中。</summary>
    public Func<UserRegistrationPasswordInput>? ReadInput { get; set; }
    /// <summary>读取 PasswordBox 是否存在待放弃输入。</summary>
    public Func<bool>? HasInputDraft { get; set; }
    /// <summary>视图提供的敏感输入清理动作，释放时解绑。</summary>
    public Action? ClearInput { get; set; }
    private bool CanSave() => !_isDisposed && !CloseState.WasForced && IsNotBusy && ReadInput is not null;
    /// <summary>主按钮获取一次性输入，再复用原有验证和提交命令。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (ReadInput is not null) await SubmitCommand.ExecuteAsync(ReadInput());
    }

    /// <summary>服务端确认创建后的用户标识，取消或失败时为空。</summary>
    public Guid? CreatedUserId { get; private set; }
    /// <summary>服务端确认创建后的用户名，不依赖释放时会擦除的输入字段。</summary>
    public string? CreatedUserName { get; private set; }

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
    public partial string StatusMessage { get; private set; } = "新用户创建后默认没有角色与业务权限。";

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
            // 宿主已因撤权或会话切换关闭时，迟到的响应不能再写回状态或提示成功。
            if (_isDisposed) return;
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            CreatedUserId = result.Data;
            CreatedUserName = UserName;
            succeeded = true;
            const string successMessage = "用户已创建，默认没有角色与业务权限，请继续在“用户、角色与权限”中分配角色。";
            SetStatus(successMessage, isError: false);
            _messageService.ShowSuccess("用户已创建", successMessage);
        }, "正在创建用户...");

        if (succeeded && !_isDisposed)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            CloseState.Complete();
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

    /// <summary>结束一次性创建表单生命周期，阻止迟到的请求结果操作已关闭页面。</summary>
    public void Dispose()
    {
        _isDisposed = true;
        ReadInput = null; HasInputDraft = null; ClearInput = null;
    }
}
