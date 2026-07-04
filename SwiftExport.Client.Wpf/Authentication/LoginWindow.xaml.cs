using System.Windows;
using System.Windows.Input;

namespace SwiftExportClient.Authentication;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        LoginButton.Click += OnLoginButtonClick;
        CancelButton.Click += OnCancelButtonClick;
        PasswordBox.KeyDown += OnPasswordBoxKeyDown;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    private void OnLoginButtonClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.LoginCommand.CanExecute(PasswordBox.Password))
        {
            _viewModel.LoginCommand.Execute(PasswordBox.Password);
        }
    }

    private void OnCancelButtonClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
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
        PasswordBox.Clear();
        DialogResult = true;
    }
}
