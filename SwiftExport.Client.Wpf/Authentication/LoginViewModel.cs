using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Authentication;

public sealed class LoginViewModel : ViewModelBaseEx
{
    private readonly IAuthService _authService;
    private string _userName = string.Empty;
    private string _statusMessage = "请输入账号密码。";
    private bool _isError;

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        Title = "SwiftExport Login";
        LoginCommand = new AsyncRelayCommand<string?>(LoginAsync, CanLogin);
        RegisterBusyAwareCommand(LoginCommand);
    }

    public event EventHandler? LoginSucceeded;

    public string UserName
    {
        get => _userName;
        set
        {
            if (SetProperty(ref _userName, value))
            {
                LoginCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsError
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public IAsyncRelayCommand<string?> LoginCommand { get; }

    private bool CanLogin(string? password)
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(UserName);
    }

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
