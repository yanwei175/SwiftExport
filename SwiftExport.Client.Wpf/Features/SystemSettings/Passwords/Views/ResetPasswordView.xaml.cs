using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.Passwords.Models;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Passwords.Views;

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
        _model.FocusRequested += OnFocusRequested;
        _model.PropertyChanged += OnModelPropertyChanged;
    }

    /// <summary>请求宿主取消当前表单。</summary>
    public event EventHandler? CancelRequested;

    /// <summary>仅密码输入属于该固定目标表单的待放弃草稿。</summary>
    public bool HasDraft => PasswordInput.Password.Length > 0 || ConfirmInput.Password.Length > 0;

    /// <summary>清除 PasswordBox 中的明文，目标用户由宿主在释放时清理。</summary>
    public void ClearSensitiveInput() { PasswordInput.Clear(); ConfirmInput.Clear(); }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        var input = new ResetPasswordInput(PasswordInput.Password, ConfirmInput.Password);
        if (_model.SubmitCommand.CanExecute(input))
        {
            // 服务端拒绝后保留密码供修改重试；成功及关闭时由宿主释放表单并清理。
            await _model.SubmitCommand.ExecuteAsync(input);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnFocusRequested(string field)
    {
        if (field == "NewPassword") PasswordInput.Focus();
        else ConfirmInput.Focus();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_model.IsBusy)) FormPanel.IsEnabled = !_model.IsBusy;
    }

    /// <summary>解除焦点及 Busy 订阅并擦除密码。</summary>
    public void Dispose()
    {
        _model.FocusRequested -= OnFocusRequested;
        _model.PropertyChanged -= OnModelPropertyChanged;
        ClearSensitiveInput();
    }
}
