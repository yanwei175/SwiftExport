using SwiftExportClient.Features.SystemSettings.Passwords.Models;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Services.UI;
using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.SystemSettings.Passwords.Views;

/// <summary>安全读取 PasswordBox，并把一次性密码参数交给改密命令。</summary>
public partial class ChangePasswordWindow : FluentWindow, IManagedSettingsWindow
{
    private readonly ChangePasswordViewModel _viewModel;
    private bool _allowClose;

    /// <summary>创建本人改密模态窗口并连接一次性密码输入。</summary>
    public ChangePasswordWindow(ChangePasswordViewModel viewModel)
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

    private void OnLoaded(object sender, RoutedEventArgs e) => CurrentPasswordBox.Focus();

    private void OnSubmit(object sender, RoutedEventArgs e)
    {
        var input = new ChangePasswordInput(
            CurrentPasswordBox.Password,
            NewPasswordBox.Password,
            ConfirmPasswordBox.Password);
        if (_viewModel.SubmitCommand.CanExecute(input))
        {
            _viewModel.SubmitCommand.Execute(input);
        }
    }

    private void OnFocusRequested(string field)
    {
        _ = field switch
        {
            "CurrentPassword" => CurrentPasswordBox.Focus(),
            "NewPassword" => NewPasswordBox.Focus(),
            _ => ConfirmPasswordBox.Focus()
        };
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
        CurrentPasswordBox.Clear();
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
