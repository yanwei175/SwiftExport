using SwiftExportClient.Features.SystemSettings.Passwords.Models;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Services.UI;
using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.SystemSettings.Passwords.Views;

/// <summary>加载用户选择并把一次性密码输入交给管理员重置命令。</summary>
public partial class ResetPasswordWindow : FluentWindow, IManagedSettingsWindow
{
    private readonly ResetPasswordViewModel _viewModel;
    private bool _allowClose;

    /// <summary>创建管理员密码重置模态窗口。</summary>
    public ResetPasswordWindow(ResetPasswordViewModel viewModel)
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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        UserBox.Focus();
    }

    private void OnSubmit(object sender, RoutedEventArgs e)
    {
        var input = new ResetPasswordInput(NewPasswordBox.Password, ConfirmPasswordBox.Password);
        if (_viewModel.SubmitCommand.CanExecute(input))
        {
            _viewModel.SubmitCommand.Execute(input);
        }
    }

    private void OnFocusRequested(string field)
    {
        if (field == "User")
        {
            UserBox.Focus();
        }
        else if (field == "NewPassword")
        {
            NewPasswordBox.Focus();
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
        _viewModel.Dispose();
        SubmitButton.Click -= OnSubmit;
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.FocusRequested -= OnFocusRequested;
    }

    private void ClearPasswords()
    {
        NewPasswordBox.Clear();
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
