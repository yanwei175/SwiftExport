using SwiftExportClient.Features.SystemSettings.UserRegistration.Models;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Services.UI;
using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.SystemSettings.UserRegistration.Views;

/// <summary>承载用户创建表单，并只在提交瞬间读取 PasswordBox。</summary>
public partial class UserRegistrationWindow : FluentWindow, IManagedSettingsWindow
{
    private readonly UserRegistrationViewModel _viewModel;
    private bool _allowClose;

    /// <summary>创建普通用户注册模态窗口并连接一次性密码输入。</summary>
    public UserRegistrationWindow(UserRegistrationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        SubmitButton.Click += OnSubmit;
        _viewModel.RequestClose += OnRequestClose;
        _viewModel.FocusRequested += OnFocusRequested;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => UserNameBox.Focus();

    private void OnSubmit(object sender, RoutedEventArgs e)
    {
        var input = new UserRegistrationPasswordInput(PasswordBox.Password, ConfirmPasswordBox.Password);
        if (_viewModel.SubmitCommand.CanExecute(input))
        {
            _viewModel.SubmitCommand.Execute(input);
        }
    }

    private void OnFocusRequested(string field)
    {
        if (field == "UserName")
        {
            UserNameBox.Focus();
        }
        else if (field == "Password")
        {
            PasswordBox.Focus();
        }
        else
        {
            ConfirmPasswordBox.Focus();
        }
    }

    private void OnRequestClose(object? sender, EventArgs e)
    {
        _allowClose = true;
        ClearPasswords();
        DialogResult = true;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && _viewModel.IsBusy)
        {
            e.Cancel = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ClearPasswords();
        SubmitButton.Click -= OnSubmit;
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.FocusRequested -= OnFocusRequested;
    }

    private void ClearPasswords()
    {
        PasswordBox.Clear();
        ConfirmPasswordBox.Clear();
    }

    /// <inheritdoc />
    public void ForceClose()
    {
        _allowClose = true;
        ClearPasswords();
        Close();
    }
}
