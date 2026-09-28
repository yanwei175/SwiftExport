using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.Models;
using SwiftExportClient.Features.SystemSettings.Passwords.Change.ViewModels;
namespace SwiftExportClient.Features.SystemSettings.Passwords.Change.Views;

/// <summary>仅负责密码输入、焦点与清理，提交及关闭决策交给模型。</summary>
public partial class ChangePasswordView : UserControl, IDisposable
{
    private readonly ChangePasswordViewModel _model;
    /// <summary>为本次表单绑定一次性输入回调。</summary>
    public ChangePasswordView(ChangePasswordViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        model.ReadInput = () => new(CurrentPasswordBox.Password, NewPasswordBox.Password, ConfirmPasswordBox.Password);
        model.HasInputDraft = () => CurrentPasswordBox.Password.Length + NewPasswordBox.Password.Length + ConfirmPasswordBox.Password.Length > 0;
        model.ClearInput = ClearPasswords;
        model.SaveCommand.NotifyCanExecuteChanged();
        model.FocusRequested += OnFocusRequested;
        Loaded += OnLoaded;
    }
    private void OnLoaded(object sender, RoutedEventArgs e) => CurrentPasswordBox.Focus();
    private void OnFocusRequested(string field)
    {
        if (field == "CurrentPassword") CurrentPasswordBox.Focus();
        else if (field == "NewPassword") NewPasswordBox.Focus();
        else ConfirmPasswordBox.Focus();
    }
    /// <summary>擦除控件持有的密码，不保留到下一次打开。</summary>
    private void ClearPasswords()
    {
        CurrentPasswordBox.Clear();
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
    }
    /// <summary>释放焦点事件和密码回调，阻止迟到结果回写。</summary>
    public void Dispose()
    {
        Loaded -= OnLoaded;
        _model.FocusRequested -= OnFocusRequested;
        _model.Dispose();
    }
}
