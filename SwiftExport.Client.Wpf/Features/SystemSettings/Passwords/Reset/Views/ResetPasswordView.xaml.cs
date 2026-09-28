using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.Passwords.Reset.Models;
using SwiftExportClient.Features.SystemSettings.Passwords.Reset.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Passwords.Reset.Views;

/// <summary>固定呈现目标用户并采集一次性密码；不自行决定窗口关闭和目标身份。</summary>
public partial class ResetPasswordView : UserControl, IDisposable
{
    private readonly ResetPasswordViewModel _model;

    /// <summary>绑定现有重置用例，目标用户由宿主传入。</summary>
    public ResetPasswordView(ResetPasswordViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.ReadInput = () => new ResetPasswordInput(PasswordInput.Password, ConfirmInput.Password);
        model.HasInputDraft = () => HasDraft;
        model.ClearInput = ClearSensitiveInput;
        model.SaveCommand.NotifyCanExecuteChanged();
        _model.FocusRequested += OnFocusRequested;
    }


    /// <summary>仅密码输入属于该固定目标表单的待放弃草稿。</summary>
    public bool HasDraft => PasswordInput.Password.Length > 0 || ConfirmInput.Password.Length > 0;

    /// <summary>清除 PasswordBox 中的明文，目标用户由宿主在释放时清理。</summary>
    public void ClearSensitiveInput() { PasswordInput.Clear(); ConfirmInput.Clear(); }

    private void OnFocusRequested(string field)
    {
        if (field == "NewPassword") PasswordInput.Focus();
        else ConfirmInput.Focus();
    }

    /// <summary>解除焦点及 Busy 订阅并擦除密码。</summary>
    public void Dispose()
    {
        _model.ReadInput = null; _model.HasInputDraft = null; _model.ClearInput = null;
        _model.FocusRequested -= OnFocusRequested;
        ClearSensitiveInput();
    }
}
