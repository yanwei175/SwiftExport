using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.SystemSettings.Passwords.Change.ViewModels;

/// <summary>验证并提交当前用户密码修改；成功后清除敏感输入并结束旧凭据会话。</summary>
public sealed partial class ChangePasswordViewModel : ViewModelBaseEx, IDisposable
{
    private bool _disposed;
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
        CloseState = new(() => HasInputDraft?.Invoke() == true,
            () => IsBusy || SaveCommand.IsRunning, () => ClearInput?.Invoke());
        RegisterBusyAwareCommand(SaveCommand);
        PropertyChanged += (_, _) => CloseState.NotifyStateChanged();
    }

    /// <summary>由宿主显示草稿确认并响应关闭意图。</summary>
    public SettingsFormState CloseState { get; }
    /// <summary>仅在提交时读取 PasswordBox，不保存绑定密码。</summary>
    public Func<ChangePasswordInput>? ReadInput { get; set; }
    /// <summary>读取当前视图是否包含密码草稿。</summary>
    public Func<bool>? HasInputDraft { get; set; }
    /// <summary>关闭时清理视图密码，释放时解绑。</summary>
    public Action? ClearInput { get; set; }
    private bool CanSave() => !_disposed && !CloseState.WasForced && IsNotBusy && ReadInput is not null;
    /// <summary>公共主按钮读取一次性密码并复用现有提交验证。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (ReadInput is not null) await SubmitCommand.ExecuteAsync(ReadInput());
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
        if (_disposed || !TryValidate(input))
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
            // 强制关闭信号可能早于 Dialog 的异步释放，仍须忽略旧会话响应。
            if (_disposed || CloseState.WasForced) return;
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            succeeded = true;
            SetStatus("密码已修改，正在返回登录窗口。", isError: false);
        }, "正在修改密码...");

        if (succeeded && !_disposed && !CloseState.WasForced)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            CloseState.Complete();
            _sessionService.EndSession(SessionEndReason.CredentialsChanged);
        }
    }

    private bool CanSubmit(ChangePasswordInput? input) => !_disposed && IsNotBusy && input is not null;

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
    /// <summary>关闭后禁止迟到响应结束新会话，并解除密码读取回调。</summary>
    public void Dispose()
    {
        _disposed = true;
        ClearInput?.Invoke();
        ReadInput = null;
        HasInputDraft = null;
        ClearInput = null;
    }
}
