using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;
using SwiftExportClient.Features.SystemSettings.Page.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.Apis.AuthApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.SystemSettings.Passwords.Reset.Models;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.SystemSettings.Passwords.Reset.ViewModels;

/// <summary>加载最小用户列表，并允许管理员重置普通用户密码。</summary>
public sealed partial class ResetPasswordViewModel : ViewModelBaseEx, IAsyncInitializable, IDisposable
{
    private readonly IAuthApi _authApi;
    private readonly IApiCallRunner _apiCallRunner;
    private readonly IMessageService _messageService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>创建管理员密码重置用例。</summary>
    public ResetPasswordViewModel(
        IAuthApi authApi,
        IApiCallRunner apiCallRunner,
        IMessageService messageService)
    {
        _authApi = authApi;
        _apiCallRunner = apiCallRunner;
        _messageService = messageService;
        Title = "重置用户密码";
        RegisterBusyAwareCommand(SubmitCommand);
        CloseState = new(() => HasInputDraft?.Invoke() == true,
            () => IsBusy || SaveCommand.IsRunning, () => ClearInput?.Invoke());
        RegisterBusyAwareCommand(SaveCommand);
        PropertyChanged += (_, _) => CloseState.NotifyStateChanged();
    }

    /// <summary>可选择的最小用户列表；管理员目标保留显示但不能重置。</summary>
    public ObservableCollection<UserSummaryDto> Users { get; } = [];

    /// <summary>关闭意图与草稿保护，供 Dialog 或其他宿主复用。</summary>
    public SettingsFormState CloseState { get; }
    /// <summary>视图在提交时提供一次性密码，不把明文保存在绑定属性中。</summary>
    public Func<ResetPasswordInput>? ReadInput { get; set; }
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

    /// <summary>本次重置已由服务端确认。</summary>
    public bool Saved { get; private set; }

    /// <summary>请求窗口清除密码并关闭。</summary>
    public event EventHandler? RequestClose;

    /// <summary>请求窗口聚焦指定输入项。</summary>
    public event Action<string>? FocusRequested;

    /// <summary>获取或设置当前选择的目标用户。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial UserSummaryDto? SelectedUser { get; set; }

    /// <summary>获取当前操作提示或错误消息。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "请选择需要重置密码的普通用户。";

    /// <summary>指示当前状态消息是否为错误。</summary>
    [ObservableProperty]
    public partial bool IsError { get; private set; }

    /// <summary>幂等加载用户列表，并忽略窗口关闭后的迟到结果。</summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        _isInitialized = true;
        await RunBusyAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _authApi.GetUsersAsync().WaitAsync(_lifetimeCancellation.Token),
                _lifetimeCancellation.Token,
                "加载用户失败");
            if (_isDisposed || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            Users.Clear();
            foreach (var user in result.Data ?? [])
            {
                Users.Add(user);
            }

            SelectedUser = Users.FirstOrDefault(user => !user.IsAdmin);
            SetStatus(SelectedUser is null
                ? "没有可重置密码的普通用户。"
                : "管理员账号会显示在列表中，但不能作为重置目标。", isError: false);
        }, "正在加载用户...");
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(ResetPasswordInput? input)
    {
        if (!TryValidate(input))
        {
            return;
        }

        var succeeded = false;
        await RunWriteAsync(async () =>
        {
            var result = await _apiCallRunner.RunAsync(
                () => _authApi.ResetUserPasswordAsync(new AdminResetPasswordRequestDto
                {
                    UserId = SelectedUser!.Uid,
                    NewPassword = input!.NewPassword,
                    ConfirmPassword = input.ConfirmPassword
                }),
                "重置密码失败");
            // 表单关闭后忽略迟到的写入结果，避免撤权后的状态或成功提示回写。
            if (_isDisposed) return;
            if (!result.Success)
            {
                SetStatus(result.Message, isError: true);
                return;
            }

            Saved = true;
            succeeded = true;
            SetStatus("密码已重置，目标用户的旧 Token 已失效。", isError: false);
            _messageService.ShowSuccess("密码已重置", "目标用户需要使用新密码重新登录。");
        }, "正在重置密码...");

        if (succeeded && !_isDisposed)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            CloseState.Complete();
        }
    }

    private bool CanSubmit(ResetPasswordInput? input)
        => IsNotBusy && input is not null;

    private bool TryValidate(ResetPasswordInput? input)
    {
        if (SelectedUser is null)
        {
            SetValidationError("请选择目标用户。", "User");
            return false;
        }

        if (SelectedUser.IsAdmin)
        {
            SetValidationError("不能通过此入口重置管理员密码。", "User");
            return false;
        }

        if (string.IsNullOrEmpty(input?.NewPassword) || input.NewPassword.Length < 6)
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

    /// <summary>取消尚未完成的用户读取，并阻止关闭后的结果回写。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        ReadInput = null; HasInputDraft = null; ClearInput = null;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
    }
}
