using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Authentication;

/// <summary>验证登录输入并通过认证服务建立当前客户端会话。</summary>
public sealed partial class LoginViewModel : ViewModelBaseEx
{
    private readonly IAuthService _authService;

    /// <summary>创建登录页面状态，并注册 Busy 感知的登录命令。</summary>
    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        Title = "SwiftExport Login";
        RegisterBusyAwareCommand(LoginCommand);
    }

    /// <summary>认证和当前用户加载全部成功后触发。</summary>
    public event EventHandler? LoginSucceeded;

    /// <summary>获取或设置待登录用户名。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial string UserName { get; set; } = string.Empty;

    /// <summary>获取当前登录提示或错误文本。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "请输入账号密码。";

    /// <summary>指示当前状态文本是否为错误。</summary>
    [ObservableProperty]
    public partial bool IsError { get; private set; }

    private bool CanLogin(string? password)
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(UserName);
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(string? password)
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            SetStatus("请输入用户名。", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetStatus("请输入密码。", isError: true);
            return;
        }

        await RunBusyAsync(async () =>
        {
            SetStatus("登录中...", isError: false);

            var result = await _authService.LoginAsync(UserName, password);
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            SetStatus("登录成功。", isError: false);
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        }, "正在登录...");
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsError = isError;
    }
}
