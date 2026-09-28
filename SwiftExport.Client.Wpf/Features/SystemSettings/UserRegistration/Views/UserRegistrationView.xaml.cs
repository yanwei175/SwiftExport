using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.UserRegistration.Models;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.UserRegistration.Views;

/// <summary>采集创建用户表单和一次性密码；宿主窗口决定确认、取消及关闭。</summary>
public partial class UserRegistrationView : UserControl, IDisposable
{
    private readonly UserRegistrationViewModel _model;

    /// <summary>绑定既有注册用例，并将密码仅保存在当前视图的 PasswordBox。</summary>
    public UserRegistrationView(UserRegistrationViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        _model.FocusRequested += OnFocusRequested;
        _model.PropertyChanged += OnModelPropertyChanged;
    }

    /// <summary>请求宿主执行取消及草稿确认。</summary>
    public event EventHandler? CancelRequested;

    /// <summary>任一输入仍在内存中时要求取消确认。</summary>
    public bool HasDraft => !string.IsNullOrWhiteSpace(_model.UserName)
        || !string.IsNullOrWhiteSpace(_model.NickName) || !string.IsNullOrWhiteSpace(_model.Email)
        || !string.IsNullOrWhiteSpace(_model.PhoneNumber) || PasswordInput.Password.Length > 0
        || ConfirmInput.Password.Length > 0;

    /// <summary>关闭时清除一次性密码和字段，避免窗口对象被短暂持有时留下明文。</summary>
    public void ClearSensitiveInput()
    {
        PasswordInput.Clear(); ConfirmInput.Clear();
        _model.UserName = _model.NickName = _model.Email = _model.PhoneNumber = string.Empty;
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        var input = new UserRegistrationPasswordInput(PasswordInput.Password, ConfirmInput.Password);
        if (_model.SubmitCommand.CanExecute(input))
        {
            // 服务端拒绝后保留全部输入供修改重试；成功与强制关闭由弹窗释放时清理。
            await _model.SubmitCommand.ExecuteAsync(input);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnFocusRequested(string field)
    {
        if (field == "UserName") UserNameBox.Focus();
        else if (field == "Password") PasswordInput.Focus();
        else ConfirmInput.Focus();
    }

    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_model.IsBusy)) FormPanel.IsEnabled = !_model.IsBusy;
    }

    /// <summary>解除 ViewModel 事件，迟到的注册响应不得再操作已关闭表单。</summary>
    public void Dispose()
    {
        _model.FocusRequested -= OnFocusRequested;
        _model.PropertyChanged -= OnModelPropertyChanged;
        _model.Dispose();
        ClearSensitiveInput();
    }
}
