using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Authentication;

/// <summary>承载登录输入，并以明确成功状态结束模态窗口后交回 App 切换 Shell。</summary>
public partial class LoginWindow : FluentWindow
{
    private readonly LoginViewModel _viewModel;
    private bool _allowClose;

    /// <summary>创建登录窗口并连接输入事件与认证成功通知。</summary>
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        LoginButton.Click += OnLoginButtonClick;
        CancelButton.Click += OnCancelButtonClick;
        PasswordBox.KeyDown += OnPasswordBoxKeyDown;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>指示本次模态窗口是否因完整认证成功而关闭。</summary>
    public bool IsLoginSuccessful { get; private set; }

    private void OnLoginButtonClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LoginCommand.CanExecute(PasswordBox.Password))
        {
            _viewModel.LoginCommand.Execute(PasswordBox.Password);
        }
    }

    private void OnCancelButtonClick(object sender, RoutedEventArgs e)
    {
        _allowClose = true;
        Close();
    }

    private void OnPasswordBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter && _viewModel.LoginCommand.CanExecute(PasswordBox.Password))
        {
            _viewModel.LoginCommand.Execute(PasswordBox.Password);
            e.Handled = true;
        }
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        IsLoginSuccessful = true;
        _allowClose = true;
        PasswordBox.Clear();
        Close();
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
        PasswordBox.Clear();
        LoginButton.Click -= OnLoginButtonClick;
        CancelButton.Click -= OnCancelButtonClick;
        PasswordBox.KeyDown -= OnPasswordBoxKeyDown;
        _viewModel.LoginSucceeded -= OnLoginSucceeded;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }
}
